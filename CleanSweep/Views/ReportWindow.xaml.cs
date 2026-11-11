using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CleanSweep.Common;
using CleanSweep.Models;
using CleanSweep.Services;

namespace CleanSweep.Views
{
    public partial class ReportWindow : Window
    {
        public ReportWindow(CleanResult result)
        {
            InitializeComponent();
            DataContext = this;

            Rows = new ObservableCollection<ReportRow>();
            foreach (var record in result.Records.Take(200))
            {
                Rows.Add(new ReportRow(record));
            }

            FreedText = Format.Bytes(result.FreedBytes);
            RecycledText = Format.Bytes(result.RecycledBytes);
            HasFailures = result.FailureCount > 0 || result.PartialCount > 0;
            if (result.FailureCount > 0 && result.PartialCount > 0)
            {
                FailureText = "有 " + result.PartialCount + " 项部分完成，" + result.FailureCount +
                              " 项未能完成。常见原因是文件被占用、权限不足或目标已不存在；未完成的项目已保留在扫描结果中。";
            }
            else if (result.FailureCount > 0)
            {
                FailureText = "有 " + result.FailureCount +
                              " 项未能完成，可能是文件被占用、权限不足或目标已不存在。失败项已保留在扫描结果中，可查看原因后重试。";
            }
            else if (result.PartialCount > 0)
            {
                FailureText = "有 " + result.PartialCount +
                              " 项部分完成：可删除的内容已经处理，少量文件因被占用或权限不足而保留。详细原因见下方。";
            }
            else
            {
                FailureText = string.Empty;
            }

            ResultText = result.Cancelled
                ? "已取消"
                : BuildResultText(result);
            DurationText = "用时 " + Format.Duration(result.Duration);
            LogPath = string.IsNullOrEmpty(result.LogPath) ? "日志写入失败，请查看数据目录。" : result.LogPath;
            BackupDir = result.BackupDir;
            MoreText = result.Records.Count > 200
                ? "仅显示前 200 条明细；其余 " + (result.Records.Count - 200) + " 条已写入完整日志。"
                : string.Empty;

            HeaderTitle = result.Cancelled
                ? "清理已取消"
                : result.FailureCount > 0
                    ? "清理完成，部分项目未完成"
                    : result.PartialCount > 0
                        ? "清理完成，部分内容保留"
                        : "清理完成";
            HeaderSubtitle = "已直接释放 " + FreedText +
                             (result.RecycledBytes > 0 ? " · 另有 " + RecycledText + " 移至回收站" : string.Empty);
        }

        public ObservableCollection<ReportRow> Rows { get; private set; }
        public string FreedText { get; private set; }
        public string RecycledText { get; private set; }
        public string ResultText { get; private set; }
        public string DurationText { get; private set; }
        public string LogPath { get; private set; }
        public string BackupDir { get; private set; }
        public bool HasFailures { get; private set; }
        public string FailureText { get; private set; }
        public string MoreText { get; private set; }
        public string HeaderTitle { get; private set; }
        public string HeaderSubtitle { get; private set; }

        private static string BuildResultText(CleanResult result)
        {
            if (result.FailureCount > 0 || result.PartialCount > 0)
            {
                string text = "成功 " + result.SuccessCount;
                if (result.PartialCount > 0) text += " · 部分 " + result.PartialCount;
                if (result.FailureCount > 0) text += " · 失败 " + result.FailureCount;
                return text;
            }

            return result.SuccessCount + " 项成功";
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(LogPath) && File.Exists(LogPath))
                {
                    Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
                }
                else
                {
                    Directory.CreateDirectory(AppPaths.LogsDir);
                    Process.Start("explorer.exe", "\"" + AppPaths.LogsDir + "\"");
                }
            }
            catch
            {
            }
        }

        private void OpenBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(string.IsNullOrEmpty(BackupDir) ? AppPaths.BackupsDir : BackupDir);
                Process.Start("explorer.exe", "\"" +
                    (string.IsNullOrEmpty(BackupDir) ? AppPaths.BackupsDir : BackupDir) + "\"");
            }
            catch
            {
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public sealed class ReportRow
    {
        private readonly CleanRecord _record;

        public ReportRow(CleanRecord record)
        {
            _record = record;
            var item = record.Item;
            Title = item.Title;
            Path = item.DisplayPath;
            Basis = item.Basis;
            Error = record.Error;
            BackupText = string.IsNullOrEmpty(record.BackupPath) ? string.Empty : "备份：" + record.BackupPath;
            StatusText = record.Cancelled ? "已取消" : record.Success ? "已清理" :
                record.Partial ? "部分完成" : "失败";
            StatusBrush = record.Cancelled
                ? new SolidColorBrush(Color.FromRgb(138, 155, 148))
                : record.Success
                    ? new SolidColorBrush(Color.FromRgb(22, 163, 109))
                    : record.Partial
                        ? new SolidColorBrush(Color.FromRgb(176, 111, 25))
                        : new SolidColorBrush(Color.FromRgb(183, 74, 74));
            if (record.FreedBytes > 0)
            {
                SizeText = "释放 " + Format.Bytes(record.FreedBytes);
            }
            else if (record.RecycledBytes > 0)
            {
                SizeText = "回收站 " + Format.Bytes(record.RecycledBytes);
            }
            else
            {
                SizeText = string.Empty;
            }
        }

        public string Title { get; private set; }
        public string Path { get; private set; }
        public string Basis { get; private set; }
        public string Error { get; private set; }
        public string BackupText { get; private set; }
        public string StatusText { get; private set; }
        public Brush StatusBrush { get; private set; }
        public string SizeText { get; private set; }
    }
}
