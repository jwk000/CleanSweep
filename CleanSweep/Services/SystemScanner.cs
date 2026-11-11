using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CleanSweep.Common;
using CleanSweep.Models;

namespace CleanSweep.Services
{
    public static class SystemScanner
    {
        public static List<FindingItem> Scan(ScanContext context)
        {
            var results = new List<FindingItem>();
            if (!context.Settings.IncludeSystemCache)
            {
                return results;
            }

            ScanUpgradeLeftovers(context, results);
            ScanRecycleBin(context, results);
            return results;
        }

        private static void ScanUpgradeLeftovers(ScanContext context, List<FindingItem> results)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemDrive = Path.GetPathRoot(windows);
            if (string.IsNullOrWhiteSpace(systemDrive))
            {
                return;
            }

            var candidates = new[]
            {
                new UpgradePath(Path.Combine(systemDrive, "Windows.old"), "旧 Windows 安装",
                    "Windows 升级后保留的上一版本系统目录。系统通常会在 10 天后自动删除；删除后无法回滚到上一版本。"),
                new UpgradePath(Path.Combine(systemDrive, "$Windows.~BT"), "Windows 升级临时文件",
                    "Windows 升级过程中留下的安装暂存目录；升级完成后不再需要。"),
                new UpgradePath(Path.Combine(systemDrive, "$Windows.~WS"), "Windows 升级工作目录",
                    "Windows 升级过程中留下的工作目录；升级完成后不再需要。"),
                new UpgradePath(Path.Combine(systemDrive, "$WinREAgent"), "恢复环境代理残留",
                    "Windows 恢复环境更新留下的临时目录；系统可重新创建。"),
                new UpgradePath(Path.Combine(systemDrive, "$GetCurrent"), "Windows 升级下载器残留",
                    "Windows 升级下载器留下的临时目录。")
            };

            foreach (var candidate in candidates)
            {
                context.Token.ThrowIfCancellationRequested();
                if (!Directory.Exists(candidate.Path))
                {
                    continue;
                }

                context.Report("评估系统冗余 · " + candidate.Title);
                try
                {
                    var walk = FileWalker.EnumerateFiles(candidate.Path, context.Token, 500000);
                    if (walk.Files.Count == 0)
                    {
                        continue;
                    }

                    DateTime? created = null;
                    DateTime? lastWrite = null;
                    try
                    {
                        created = Directory.GetCreationTime(candidate.Path);
                        lastWrite = Directory.GetLastWriteTime(candidate.Path);
                    }
                    catch
                    {
                    }

                    var item = new FindingItem
                    {
                        Id = "system.upgrade." + candidate.Path.ToLowerInvariant(),
                        Category = CategoryKeys.System,
                        RuleId = "system.upgrade-leftover",
                        Title = candidate.Title,
                        Subtitle = "Windows 升级残留",
                        DisplayPath = candidate.Path,
                        Paths = new List<string> { candidate.Path },
                        AllowedRoots = new List<string> { Path.GetDirectoryName(candidate.Path) },
                        Action = CleanAction.DeleteDirectories,
                        Size = walk.Size,
                        FileCount = walk.Files.Count,
                        LastWrite = lastWrite,
                        Risk = RiskLevel.High,
                        DefaultSelected = false,
                        RequiresAdmin = true,
                        IsSelectable = context.IsElevated,
                        RequiresTakeOwnership = true,
                        Basis = candidate.Basis,
                        EvidenceSummary = "系统升级备份目录，占用 " + Format.Bytes(walk.Size),
                        Glyph = "\uE7BA",
                        KindLabel = "系统目录"
                    };

                    item.Evidence.Add("目录：" + candidate.Path);
                    item.Evidence.Add("文件数量：" + walk.Files.Count.ToString("N0") + "，合计 " + Format.Bytes(walk.Size));
                    if (created.HasValue)
                    {
                        item.Evidence.Add("目录创建时间：" + created.Value.ToString("yyyy-MM-dd HH:mm"));
                    }
                    item.Evidence.Add("删除方式：先尝试普通删除；若权限不足，会在管理员模式下修复目录权限后删除。");
                    item.Evidence.Add("高风险：该目录可能用于系统回滚或恢复，删除后不可恢复。");
                    if (!context.IsElevated)
                    {
                        item.Evidence.Add("当前为标准权限，此项只预览、不可选择。");
                    }
                    if (walk.AccessLimited)
                    {
                        item.Evidence.Add("部分子目录无权访问，统计可能偏小。");
                    }

                    results.Add(item);
                    context.ReportFound(results.Count, results.Sum(x => x.Size));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    context.AddNote(candidate.Title + "：扫描失败（" + ex.Message + "）。");
                }
            }
        }

        private static void ScanRecycleBin(ScanContext context, List<FindingItem> results)
        {
            context.Report("读取回收站");
            long size;
            long count;
            if (!NativeMethods.TryGetRecycleBinInfo(out size, out count) || size <= 0)
            {
                return;
            }

            var item = new FindingItem
            {
                Id = "system.recyclebin",
                Category = CategoryKeys.System,
                RuleId = "system.recycle-bin",
                Title = "回收站",
                Subtitle = "已删除但尚未释放的空间",
                DisplayPath = "回收站",
                Paths = new List<string>(),
                Action = CleanAction.EmptyRecycleBin,
                Size = size,
                FileCount = count > int.MaxValue ? int.MaxValue : (int)count,
                Risk = RiskLevel.High,
                DefaultSelected = false,
                RequiresAdmin = false,
                IsSelectable = true,
                Basis = "回收站中的文件仍占用磁盘。清空后不可恢复；默认不勾选，避免误删可还原内容。",
                EvidenceSummary = "回收站中有 " + Format.Count((int)Math.Min(count, int.MaxValue), "个项目") +
                                  "，占用 " + Format.Bytes(size),
                Glyph = "\uE74D",
                KindLabel = "回收站"
            };

            item.Evidence.Add("通过 Windows Shell 查询回收站，共 " + count.ToString("N0") + " 个项目，" +
                              Format.Bytes(size) + "。");
            item.Evidence.Add("清空操作不可撤销；如果回收站里有需要恢复的文件，请不要选择此项。");
            results.Add(item);
            context.ReportFound(results.Count, results.Sum(x => x.Size));
        }

        private sealed class UpgradePath
        {
            public UpgradePath(string path, string title, string basis)
            {
                Path = path;
                Title = title;
                Basis = basis;
            }

            public string Path { get; private set; }
            public string Title { get; private set; }
            public string Basis { get; private set; }
        }
    }
}
