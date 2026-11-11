using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CleanSweep.Models;
using Microsoft.Win32;

namespace CleanSweep.Services
{
    public static class LeftoverScanner
    {
        public static List<FindingItem> Scan(ScanContext context)
        {
            var results = new List<FindingItem>();
            ScanShortcuts(context, results);
            ScanUninstallEntries(context, results);
            ScanEmptyStartMenuFolders(context, results);
            return results;
        }

        private static void ScanUninstallEntries(ScanContext context, List<FindingItem> results)
        {
            var locations = new[]
            {
                new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM", true),
                new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM(32)", true),
                new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Default,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU", false)
            };

            foreach (var location in locations)
            {
                context.Token.ThrowIfCancellationRequested();
                context.Report("检查卸载登记 · " + location.DisplayName);
                RegistryKey baseKey = null;
                RegistryKey uninstall = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
                    uninstall = baseKey.OpenSubKey(location.SubKey);
                    if (uninstall == null)
                    {
                        continue;
                    }

                    foreach (string subName in uninstall.GetSubKeyNames())
                    {
                        context.Token.ThrowIfCancellationRequested();
                        RegistryKey key = null;
                        try
                        {
                            key = uninstall.OpenSubKey(subName);
                            if (key == null)
                            {
                                continue;
                            }

                            var item = EvaluateUninstallEntry(key, location, subName, context);
                            if (item != null)
                            {
                                results.Add(item);
                                context.ReportFound(results.Count, results.Sum(x => x.Size));
                            }
                        }
                        catch
                        {
                            context.AddNote("读取卸载登记项失败：" + subName);
                        }
                        finally
                        {
                            if (key != null) key.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    context.AddNote("无法读取卸载登记（" + location.DisplayName + "）：" + ex.Message);
                }
                finally
                {
                    if (uninstall != null) uninstall.Dispose();
                    if (baseKey != null) baseKey.Dispose();
                }
            }
        }

        private static FindingItem EvaluateUninstallEntry(RegistryKey key, RegistryLocation location,
            string subName, ScanContext context)
        {
            string displayName = ReadString(key, "DisplayName");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            if (ReadDword(key, "SystemComponent") == 1 || ReadDword(key, "NoRemove") == 1)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(ReadString(key, "ParentKeyName")))
            {
                return null;
            }

            string releaseType = ReadString(key, "ReleaseType");
            if (!string.IsNullOrWhiteSpace(releaseType) &&
                (releaseType.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 releaseType.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 releaseType.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 releaseType.IndexOf("ServicePack", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return null;
            }

            string uninstallString = ReadString(key, "UninstallString");
            string quiet = ReadString(key, "QuietUninstallString");
            string installLocation = NormalizeLocation(ReadString(key, "InstallLocation"));
            string displayIcon = NormalizeLocation(ReadString(key, "DisplayIcon"));

            bool isMsi = ContainsMsiExec(uninstallString) || ContainsMsiExec(quiet) ||
                         ReadDword(key, "WindowsInstaller") == 1;
            if (isMsi)
            {
                // Windows Installer entries can sometimes repair themselves from the
                // product code. Never remove them automatically.
                return null;
            }

            string uninstaller;
            bool uninstallerKnown;
            ParseUninstaller(uninstallString, out uninstaller, out uninstallerKnown);
            if (!uninstallerKnown && string.IsNullOrWhiteSpace(quiet))
            {
                ParseUninstaller(quiet, out uninstaller, out uninstallerKnown);
            }

            if (uninstallerKnown && !string.IsNullOrWhiteSpace(uninstaller) &&
                PathSafety.IsLocalPath(uninstaller) &&
                (File.Exists(uninstaller) || Directory.Exists(uninstaller)))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(installLocation) &&
                PathSafety.IsLocalPath(installLocation) &&
                (Directory.Exists(installLocation) || File.Exists(installLocation)))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(installLocation) && !PathSafety.IsLocalPath(installLocation))
            {
                // Removable or network installs may simply be disconnected.
                return null;
            }

            if (!string.IsNullOrWhiteSpace(displayIcon) && PathSafety.IsLocalPath(displayIcon) &&
                File.Exists(displayIcon))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(installLocation) && !uninstallerKnown)
            {
                return null;
            }

            bool strong = uninstallerKnown;
            if (!strong && string.IsNullOrWhiteSpace(installLocation))
            {
                return null;
            }

            string hivePrefix = location.DisplayName.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                ? "HKEY_LOCAL_MACHINE"
                : "HKEY_CURRENT_USER";
            string fullKey = hivePrefix + "\\" + location.SubKey + "\\" + subName;
            string exportKey = fullKey;
            if (location.View == RegistryView.Registry32 &&
                location.SubKey.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase))
            {
                // reg.exe defaults to the 64-bit view. WOW6432Node makes the export
                // target the same 32-bit entry that we are about to delete.
                exportKey = hivePrefix + @"\SOFTWARE\WOW6432Node\" +
                            location.SubKey.Substring(@"SOFTWARE\".Length) + "\\" + subName;
            }

            string shortHive = location.DisplayName.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                ? "HKLM"
                : "HKCU";
            string shortKey = shortHive + "\\...\\Uninstall\\" + subName;

            bool requiresAdmin = location.Hive == RegistryHive.LocalMachine;
            bool selectable = !requiresAdmin || context.IsElevated;

            var item = new FindingItem
            {
                Id = "leftover.uninstall." + fullKey,
                Category = CategoryKeys.Leftover,
                RuleId = "leftover.uninstall",
                Title = displayName,
                Subtitle = "卸载登记残留",
                DisplayPath = shortKey,
                Action = CleanAction.RegistryKey,
                Size = 0,
                FileCount = 0,
                Risk = strong ? RiskLevel.Medium : RiskLevel.High,
                DefaultSelected = strong && selectable,
                RequiresAdmin = requiresAdmin,
                IsSelectable = selectable,
                Basis = strong
                    ? "卸载登记仍存在，但安装目录和卸载程序都不存在。删除前会导出 .reg 备份。"
                    : "卸载登记指向的安装目录不存在，且没有可验证的卸载程序。属于疑似残留，默认不勾选。",
                EvidenceSummary = "安装目录 / 卸载程序均不存在",
                Glyph = "\uE71D",
                KindLabel = "注册表残留",
                Registry = new RegistryTarget
                {
                    Hive = location.Hive,
                    View = location.View,
                    SubKey = location.SubKey + "\\" + subName,
                    FullPath = exportKey
                }
            };

            item.Evidence.Add("注册表路径：" + exportKey);
            item.Evidence.Add("DisplayName：" + displayName);
            string publisher = ReadString(key, "Publisher");
            if (!string.IsNullOrWhiteSpace(publisher))
            {
                item.Evidence.Add("发布者：" + publisher);
            }
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                item.Evidence.Add("InstallLocation 不存在：" + installLocation);
            }
            else
            {
                item.Evidence.Add("InstallLocation：未填写");
            }
            if (uninstallerKnown)
            {
                item.Evidence.Add("UninstallString 指向的程序不存在：" + uninstaller);
            }
            else
            {
                item.Evidence.Add("UninstallString：未填写可验证的本地卸载程序");
            }
            item.Evidence.Add("判断规则：安装目录与卸载程序都缺失才列为残留；MSI 登记不处理。");
            if (requiresAdmin && !context.IsElevated)
            {
                item.Evidence.Add("当前为标准权限，此项只预览、不可选择。");
            }
            if (!strong)
            {
                item.Evidence.Add("注意：该条属于“疑似残留”，请确认软件确实已经卸载。");
            }

            return item;
        }

        private static void ScanShortcuts(ScanContext context, List<FindingItem> results)
        {
            var roots = new List<ShortcutRoot>();
            AddShortcutRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "开始菜单", false);
            AddShortcutRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "公共开始菜单", true);
            AddShortcutRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "桌面", false);
            AddShortcutRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                "公共桌面", true);

            foreach (var root in roots)
            {
                context.Token.ThrowIfCancellationRequested();
                if (!Directory.Exists(root.Path))
                {
                    continue;
                }

                context.Report("检查快捷方式 · " + root.Label);
                var links = new List<string>();
                CollectLinks(root.Path, links, context.Token);
                if (links.Count == 0)
                {
                    continue;
                }

                var resolved = ShortcutResolver.ResolveMany(links);
                foreach (var shortcut in resolved)
                {
                    context.Token.ThrowIfCancellationRequested();
                    if (shortcut.Error != null || string.IsNullOrWhiteSpace(shortcut.TargetPath))
                    {
                        continue;
                    }

                    string target = Environment.ExpandEnvironmentVariables(shortcut.TargetPath.Trim().Trim('"'));
                    if (string.IsNullOrWhiteSpace(target) ||
                        target.StartsWith(@"\\", StringComparison.Ordinal) ||
                        target.IndexOf("shell:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        target.IndexOf(@"\Windows\Installer\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        target.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (!PathSafety.IsLocalPath(target))
                    {
                        continue;
                    }

                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        continue;
                    }

                    var info = new FileInfo(shortcut.LinkPath);
                    bool requiresAdmin = root.RequiresAdmin;
                    bool selectable = !requiresAdmin || context.IsElevated;
                    var item = new FindingItem
                    {
                        Id = "leftover.shortcut." + shortcut.LinkPath.ToLowerInvariant(),
                        Category = CategoryKeys.Leftover,
                        RuleId = "leftover.shortcut",
                        Title = "失效快捷方式 · " + Path.GetFileNameWithoutExtension(shortcut.LinkPath),
                        Subtitle = root.Label,
                        DisplayPath = shortcut.LinkPath,
                        Paths = new List<string> { shortcut.LinkPath },
                        AllowedRoots = new List<string> { root.Path },
                        Action = CleanAction.RecyclePaths,
                        Size = info.Exists ? info.Length : 0,
                        FileCount = 1,
                        LastWrite = info.Exists ? info.LastWriteTime : (DateTime?)null,
                        Risk = RiskLevel.Safe,
                        DefaultSelected = selectable,
                        RequiresAdmin = requiresAdmin,
                        IsSelectable = selectable,
                        Basis = "快捷方式指向的目标不存在。只把 .lnk 文件移入回收站，不删除目标目录或任何应用文件。",
                        EvidenceSummary = "目标不存在：" + target,
                        Glyph = "\uE71B",
                        KindLabel = "快捷方式"
                    };

                    item.Evidence.Add("快捷方式：" + shortcut.LinkPath);
                    item.Evidence.Add("目标路径不存在：" + target);
                    item.Evidence.Add("保护条件：网络路径、可移动磁盘未就绪、MSI 广告快捷方式会被跳过。");
                    if (requiresAdmin && !context.IsElevated)
                    {
                        item.Evidence.Add("当前为标准权限，此项只预览、不可选择。");
                    }

                    results.Add(item);
                    context.ReportFound(results.Count, results.Sum(x => x.Size));
                }
            }
        }

        private static void ScanEmptyStartMenuFolders(ScanContext context, List<FindingItem> results)
        {
            var roots = new List<ShortcutRoot>();
            AddShortcutRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "开始菜单", false);
            AddShortcutRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "公共开始菜单", true);

            foreach (var root in roots)
            {
                if (!Directory.Exists(root.Path))
                {
                    continue;
                }

                var dirs = new List<string>();
                CollectDirectories(root.Path, dirs, context.Token);
                foreach (string dir in dirs)
                {
                    context.Token.ThrowIfCancellationRequested();
                    try
                    {
                        if (Directory.GetFileSystemEntries(dir).Length != 0)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    bool requiresAdmin = root.RequiresAdmin;
                    bool selectable = !requiresAdmin || context.IsElevated;
                    var item = new FindingItem
                    {
                        Id = "leftover.emptymenu." + dir.ToLowerInvariant(),
                        Category = CategoryKeys.Leftover,
                        RuleId = "leftover.emptymenu",
                        Title = "空开始菜单文件夹 · " + Path.GetFileName(dir),
                        Subtitle = root.Label,
                        DisplayPath = dir,
                        Paths = new List<string> { dir },
                        AllowedRoots = new List<string> { root.Path },
                        Action = CleanAction.DeleteDirectories,
                        Size = 0,
                        FileCount = 0,
                        LastWrite = Directory.GetLastWriteTime(dir),
                        Risk = RiskLevel.Safe,
                        DefaultSelected = false,
                        RequiresAdmin = requiresAdmin,
                        IsSelectable = selectable,
                        Basis = "该开始菜单文件夹完全为空，删除它不会影响任何快捷方式；默认不勾选。",
                        EvidenceSummary = "空文件夹",
                        Glyph = "\uE8B7",
                        KindLabel = "空文件夹"
                    };

                    item.Evidence.Add("目录：" + dir);
                    item.Evidence.Add("目录中没有任何文件或子目录。");
                    results.Add(item);
                    context.ReportFound(results.Count, results.Sum(x => x.Size));
                }
            }
        }

        private static void AddShortcutRoot(List<ShortcutRoot> roots, string path, string label, bool admin)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (roots.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            roots.Add(new ShortcutRoot { Path = path, Label = label, RequiresAdmin = admin });
        }

        private static void CollectLinks(string root, List<string> links, System.Threading.CancellationToken token)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(dir);
                }
                catch
                {
                    continue;
                }

                foreach (string entry in entries)
                {
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        stack.Push(entry);
                    }
                    else if (entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        links.Add(entry);
                    }
                }
            }
        }

        private static void CollectDirectories(string root, List<string> dirs, System.Threading.CancellationToken token)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (string entry in entries)
                {
                    if (PathSafety.IsReparsePoint(entry))
                    {
                        continue;
                    }

                    dirs.Add(entry);
                    stack.Push(entry);
                }
            }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return value == null ? null : value.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static int ReadDword(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name, null);
                if (value is int)
                {
                    return (int)value;
                }

                int parsed;
                return value != null && int.TryParse(value.ToString(), out parsed) ? parsed : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string NormalizeLocation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string result = Environment.ExpandEnvironmentVariables(value.Trim());
            int comma = result.LastIndexOf(',');
            if (comma > 2 && result.Length - comma <= 3)
            {
                result = result.Substring(0, comma);
            }

            return result.Trim().Trim('"');
        }

        private static bool ContainsMsiExec(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ParseUninstaller(string command, out string path, out bool known)
        {
            path = null;
            known = false;
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            string text = Environment.ExpandEnvironmentVariables(command.Trim());
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = text.IndexOf('"', 1);
                if (end > 1)
                {
                    path = text.Substring(1, end - 1).Trim();
                }
            }
            else
            {
                int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe >= 0)
                {
                    path = text.Substring(0, exe + 4).Trim();
                }
                else
                {
                    int space = text.IndexOf(' ');
                    path = (space > 0 ? text.Substring(0, space) : text).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\\') < 0)
            {
                path = null;
                known = false;
                return;
            }

            known = true;
        }

        private sealed class RegistryLocation
        {
            public RegistryLocation(RegistryHive hive, RegistryView view, string subKey, string displayName,
                bool machine)
            {
                Hive = hive;
                View = view;
                SubKey = subKey;
                DisplayName = displayName;
                Machine = machine;
            }

            public RegistryHive Hive { get; private set; }
            public RegistryView View { get; private set; }
            public string SubKey { get; private set; }
            public string DisplayName { get; private set; }
            public bool Machine { get; private set; }
        }

        private sealed class ShortcutRoot
        {
            public string Path { get; set; }
            public string Label { get; set; }
            public bool RequiresAdmin { get; set; }
        }
    }
}
