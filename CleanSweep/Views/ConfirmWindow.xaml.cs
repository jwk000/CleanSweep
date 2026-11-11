using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CleanSweep.Common;
using CleanSweep.Models;

namespace CleanSweep.Views
{
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow(IList<FindingItem> items, long totalBytes, long recycleBytes, int highRiskCount)
        {
            InitializeComponent();
            DataContext = this;

            Rows = new ObservableCollection<ConfirmRow>();
            var metadata = new Dictionary<string, Tuple<string, string>>
            {
                { CategoryKeys.Temp, Tuple.Create("临时文件", "#16A36D") },
                { CategoryKeys.Leftover, Tuple.Create("软件残留", "#C77C18") },
                { CategoryKeys.Duplicates, Tuple.Create("重复文件", "#3B72C9") },
                { CategoryKeys.System, Tuple.Create("系统冗余", "#B05252") }
            };

            foreach (var group in items.GroupBy(x => x.Category))
            {
                var meta = metadata.ContainsKey(group.Key)
                    ? metadata[group.Key]
                    : Tuple.Create(group.Key, "#6C8079");
                Rows.Add(new ConfirmRow
                {
                    Name = meta.Item1,
                    Accent = meta.Item2,
                    Count = group.Count(),
                    Size = group.Sum(x => x.Size)
                });
            }

            ItemCount = items.Count;
            TotalBytes = totalBytes;
            RecycleBytes = recycleBytes;
            HasHighRisk = highRiskCount > 0;
            var highRiskTitles = items.Where(x => x.Risk == RiskLevel.High)
                .Select(x => x.Title)
                .Distinct()
                .Take(4)
                .ToList();
            HighRiskText = HasHighRisk
                ? "本次选中 " + highRiskCount + " 个高风险项目" +
                  (highRiskTitles.Count > 0 ? "：" + string.Join("、", highRiskTitles) : string.Empty) +
                  "。这些内容可能用于系统回滚、恢复或故障排查；删除后无法恢复。"
                : string.Empty;
            ConfirmButton.IsEnabled = !HasHighRisk;
        }

        public ObservableCollection<ConfirmRow> Rows { get; private set; }
        public int ItemCount { get; private set; }
        public long TotalBytes { get; private set; }
        public long RecycleBytes { get; private set; }
        public bool HasHighRisk { get; private set; }
        public string HighRiskText { get; private set; }

        public string ItemCountText
        {
            get { return "共 " + Format.Count(ItemCount, "项") + "，合计 " + Format.Bytes(TotalBytes); }
        }

        public string TotalText
        {
            get { return Format.Bytes(TotalBytes); }
        }

        public string RecycleText
        {
            get
            {
                return RecycleBytes > 0
                    ? "其中 " + Format.Bytes(RecycleBytes) + " 将移至回收站"
                    : "全部为直接删除";
            }
        }

        public string MethodSummary
        {
            get { return "临时文件永久删除；重复文件与快捷方式进回收站；注册表先备份"; }
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

        private void RiskCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (ConfirmButton != null)
            {
                ConfirmButton.IsEnabled = !HasHighRisk || (RiskCheck != null && RiskCheck.IsChecked == true);
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (HasHighRisk && RiskCheck.IsChecked != true)
            {
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public sealed class ConfirmRow
    {
        public string Name { get; set; }
        public string Accent { get; set; }
        public int Count { get; set; }
        public long Size { get; set; }

        public string CountText
        {
            get { return Format.Count(Count, "项"); }
        }

        public string SizeText
        {
            get { return Format.Bytes(Size); }
        }
    }
}
