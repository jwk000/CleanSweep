using System;
using CleanSweep.Common;

namespace CleanSweep.Models
{
    public sealed class CategorySummary : ObservableObject
    {
        private int _itemCount;
        private int _fileCount;
        private long _size;
        private int _selectedCount;
        private long _selectedBytes;

        public string Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Glyph { get; set; }
        public string Accent { get; set; }
        public string SoftAccent { get; set; }

        public int ItemCount
        {
            get { return _itemCount; }
            set
            {
                if (SetProperty(ref _itemCount, value))
                {
                    OnPropertyChanged(nameof(CountText));
                    OnPropertyChanged(nameof(HasFindings));
                }
            }
        }

        public int FileCount
        {
            get { return _fileCount; }
            set
            {
                if (SetProperty(ref _fileCount, value))
                {
                    OnPropertyChanged(nameof(CountText));
                }
            }
        }

        public long Size
        {
            get { return _size; }
            set
            {
                if (SetProperty(ref _size, value))
                {
                    OnPropertyChanged(nameof(SizeText));
                }
            }
        }

        public int SelectedCount
        {
            get { return _selectedCount; }
            set
            {
                if (SetProperty(ref _selectedCount, value))
                {
                    OnPropertyChanged(nameof(SelectionText));
                }
            }
        }

        public long SelectedBytes
        {
            get { return _selectedBytes; }
            set
            {
                if (SetProperty(ref _selectedBytes, value))
                {
                    OnPropertyChanged(nameof(SelectionText));
                }
            }
        }

        public string CountText
        {
            get
            {
                if (ItemCount <= 0)
                {
                    return "未发现";
                }

                if (Key == CategoryKeys.Duplicates)
                {
                    return Format.Count(ItemCount, "组");
                }

                if (FileCount > ItemCount)
                {
                    return Format.Count(ItemCount, "项") + " · " + Format.Count(FileCount, "个文件");
                }

                return Format.Count(ItemCount, "项");
            }
        }

        public string SizeText
        {
            get { return Size > 0 ? Format.Bytes(Size) : "0 B"; }
        }

        public string SelectionText
        {
            get
            {
                if (SelectedCount <= 0)
                {
                    return "未选择";
                }

                return "已选 " + Format.Count(SelectedCount, "项") + " · " + Format.Bytes(SelectedBytes);
            }
        }

        public bool HasFindings
        {
            get { return ItemCount > 0; }
        }
    }

    public sealed class NavItem : ObservableObject
    {
        private string _badge;

        public string Key { get; set; }
        public string Title { get; set; }
        public string Glyph { get; set; }

        public string Badge
        {
            get { return _badge; }
            set
            {
                if (SetProperty(ref _badge, value))
                {
                    OnPropertyChanged(nameof(HasBadge));
                }
            }
        }

        public bool HasBadge
        {
            get { return !string.IsNullOrEmpty(Badge); }
        }
    }
}
