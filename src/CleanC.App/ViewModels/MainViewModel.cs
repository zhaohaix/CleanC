using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CleanC.App.Infrastructure;
using CleanC.Core;

namespace CleanC.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<RuleDefinition> _rules;
    private readonly Scanner _scanner;
    private readonly Cleaner _cleaner;
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private string _statusText = "点击“开始扫描”，只读取，不删除。";
    private string _freeSpaceText = "—";
    private string _driveSummary = "";
    private string _selectedText = "0 B";
    private string _lastLogPath = "";
    private string _lastLogDirectory = "";
    private string _headerStatus = "第一版 · 无需管理员";
    private string _environmentHint = "第一版 · 无需管理员权限";
    private bool _isRestrictedEnvironment;

    public MainViewModel()
    {
        _rules = RulesLoader.Load();
        var guard = PathGuard.CreateDefault();
        _scanner = new Scanner(guard);
        _cleaner = new Cleaner(guard);

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        SimulateCleanCommand = new AsyncRelayCommand(() => CleanAsync(true), () => !IsBusy && CanClean);
        CleanCommand = new AsyncRelayCommand(() => CleanAsync(false), () => !IsBusy && CanClean);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        CopyLogPathCommand = new RelayCommand(CopyLogPath);

        Items.CollectionChanged += OnItemsCollectionChanged;

        var appDataWritable = OperationLogger.IsAppDataWritable();
        IsRestrictedEnvironment = !appDataWritable;
        HeaderStatus = appDataWritable ? "第一版 · 无需管理员" : "受限环境 · 日志回退到程序目录";
        EnvironmentHint = appDataWritable ? "第一版 · 无需管理员权限" : "请从桌面/资源管理器启动";
        if (!appDataWritable)
        {
            StatusText = "当前环境无法写 AppData；真实清理可能被拒绝，请从桌面/资源管理器启动，不要从 DSH 文件卡片启动。";
        }

        RefreshDriveInfo();
    }

    public ReviewViewModel Review { get; } = new();

    public ObservableCollection<ScanItemViewModel> Items { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand SimulateCleanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand CopyLogPathCommand { get; }

    public string HeaderStatus
    {
        get => _headerStatus;
        private set => SetProperty(ref _headerStatus, value);
    }

    public string EnvironmentHint
    {
        get => _environmentHint;
        private set => SetProperty(ref _environmentHint, value);
    }

    public bool IsRestrictedEnvironment
    {
        get => _isRestrictedEnvironment;
        private set => SetProperty(ref _isRestrictedEnvironment, value);
    }

    public string FreeSpaceText
    {
        get => _freeSpaceText;
        private set => SetProperty(ref _freeSpaceText, value);
    }

    public string DriveSummary
    {
        get => _driveSummary;
        private set => SetProperty(ref _driveSummary, value);
    }

    public string SelectedText
    {
        get => _selectedText;
        private set => SetProperty(ref _selectedText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastLogPath
    {
        get => _lastLogPath;
        private set => SetProperty(ref _lastLogPath, value);
    }

    public string LastLogDirectory
    {
        get => _lastLogDirectory;
        private set => SetProperty(ref _lastLogDirectory, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            ScanCommand.RaiseCanExecuteChanged();
            SimulateCleanCommand.RaiseCanExecuteChanged();
        CleanCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanClean));
        }
    }

    public bool CanClean => Items.Any(item => item.IsSelected && !item.IsSkipped);

    public async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Items.Clear();
        LogLines.Clear();
        RefreshSelection();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusText = "正在扫描...";
        Log("开始扫描：仅读取，不删除任何文件。");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
                StatusText = $"正在扫描 {p.ItemName}：{p.FilesScanned:N0} 个文件，{ByteSizeFormatter.Format(p.BytesScanned)}");

            var result = await _scanner.ScanAsync(_rules, progress, _cts.Token);
            foreach (var item in result.Items)
            {
                Items.Add(new ScanItemViewModel(item));
            }

            RefreshSelection();
            RefreshDriveInfo();

            var cleanable = Items.Where(item => !item.IsSkipped).Sum(item => item.Model.TotalBytes);
            Log($"扫描完成：{result.Items.Count} 项，预计可清理 {ByteSizeFormatter.Format(cleanable)}，耗时 {result.Duration.TotalSeconds:0.0} 秒。");
            StatusText = $"扫描完成：{result.Items.Count} 项，预计可清理 {ByteSizeFormatter.Format(cleanable)}。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消。";
            Log("扫描已取消。");
        }
        catch (Exception ex)
        {
            StatusText = "扫描失败：" + ex.Message;
            Log("扫描失败：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task CleanAsync(bool dryRun)
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Items
            .Where(item => item.IsSelected && !item.IsSkipped)
            .Select(item => item.Model)
            .ToList();

        if (selected.Count == 0)
        {
            StatusText = "没有选择清理项。";
            return;
        }

        var plannedText = ByteSizeFormatter.Format(selected.Sum(item => item.TotalBytes));
        if (!dryRun)
        {
            var lines = string.Join("\n", selected.Select(item => $"· {item.Name}（{ByteSizeFormatter.Format(item.TotalBytes)}）"));
            var message =
                "将永久删除以下项目中的文件：\n\n" +
                lines +
                $"\n\n预计释放：{plannedText}\n\n" +
                "只处理白名单内的用户缓存；不碰系统目录、文档、桌面、下载和 DSH 运行环境。\n\n" +
                "确定继续？";

            var answer = MessageBox.Show(
                Application.Current.MainWindow,
                message,
                "确认清理",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                Log("用户取消清理。");
                return;
            }
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusText = dryRun ? "正在模拟清理..." : "正在清理...";
        Log($"开始{(dryRun ? "模拟清理" : "清理")}：{selected.Count} 项，预计 {plannedText}。");

        OperationLogger? logger = null;
        try
        {
            logger = OperationLogger.CreateDefault();
            LastLogPath = logger.LogPath;
            LastLogDirectory = Path.GetDirectoryName(logger.LogPath) ?? "";
            Log("日志文件：" + logger.LogPath);

            var progress = new Progress<CleanProgress>(p =>
                StatusText = $"正在处理 {p.ItemName}：{p.ProcessedFiles:N0} / {p.TotalFiles:N0} 个文件，已释放 {ByteSizeFormatter.Format(p.ProcessedBytes)}");

            var result = await _cleaner.CleanAsync(
                selected,
                dryRun,
                new CleanLimits(),
                logger,
                progress,
                _cts.Token);

            if (result.Aborted)
            {
                var reason = result.AbortReason ?? "本次操作超过安全上限，已停止。";
                StatusText = "已停止：超过安全上限。";
                Log("已停止：" + reason);
                MessageBox.Show(
                    Application.Current.MainWindow,
                    reason,
                    "已停止",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Log($"计划 {result.PlannedFiles:N0} 个文件，{ByteSizeFormatter.Format(result.PlannedBytes)}；" +
                $"删除 {result.DeletedFiles:N0} 个，释放 {ByteSizeFormatter.Format(result.DeletedBytes)}；" +
                $"跳过 {result.SkippedFiles:N0}，失败 {result.FailedFiles:N0}。");

            if (dryRun)
            {
                StatusText = $"模拟完成：计划处理 {result.PlannedFiles:N0} 个文件，未删除。";
            }
            else if (result.DeletedFiles == 0)
            {
                StatusText = result.FailedFiles > 0
                    ? $"清理失败：未删除任何文件，失败 {result.FailedFiles:N0} 个，跳过 {result.SkippedFiles:N0} 个。请查看日志中的失败原因。"
                    : $"未执行删除：没有可处理的文件，跳过 {result.SkippedFiles:N0} 个。";
            }
            else
            {
                var outcome = result.FailedFiles > 0 ? "清理部分失败" : "清理完成";
                StatusText = $"{outcome}：删除 {result.DeletedFiles:N0} 个文件，释放 {ByteSizeFormatter.Format(result.DeletedBytes)}；" +
                    $"跳过 {result.SkippedFiles:N0} 个，失败 {result.FailedFiles:N0} 个。";
            }

            Log(StatusText);


        }
        catch (OperationCanceledException)
        {
            StatusText = "清理已取消。";
            Log("清理已取消。");
        }
        catch (Exception ex)
        {
            StatusText = "清理失败：" + ex.Message;
            Log("清理失败：" + ex.Message);
        }
        finally
        {
            if (!dryRun)
            {
                Items.Clear();
                RefreshSelection();
                RefreshDriveInfo();
                Log("本次扫描结果已失效，请重新扫描后继续清理。");
                StatusText += " 请重新扫描。";
            }
            if (logger is not null)
            {
                await logger.DisposeAsync();
            }

            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Cancel()
    {
        if (_cts is null)
        {
            return;
        }

        StatusText = "正在取消...";
        _cts.Cancel();
    }

    private void OpenLogFolder()
    {
        if (string.IsNullOrWhiteSpace(LastLogDirectory))
        {
            MessageBox.Show(
                Application.Current.MainWindow,
                "还没有生成日志。先点一次“模拟清理”，成功后再打开日志目录。",
                "暂无日志",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsRestrictedEnvironment)
        {
            CopyLogPath();
            MessageBox.Show(
                Application.Current.MainWindow,
                "当前是受限环境，不能启动资源管理器。\n\n" +
                "日志路径已经复制到剪贴板，请打开资源管理器，粘贴到地址栏后回车：\n\n" +
                LastLogDirectory,
                "受限环境",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory(LastLogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{LastLogDirectory}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            var copied = false;
            try
            {
                Clipboard.SetText(LastLogDirectory);
                copied = true;
            }
            catch
            {
                // 剪贴板也可能被策略禁用，继续显示路径即可。
            }

            MessageBox.Show(
                Application.Current.MainWindow,
                "无法打开资源管理器：" + ex.Message +
                (copied ? "\n\n日志路径已复制到剪贴板，可以粘贴到资源管理器地址栏。" : "") +
                "\n\n目录：" + LastLogDirectory,
                "打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CopyLogPath()
    {
        if (string.IsNullOrWhiteSpace(LastLogDirectory))
        {
            MessageBox.Show(
                Application.Current.MainWindow,
                "还没有生成日志。先点一次“模拟清理”。",
                "暂无日志",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Clipboard.SetText(LastLogDirectory);
            StatusText = "日志路径已复制到剪贴板。";
            Log("日志路径已复制：" + LastLogDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current.MainWindow,
                "复制失败：" + ex.Message + "\n\n目录：" + LastLogDirectory,
                "复制失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshDriveInfo()
    {
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(item =>
                item.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase) && item.IsReady);
            if (drive is null)
            {
                FreeSpaceText = "—";
                DriveSummary = "未找到 C 盘";
                return;
            }

            FreeSpaceText = ByteSizeFormatter.Format(drive.AvailableFreeSpace);
            DriveSummary = $"总容量 {ByteSizeFormatter.Format(drive.TotalSize)} · {drive.DriveFormat}";
        }
        catch (Exception ex)
        {
            FreeSpaceText = "—";
            DriveSummary = ex.Message;
        }
    }

    private void RefreshSelection()
    {
        SelectedText = ByteSizeFormatter.Format(
            Items.Where(item => item.IsSelected && !item.IsSkipped)
                .Sum(item => item.Model.TotalBytes));
        OnPropertyChanged(nameof(CanClean));
        CommandManager.InvalidateRequerySuggested();
        SimulateCleanCommand.RaiseCanExecuteChanged();
        CleanCommand.RaiseCanExecuteChanged();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ScanItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ScanItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }

        RefreshSelection();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanItemViewModel.IsSelected))
        {
            RefreshSelection();
        }
    }

    private void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogLines.Count > 300)
        {
            LogLines.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        Review.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
