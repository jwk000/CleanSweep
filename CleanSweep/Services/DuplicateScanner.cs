using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CleanSweep.Common;
using CleanSweep.Models;

namespace CleanSweep.Services
{
    public static class DuplicateScanner
    {
        private static readonly HashSet<string> ExcludedDirectories = new HashSet<string>(
            new[]
            {
                "node_modules", ".git", ".svn", ".hg", "obj", "bin", "packages",
                "appdata", "windows", "program files", "program files (x86)",
                "$recycle.bin", "system volume information", ".vs", "venv", ".venv",
                "__pycache__", ".nuget"
            }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ExcludedFiles = new HashSet<string>(
            new[] { "desktop.ini", "thumbs.db", ".ds_store" }, StringComparer.OrdinalIgnoreCase);

        public static List<DuplicateGroup> Scan(ScanContext context)
        {
            var groups = new List<DuplicateGroup>();
            var roots = GetDefaultRoots(context);
            if (roots.Count == 0)
            {
                context.AddNote("没有找到可扫描的用户文件夹。");
                return groups;
            }

            long minSize = Math.Max(1, context.Settings.DuplicateMinSizeKb) * 1024L;
            var candidates = new Dictionary<long, List<FileCandidate>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int examined = 0;
            const int maxCandidates = 250000;

            foreach (string root in roots)
            {
                context.Token.ThrowIfCancellationRequested();
                if (!Directory.Exists(root) || !PathSafety.IsLocalPath(root))
                {
                    continue;
                }

                context.Report("收集重复文件候选 · " + Shorten(root));
                var walk = FileWalker.EnumerateFiles(root,
                    dir => ShouldVisitDirectory(dir),
                    file => ShouldVisitFile(file),
                    context.Token, maxCandidates - examined);

                foreach (string path in walk.Files)
                {
                    context.Token.ThrowIfCancellationRequested();
                    if (!seen.Add(path))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists || info.Length < minSize || info.Length <= 0)
                        {
                            continue;
                        }

                        FileAttributes attributes = info.Attributes;
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System |
                                           FileAttributes.Offline | FileAttributes.ReparsePoint)) != 0)
                        {
                            continue;
                        }

                        List<FileCandidate> list;
                        if (!candidates.TryGetValue(info.Length, out list))
                        {
                            list = new List<FileCandidate>();
                            candidates[info.Length] = list;
                        }

                        list.Add(new FileCandidate
                        {
                            Path = path,
                            Size = info.Length,
                            LastWrite = info.LastWriteTime
                        });
                        examined++;
                    }
                    catch
                    {
                        // Inaccessible or disappeared.
                    }

                    if (examined >= maxCandidates)
                    {
                        break;
                    }
                }

                if (examined >= maxCandidates)
                {
                    context.AddNote("重复文件候选超过 " + maxCandidates.ToString("N0") +
                                    " 个，已截断。可提高最小文件大小后重试。");
                    break;
                }

                if (walk.AccessLimited)
                {
                    context.AddNote("部分用户文件夹无权访问，重复文件扫描可能不完整。");
                }
            }

            var sizeGroups = candidates.Values.Where(list => list.Count > 1).ToList();
            if (sizeGroups.Count == 0)
            {
                return groups;
            }

            // First pass: a cheap prefix hash removes files with equal size but
            // different content. Only survivors receive the full SHA-256 pass.
            var quickGroups = new Dictionary<string, List<FileCandidate>>(StringComparer.Ordinal);
            int processed = 0;
            int sizeGroupTotal = sizeGroups.Count;
            foreach (var list in sizeGroups)
            {
                context.Token.ThrowIfCancellationRequested();
                processed++;
                context.Report("快速比对 " + processed + "/" + sizeGroupTotal + " 组同大小文件");
                if (context.SubProgress != null)
                {
                    context.SubProgress(0.15 * processed / Math.Max(1, sizeGroupTotal));
                }

                foreach (var candidate in list)
                {
                    string quick;
                    if (!TryQuickHash(candidate.Path, out quick))
                    {
                        continue;
                    }

                    string key = candidate.Size + ":" + quick;
                    List<FileCandidate> bucket;
                    if (!quickGroups.TryGetValue(key, out bucket))
                    {
                        bucket = new List<FileCandidate>();
                        quickGroups[key] = bucket;
                    }

                    bucket.Add(candidate);
                }
            }

            var fullGroups = new List<HashGroup>();
            var quickList = quickGroups.Values.Where(b => b.Count > 1).ToList();
            int quickCount = 0;
            foreach (var bucket in quickList)
            {
                context.Token.ThrowIfCancellationRequested();
                quickCount++;
                context.Report("计算 SHA-256 · 第 " + quickCount + " 组");
                if (context.SubProgress != null)
                {
                    context.SubProgress(0.15 + 0.8 * quickCount / Math.Max(1, quickList.Count));
                }

                var byHash = new Dictionary<string, List<FileCandidate>>(StringComparer.Ordinal);
                foreach (var candidate in bucket)
                {
                    string hash;
                    if (!TryFullHash(candidate.Path, out hash))
                    {
                        continue;
                    }

                    List<FileCandidate> same;
                    if (!byHash.TryGetValue(hash, out same))
                    {
                        same = new List<FileCandidate>();
                        byHash[hash] = same;
                    }

                    same.Add(candidate);
                }

                foreach (var pair in byHash.Where(p => p.Value.Count > 1))
                {
                    fullGroups.Add(new HashGroup { Hash = pair.Key, Files = pair.Value });
                }
            }

            if (context.SubProgress != null)
            {
                context.SubProgress(1.0);
            }

            int groupIndex = 0;
            foreach (var hashGroup in fullGroups)
            {
                context.Token.ThrowIfCancellationRequested();
                var list = hashGroup.Files;
                if (list.Count < 2)
                {
                    continue;
                }

                if (groups.Count >= 1000)
                {
                    context.AddNote("重复文件组超过 1000 组，已截断显示。");
                    break;
                }

                groupIndex++;
                string hash = hashGroup.Hash;
                var group = new DuplicateGroup
                {
                    Id = "dup-" + groupIndex + "-" + hash.Substring(0, Math.Min(12, hash.Length)),
                    FileName = Path.GetFileName(list[0].Path),
                    Hash = hash,
                    Size = list[0].Size
                };

                foreach (var candidate in list)
                {
                    var item = new FindingItem
                    {
                        Id = "duplicate." + candidate.Path.ToLowerInvariant(),
                        Category = CategoryKeys.Duplicates,
                        RuleId = "duplicate.sha256",
                        Title = Path.GetFileName(candidate.Path),
                        Subtitle = "重复文件",
                        DisplayPath = candidate.Path,
                        Paths = new List<string> { candidate.Path },
                        AllowedRoots = new List<string>(roots),
                        Action = CleanAction.RecyclePaths,
                        Size = candidate.Size,
                        FileCount = 1,
                        LastWrite = candidate.LastWrite,
                        Risk = RiskLevel.Medium,
                        DefaultSelected = true,
                        IsSelectable = true,
                        Basis = "与同组文件大小和 SHA-256 完全一致。默认保留修改时间最早的一份；其余移入回收站，清空回收站后才释放空间。",
                        EvidenceSummary = "SHA-256 完全一致：" + hash.Substring(0, Math.Min(20, hash.Length)) + "…",
                        Glyph = "\uE8C8",
                        KindLabel = "重复文件",
                        GroupId = group.Id,
                        Hash = hash
                    };

                    item.Evidence.Add("文件大小：" + Format.Bytes(candidate.Size));
                    item.Evidence.Add("SHA-256：" + hash);
                    item.Evidence.Add("同组文件数：" + list.Count);
                    item.Evidence.Add("保护条件：只扫描用户文档目录；跳过隐藏/系统文件、符号链接和程序目录。");
                    group.Files.Add(item);
                }

                group.Initialize();
                foreach (var file in group.Files)
                {
                    file.KeepPath = group.KeepPath;
                    file.Evidence.Add("保留副本：" + group.KeepPath);
                    file.Evidence.Add("选择理由：保留修改时间最早的一份；若时间相同，保留路径排序靠前的一份。");
                }

                groups.Add(group);
                context.ReportFound(groups.Count, groups.Sum(g => g.ReclaimableBytes));
            }

            if (groups.Count == 0)
            {
                context.AddNote("没有发现满足条件的重复文件（最小 " + Format.Bytes(minSize) + "，按 SHA-256 全量比对）。");
            }

            return groups;
        }

        public static List<string> GetDefaultRoots(ScanContext context)
        {
            var roots = new List<string>();
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            AddRoot(roots, downloads);

            if (context.Settings != null && context.Settings.ExcludedDuplicateFolders != null)
            {
                roots.RemoveAll(r => context.Settings.ExcludedDuplicateFolders.Any(ex =>
                {
                    if (string.IsNullOrWhiteSpace(ex))
                    {
                        return false;
                    }

                    string normalized = PathSafety.Normalize(ex);
                    return !string.IsNullOrEmpty(normalized) &&
                           (string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase) ||
                            r.StartsWith(normalized + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase));
                }));
            }

            return roots;
        }

        public static List<DuplicateGroup> ScanRootsForTests(IEnumerable<string> roots, long minSize,
            bool elevated)
        {
            var context = new ScanContext
            {
                Settings = new AppSettings { DuplicateMinSizeKb = (int)Math.Max(1, minSize / 1024) },
                IsElevated = elevated,
                Token = System.Threading.CancellationToken.None,
                Status = _ => { },
                Found = (_, __) => { }
            };

            return ScanRootsInternal(roots, context);
        }

        private static List<DuplicateGroup> ScanRootsInternal(IEnumerable<string> roots, ScanContext context)
        {
            var groups = new List<DuplicateGroup>();
            long minSize = Math.Max(1, context.Settings.DuplicateMinSizeKb) * 1024L;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var bySize = FileWalker.EnumerateFiles(root, null, null, context.Token)
                    .Files.Select(p => new FileInfo(p))
                    .Where(f => f.Exists && f.Length >= minSize)
                    .GroupBy(f => f.Length)
                    .Where(g => g.Count() > 1);

                foreach (var sizeGroup in bySize)
                {
                    var byHash = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
                    foreach (var file in sizeGroup)
                    {
                        string hash;
                        if (!TryFullHash(file.FullName, out hash))
                        {
                            continue;
                        }

                        List<FileInfo> list;
                        if (!byHash.TryGetValue(hash, out list))
                        {
                            list = new List<FileInfo>();
                            byHash[hash] = list;
                        }

                        list.Add(file);
                    }

                    foreach (var pair in byHash.Where(p => p.Value.Count > 1))
                    {
                        string groupId = "test-" + groups.Count;
                        var group = new DuplicateGroup
                        {
                            Id = groupId,
                            FileName = pair.Value[0].Name,
                            Hash = pair.Key,
                            Size = pair.Value[0].Length
                        };
                        foreach (var file in pair.Value)
                        {
                            group.Files.Add(new FindingItem
                            {
                                Id = "dup." + file.FullName,
                                Category = CategoryKeys.Duplicates,
                                RuleId = "duplicate.sha256",
                                Title = file.Name,
                                DisplayPath = file.FullName,
                                Paths = new List<string> { file.FullName },
                                AllowedRoots = new List<string> { root },
                                Action = CleanAction.RecyclePaths,
                                Size = file.Length,
                                FileCount = 1,
                                LastWrite = file.LastWriteTime,
                                Risk = RiskLevel.Medium,
                                DefaultSelected = true,
                                IsSelectable = true,
                                Basis = "test",
                                GroupId = groupId,
                                Hash = pair.Key
                            });
                        }

                        group.Initialize();
                        groups.Add(group);
                    }
                }
            }

            return groups;
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string normalized = PathSafety.Normalize(path);
                if (!string.IsNullOrEmpty(normalized) && !roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(normalized);
                }
            }
            catch
            {
            }
        }

        private static bool ShouldVisitDirectory(string path)
        {
            try
            {
                var info = new DirectoryInfo(path);
                if (ExcludedDirectories.Contains(info.Name))
                {
                    return false;
                }

                FileAttributes attributes = info.Attributes;
                return (attributes & (FileAttributes.Hidden | FileAttributes.System |
                                      FileAttributes.ReparsePoint | FileAttributes.Offline)) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldVisitFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (ExcludedFiles.Contains(info.Name))
                {
                    return false;
                }

                FileAttributes attributes = info.Attributes;
                return (attributes & (FileAttributes.Hidden | FileAttributes.System |
                                      FileAttributes.ReparsePoint | FileAttributes.Offline)) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryQuickHash(string path, out string hash)
        {
            hash = null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                {
                    var buffer = new byte[64 * 1024];
                    int total = 0;
                    while (total < buffer.Length)
                    {
                        int read = stream.Read(buffer, total, buffer.Length - total);
                        if (read <= 0)
                        {
                            break;
                        }

                        total += read;
                    }

                    hash = BitConverter.ToString(sha.ComputeHash(buffer, 0, total)).Replace("-", "");
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFullHash(string path, out string hash)
        {
            hash = null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                {
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string Shorten(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            if (path.Length <= 56)
            {
                return path;
            }

            return "…" + path.Substring(path.Length - 55);
        }

        private sealed class FileCandidate
        {
            public string Path { get; set; }
            public long Size { get; set; }
            public DateTime LastWrite { get; set; }
        }

        private sealed class HashGroup
        {
            public string Hash { get; set; }
            public List<FileCandidate> Files { get; set; }
        }
    }
}
