using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CleanSweep.Common;
using CleanSweep.Models;
using CleanSweep.Services;
using CleanSweep.Views;

namespace CleanSweep.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ScanService _scanService = new ScanService();
        private readonly CleanService _cleanService = new CleanService();
        private CancellationTokenSource _scanCts;
        private CancellationTokenSource _cleanCts;
        private AppSettings _settings;
        private bool _isScanning;
        private bool _isCleaning;
        private bool _hasScanned;
        private bool _isElevated;
        private string _currentPage = "overview";
        private string _currentCategory = CategoryKeys.Temp;
        private NavItem _selectedNavItem;
        private string _pageTitle = "概览";
        private string _pageSubtitle = "只清理有证据的垃圾，每一项都可预览和追溯。";
        private string _busyTitle;
        private string _busyStage;
        private string _busyDetail;
        private string _busyStats;
        private double _busyPercent;
        private int _selectedCount;
        private long _selectedBytes;
        private long _selectedRecycleBytes;
        private int _highRiskCount;
        private string _scanStatusText = "尚未扫描";
        private string _lastScanText = "还没有扫描记录";
        private string _lastCleanText = string.Empty;
        private string _settingsStatus = string.Empty;
        private long _foundBytes;
        private int _foundItems;
        private string _scanDurationText;
        private int _tempAgeHours;
        private int _duplicateMinSizeKb;
        private bool _includeBrowserCache;
        private bool _includeCrashDumps;
        private bool _includeSystemCache;
        private string _selectAllLabel = "全选";

        public MainViewModel()
        {
            _settings = AppSettings.Load();
            _isElevated = NativeMethods.IsElevated();

            TempAgeOptions = new ObservableCollection<OptionItem>
            {
                new OptionItem { Value = 24, Label = "1 天" },
                new OptionItem { Value = 72, Label = "3 天" },
                new OptionItem { Value = 168, Label = "7 天" },
                new OptionItem { Value = 720, Label = "30 天" }
            };
            DuplicateSizeOptions = new ObservableCollection<OptionItem>
            {
                new OptionItem { Value = 64, Label = "64 KB" },
                new OptionItem { Value = 256, Label = "256 KB" },
                new OptionItem { Value = 1024, Label = "1 MB" },
                new OptionItem { Value = 4096, Label = "4 MB" }
            };

            Categories = new ObservableCollection<CategorySummary>
            {
                new CategorySummary
                {
                    Key = CategoryKeys.Temp, Name = "临时文件", Glyph = "\uE74D",
                    Description = "临时目录、更新缓存、浏览器缓存",
                    Accent = "#16A36D", SoftAccent = "#E7F6EF"
                },
                new CategorySummary
                {
                    Key = CategoryKeys.Leftover, Name = "软件残留", Glyph = "\uE71D",
                    Description = "卸载登记、失效快捷方式、空菜单",
                    Accent = "#C77C18", SoftAccent = "#FFF4E2"
                },
                new CategorySummary
                {
                    Key = CategoryKeys.Duplicates, Name = "重复文件", Glyph = "\uE8C8",
                    Description = "SHA-256 完全一致的重复副本",
                    Accent = "#3B72C9", SoftAccent = "#EAF1FC"
                },
                new CategorySummary
                {
                    Key = CategoryKeys.System, Name = "系统冗余", Glyph = "\uE770",
                    Description = "旧系统备份、升级残留、回收站",
                    Accent = "#B05252", SoftAccent = "#FBECEC"
                }
            };

            TempItems = new ObservableCollection<FindingItem>();
            LeftoverItems = new ObservableCollection<FindingItem>();
            SystemItems = new ObservableCollection<FindingItem>();
            CurrentItems = new ObservableCollection<FindingItem>();
            DuplicateGroups = new ObservableCollection<DuplicateGroup>();
            Logs = new ObservableCollection<LogSummary>();
            ScanNotes = new ObservableCollection<string>();
            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem { Key = "overview", Title = "概览", Glyph = "\uE80F" },
                new NavItem { Key = CategoryKeys.Temp, Title = "临时文件", Glyph = "\uE74D" },
                new NavItem { Key = CategoryKeys.Leftover, Title = "软件残留", Glyph = "\uE71D" },
                new NavItem { Key = CategoryKeys.Duplicates, Title = "重复文件", Glyph = "\uE8C8" },
                new NavItem { Key = CategoryKeys.System, Title = "系统冗余", Glyph = "\uE770" },
                new NavItem { Key = "logs", Title = "清理记录", Glyph = "\uE81C" },
                new NavItem { Key = "settings", Title = "设置", Glyph = "\uE713" }
            };

            _tempAgeHours = _settings.TempAgeHours;
            _duplicateMinSizeKb = _settings.DuplicateMinSizeKb;
            _includeBrowserCache = _settings.IncludeBrowserCache;
            _includeCrashDumps = _settings.IncludeCrashDumps;
            _includeSystemCache = _settings.IncludeSystemCache;

            ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);
            CleanCommand = new AsyncRelayCommand(CleanSelectedAsync, () => CanClean);
            SelectAllCommand = new RelayCommand(SelectAllCurrent);
            NavigateCommand = new RelayCommand(Navigate);
            ElevateCommand = new RelayCommand(Elevate, () => !IsElevated);
            OpenLogCommand = new RelayCommand(OpenLog);
            OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
            OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
            OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
            OpenItemLocationCommand = new RelayCommand(OpenItemLocation);
            ShowPathsCommand = new RelayCommand(ShowPaths);
            KeepFileCommand = new RelayCommand(KeepFile);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            RefreshLogsCommand = new RelayCommand(LoadLogs);

            _selectedNavItem = NavItems[0];
            LoadLogs();
            RefreshCategories();
            UpdateTotals();
        }

        public ObservableCollection<NavItem> NavItems { get; private set; }
        public ObservableCollection<CategorySummary> Categories { get; private set; }
        public ObservableCollection<FindingItem> TempItems { get; private set; }
        public ObservableCollection<FindingItem> LeftoverItems { get; private set; }
        public ObservableCollection<FindingItem> SystemItems { get; private set; }
        public ObservableCollection<FindingItem> CurrentItems { get; private set; }
        public ObservableCollection<DuplicateGroup> DuplicateGroups { get; private set; }
        public ObservableCollection<LogSummary> Logs { get; private set; }
        public ObservableCollection<string> ScanNotes { get; private set; }
        public ObservableCollection<OptionItem> TempAgeOptions { get; private set; }
        public ObservableCollection<OptionItem> DuplicateSizeOptions { get; private set; }

        public ICommand ScanCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand CleanCommand { get; private set; }
        public ICommand SelectAllCommand { get; private set; }
        public ICommand NavigateCommand { get; private set; }
        public ICommand ElevateCommand { get; private set; }
        public ICommand OpenLogCommand { get; private set; }
        public ICommand OpenLogFolderCommand { get; private set; }
        public ICommand OpenDataFolderCommand { get; private set; }
        public ICommand OpenBackupFolderCommand { get; private set; }
        public ICommand OpenItemLocationCommand { get; private set; }
        public ICommand ShowPathsCommand { get; private set; }
        public ICommand KeepFileCommand { get; private set; }
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand RefreshLogsCommand { get; private set; }

        public CategorySummary CurrentCategorySummary
        {
            get { return Categories.FirstOrDefault(c => c.Key == _currentCategory); }
        }

        public string CurrentCategoryName
        {
            get { return CurrentCategorySummary == null ? string.Empty : CurrentCategorySummary.Name; }
        }

        public string CurrentCategoryDescription
        {
            get { return CurrentCategorySummary == null ? string.Empty : CurrentCategorySummary.Description; }
        }

        public string CurrentCategoryGlyph
        {
            get { return CurrentCategorySummary == null ? string.Empty : CurrentCategorySummary.Glyph; }
        }

        public string CurrentCategoryAccent
        {
            get { return CurrentCategorySummary == null ? "#16A36D" : CurrentCategorySummary.Accent; }
        }

        public string CurrentCategorySoftAccent
        {
            get { return CurrentCategorySummary == null ? "#E7F6EF" : CurrentCategorySummary.SoftAccent; }
        }

        public NavItem SelectedNavItem
        {
            get { return _selectedNavItem; }
            set
            {
                if (SetProperty(ref _selectedNavItem, value) && value != null)
                {
                    NavigateTo(value.Key);
                }
            }
        }

        public string CurrentPage
        {
            get { return _currentPage; }
            private set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(IsOverview));
                    OnPropertyChanged(nameof(IsFindings));
                    OnPropertyChanged(nameof(IsDuplicates));
                    OnPropertyChanged(nameof(IsLogs));
                    OnPropertyChanged(nameof(IsSettings));
                    OnPropertyChanged(nameof(ShowActionBar));
                    OnPropertyChanged(nameof(ShowSelectAll));
                }
            }
        }

        public bool IsOverview { get { return _currentPage == "overview"; } }
        public bool IsFindings { get { return _currentPage == "findings"; } }
        public bool IsDuplicates { get { return _currentPage == "duplicates"; } }
        public bool IsLogs { get { return _currentPage == "logs"; } }
        public bool IsSettings { get { return _currentPage == "settings"; } }

        public bool ShowActionBar
        {
            get { return HasScanned && (IsOverview || IsFindings || IsDuplicates); }
        }

        public bool ShowSelectAll
        {
            get { return ShowActionBar && !IsOverview; }
        }

        public string PageTitle
        {
            get { return _pageTitle; }
            private set { SetProperty(ref _pageTitle, value); }
        }

        public string PageSubtitle
        {
            get { return _pageSubtitle; }
            private set { SetProperty(ref _pageSubtitle, value); }
        }

        public bool IsScanning
        {
            get { return _isScanning; }
            private set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnBusyChanged();
                }
            }
        }

        public bool IsCleaning
        {
            get { return _isCleaning; }
            private set
            {
                if (SetProperty(ref _isCleaning, value))
                {
                    OnBusyChanged();
                }
            }
        }

        public bool IsBusy { get { return IsScanning || IsCleaning; } }
        public bool CanCancel { get { return IsBusy; } }

        public bool HasScanned
        {
            get { return _hasScanned; }
            private set
            {
                if (SetProperty(ref _hasScanned, value))
                {
                    OnPropertyChanged(nameof(ShowActionBar));
                    OnPropertyChanged(nameof(ShowSelectAll));
                    OnPropertyChanged(nameof(ScanButtonText));
                    OnPropertyChanged(nameof(ShowEmptyOverview));
                    OnPropertyChanged(nameof(HeroMetricText));
                    OnPropertyChanged(nameof(HeroSubMetric));
                    OnPropertyChanged(nameof(HeroHint));
                }
            }
        }

        public bool ShowEmptyOverview { get { return !HasScanned; } }
        public bool IsElevated { get { return _isElevated; } }
        public bool ShowElevationHint { get { return !IsElevated; } }
        public string PrivilegeText { get { return IsElevated ? "管理员权限" : "标准权限"; } }
        public string ElevateButtonText { get { return IsElevated ? "已提升" : "提升权限"; } }
        public string ScanButtonText { get { return HasScanned ? "重新扫描" : "开始扫描"; } }

        public string BusyTitle
        {
            get { return _busyTitle; }
            private set { SetProperty(ref _busyTitle, value); }
        }

        public string BusyStage
        {
            get { return _busyStage; }
            private set { SetProperty(ref _busyStage, value); }
        }

        public string BusyDetail
        {
            get { return _busyDetail; }
            private set { SetProperty(ref _busyDetail, value); }
        }

        public string BusyStats
        {
            get { return _busyStats; }
            private set { SetProperty(ref _busyStats, value); }
        }

        public double BusyPercent
        {
            get { return _busyPercent; }
            private set { SetProperty(ref _busyPercent, value); }
        }

        public int SelectedCount
        {
            get { return _selectedCount; }
            private set
            {
                if (SetProperty(ref _selectedCount, value))
                {
                    OnPropertyChanged(nameof(SelectedCountText));
                    OnPropertyChanged(nameof(CanClean));
                    OnPropertyChanged(nameof(SelectedSubText));
                }
            }
        }

        public long SelectedBytes
        {
            get { return _selectedBytes; }
            private set
            {
                if (SetProperty(ref _selectedBytes, value))
                {
                    OnPropertyChanged(nameof(SelectedSizeText));
                    OnPropertyChanged(nameof(SelectedSubText));
                    OnPropertyChanged(nameof(CanClean));
                }
            }
        }

        public long SelectedRecycleBytes
        {
            get { return _selectedRecycleBytes; }
            private set
            {
                if (SetProperty(ref _selectedRecycleBytes, value))
                {
                    OnPropertyChanged(nameof(SelectedSubText));
                }
            }
        }

        public int HighRiskCount
        {
            get { return _highRiskCount; }
            private set { SetProperty(ref _highRiskCount, value); }
        }

        public bool CanClean
        {
            get { return !IsBusy && SelectedCount > 0; }
        }

        public string SelectedCountText
        {
            get { return "全部选中 " + Format.Count(SelectedCount, "项"); }
        }

        public string SelectedSizeText
        {
            get { return Format.Bytes(SelectedBytes); }
        }

        public string SelectedSubText
        {
            get
            {
                if (SelectedCount == 0)
                {
                    return "勾选需要清理的项目";
                }

                long permanent = Math.Max(0, SelectedBytes - SelectedRecycleBytes);
                if (SelectedRecycleBytes > 0)
                {
                    return "直接释放 " + Format.Bytes(permanent) + "；" +
                           Format.Bytes(SelectedRecycleBytes) + " 将移至回收站";
                }

                return "将永久删除选中的文件";
            }
        }

        public string SelectAllLabel
        {
            get { return _selectAllLabel; }
            private set { SetProperty(ref _selectAllLabel, value); }
        }

        public string ScanStatusText
        {
            get { return _scanStatusText; }
            private set { SetProperty(ref _scanStatusText, value); }
        }

        public string LastScanText
        {
            get { return _lastScanText; }
            private set { SetProperty(ref _lastScanText, value); }
        }

        public string LastCleanText
        {
            get { return _lastCleanText; }
            private set
            {
                if (SetProperty(ref _lastCleanText, value))
                {
                    OnPropertyChanged(nameof(HasLastClean));
                }
            }
        }

        public bool HasLastClean
        {
            get { return !string.IsNullOrEmpty(LastCleanText); }
        }

        public long FoundBytes
        {
            get { return _foundBytes; }
            private set
            {
                if (SetProperty(ref _foundBytes, value))
                {
                    OnPropertyChanged(nameof(FoundBytesText));
                    OnPropertyChanged(nameof(FoundSummaryText));
                    OnPropertyChanged(nameof(HeroMetricText));
                }
            }
        }

        public int FoundItems
        {
            get { return _foundItems; }
            private set
            {
                if (SetProperty(ref _foundItems, value))
                {
                    OnPropertyChanged(nameof(FoundItemsText));
                    OnPropertyChanged(nameof(FoundSummaryText));
                    OnPropertyChanged(nameof(HeroSubMetric));
                }
            }
        }

        public string FoundBytesText { get { return Format.Bytes(FoundBytes); } }
        public string FoundItemsText { get { return Format.Count(FoundItems, "项"); } }
        public string FoundSummaryText { get { return "发现 " + FoundItemsText + " · " + FoundBytesText; } }

        public string HeroMetricText
        {
            get { return HasScanned ? FoundBytesText : "准备就绪"; }
        }

        public string HeroSubMetric
        {
            get { return HasScanned ? FoundItemsText + "可清理" : "还没有扫描记录"; }
        }

        public string HeroHint
        {
            get { return HasScanned ? "勾选后可预览清理依据" : "扫描过程只读，不会修改文件"; }
        }

        public string ScanDurationText
        {
            get { return _scanDurationText; }
            private set { SetProperty(ref _scanDurationText, value); }
        }

        public int TempAgeHours
        {
            get { return _tempAgeHours; }
            set { SetProperty(ref _tempAgeHours, value); }
        }

        public int DuplicateMinSizeKb
        {
            get { return _duplicateMinSizeKb; }
            set { SetProperty(ref _duplicateMinSizeKb, value); }
        }

        public bool IncludeBrowserCache
        {
            get { return _includeBrowserCache; }
            set { SetProperty(ref _includeBrowserCache, value); }
        }

        public bool IncludeCrashDumps
        {
            get { return _includeCrashDumps; }
            set { SetProperty(ref _includeCrashDumps, value); }
        }

        public bool IncludeSystemCache
        {
            get { return _includeSystemCache; }
            set { SetProperty(ref _includeSystemCache, value); }
        }

        public string SettingsStatus
        {
            get { return _settingsStatus; }
            private set { SetProperty(ref _settingsStatus, value); }
        }

        public string DataDirectory { get { return AppPaths.BaseDir; } }
        public string LogsDirectory { get { return AppPaths.LogsDir; } }
        public string BackupsDirectory { get { return AppPaths.BackupsDir; } }
        public string VersionText { get { return "版本 " + AppInfo.Version + " · 本地运行，不上传任何文件"; } }

        public Task InitializeAsync()
        {
            LoadLogs();
            return Task.CompletedTask;
        }

        private void OnBusyChanged()
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanClean));
            var asyncScan = ScanCommand as AsyncRelayCommand;
            if (asyncScan != null) asyncScan.RaiseCanExecuteChanged();
            var asyncClean = CleanCommand as AsyncRelayCommand;
            if (asyncClean != null) asyncClean.RaiseCanExecuteChanged();
            var cancel = CancelCommand as RelayCommand;
            if (cancel != null) cancel.RaiseCanExecuteChanged();
        }

        private async Task ScanAsync()
        {
            if (IsBusy)
            {
                return;
            }

            _scanCts = new CancellationTokenSource();
            IsScanning = true;
            BusyTitle = "正在扫描";
            BusyStage = "准备中";
            BusyDetail = "正在建立只读扫描任务…";
            BusyStats = "不会修改任何文件";
            BusyPercent = 0;
            ScanStatusText = "正在扫描…";

            var progress = new Progress<ScanProgress>(p =>
            {
                BusyStage = p.Stage;
                BusyDetail = p.Detail;
                BusyPercent = p.Percent;
                BusyStats = Format.Bytes(p.FoundBytes) + " · 已发现 " + Format.Count(p.FoundItems, "项");
            });

            try
            {
                var settings = BuildSettings();
                var result = await _scanService.ScanAsync(settings, progress, _scanCts.Token);
                ApplyScanResult(result);
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "扫描已取消";
            }
            catch (Exception ex)
            {
                ScanStatusText = "扫描失败";
                MessageBox.Show("扫描失败：\n\n" + ex.Message, "净界 CleanSweep",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsScanning = false;
                BusyPercent = 0;
                if (_scanCts != null)
                {
                    _scanCts.Dispose();
                    _scanCts = null;
                }

                RefreshCommands();
            }
        }

        private void ApplyScanResult(ScanResult result)
        {
            foreach (var item in TempItems) item.PropertyChanged -= OnItemPropertyChanged;
            foreach (var item in LeftoverItems) item.PropertyChanged -= OnItemPropertyChanged;
            foreach (var item in SystemItems) item.PropertyChanged -= OnItemPropertyChanged;
            foreach (var group in DuplicateGroups)
            {
                group.PropertyChanged -= OnGroupPropertyChanged;
                foreach (var file in group.Files) file.PropertyChanged -= OnItemPropertyChanged;
            }

            TempItems.Clear();
            LeftoverItems.Clear();
            SystemItems.Clear();
            DuplicateGroups.Clear();
            ScanNotes.Clear();

            foreach (var item in result.Items)
            {
                item.IsSelected = item.DefaultSelected && item.IsSelectable;
                item.PropertyChanged += OnItemPropertyChanged;
                if (item.Category == CategoryKeys.Temp) TempItems.Add(item);
                else if (item.Category == CategoryKeys.Leftover) LeftoverItems.Add(item);
                else SystemItems.Add(item);
            }

            foreach (var group in result.DuplicateGroups)
            {
                group.PropertyChanged += OnGroupPropertyChanged;
                foreach (var file in group.Files)
                {
                    file.PropertyChanged += OnItemPropertyChanged;
                }

                DuplicateGroups.Add(group);
            }

            foreach (string note in result.Notes)
            {
                ScanNotes.Add(note);
            }

            HasScanned = true;
            ScanDurationText = Format.Duration(result.Duration);
            LastScanText = "上次扫描 " + result.FinishedAt.ToString("HH:mm") + " · 用时 " + ScanDurationText;
            ScanStatusText = "扫描于 " + result.FinishedAt.ToString("HH:mm") + " 完成";
            FoundBytes = result.Items.Sum(x => x.Size) + result.DuplicateGroups.Sum(g => g.ReclaimableBytes);
            FoundItems = result.Items.Count + result.DuplicateGroups.Count;
            OnPropertyChanged(nameof(HasScanNotes));
            RefreshCategories();
            UpdateTotals();
            SelectNav("overview");
        }

        private async Task CleanSelectedAsync()
        {
            if (IsBusy)
            {
                return;
            }

            var selected = GetSelectedItems();
            if (selected.Count == 0)
            {
                return;
            }

            var confirm = new ConfirmWindow(selected, SelectedBytes, SelectedRecycleBytes, HighRiskCount)
            {
                Owner = Application.Current.MainWindow
            };
            if (confirm.ShowDialog() != true)
            {
                return;
            }

            _cleanCts = new CancellationTokenSource();
            IsCleaning = true;
            BusyTitle = "正在清理";
            BusyStage = "准备中";
            BusyDetail = "正在创建清理日志…";
            BusyStats = Format.Bytes(SelectedBytes) + " · " + Format.Count(selected.Count, "项");
            BusyPercent = 0;

            var progress = new Progress<CleanProgress>(p =>
            {
                BusyStage = "正在处理";
                BusyDetail = p.CurrentPath;
                BusyPercent = p.Percent;
                BusyStats = "直接释放 " + Format.Bytes(p.FreedBytes) +
                            " · 回收站 " + Format.Bytes(p.RecycledBytes);
            });

            try
            {
                var result = await _cleanService.CleanAsync(selected, progress, _cleanCts.Token);
                ApplyCleanResult(result);
                LoadLogs();
                var report = new ReportWindow(result) { Owner = Application.Current.MainWindow };
                report.ShowDialog();
            }
            catch (OperationCanceledException)
            {
                LastCleanText = "清理已取消，已完成的项目仍写入日志。";
            }
            catch (Exception ex)
            {
                MessageBox.Show("清理失败：\n\n" + ex.Message, "净界 CleanSweep",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsCleaning = false;
                BusyPercent = 0;
                if (_cleanCts != null)
                {
                    _cleanCts.Dispose();
                    _cleanCts = null;
                }

                RefreshCommands();
            }
        }

        private void ApplyCleanResult(CleanResult result)
        {
            var successful = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in result.Records)
            {
                if ((record.Success || record.Partial) && record.Item != null)
                {
                    successful.Add(record.Item.Id);
                }
                else if (record.Item != null)
                {
                    record.Item.IsSelected = false;
                }
            }

            foreach (var item in TempItems.ToList())
            {
                if (successful.Contains(item.Id))
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    TempItems.Remove(item);
                }
            }

            foreach (var item in LeftoverItems.ToList())
            {
                if (successful.Contains(item.Id))
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    LeftoverItems.Remove(item);
                }
            }

            foreach (var item in SystemItems.ToList())
            {
                if (successful.Contains(item.Id))
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    SystemItems.Remove(item);
                }
            }

            foreach (var group in DuplicateGroups.ToList())
            {
                foreach (var file in group.Files.ToList())
                {
                    if (successful.Contains(file.Id))
                    {
                        file.PropertyChanged -= OnItemPropertyChanged;
                        group.Files.Remove(file);
                    }
                }

                if (group.Files.Count < 2)
                {
                    group.PropertyChanged -= OnGroupPropertyChanged;
                    foreach (var remaining in group.Files)
                    {
                        remaining.PropertyChanged -= OnItemPropertyChanged;
                    }
                    DuplicateGroups.Remove(group);
                }
                else
                {
                    group.RefreshSelectionAggregates();
                }
            }

            LastCleanText = "上次清理 " + result.FinishedAt.ToString("HH:mm") + " · 直接释放 " +
                            Format.Bytes(result.FreedBytes) +
                            (result.RecycledBytes > 0 ? " · 回收站 " + Format.Bytes(result.RecycledBytes) : string.Empty);
            FoundBytes = TempItems.Sum(x => x.Size) + LeftoverItems.Sum(x => x.Size) +
                         SystemItems.Sum(x => x.Size) + DuplicateGroups.Sum(g => g.ReclaimableBytes);
            FoundItems = TempItems.Count + LeftoverItems.Count + SystemItems.Count + DuplicateGroups.Count;
            RefreshCategories();
            UpdateTotals();
            RefreshCurrentItems();
        }

        private void RefreshCurrentItems()
        {
            CurrentItems.Clear();
            IEnumerable<FindingItem> source = null;
            if (_currentCategory == CategoryKeys.Temp) source = TempItems;
            else if (_currentCategory == CategoryKeys.Leftover) source = LeftoverItems;
            else if (_currentCategory == CategoryKeys.System) source = SystemItems;
            if (source != null)
            {
                foreach (var item in source.OrderByDescending(x => x.Risk).ThenByDescending(x => x.Size))
                {
                    CurrentItems.Add(item);
                }
            }

            OnPropertyChanged(nameof(CurrentCategorySummary));
            OnPropertyChanged(nameof(CurrentCategoryName));
            OnPropertyChanged(nameof(CurrentCategoryDescription));
            OnPropertyChanged(nameof(CurrentCategoryGlyph));
            OnPropertyChanged(nameof(CurrentCategoryAccent));
            OnPropertyChanged(nameof(CurrentCategorySoftAccent));
            OnPropertyChanged(nameof(CurrentItemsEmpty));
        }

        public bool CurrentItemsEmpty { get { return CurrentItems.Count == 0; } }
        public bool HasScanNotes { get { return ScanNotes.Count > 0; } }
        public bool HasDuplicateFindings { get { return DuplicateGroups.Count > 0; } }

        private void RefreshCategories()
        {
            RefreshCategory(CategoryKeys.Temp, TempItems);
            RefreshCategory(CategoryKeys.Leftover, LeftoverItems);
            RefreshCategory(CategoryKeys.System, SystemItems);

            var dup = Categories.First(c => c.Key == CategoryKeys.Duplicates);
            dup.ItemCount = DuplicateGroups.Count;
            dup.FileCount = DuplicateGroups.Sum(g => g.Files.Count);
            dup.Size = DuplicateGroups.Sum(g => g.ReclaimableBytes);
            dup.SelectedCount = DuplicateGroups.Sum(g => g.SelectedCount);
            dup.SelectedBytes = DuplicateGroups.Sum(g => g.SelectedBytes);
            OnPropertyChanged(nameof(CurrentCategorySummary));
            OnPropertyChanged(nameof(CurrentCategoryName));
            OnPropertyChanged(nameof(CurrentCategoryDescription));
            OnPropertyChanged(nameof(CurrentCategoryGlyph));
            OnPropertyChanged(nameof(CurrentCategoryAccent));
            OnPropertyChanged(nameof(CurrentCategorySoftAccent));
            UpdateNavBadges();
            UpdateSelectAllLabel();
            OnPropertyChanged(nameof(HasDuplicateFindings));
        }

        private void RefreshCategory(string key, IEnumerable<FindingItem> items)
        {
            var summary = Categories.First(c => c.Key == key);
            var list = items.ToList();
            summary.ItemCount = list.Count;
            summary.FileCount = list.Sum(x => x.FileCount);
            summary.Size = list.Sum(x => x.Size);
            summary.SelectedCount = list.Count(x => x.IsSelected);
            summary.SelectedBytes = list.Where(x => x.IsSelected).Sum(x => x.Size);
        }

        private void UpdateNavBadges()
        {
            foreach (var nav in NavItems)
            {
                var category = Categories.FirstOrDefault(c => c.Key == nav.Key);
                if (category == null || !HasScanned)
                {
                    nav.Badge = string.Empty;
                }
                else if (category.Size > 0)
                {
                    nav.Badge = Format.Bytes(category.Size);
                }
                else
                {
                    nav.Badge = string.Empty;
                }
            }
        }

        private void UpdateTotals()
        {
            var selected = GetSelectedItems();
            SelectedCount = selected.Count;
            SelectedBytes = selected.Sum(x => x.Size);
            SelectedRecycleBytes = selected.Where(x => x.Action == CleanAction.RecyclePaths).Sum(x => x.Size);
            HighRiskCount = selected.Count(x => x.Risk == RiskLevel.High);
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(SelectedSubText));
            UpdateSelectAllLabel();
            var clean = CleanCommand as AsyncRelayCommand;
            if (clean != null) clean.RaiseCanExecuteChanged();
        }

        private List<FindingItem> GetSelectedItems()
        {
            var selected = new List<FindingItem>();
            selected.AddRange(TempItems.Where(x => x.IsSelected && x.IsSelectable));
            selected.AddRange(LeftoverItems.Where(x => x.IsSelected && x.IsSelectable));
            selected.AddRange(SystemItems.Where(x => x.IsSelected && x.IsSelectable));
            foreach (var group in DuplicateGroups)
            {
                selected.AddRange(group.Files.Where(x => x.IsSelected && !x.IsKeep && x.IsSelectable));
            }

            return selected;
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected" || e.PropertyName == "IsKeep")
            {
                var item = sender as FindingItem;
                if (item != null && item.IsDuplicate && item.IsKeep)
                {
                    var group = DuplicateGroups.FirstOrDefault(g => g.Id == item.GroupId);
                    if (group != null && !ReferenceEquals(group.KeepItem, item))
                    {
                        group.SetKeeper(item);
                    }
                }

                RefreshCategories();
                UpdateTotals();
            }
        }

        private void OnGroupPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected" || e.PropertyName == "SelectedBytes" ||
                e.PropertyName == "SelectedCount")
            {
                RefreshCategories();
                UpdateTotals();
            }
        }

        private void SelectAllCurrent()
        {
            if (IsDuplicates)
            {
                bool all = DuplicateGroups.Count > 0 && DuplicateGroups.All(g => g.IsSelected);
                foreach (var group in DuplicateGroups)
                {
                    group.IsSelected = !all;
                }
            }
            else if (IsFindings)
            {
                var list = CurrentItems.Where(x => x.IsSelectable).ToList();
                bool all = list.Count > 0 && list.All(x => x.IsSelected);
                foreach (var item in list)
                {
                    item.IsSelected = !all;
                }
            }
            else if (IsOverview)
            {
                var allItems = TempItems.Concat(LeftoverItems).Concat(SystemItems)
                    .Where(x => x.IsSelectable).ToList();
                bool all = allItems.Count > 0 && allItems.All(x => x.IsSelected) &&
                           DuplicateGroups.All(g => g.IsSelected);
                foreach (var item in allItems)
                {
                    item.IsSelected = !all;
                }
                foreach (var group in DuplicateGroups)
                {
                    group.IsSelected = !all;
                }
            }

            RefreshCategories();
            UpdateTotals();
        }

        private void UpdateSelectAllLabel()
        {
            bool all = false;
            if (IsDuplicates)
            {
                all = DuplicateGroups.Count > 0 && DuplicateGroups.All(g => g.IsSelected);
            }
            else if (IsFindings)
            {
                var list = CurrentItems.Where(x => x.IsSelectable).ToList();
                all = list.Count > 0 && list.All(x => x.IsSelected);
            }
            else if (IsOverview)
            {
                var allItems = TempItems.Concat(LeftoverItems).Concat(SystemItems)
                    .Where(x => x.IsSelectable).ToList();
                all = allItems.Count > 0 && allItems.All(x => x.IsSelected) &&
                      DuplicateGroups.All(g => g.IsSelected);
            }

            SelectAllLabel = all && (IsFindings || IsDuplicates || IsOverview) ? "取消全选" : "全选";
        }

        private void Navigate(object parameter)
        {
            string key = parameter as string;
            if (!string.IsNullOrEmpty(key))
            {
                SelectNav(key);
            }
        }

        private void SelectNav(string key)
        {
            var nav = NavItems.FirstOrDefault(n => n.Key == key);
            if (nav != null && !ReferenceEquals(nav, SelectedNavItem))
            {
                SelectedNavItem = nav;
            }
            else
            {
                NavigateTo(key);
            }
        }

        private void NavigateTo(string key)
        {
            if (key == "overview")
            {
                CurrentPage = "overview";
                PageTitle = "概览";
                PageSubtitle = "只清理有证据的垃圾，每一项都可预览和追溯。";
            }
            else if (key == "logs")
            {
                CurrentPage = "logs";
                PageTitle = "清理记录";
                PageSubtitle = "每次清理都会留下完整的路径、依据和结果。";
                LoadLogs();
            }
            else if (key == "settings")
            {
                CurrentPage = "settings";
                PageTitle = "设置";
                PageSubtitle = "调整扫描阈值；更改在下次扫描时生效。";
            }
            else if (key == CategoryKeys.Duplicates)
            {
                CurrentPage = "duplicates";
                PageTitle = "重复文件";
                PageSubtitle = "按大小、前缀哈希和 SHA-256 全量比对，保留每组最早的一份。";
            }
            else
            {
                _currentCategory = key;
                CurrentPage = "findings";
                var category = Categories.FirstOrDefault(c => c.Key == key);
                PageTitle = category == null ? "清理" : category.Name;
                PageSubtitle = category == null ? string.Empty : category.Description + "。";
                RefreshCurrentItems();
            }

            OnPropertyChanged(nameof(ShowActionBar));
            OnPropertyChanged(nameof(ShowSelectAll));
            OnPropertyChanged(nameof(CurrentCategorySummary));
            OnPropertyChanged(nameof(CurrentCategoryName));
            OnPropertyChanged(nameof(CurrentCategoryDescription));
            OnPropertyChanged(nameof(CurrentCategoryGlyph));
            OnPropertyChanged(nameof(CurrentCategoryAccent));
            OnPropertyChanged(nameof(CurrentCategorySoftAccent));
            UpdateSelectAllLabel();
        }

        private void KeepFile(object parameter)
        {
            var item = parameter as FindingItem;
            if (item == null || string.IsNullOrEmpty(item.GroupId))
            {
                return;
            }

            var group = DuplicateGroups.FirstOrDefault(g => g.Id == item.GroupId);
            if (group == null)
            {
                return;
            }

            group.SetKeeper(item);
            RefreshCategories();
            UpdateTotals();
        }

        private void ShowPaths(object parameter)
        {
            var item = parameter as FindingItem;
            if (item == null)
            {
                return;
            }

            var window = new PathsWindow(item) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
        }

        private void OpenItemLocation(object parameter)
        {
            var item = parameter as FindingItem;
            if (item == null)
            {
                return;
            }

            try
            {
                string path = item.DisplayPath;
                if (item.Action == CleanAction.RegistryKey)
                {
                    Process.Start("regedit.exe", "/e \"" + item.Registry.FullPath + "\"");
                    return;
                }

                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                else if (File.Exists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
            }
            catch
            {
            }
        }

        private void OpenLog(object parameter)
        {
            var log = parameter as LogSummary;
            if (log == null)
            {
                var latest = Logs.FirstOrDefault();
                if (latest == null) return;
                log = latest;
            }

            try
            {
                Process.Start(new ProcessStartInfo(log.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开日志：" + ex.Message, "净界 CleanSweep");
            }
        }

        private void OpenLogFolder()
        {
            OpenFolder(AppPaths.LogsDir);
        }

        private void OpenDataFolder()
        {
            OpenFolder(AppPaths.BaseDir);
        }

        private void OpenBackupFolder()
        {
            OpenFolder(AppPaths.BackupsDir);
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch
            {
            }
        }

        private void LoadLogs()
        {
            var recent = LogStore.ListRecent(30);
            Logs.Clear();
            foreach (var log in recent)
            {
                Logs.Add(log);
            }

            OnPropertyChanged(nameof(HasLogs));
        }

        public bool HasLogs { get { return Logs.Count > 0; } }

        private void SaveSettings()
        {
            try
            {
                _settings.TempAgeHours = Math.Max(1, TempAgeHours);
                _settings.DuplicateMinSizeKb = Math.Max(16, DuplicateMinSizeKb);
                _settings.IncludeBrowserCache = IncludeBrowserCache;
                _settings.IncludeCrashDumps = IncludeCrashDumps;
                _settings.IncludeSystemCache = IncludeSystemCache;
                _settings.Save();
                SettingsStatus = "已保存 · " + DateTime.Now.ToString("HH:mm:ss") + "，重新扫描后生效";
            }
            catch (Exception ex)
            {
                SettingsStatus = "保存失败：" + ex.Message;
            }
        }

        private AppSettings BuildSettings()
        {
            var settings = _settings.Clone();
            settings.TempAgeHours = Math.Max(1, TempAgeHours);
            settings.DuplicateMinSizeKb = Math.Max(16, DuplicateMinSizeKb);
            settings.IncludeBrowserCache = IncludeBrowserCache;
            settings.IncludeCrashDumps = IncludeCrashDumps;
            settings.IncludeSystemCache = IncludeSystemCache;
            return settings;
        }

        private void Cancel()
        {
            if (_scanCts != null)
            {
                _scanCts.Cancel();
            }

            if (_cleanCts != null)
            {
                _cleanCts.Cancel();
            }
        }

        private void Elevate()
        {
            if (IsElevated)
            {
                return;
            }

            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法以管理员身份重启：\n\n" + ex.Message, "净界 CleanSweep",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshCommands()
        {
            var scan = ScanCommand as AsyncRelayCommand;
            if (scan != null) scan.RaiseCanExecuteChanged();
            var clean = CleanCommand as AsyncRelayCommand;
            if (clean != null) clean.RaiseCanExecuteChanged();
            var cancel = CancelCommand as RelayCommand;
            if (cancel != null) cancel.RaiseCanExecuteChanged();
            var elevate = ElevateCommand as RelayCommand;
            if (elevate != null) elevate.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanClean));
        }

        public void Dispose()
        {
            Cancel();
            if (_scanCts != null) _scanCts.Dispose();
            if (_cleanCts != null) _cleanCts.Dispose();
        }
    }
}
