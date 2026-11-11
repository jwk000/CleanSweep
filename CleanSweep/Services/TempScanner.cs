using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CleanSweep.Models;

namespace CleanSweep.Services
{
    public static class TempScanner
    {
        private sealed class RootRule
        {
            public string RuleId;
            public string Title;
            public string Root;
            public TimeSpan MinAge;
            public RiskLevel Risk;
            public bool DefaultSelected;
            public bool RequiresAdmin;
            public string Glyph;
            public string Kind;
            public string Basis;
            public Func<string, bool> FileFilter;
        }

        public static List<FindingItem> Scan(ScanContext context)
        {
            var results = new List<FindingItem>();
            var rules = BuildRules(context);

            foreach (var rule in rules)
            {
                context.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(rule.Root) || !Directory.Exists(rule.Root))
                {
                    continue;
                }

                context.Report(rule.Title);
                try
                {
                    var walk = FileWalker.EnumerateFiles(rule.Root, null, rule.FileFilter,
                        context.Token, 250000);
                    if (walk.AccessLimited)
                    {
                        context.AddNote(rule.Title + "：部分子目录没有访问权限，已跳过。");
                    }

                    var cutoff = DateTime.Now - rule.MinAge;
                    var paths = new List<string>();
                    long size = 0;
                    DateTime? newest = null;
                    foreach (string file in walk.Files)
                    {
                        context.Token.ThrowIfCancellationRequested();
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.LastWriteTime > cutoff)
                            {
                                continue;
                            }

                            if (info.Length <= 0)
                            {
                                continue;
                            }

                            paths.Add(file);
                            size += info.Length;
                            if (!newest.HasValue || info.LastWriteTime > newest.Value)
                            {
                                newest = info.LastWriteTime;
                            }
                        }
                        catch
                        {
                            // Locked or vanished; skip it.
                        }
                    }

                    if (paths.Count == 0)
                    {
                        continue;
                    }

                    bool selectable = !rule.RequiresAdmin || context.IsElevated;
                    var item = new FindingItem
                    {
                        Id = rule.RuleId,
                        Category = CategoryKeys.Temp,
                        RuleId = rule.RuleId,
                        Title = rule.Title,
                        Subtitle = rule.Basis,
                        DisplayPath = rule.Root,
                        Paths = paths,
                        AllowedRoots = new List<string> { rule.Root },
                        Action = CleanAction.DeleteFiles,
                        Size = size,
                        FileCount = paths.Count,
                        LastWrite = newest,
                        Risk = rule.Risk,
                        DefaultSelected = rule.DefaultSelected && selectable,
                        RequiresAdmin = rule.RequiresAdmin,
                        IsSelectable = selectable,
                        Basis = rule.Basis,
                        Glyph = rule.Glyph,
                        KindLabel = rule.Kind,
                        EvidenceSummary = "修改时间早于 " + cutoff.ToString("yyyy-MM-dd HH:mm") +
                                          "，且不是符号链接或正在使用的文件。"
                    };

                    item.AddEvidence("扫描目录：" + rule.Root);
                    item.AddEvidence("时间条件：最后修改时间早于 " + cutoff.ToString("yyyy-MM-dd HH:mm") +
                                     "（" + DescribeAge(rule.MinAge) + "）");
                    item.AddEvidence("文件数量：" + paths.Count.ToString("N0") + "，合计 " + Common.Format.Bytes(size));
                    item.AddEvidence("保护条件：不跟随符号链接，不递归删除目录，不触碰正在使用的文件。");
                    if (rule.RequiresAdmin && !context.IsElevated)
                    {
                        item.AddEvidence("当前为标准权限，此项只预览、不可选择；请以管理员身份重启后清理。");
                    }
                    if (walk.AccessLimited)
                    {
                        item.AddEvidence("部分子目录没有访问权限，未计入统计。");
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
                    context.AddNote(rule.Title + "：扫描失败（" + ex.Message + "）。");
                }
            }

            return results;
        }

        private static List<RootRule> BuildRules(ScanContext context)
        {
            var rules = new List<RootRule>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            int ageHours = Math.Max(1, context.Settings.TempAgeHours);

            rules.Add(new RootRule
            {
                RuleId = "temp.user",
                Title = "用户临时文件",
                Root = Path.GetTempPath(),
                MinAge = TimeSpan.FromHours(ageHours),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = false,
                Glyph = "\uE74D",
                Kind = "临时文件",
                Basis = "仅清理用户临时目录中超过 " + ageHours + " 小时未修改的文件；临时目录本身不会被删除。"
            });

            rules.Add(new RootRule
            {
                RuleId = "temp.windows",
                Title = "Windows 临时文件",
                Root = Path.Combine(windows, "Temp"),
                MinAge = TimeSpan.FromHours(ageHours),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = true,
                Glyph = "\uE74D",
                Kind = "系统临时文件",
                Basis = "清理 Windows\\Temp 中超过 " + ageHours + " 小时未修改的文件；需要管理员权限。"
            });

            rules.Add(new RootRule
            {
                RuleId = "cache.windows-update",
                Title = "Windows 更新下载缓存",
                Root = Path.Combine(windows, @"SoftwareDistribution\Download"),
                MinAge = TimeSpan.FromDays(7),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = true,
                Glyph = "\uE895",
                Kind = "更新缓存",
                Basis = "只清理 7 天前的更新下载文件。Windows 会在需要时重新下载；不会删除已安装更新。"
            });

            rules.Add(new RootRule
            {
                RuleId = "cache.delivery-optimization",
                Title = "传递优化缓存",
                Root = Path.Combine(windows,
                    @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                MinAge = TimeSpan.FromDays(7),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = true,
                Glyph = "\uE896",
                Kind = "分发缓存",
                Basis = "传递优化用于在局域网/互联网分发更新。7 天前的分发缓存可以安全重建。"
            });

            rules.Add(new RootRule
            {
                RuleId = "cache.wer-user",
                Title = "Windows 错误报告（用户）",
                Root = Path.Combine(local, @"Microsoft\Windows\WER"),
                MinAge = TimeSpan.FromDays(30),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = false,
                Glyph = "\uE7BA",
                Kind = "错误报告",
                Basis = "超过 30 天的错误报告归档；错误报告仅用于诊断，不影响系统运行。"
            });

            rules.Add(new RootRule
            {
                RuleId = "cache.wer-machine",
                Title = "Windows 错误报告（系统）",
                Root = Path.Combine(common, @"Microsoft\Windows\WER"),
                MinAge = TimeSpan.FromDays(30),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = true,
                Glyph = "\uE7BA",
                Kind = "错误报告",
                Basis = "清理 ProgramData 下超过 30 天的错误报告；需要管理员权限。"
            });

            if (context.Settings.IncludeCrashDumps)
            {
                rules.Add(new RootRule
                {
                    RuleId = "dump.user",
                    Title = "应用崩溃转储",
                    Root = Path.Combine(local, "CrashDumps"),
                    MinAge = TimeSpan.FromDays(7),
                    Risk = RiskLevel.Medium,
                    DefaultSelected = false,
                    RequiresAdmin = false,
                    Glyph = "\uE7BA",
                    Kind = "崩溃转储",
                    Basis = "崩溃转储用于分析程序故障，通常体积较大。默认不勾选；确认不需要排查问题时再清理。"
                });

                rules.Add(new RootRule
                {
                    RuleId = "dump.minidump",
                    Title = "Windows 小型内存转储",
                    Root = Path.Combine(windows, "Minidump"),
                    MinAge = TimeSpan.FromDays(30),
                    Risk = RiskLevel.Medium,
                    DefaultSelected = false,
                    RequiresAdmin = true,
                    Glyph = "\uE7BA",
                    Kind = "内存转储",
                    Basis = "系统蓝屏时生成的小型转储，30 天前的文件默认不勾选，保留故障排查能力。"
                });
            }

            rules.Add(new RootRule
            {
                RuleId = "cache.thumbnails",
                Title = "资源管理器缩略图缓存",
                Root = Path.Combine(local, @"Microsoft\Windows\Explorer"),
                MinAge = TimeSpan.FromDays(7),
                Risk = RiskLevel.Safe,
                DefaultSelected = true,
                RequiresAdmin = false,
                Glyph = "\uEB9F",
                Kind = "缩略图缓存",
                Basis = "只匹配 thumbcache_*.db；缩略图会按需重新生成，不删除任何原始图片。",
                FileFilter = path =>
                {
                    string name = Path.GetFileName(path);
                    return name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".db", StringComparison.OrdinalIgnoreCase);
                }
            });

            if (context.Settings.IncludeBrowserCache)
            {
                AddBrowserRules(rules, local, context);
            }

            return rules;
        }

        private static void AddBrowserRules(List<RootRule> rules, string local, ScanContext context)
        {
            AddChromiumRules(rules, "Chrome", Path.Combine(local, @"Google\Chrome\User Data"), "chrome", context);
            AddChromiumRules(rules, "Edge", Path.Combine(local, @"Microsoft\Edge\User Data"), "msedge", context);
            AddChromiumRules(rules, "Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"), "brave", context);
            AddFirefoxRules(rules, Path.Combine(local, @"Mozilla\Firefox\Profiles"), context);
        }

        private static void AddChromiumRules(List<RootRule> rules, string browser, string userData,
            string processName, ScanContext context)
        {
            if (!Directory.Exists(userData))
            {
                return;
            }

            if (IsProcessRunning(processName))
            {
                context.AddNote(browser + " 正在运行，已跳过它的浏览器缓存。关闭浏览器后重新扫描即可。");
                return;
            }

            string[] profiles;
            try
            {
                profiles = Directory.GetDirectories(userData);
            }
            catch
            {
                return;
            }

            foreach (string profile in profiles)
            {
                string profileName = Path.GetFileName(profile);
                if (!string.Equals(profileName, "Default", StringComparison.OrdinalIgnoreCase) &&
                    !profileName.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string cacheName in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    string root = Path.Combine(profile, cacheName);
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    rules.Add(new RootRule
                    {
                        RuleId = "cache." + browser.ToLowerInvariant() + "." + profileName.Replace(' ', '-').ToLowerInvariant() +
                                 "." + cacheName.Replace(' ', '-').ToLowerInvariant(),
                        Title = browser + " 缓存 · " + profileName + " · " + cacheName,
                        Root = root,
                        MinAge = TimeSpan.FromDays(7),
                        Risk = RiskLevel.Safe,
                        DefaultSelected = true,
                        RequiresAdmin = false,
                        Glyph = "\uE774",
                        Kind = "浏览器缓存",
                        Basis = "只清理 " + browser + " 缓存目录中 7 天前的文件；不会删除书签、密码、历史记录或 Cookie。"
                    });
                }
            }
        }

        private static void AddFirefoxRules(List<RootRule> rules, string profilesRoot, ScanContext context)
        {
            if (!Directory.Exists(profilesRoot))
            {
                return;
            }

            if (IsProcessRunning("firefox"))
            {
                context.AddNote("Firefox 正在运行，已跳过它的浏览器缓存。关闭浏览器后重新扫描即可。");
                return;
            }

            string[] profiles;
            try
            {
                profiles = Directory.GetDirectories(profilesRoot);
            }
            catch
            {
                return;
            }

            foreach (string profile in profiles)
            {
                string root = Path.Combine(profile, "cache2");
                if (!Directory.Exists(root))
                {
                    continue;
                }

                rules.Add(new RootRule
                {
                    RuleId = "cache.firefox." + Path.GetFileName(profile).ToLowerInvariant(),
                    Title = "Firefox 缓存 · " + Path.GetFileName(profile),
                    Root = root,
                    MinAge = TimeSpan.FromDays(7),
                    Risk = RiskLevel.Safe,
                    DefaultSelected = true,
                    RequiresAdmin = false,
                    Glyph = "\uE774",
                    Kind = "浏览器缓存",
                    Basis = "只清理 Firefox cache2 中 7 天前的缓存；不会删除书签、密码或浏览历史。"
                });
            }
        }

        private static bool IsProcessRunning(string name)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                bool running = processes.Length > 0;
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                return running;
            }
            catch
            {
                return false;
            }
        }

        private static string DescribeAge(TimeSpan age)
        {
            if (age.TotalDays >= 1)
            {
                return (int)age.TotalDays + " 天";
            }

            return (int)age.TotalHours + " 小时";
        }
    }
}
