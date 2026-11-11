using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CleanSweep.Common;
using Microsoft.Win32;

namespace CleanSweep.Models
{
    public enum RiskLevel
    {
        Safe = 0,
        Medium = 1,
        High = 2
    }

    public enum CleanAction
    {
        DeleteFiles,
        DeleteDirectories,
        RecyclePaths,
        RegistryKey,
        EmptyRecycleBin
    }

    public static class CategoryKeys
    {
        public const string Temp = "temp";
        public const string Leftover = "leftover";
        public const string Duplicates = "duplicates";
        public const string System = "system";
    }

    public sealed class RegistryTarget
    {
        public RegistryHive Hive { get; set; }
        public RegistryView View { get; set; }
        public string SubKey { get; set; }
        public string FullPath { get; set; }

        public bool IsSupportedUninstallKey
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SubKey))
                {
                    return false;
                }

                return SubKey.IndexOf(@"Microsoft\Windows\CurrentVersion\Uninstall",
                           StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }

    public sealed class FindingItem : ObservableObject
    {
        private bool _isSelected;
        private bool _isExpanded;
        private bool _isKeep;
        private bool _isSelectable = true;

        public string Id { get; set; }
        public string Category { get; set; }
        public string RuleId { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string DisplayPath { get; set; }
        public List<string> Paths { get; set; } = new List<string>();
        public List<string> AllowedRoots { get; set; } = new List<string>();
        public RegistryTarget Registry { get; set; }
        public CleanAction Action { get; set; }
        public long Size { get; set; }
        public int FileCount { get; set; }
        public DateTime? LastWrite { get; set; }
        public RiskLevel Risk { get; set; }
        public bool DefaultSelected { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool RequiresTakeOwnership { get; set; }
        public string Basis { get; set; }
        public string EvidenceSummary { get; set; }
        public string Glyph { get; set; }
        public string KindLabel { get; set; }
        public string GroupId { get; set; }
        public string Hash { get; set; }
        public string KeepPath { get; set; }
        public ObservableCollection<string> Evidence { get; set; } = new ObservableCollection<string>();

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    OnPropertyChanged(nameof(SelectionText));
                }
            }
        }

        public bool IsSelectable
        {
            get { return _isSelectable; }
            set { SetProperty(ref _isSelectable, value); }
        }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { SetProperty(ref _isExpanded, value); }
        }

        public bool IsKeep
        {
            get { return _isKeep; }
            set
            {
                if (SetProperty(ref _isKeep, value))
                {
                    OnPropertyChanged(nameof(KeepLabel));
                    OnPropertyChanged(nameof(SelectionText));
                }
            }
        }

        public bool IsDuplicate
        {
            get { return !string.IsNullOrEmpty(GroupId); }
        }

        public bool IsHighRisk
        {
            get { return Risk == RiskLevel.High; }
        }

        public string SizeText
        {
            get { return Action == CleanAction.RegistryKey || Action == CleanAction.EmptyRecycleBin && Size <= 0
                ? "—"
                : Format.Bytes(Size); }
        }

        public string CountText
        {
            get
            {
                if (Action == CleanAction.RegistryKey)
                {
                    return "注册表项";
                }

                if (Action == CleanAction.EmptyRecycleBin)
                {
                    return FileCount > 0 ? Format.Count(FileCount, "个项目") : "回收站";
                }

                if (FileCount > 1)
                {
                    return Format.Count(FileCount, "个文件");
                }

                if (FileCount == 1)
                {
                    return "1 个文件";
                }

                return KindLabel ?? string.Empty;
            }
        }

        public string AgeText
        {
            get { return LastWrite.HasValue ? "修改于 " + Format.Age(LastWrite) : string.Empty; }
        }

        public string RiskText
        {
            get
            {
                switch (Risk)
                {
                    case RiskLevel.Safe:
                        return "安全";
                    case RiskLevel.Medium:
                        return "谨慎";
                    default:
                        return "高风险";
                }
            }
        }

        public string MethodText
        {
            get
            {
                switch (Action)
                {
                    case CleanAction.RecyclePaths:
                        return "移至回收站";
                    case CleanAction.RegistryKey:
                        return "备份后删除注册表项";
                    case CleanAction.EmptyRecycleBin:
                        return "清空回收站";
                    case CleanAction.DeleteDirectories:
                        return "永久删除目录";
                    default:
                        return "永久删除";
                }
            }
        }

        public string SelectionText
        {
            get
            {
                if (IsDuplicate && IsKeep)
                {
                    return "保留";
                }

                return IsSelected ? "清理" : "跳过";
            }
        }

        public string KeepLabel
        {
            get { return IsKeep ? "保留此份" : "设为保留"; }
        }

        public string PathPreview
        {
            get
            {
                if (Paths == null || Paths.Count == 0)
                {
                    return DisplayPath ?? string.Empty;
                }

                int take = Math.Min(8, Paths.Count);
                return string.Join(Environment.NewLine, Paths.Take(take));
            }
        }

        public string PathCountText
        {
            get
            {
                int count = Paths == null ? 0 : Paths.Count;
                return count > 8 ? "查看全部 " + Format.Count(count, "个路径") : string.Empty;
            }
        }

        public string AdminText
        {
            get
            {
                if (!RequiresAdmin)
                {
                    return string.Empty;
                }

                return IsSelectable ? "系统级" : "需管理员";
            }
        }

        public void AddEvidence(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                Evidence.Add(text);
            }
        }
    }
}
