using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanSweep.Models;

namespace CleanSweep.Services
{
    public sealed class ScanService
    {
        public Task<ScanResult> ScanAsync(AppSettings settings, IProgress<ScanProgress> progress,
            CancellationToken token)
        {
            return Task.Run(() => Scan(settings, progress, token));
        }

        private ScanResult Scan(AppSettings settings, IProgress<ScanProgress> progress, CancellationToken token)
        {
            var result = new ScanResult { StartedAt = DateTime.Now };
            var stopwatch = Stopwatch.StartNew();
            var context = new ScanContext
            {
                Settings = settings ?? new AppSettings(),
                IsElevated = NativeMethods.IsElevated(),
                Token = token
            };

            int foundItems = 0;
            long foundBytes = 0;
            Action<string, string, double> report = (stage, detail, percent) =>
            {
                if (progress != null)
                {
                    progress.Report(new ScanProgress
                    {
                        Stage = stage,
                        Detail = detail,
                        Percent = Math.Max(0, Math.Min(100, percent)),
                        FoundItems = foundItems,
                        FoundBytes = foundBytes
                    });
                }
            };

            try
            {
                report("临时文件与缓存", "正在检查用户临时目录…", 1);
                context.Status = text => report("临时文件与缓存", text, 4);
                context.Found = (items, bytes) =>
                {
                    foundItems = items;
                    foundBytes = bytes;
                    report("临时文件与缓存", "已发现 " + items + " 项", 12);
                };
                var tempItems = TempScanner.Scan(context);
                result.Items.AddRange(tempItems);
                foundItems = result.Items.Count;
                foundBytes = Sum(result);
                report("临时文件与缓存", "完成，共 " + tempItems.Count + " 项", 32);

                token.ThrowIfCancellationRequested();
                report("软件残留", "正在检查卸载登记与快捷方式…", 34);
                context.Status = text => report("软件残留", text, 38);
                context.Found = (items, bytes) =>
                {
                    foundItems = result.Items.Count + items;
                    foundBytes = Sum(result);
                    report("软件残留", "已发现 " + items + " 项残留", 46);
                };
                var leftoverItems = LeftoverScanner.Scan(context);
                result.Items.AddRange(leftoverItems);
                foundItems = result.Items.Count;
                foundBytes = Sum(result);
                report("软件残留", "完成，共 " + leftoverItems.Count + " 项", 55);

                token.ThrowIfCancellationRequested();
                report("系统冗余", "正在评估升级备份与回收站…", 57);
                context.Status = text => report("系统冗余", text, 60);
                context.Found = (items, bytes) =>
                {
                    foundItems = result.Items.Count + items;
                    foundBytes = Sum(result);
                    report("系统冗余", "已发现 " + items + " 项", 66);
                };
                var systemItems = SystemScanner.Scan(context);
                result.Items.AddRange(systemItems);
                foundItems = result.Items.Count;
                foundBytes = Sum(result);
                report("系统冗余", "完成，共 " + systemItems.Count + " 项", 70);

                token.ThrowIfCancellationRequested();
                report("重复文件", "正在收集候选文件…", 72);
                context.Status = text => report("重复文件", text, 76);
                context.SubProgress = value =>
                {
                    report("重复文件", "正在按 SHA-256 比对内容…", 76 + Math.Max(0, Math.Min(22, value * 22)));
                };
                context.Found = (groups, bytes) =>
                {
                    foundItems = result.Items.Count + groups;
                    foundBytes = Sum(result) + bytes;
                    report("重复文件", "已确认 " + groups + " 组重复文件", 96);
                };
                var duplicateGroups = DuplicateScanner.Scan(context);
                result.DuplicateGroups.AddRange(duplicateGroups);
                foundItems = result.Items.Count + duplicateGroups.Count;
                foundBytes = Sum(result) + duplicateGroups.Sum(g => g.ReclaimableBytes);
                report("重复文件", "完成，共 " + duplicateGroups.Count + " 组", 99);

                result.Notes.AddRange(context.Notes);
                result.FinishedAt = DateTime.Now;
                result.Duration = stopwatch.Elapsed;
                report("扫描完成", "共发现 " + foundItems + " 项可清理内容", 100);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Notes.Add("扫描过程中出现错误：" + ex.Message);
                result.FinishedAt = DateTime.Now;
                result.Duration = stopwatch.Elapsed;
                return result;
            }
        }

        private static long Sum(ScanResult result)
        {
            long total = 0;
            foreach (var item in result.Items)
            {
                total += item.Size;
            }

            foreach (var group in result.DuplicateGroups)
            {
                total += group.ReclaimableBytes;
            }

            return total;
        }
    }
}
