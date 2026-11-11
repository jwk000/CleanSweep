using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using CleanSweep.Common;
using CleanSweep.Services;

namespace CleanSweep.Models
{
    [DataContract]
    public sealed class CleanupLogDocument
    {
        [DataMember] public string App { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public string StartedAt { get; set; }
        [DataMember] public string FinishedAt { get; set; }
        [DataMember] public string User { get; set; }
        [DataMember] public string Machine { get; set; }
        [DataMember] public bool Elevated { get; set; }
        [DataMember] public long FreedBytes { get; set; }
        [DataMember] public long RecycledBytes { get; set; }
        [DataMember] public int SuccessCount { get; set; }
        [DataMember] public int PartialCount { get; set; }
        [DataMember] public int FailureCount { get; set; }
        [DataMember] public int RecycledCount { get; set; }
        [DataMember] public bool Cancelled { get; set; }
        [DataMember] public List<LogEntry> Entries { get; set; }
        [DataMember] public List<string> Notes { get; set; }

        public CleanupLogDocument()
        {
            Entries = new List<LogEntry>();
            Notes = new List<string>();
        }
    }

    [DataContract]
    public sealed class LogEntry
    {
        [DataMember] public string Time { get; set; }
        [DataMember] public string Category { get; set; }
        [DataMember] public string RuleId { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public string Action { get; set; }
        [DataMember] public string Risk { get; set; }
        [DataMember] public long Size { get; set; }
        [DataMember] public int FileCount { get; set; }
        [DataMember] public string Basis { get; set; }
        [DataMember] public string DisplayPath { get; set; }
        [DataMember] public string Hash { get; set; }
        [DataMember] public string KeepPath { get; set; }
        [DataMember] public List<string> Paths { get; set; }
        [DataMember] public List<string> Evidence { get; set; }
        [DataMember] public bool Success { get; set; }
        [DataMember] public bool Partial { get; set; }
        [DataMember] public string Error { get; set; }
        [DataMember] public string BackupPath { get; set; }
        [DataMember] public long FreedBytes { get; set; }
        [DataMember] public long RecycledBytes { get; set; }
        [DataMember] public int DeletedCount { get; set; }
        [DataMember] public bool Cancelled { get; set; }

        public LogEntry()
        {
            Paths = new List<string>();
            Evidence = new List<string>();
        }

        public string StatusText
        {
            get
            {
                if (Cancelled) return "已取消";
                if (Success) return "已清理";
                return Partial ? "部分完成" : "失败";
            }
        }

        public string SizeText
        {
            get { return FreedBytes > 0 ? Format.Bytes(FreedBytes) : RecycledBytes > 0 ? Format.Bytes(RecycledBytes) + "（回收站）" : "—"; }
        }

        public string ResultBrush
        {
            get
            {
                if (Cancelled) return "#8A9B94";
                if (Success) return "#16A36D";
                return Partial ? "#B06F19" : "#C95252";
            }
        }
    }

    public sealed class LogSummary
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public DateTime StartedAt { get; set; }
        public long FreedBytes { get; set; }
        public long RecycledBytes { get; set; }
        public int SuccessCount { get; set; }
        public int PartialCount { get; set; }
        public int FailureCount { get; set; }
        public bool Cancelled { get; set; }

        public string DateText
        {
            get { return StartedAt.ToString("yyyy-MM-dd HH:mm"); }
        }

        public string FreedText
        {
            get { return Format.Bytes(FreedBytes); }
        }

        public string RecycledText
        {
            get { return Format.Bytes(RecycledBytes); }
        }

        public string SummaryText
        {
            get
            {
                if (Cancelled)
                {
                    return "已取消 · " + Format.Count(SuccessCount, "项");
                }

                if (FailureCount > 0)
                {
                    return Format.Count(SuccessCount, "项") + "成功 · " +
                           (PartialCount > 0 ? Format.Count(PartialCount, "项") + "部分 · " : string.Empty) +
                           Format.Count(FailureCount, "项") + "失败";
                }

                if (PartialCount > 0)
                {
                    return Format.Count(SuccessCount, "项") + "成功 · " +
                           Format.Count(PartialCount, "项") + "部分完成";
                }

                return Format.Count(SuccessCount, "项") + "已处理";
            }
        }
    }

    public static class LogStore
    {
        private static readonly object Sync = new object();

        public static string Write(CleanResult result, List<FindingItem> items, List<string> notes)
        {
            lock (Sync)
            {
                AppPaths.Ensure();
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string jsonPath = Path.Combine(AppPaths.LogsDir, "cleanup-" + stamp + ".json");
                string textPath = Path.Combine(AppPaths.LogsDir, "cleanup-" + stamp + ".txt");

                var document = new CleanupLogDocument
                {
                    App = "净界 CleanSweep",
                    Version = AppInfo.Version,
                    StartedAt = result.StartedAt.ToString("O"),
                    FinishedAt = result.FinishedAt.ToString("O"),
                    User = Environment.UserName,
                    Machine = Environment.MachineName,
                    Elevated = result.IsElevated,
                    FreedBytes = result.FreedBytes,
                    RecycledBytes = result.RecycledBytes,
                    SuccessCount = result.SuccessCount,
                    PartialCount = result.PartialCount,
                    FailureCount = result.FailureCount,
                    RecycledCount = result.RecycledCount,
                    Cancelled = result.Cancelled,
                    Notes = new List<string>(notes ?? new List<string>())
                };

                foreach (var record in result.Records)
                {
                    var item = record.Item;
                    document.Entries.Add(new LogEntry
                    {
                        Time = DateTime.Now.ToString("O"),
                        Category = item.Category,
                        RuleId = item.RuleId,
                        Title = item.Title,
                        Action = item.MethodText,
                        Risk = item.RiskText,
                        Size = item.Size,
                        FileCount = item.FileCount,
                        Basis = item.Basis,
                        DisplayPath = item.DisplayPath,
                        Hash = item.Hash,
                        KeepPath = item.KeepPath,
                        Paths = item.Paths == null ? new List<string>() : new List<string>(item.Paths),
                        Evidence = item.Evidence == null ? new List<string>() : new List<string>(item.Evidence),
                        Success = record.Success,
                        Partial = record.Partial,
                        Error = record.Error,
                        BackupPath = record.BackupPath,
                        FreedBytes = record.FreedBytes,
                        RecycledBytes = record.RecycledBytes,
                        DeletedCount = record.DeletedCount,
                        Cancelled = record.Cancelled
                    });
                }

                var serializer = new DataContractJsonSerializer(typeof(CleanupLogDocument));
                using (var stream = File.Create(jsonPath))
                {
                    serializer.WriteObject(stream, document);
                }

                File.WriteAllText(textPath, BuildText(document), new UTF8Encoding(true));
                result.LogPath = jsonPath;
                result.TextLogPath = textPath;
                return jsonPath;
            }
        }

        public static List<LogSummary> ListRecent(int max)
        {
            var list = new List<LogSummary>();
            try
            {
                AppPaths.Ensure();
                var files = new List<FileInfo>(new DirectoryInfo(AppPaths.LogsDir).GetFiles("cleanup-*.json"));
                files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                foreach (var file in files)
                {
                    if (list.Count >= max)
                    {
                        break;
                    }

                    try
                    {
                        using (var stream = File.OpenRead(file.FullName))
                        {
                            var serializer = new DataContractJsonSerializer(typeof(CleanupLogDocument));
                            var doc = serializer.ReadObject(stream) as CleanupLogDocument;
                            if (doc == null)
                            {
                                continue;
                            }

                            DateTime started;
                            DateTime.TryParse(doc.StartedAt, out started);
                            list.Add(new LogSummary
                            {
                                FilePath = file.FullName,
                                FileName = file.Name,
                                StartedAt = started == default(DateTime) ? file.LastWriteTime : started,
                                FreedBytes = doc.FreedBytes,
                                RecycledBytes = doc.RecycledBytes,
                                SuccessCount = doc.SuccessCount,
                                PartialCount = doc.PartialCount,
                                FailureCount = doc.FailureCount,
                                Cancelled = doc.Cancelled
                            });
                        }
                    }
                    catch
                    {
                        // A damaged log should not stop the list from loading.
                    }
                }
            }
            catch
            {
                // No data directory yet.
            }

            return list;
        }

        public static CleanupLogDocument Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(CleanupLogDocument));
                return serializer.ReadObject(stream) as CleanupLogDocument;
            }
        }

        private static string BuildText(CleanupLogDocument doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("净界 CleanSweep 清理报告");
            sb.AppendLine("============================================================");
            sb.AppendLine("开始时间: " + doc.StartedAt);
            sb.AppendLine("结束时间: " + doc.FinishedAt);
            sb.AppendLine("运行账户: " + doc.User + " @ " + doc.Machine + (doc.Elevated ? "（管理员）" : "（标准权限）"));
            sb.AppendLine("直接释放: " + Format.Bytes(doc.FreedBytes));
            sb.AppendLine("移入回收站: " + Format.Bytes(doc.RecycledBytes));
            sb.AppendLine("成功: " + doc.SuccessCount + "  部分完成: " + doc.PartialCount +
                          "  失败: " + doc.FailureCount +
                          (doc.Cancelled ? "  已取消" : string.Empty));
            sb.AppendLine();

            foreach (var entry in doc.Entries)
            {
                string tag = entry.Success ? "[成功] " : entry.Cancelled ? "[取消] " :
                    entry.Partial ? "[部分] " : "[失败] ";
                sb.AppendLine(tag + entry.Category + " / " + entry.Title);
                sb.AppendLine("  操作: " + entry.Action + "    风险: " + entry.Risk);
                if (entry.FreedBytes > 0)
                {
                    sb.AppendLine("  释放: " + Format.Bytes(entry.FreedBytes));
                }
                if (entry.RecycledBytes > 0)
                {
                    sb.AppendLine("  移至回收站: " + Format.Bytes(entry.RecycledBytes));
                }
                sb.AppendLine("  依据: " + entry.Basis);
                if (!string.IsNullOrEmpty(entry.Hash))
                {
                    sb.AppendLine("  SHA-256: " + entry.Hash);
                }
                if (!string.IsNullOrEmpty(entry.KeepPath))
                {
                    sb.AppendLine("  保留副本: " + entry.KeepPath);
                }
                if (!string.IsNullOrEmpty(entry.BackupPath))
                {
                    sb.AppendLine("  注册表备份: " + entry.BackupPath);
                }
                if (!string.IsNullOrEmpty(entry.Error))
                {
                    sb.AppendLine("  错误: " + entry.Error);
                }
                sb.AppendLine("  路径:");
                if (entry.Paths != null)
                {
                    foreach (var path in entry.Paths)
                    {
                        sb.AppendLine("    " + path);
                    }
                }
                sb.AppendLine();
            }

            if (doc.Notes != null && doc.Notes.Count > 0)
            {
                sb.AppendLine("扫描备注:");
                foreach (var note in doc.Notes)
                {
                    sb.AppendLine("  - " + note);
                }
            }

            return sb.ToString();
        }
    }

    public static class AppInfo
    {
        public static string Version
        {
            get
            {
                var version = typeof(AppInfo).Assembly.GetName().Version;
                return version == null ? "1.0.0" : version.ToString(3);
            }
        }
    }
}
