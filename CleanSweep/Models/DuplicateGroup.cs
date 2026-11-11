using System;
using System.Collections.ObjectModel;
using System.Linq;
using CleanSweep.Common;

namespace CleanSweep.Models
{
    public sealed class DuplicateGroup : ObservableObject
    {
        private bool _isSelected;
        private FindingItem _keepItem;
        private bool _suppressSelection;

        public DuplicateGroup()
        {
            Files = new ObservableCollection<FindingItem>();
        }

        public string Id { get; set; }
        public string FileName { get; set; }
        public string Hash { get; set; }
        public long Size { get; set; }
        public ObservableCollection<FindingItem> Files { get; private set; }

        public FindingItem KeepItem
        {
            get { return _keepItem; }
            private set
            {
                if (SetProperty(ref _keepItem, value))
                {
                    OnPropertyChanged(nameof(KeepPath));
                }
            }
        }

        public string KeepPath
        {
            get { return KeepItem == null ? string.Empty : KeepItem.DisplayPath; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    ApplySelection();
                    OnPropertyChanged(nameof(SelectionText));
                }
            }
        }

        public int FileCount
        {
            get { return Files.Count; }
        }

        public long ReclaimableBytes
        {
            get { return Size * Math.Max(0, Files.Count - 1); }
        }

        public long SelectedBytes
        {
            get
            {
                return Files.Where(f => !f.IsKeep && f.IsSelected).Sum(f => f.Size);
            }
        }

        public int SelectedCount
        {
            get { return Files.Count(f => !f.IsKeep && f.IsSelected); }
        }

        public string SizeText
        {
            get { return Format.Bytes(Size); }
        }

        public string ReclaimableText
        {
            get { return Format.Bytes(ReclaimableBytes); }
        }

        public string SummaryText
        {
            get { return Format.Count(Files.Count, "个文件") + " · 可释放 " + ReclaimableText; }
        }

        public string HashText
        {
            get
            {
                if (string.IsNullOrEmpty(Hash) || Hash.Length < 16)
                {
                    return Hash ?? string.Empty;
                }

                return Hash.Substring(0, 16) + "…";
            }
        }

        public string SelectionText
        {
            get { return IsSelected ? "已选择清理" : "本组跳过"; }
        }

        public void Initialize()
        {
            if (Files.Count == 0)
            {
                return;
            }

            var ordered = Files
                .OrderBy(f => f.LastWrite ?? DateTime.MaxValue)
                .ThenBy(f => f.DisplayPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _suppressSelection = true;
            foreach (var file in Files)
            {
                file.IsKeep = false;
                file.IsSelectable = true;
            }

            KeepItem = ordered[0];
            if (KeepItem != null)
            {
                KeepItem.IsKeep = true;
                KeepItem.IsSelected = false;
            }

            _suppressSelection = false;
            ApplySelection();
        }

        public void SetKeeper(FindingItem item)
        {
            if (item == null || !Files.Contains(item))
            {
                return;
            }

            _suppressSelection = true;
            KeepItem = item;
            foreach (var file in Files)
            {
                file.IsKeep = ReferenceEquals(file, item);
            }

            item.IsSelected = false;
            _suppressSelection = false;
            foreach (var file in Files)
            {
                file.KeepPath = KeepPath;
            }

            ApplySelection();
            OnPropertyChanged(nameof(KeepPath));
            OnPropertyChanged(nameof(SelectedBytes));
            OnPropertyChanged(nameof(SelectedCount));
        }

        public void RefreshSelectionAggregates()
        {
            OnPropertyChanged(nameof(SelectedBytes));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(ReclaimableBytes));
            OnPropertyChanged(nameof(ReclaimableText));
        }

        private void ApplySelection()
        {
            if (_suppressSelection)
            {
                return;
            }

            foreach (var file in Files)
            {
                file.IsSelected = !file.IsKeep && IsSelected;
            }

            OnPropertyChanged(nameof(SelectedBytes));
            OnPropertyChanged(nameof(SelectedCount));
        }
    }
}
