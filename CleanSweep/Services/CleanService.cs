using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CleanSweep.Common;
using CleanSweep.Models;
using Microsoft.Win32;

namespace CleanSweep.Services
{
    public sealed class CleanService
    {
        public Task<CleanResult> CleanAsync(IList<FindingItem> items, IProgress<CleanProgress> progress,
            CancellationToken token)
        {
            return Task.Run(() => Clean(items, progress, token));
        }

        private CleanResult Clean(IList<FindingItem> items, IProgress<CleanProgress> progress,
            CancellationToken token)
        {
            var result = new CleanResult
            {
                StartedAt = DateTime.Now,
                IsElevated = NativeMethods.IsElevated(),
                BackupDir = AppPaths.BackupsDir
            };
            AppPaths.Ensure();

            int total = items == null ? 0 : items.Count;
            var notes = new List<string>();
            for (int i = 0; i < total; i++)
            {
                var item = items[i];
                if (token.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    result.Records.Add(new CleanRecord
                    {
                        Item = item,
                        Success = false,
                        Cancelled = true,
                        Error = "用户取消"
                    });
                    continue;
                }

                if (progress != null)
                {
                    progress.Report(new CleanProgress
                    {
                        Current = i,
                        Total = total,
                        CurrentPath = item.Title,
                        Percent = total == 0 ? 0 : (double)i / total * 100,
                        FreedBytes = result.FreedBytes,
                        RecycledBytes = result.RecycledBytes
                    });
                }

                var record = CleanOne(item, notes, token);
                result.Records.Add(record);
                result.FreedBytes += record.FreedBytes;
                result.RecycledBytes += record.RecycledBytes;
                result.RecycledCount += record.RecycledBytes > 0 ? record.DeletedCount : 0;
                if (record.Success)
                {
                    result.SuccessCount++;
                }
                else if (record.Partial)
                {
                    result.PartialCount++;
                }
                else if (!record.Cancelled)
                {
                    result.FailureCount++;
                }

                if (record.Cancelled)
                {
                    result.Cancelled = true;
                }

                if (progress != null)
                {
                    progress.Report(new CleanProgress
                    {
                        Current = i + 1,
                        Total = total,
                        CurrentPath = item.Title,
                        Percent = total == 0 ? 0 : (double)(i + 1) / total * 100,
                        FreedBytes = result.FreedBytes,
                        RecycledBytes = result.RecycledBytes
                    });
                }
            }

            result.FinishedAt = DateTime.Now;
            result.Duration = result.FinishedAt - result.StartedAt;
            result.Notes.AddRange(notes);
            try
            {
                LogStore.Write(result, items == null ? new List<FindingItem>() : items.ToList(), notes);
            }
            catch (Exception ex)
            {
                result.Notes.Add("写入清理日志失败：" + ex.Message);
            }

            return result;
        }

        private CleanRecord CleanOne(FindingItem item, List<string> notes, CancellationToken token)
        {
            var record = new CleanRecord { Item = item };
            try
            {
                switch (item.Action)
                {
                    case CleanAction.DeleteFiles:
                        CleanFiles(item, record, notes, token);
                        break;
                    case CleanAction.DeleteDirectories:
                        CleanDirectories(item, record, notes, token);
                        break;
                    case CleanAction.RecyclePaths:
                        CleanToRecycleBin(item, record, notes, token);
                        break;
                    case CleanAction.RegistryKey:
                        CleanRegistryKey(item, record, notes, token);
                        break;
                    case CleanAction.EmptyRecycleBin:
                        CleanRecycleBin(item, record, notes, token);
                        break;
                    default:
                        record.Error = "未知清理动作。";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                record.Cancelled = true;
                record.Error = "用户取消";
            }
            catch (Exception ex)
            {
                record.Error = ex.Message;
            }

            return record;
        }

        private void CleanFiles(FindingItem item, CleanRecord record, List<string> notes,
            CancellationToken token)
        {
            if (item.Paths == null || item.Paths.Count == 0)
            {
                record.Error = "没有可删除的文件。";
                return;
            }

            if (item.AllowedRoots == null || item.AllowedRoots.Count == 0)
            {
                record.Error = "缺少安全边界，拒绝删除。";
                return;
            }

            int deleted = 0;
            var errors = new List<string>();
            foreach (string path in item.Paths)
            {
                token.ThrowIfCancellationRequested();
                if (!PathSafety.CanDelete(path, item.AllowedRoots, false))
                {
                    continue;
                }

                try
                {
                    long size = 0;
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        size = info.Length;
                    }

                    PathSafety.TryClearReadOnly(path);
                    File.Delete(path);
                    deleted++;
                    record.FreedBytes += size;
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(path) + "：" + ShortError(ex));
                }
            }

            record.DeletedCount = deleted;
            record.Success = deleted > 0 && errors.Count == 0;
            record.Partial = deleted > 0 && errors.Count > 0;
            if (errors.Count > 0)
            {
                record.Error = errors.Count > 4
                    ? string.Join("；", errors.Take(4)) + "；另有 " + (errors.Count - 4) + " 个文件失败"
                    : string.Join("；", errors);
            }

            if (deleted > 0)
            {
                foreach (string root in item.AllowedRoots)
                {
                    try
                    {
                        FileWalker.TryDeleteEmptyDirectoryTree(root, token);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void CleanDirectories(FindingItem item, CleanRecord record, List<string> notes,
            CancellationToken token)
        {
            if (item.Paths == null || item.Paths.Count == 0)
            {
                record.Error = "没有可删除的目录。";
                return;
            }

            if (item.AllowedRoots == null || item.AllowedRoots.Count == 0)
            {
                record.Error = "缺少安全边界，拒绝删除。";
                return;
            }

            int deleted = 0;
            var errors = new List<string>();
            foreach (string path in item.Paths)
            {
                token.ThrowIfCancellationRequested();
                if (!PathSafety.CanDelete(path, item.AllowedRoots, true))
                {
                    continue;
                }

                try
                {
                    if (item.RuleId == "leftover.emptymenu" &&
                        Directory.GetFileSystemEntries(path).Length != 0)
                    {
                        errors.Add(Path.GetFileName(path) + "：目录已不再为空，已跳过");
                        continue;
                    }

                    if (item.RuleId == "system.upgrade-leftover" && !IsKnownSystemUpgradePath(path))
                    {
                        errors.Add(Path.GetFileName(path) + "：不是受支持的 Windows 升级残留路径，已跳过");
                        continue;
                    }

                    long before = item.Size;
                    try
                    {
                        before = FileWalker.EnumerateFiles(path, token).Size;
                    }
                    catch
                    {
                    }

                    try
                    {
                        FileWalker.DeleteDirectoryTree(path, token);
                    }
                    catch (UnauthorizedAccessException) when (item.RequiresTakeOwnership && NativeMethods.IsElevated())
                    {
                        notes.Add("目录权限不足，正在修复 ACL：" + path);
                        if (TakeOwnership(path, token))
                        {
                            FileWalker.DeleteDirectoryTree(path, token);
                        }
                        else
                        {
                            throw;
                        }
                    }

                    deleted++;
                    if (!Directory.Exists(path))
                    {
                        record.FreedBytes += before;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(path) + "：" + ShortError(ex));
                }
            }

            record.DeletedCount = deleted;
            record.Success = deleted > 0 && errors.Count == 0;
            record.Partial = deleted > 0 && errors.Count > 0;
            if (errors.Count > 0)
            {
                record.Error = string.Join("；", errors.Take(4));
            }
        }

        private void CleanToRecycleBin(FindingItem item, CleanRecord record, List<string> notes,
            CancellationToken token)
        {
            if (item.Paths == null || item.Paths.Count == 0)
            {
                record.Error = "没有可移动的文件。";
                return;
            }

            if (item.AllowedRoots == null || item.AllowedRoots.Count == 0)
            {
                record.Error = "缺少安全边界，拒绝操作。";
                return;
            }

            int moved = 0;
            var errors = new List<string>();
            foreach (string path in item.Paths)
            {
                token.ThrowIfCancellationRequested();
                if (!PathSafety.CanDelete(path, item.AllowedRoots, false))
                {
                    continue;
                }

                long size = 0;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        size = info.Length;
                    }
                }
                catch
                {
                }

                string error;
                if (NativeMethods.MoveToRecycleBin(path, out error))
                {
                    moved++;
                    record.RecycledBytes += size;
                }
                else
                {
                    errors.Add(Path.GetFileName(path) + "：" + error);
                }
            }

            record.DeletedCount = moved;
            record.Success = moved > 0 && errors.Count == 0;
            record.Partial = moved > 0 && errors.Count > 0;
            if (errors.Count > 0)
            {
                record.Error = string.Join("；", errors.Take(4));
            }
        }

        private void CleanRegistryKey(FindingItem item, CleanRecord record, List<string> notes,
            CancellationToken token)
        {
            var target = item.Registry;
            if (target == null || !target.IsSupportedUninstallKey)
            {
                record.Error = "注册表目标不在允许的卸载登记范围内。";
                return;
            }

            if (string.IsNullOrWhiteSpace(target.SubKey) ||
                target.SubKey.StartsWith("\\", StringComparison.Ordinal) ||
                target.SubKey.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                record.Error = "注册表目标格式不安全。";
                return;
            }

            string backup = RegistryBackup.Export(target, item.Title, token);
            if (string.IsNullOrWhiteSpace(backup))
            {
                record.Error = "无法导出注册表备份，已取消删除。";
                return;
            }

            record.BackupPath = backup;
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(target.Hive, target.View))
                using (var parent = baseKey.OpenSubKey(GetParentPath(target.SubKey), true))
                {
                    if (parent == null)
                    {
                        record.Error = "找不到注册表父项。";
                        return;
                    }

                    string leaf = GetLeaf(target.SubKey);
                    parent.DeleteSubKeyTree(leaf, false);
                }

                record.DeletedCount = 1;
                record.Success = true;
                notes.Add("已备份注册表项：" + backup);
            }
            catch (Exception ex)
            {
                record.Error = "删除注册表项失败：" + ShortError(ex) + "；备份位于 " + backup;
            }
        }

        private void CleanRecycleBin(FindingItem item, CleanRecord record, List<string> notes,
            CancellationToken token)
        {
            string error;
            if (NativeMethods.EmptyRecycleBin(out error))
            {
                record.DeletedCount = item.FileCount;
                record.FreedBytes = item.Size;
                record.Success = true;
            }
            else
            {
                record.Error = error;
            }
        }

        private bool TakeOwnership(string path, CancellationToken token)
        {
            try
            {
                string arguments = "/f \"" + path + "\" /r /d y";
                var takeown = ProcessRunner.Run("takeown.exe", arguments, 300000, token);
                if (!takeown.Success)
                {
                    return false;
                }

                string aclArgs = "\"" + path + "\" /grant *S-1-5-32-544:F /t /c /q";
                var icacls = ProcessRunner.Run("icacls.exe", aclArgs, 300000, token);
                return icacls.Success;
            }
            catch
            {
                return false;
            }
        }

        private static string GetParentPath(string subKey)
        {
            int index = subKey.LastIndexOf('\\');
            return index <= 0 ? subKey : subKey.Substring(0, index);
        }

        private static string GetLeaf(string subKey)
        {
            int index = subKey.LastIndexOf('\\');
            return index < 0 ? subKey : subKey.Substring(index + 1);
        }

        private static bool IsKnownSystemUpgradePath(string path)
        {
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string name = Path.GetFileName(full);
                string parent = Path.GetDirectoryName(full);
                string root = Path.GetPathRoot(full);
                if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(name, "Windows.old", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string[] allowed = { "$Windows.~BT", "$Windows.~WS", "$WinREAgent", "$GetCurrent" };
                return allowed.Any(x => string.Equals(name, x, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static string ShortError(Exception ex)
        {
            string text = ex.Message;
            if (text.Length > 120)
            {
                text = text.Substring(0, 120) + "…";
            }

            return text;
        }
    }
}
