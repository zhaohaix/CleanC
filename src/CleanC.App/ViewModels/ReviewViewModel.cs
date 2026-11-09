using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CleanC.App.Infrastructure;
using CleanC.Core;

namespace CleanC.App.ViewModels;

public sealed class ReviewItem : ObservableObject
{
    private string _sizeText = "尚未扫描";
    public required string Name { get; init; }
    public required string Impact { get; init; }
    public required string Guidance { get; init; }
    public required string EntryLabel { get; init; }
    public required string Target { get; init; }
    public string[] Paths { get; init; } = [];
    public bool VirtualDisksOnly { get; init; }
    public bool DirectoryProbeOnly => Target == "folder";
    public bool CanMeasure => Paths.Length > 0 && !DirectoryProbeOnly;
    public string Scope => Paths.Length == 0 ? "本版不直接统计此项目" : string.Join("；", Paths);
    public string SizeText { get => _sizeText; set => SetProperty(ref _sizeText, value); }
    public RelayCommand OpenCommand => new(Open);
    private void Open()
    {
        try
        {
            // Targets come only from the built-in catalog, never from scanned files.
            if (Target == "chat-details")
            {
                new ChatStorageWindow(Paths) { Owner = Application.Current.MainWindow }.Show();
            }
            else if (Target == "protection")
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe")) { UseShellExecute = true });
            else if (Target == "device")
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "mmc.exe"))
                { ArgumentList = { Path.Combine(Environment.SystemDirectory, "devmgmt.msc") }, UseShellExecute = true });
            else if (Target == "folder")
            {
                var path = Paths.FirstOrDefault(Directory.Exists);
                if (path is null) throw new DirectoryNotFoundException("未找到常见目录。请在应用设置中确认实际存储位置。");
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                { ArgumentList = { path }, UseShellExecute = true });
            }
            else Process.Start(new ProcessStartInfo(Target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开入口：" + ex.Message + "\n\n" + Guidance, "打开入口失败", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

public sealed class ReviewViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cts;
    private bool _busy;
    private string _status = "点击扫描，查看 B 级项目的占用与处理建议。";
    public ObservableCollection<ReviewItem> Items { get; } = [];
    public ObservableCollection<ReviewItem> ScanItems { get; } = [];
    public ObservableCollection<ReviewItem> ToolItems { get; } = [];
    public string ScanTabTitle => $"占用扫描 · {ScanItems.Count}";
    public string ToolsTabTitle => $"未取得占用与工具入口 · {ToolItems.Count}";
    private void RefreshGroups()
    {
        OnPropertyChanged(nameof(ScanTabTitle));
        OnPropertyChanged(nameof(ToolsTabTitle));
    }
    private void Route(ReviewItem item, bool hasSize)
    {
        if (hasSize) { ToolItems.Remove(item); if (!ScanItems.Contains(item)) ScanItems.Add(item); }
        else { ScanItems.Remove(item); if (!ToolItems.Contains(item)) ToolItems.Add(item); }
        RefreshGroups();
    }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); ScanCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); } }

    public ReviewViewModel()
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var drive = Path.GetPathRoot(win) ?? @"C:\";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Add(string name, string impact, string guidance, string label, string target, params string[] paths)
            => Items.Add(new ReviewItem { Name = name, Impact = impact, Guidance = guidance, EntryLabel = label, Target = target, Paths = paths });
        Add("旧系统文件 · Windows.old", "清理后可能无法回退系统版本；先检查旧目录中是否有需要取回的个人文件。", "进入 存储 → 临时文件，检查以前的 Windows 安装。", "打开存储设置", "ms-settings:storagesense", Path.Combine(drive, "Windows.old"));
        Add("休眠文件", "处理前请先保存工作。关闭休眠后，电脑将无法使用休眠和快速启动，依赖休眠文件的混合睡眠断电保护也会失效。笔记本若依赖低电量休眠或经常离电使用，建议保留。普通睡眠不能代替休眠的断电保护。", "不要手动删除 hiberfil.sys。正确做法是按微软说明，以管理员身份运行 powercfg /hibernate off，由 Windows 关闭休眠并释放文件；需要时可用 powercfg /hibernate on 恢复，但会重新占用空间。本页仅展示文件大小并打开电源设置，不执行上述命令。", "打开电源设置", "ms-settings:powersleep", Path.Combine(drive, "hiberfil.sys"));
        Add("系统还原点", "删除还原点会失去对应的恢复能力；调整上限也可能删除旧还原点。", "在系统保护 → 配置中查看磁盘使用量。", "打开系统保护", "protection");
        Add("内存转储", "蓝屏分析可能需要这些文件，正在排障时建议保留。", "进入 存储 → 临时文件，检查系统错误内存转储文件。", "打开存储设置", "ms-settings:storagesense", Path.Combine(win, "MEMORY.DMP"));
        Add("旧驱动包", "旧驱动可能仍用于设备或驱动回退；不能仅按日期判断是否可删。", "设备管理器可查看设备和驱动；此入口不会自动删除驱动包。", "打开设备管理器", "device");
        Add("Windows 组件存储", "仅使用 Windows 组件维护功能；目录大小不等于可释放空间。", "在 Windows 存储设置中查看系统清理建议；本版不执行 DISM。", "打开存储设置", "ms-settings:storagesense");
        Add("应用与卸载残留", "应用目录可能包含共享组件、配置和用户数据。", "先在 已安装的应用 中正常卸载；本版不判断和删除残留。", "打开应用设置", "ms-settings:appsfeatures");
        Items.Add(new ReviewItem { Name = "WSL / Docker 虚拟磁盘", Impact = "虚拟磁盘包含发行版、容器和数据卷。文件逻辑大小不等于实际占用或可释放空间。", Guidance = "Docker 请使用 Settings → Resources 中的存储管理；WSL 请按微软官方文档处理。只检测常见默认位置，其他位置需在应用中确认。", EntryLabel = "打开 WSL 官方说明", Target = "https://learn.microsoft.com/windows/wsl/disk-space", Paths = [Path.Combine(local, "Docker", "wsl"), Path.Combine(local, "Packages"), Path.Combine(local, "wsl")], VirtualDisksOnly = true });
        Add("微信 / QQ 文件", "聊天文件属于个人数据，清理或迁移前先备份并验证聊天记录。", "这里只检测常见文档目录；请在微信或 QQ 的文件管理设置中确认实际位置并处理。", "查看空间明细", "chat-details", Path.Combine(docs, "xwechat_files"), Path.Combine(docs, "WeChat Files"), Path.Combine(docs, "Tencent Files"));
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? Path.Combine(user, "OneDrive");
        Add("OneDrive 本地副本", "普通删除会同步到云端；释放本地空间后，离线时可能无法打开文件。", "在文件资源管理器中确认同步完成，再右键选择“释放空间”。本版不遍历云端占位文件，避免触发下载。", "打开 OneDrive 目录", "folder", oneDrive);
        Add("游戏 / 虚拟机", "迁移前退出游戏或关闭虚拟机，迁移后验证能否启动。", "游戏请在 Steam / Epic 中使用移动功能；虚拟机使用对应平台的导出或迁移。应用设置仅用于定位已安装应用。", "打开应用设置", "ms-settings:appsfeatures");
        foreach (var item in Items)
        {
            if (item.Target == "folder")
            {
                // This catalog entry inspects cloud folder presence only.
                item.SizeText = "本版未统计本地占用 · 请在 OneDrive 中查看同步状态并释放空间";
                ToolItems.Add(item);
            }
            else if (item.CanMeasure) ScanItems.Add(item);
            else
            {
                item.SizeText = ToolReason(item);
                ToolItems.Add(item);
            }
        }
        RefreshGroups();
    }

    private static string ToolReason(ReviewItem item) => item.Target switch
    {
        "protection" => "本版未统计 · 还原点使用专门的系统存储，需在系统保护中查看",
        "device" => "本版未统计 · 驱动目录大小不能代表可安全删除的旧驱动",
        _ when item.Name == "Windows 组件存储" => "本版未统计 · 组件存在共享文件，需由 Windows 分析",
        _ => "本版未统计 · 缺少统一可靠的存储位置，请在对应应用中查看"
    };

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;
        var candidates = Items.Where(i => i.CanMeasure).ToArray();
        foreach (var item in candidates) { item.SizeText = "等待扫描"; Route(item, true); }
        try
        {
            foreach (var item in candidates)
            {
                Status = "正在检查：" + item.Name;
                var result = await Task.Run(() => Inspect(item, cts.Token), cts.Token);
                item.SizeText = result.Message;
                Route(item, result.HasSize);
            }
            Status = $"扫描完成：{ScanItems.Count} 项取得文件大小，{ToolItems.Count} 项转至工具入口。大小不代表可释放空间。";
        }
        catch (OperationCanceledException)
        {
            foreach (var item in Items.Where(i => i.SizeText == "等待扫描")) item.SizeText = "已取消 · 尚未扫描";
            Status = "扫描已取消，已完成的结果保留。";
        }
        catch (Exception ex) { Status = "扫描失败：" + ex.Message; }
        finally { _cts = null; IsBusy = false; }
    }

    private sealed record Inspection(bool HasSize, string Message);

    private static Inspection Inspect(ReviewItem item, CancellationToken token)
    {
        long bytes = 0; int count = 0, visited = 0; bool partial = false, found = false;
        var reasons = new HashSet<string>();
        var timer = Stopwatch.StartNew();
        var pending = new Stack<string>(item.Paths.Distinct(StringComparer.OrdinalIgnoreCase));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            if (visited++ >= 100000 || timer.Elapsed > TimeSpan.FromSeconds(8)) { partial = true; reasons.Add("达到扫描上限"); break; }
            var path = pending.Pop();
            try
            {
                var attr = File.GetAttributes(path);
                found = true;
                if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { partial = true; reasons.Add("跳过链接或离线文件"); continue; }
                if ((attr & FileAttributes.Directory) == 0)
                {
                    if (!item.VirtualDisksOnly || Path.GetExtension(path).Equals(".vhdx", StringComparison.OrdinalIgnoreCase))
                    { bytes += new FileInfo(path).Length; count++; }
                }
                else
                {
                    foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    {
                        token.ThrowIfCancellationRequested();
                        if (pending.Count >= 100000 || timer.Elapsed > TimeSpan.FromSeconds(8)) { partial = true; reasons.Add("达到扫描上限"); break; }
                        pending.Push(child);
                    }
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { partial = true; reasons.Add("权限不足"); }
            catch (IOException) { partial = true; reasons.Add("文件访问失败"); }
        }
        var reason = string.Join("、", reasons);
        if (partial) return new(count > 0, count > 0
            ? $"已统计文件大小 {ByteSizeFormatter.Format(bytes)} · 不完整：{reason}"
            : $"未取得占用 · {reason}。可重新扫描或使用下方入口");
        if (!found || (item.VirtualDisksOnly && count == 0))
            return new(false, "未发现目标文件 · 仅检查常见位置，自定义位置需在应用中确认");
        return new(true, $"文件逻辑大小 {ByteSizeFormatter.Format(bytes)} · {count:N0} 个文件（常见位置）");

    }

    public void Dispose() => _cts?.Cancel();
}
