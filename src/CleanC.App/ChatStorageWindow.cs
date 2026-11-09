using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CleanC.Core;
using Microsoft.Win32;

namespace CleanC.App;

public sealed class ChatStorageWindow : Window
{
    private readonly List<ChatStorageRoot> _roots;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) };
    private readonly ComboBox _filter = new() { MinWidth = 220, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _scan = new() { Content = "重新扫描" };
    private readonly Button _cancel = new() { Content = "取消扫描", IsEnabled = false };
    private readonly Button _add = new() { Content = "添加实际存储目录" };
    private readonly TabControl _tabs = new();
    private readonly List<DataGrid> _tables = [];
    private CancellationTokenSource? _cts;
    private bool _closed;

    public ChatStorageWindow(IEnumerable<string> paths)
    {
        Title = "CleanC · 微信 / QQ 空间明细";
        Width = 1100; Height = 780; MinWidth = 900; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(243, 246, 245));
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        _roots = paths.Select(p => new ChatStorageRoot(Path.GetFileName(p).Equals("Tencent Files", StringComparison.OrdinalIgnoreCase) ? "QQ" : "微信", p)).ToList();
        var panel = new Grid { Margin = new Thickness(24) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "聊天文件 · 查看空间花在哪里", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray });
        heading.Children.Add(new TextBlock { Text = "按账号目录与类型统计；附件目录标识无法可靠映射群名，不能据此区分群聊和私聊。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 16), Foreground = Brushes.SlateGray });
        panel.Children.Add(heading);
        var toolbar = new WrapPanel();
        foreach (var button in new[] { _scan, _cancel, _add })
        { button.Padding = new Thickness(16, 9, 16, 9); button.Margin = new Thickness(0, 0, 8, 0); toolbar.Children.Add(button); }
        toolbar.Children.Add(_filter);
        Grid.SetRow(toolbar, 1); panel.Children.Add(toolbar);
        Grid.SetRow(_status, 2); panel.Children.Add(_status);
        foreach (var title in new[] { "按账号与文件类型", "附件存储目录 · 群名未识别", "最大文件 · 前 100 项" })
        {
            var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 38, Background = Brushes.White, BorderThickness = new Thickness(0), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
            void Col(string header, string binding, double width) => table.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding), Width = width });
            Col("应用", "App", 65); Col("账号或顶层目录", "Account", 170); Col("类别", "Category", 185);
            table.Columns.Add(new DataGridTextColumn { Header = "大小", Binding = new Binding("SizeText"), SortMemberPath = "Bytes", Width = 95 });
            Col("文件数", "Files", 75);
            table.Columns.Add(new DataGridTextColumn { Header = "路径（其他数据不保证会话归属）", Binding = new Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _tables.Add(table); _tabs.Items.Add(new TabItem { Header = title, Content = table, Padding = new Thickness(14, 10, 14, 10) });
        }
        Grid.SetRow(_tabs, 3); panel.Children.Add(_tabs);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var open = new Button { Content = "打开选中项所在目录", Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => OpenSelected();
        footer.Children.Add(open);
        footer.Children.Add(new TextBlock { Text = "统计文件逻辑大小，按扩展名和目录位置分类；图片目录中的文件可能加密。共享数据不分摊到群聊。请在微信 / QQ 自带存储管理中核对后清理；数据库和备份需保留。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 0) });
        Grid.SetRow(footer, 4); panel.Children.Add(footer);
        Content = panel;
        _filter.SelectionChanged += (_, _) => ApplyFilter();
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _add.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "选择微信或 QQ 设置中显示的实际存储目录" };
            if (picker.ShowDialog(this) == true && !_roots.Any(r => r.Path.Equals(picker.FolderName, StringComparison.OrdinalIgnoreCase)))
            {
                _roots.Add(new ChatStorageRoot("自选目录", picker.FolderName));
                foreach (var table in _tables) table.ItemsSource = null;
                _status.Text = "已添加目录，旧统计已清空。请点击重新扫描；自选目录的顶层子目录不一定是账号。";
            }
        };
        Loaded += async (_, _) => await ScanAsync();
        Closed += (_, _) => { _closed = true; _cts?.Cancel(); };
    }

    private async Task ScanAsync()
    {
        if (_cts is not null) return;
        using var cts = new CancellationTokenSource(); _cts = cts;
        _scan.IsEnabled = _add.IsEnabled = false; _cancel.IsEnabled = true;
        foreach (var table in _tables) table.ItemsSource = null;
        _filter.ItemsSource = null;
        _status.Text = "正在只读统计文件大小…";
        try
        {
            var result = await Task.Run(() => ChatStorageAnalyzer.Analyze(_roots, cts.Token), cts.Token);
            if (_closed) return;
            _tables[0].ItemsSource = result.Categories;
            _tables[1].ItemsSource = result.AttachmentFolders;
            _tables[2].ItemsSource = result.LargestFiles;
            _filter.ItemsSource = new[] { "全部账号 / 目录" }.Concat(result.Categories.Select(r => r.App + " / " + r.Account).Distinct()).ToArray();
            _filter.SelectedIndex = 0;
            _status.Text = $"已读取 {result.Files:N0} 个文件，文件大小合计 {ByteSizeFormatter.Format(result.TotalBytes)}。群名尚未可靠识别。" +
                (result.Notices.Count > 0 ? " 提示：" + string.Join("；", result.Notices) : "");
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "扫描已取消，未展示不完整的本次结果。"; }
        catch (Exception ex) { if (!_closed) _status.Text = "扫描失败：" + ex.Message; }
        finally { _cts = null; _scan.IsEnabled = _add.IsEnabled = true; _cancel.IsEnabled = false; }
    }
    private void ApplyFilter()
    {
        var selected = _filter.SelectedItem as string;
        foreach (var table in _tables)
        {
            if (table.ItemsSource is null) continue;
            var view = CollectionViewSource.GetDefaultView(table.ItemsSource);
            view.Filter = o => selected is null || selected == "全部账号 / 目录" || o is ChatStorageRow r && r.App + " / " + r.Account == selected;
        }
    }
    private void OpenSelected()
    {
        if (_tabs.SelectedIndex < 0 || _tables[_tabs.SelectedIndex].SelectedItem is not ChatStorageRow row || !Path.IsPathFullyQualified(row.Path))
        { MessageBox.Show(this, "请在附件目录或大文件页面选择一项；类型汇总行没有单一对应目录。", "选择文件位置"); return; }
        try
        {
            var folder = Directory.Exists(row.Path) ? row.Path : Path.GetDirectoryName(row.Path);
            if (folder is null || !Directory.Exists(folder)) throw new DirectoryNotFoundException("目录已不存在，请重新扫描。");
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                { UseShellExecute = true, ArgumentList = { folder } });
        }
        catch (Exception ex) { MessageBox.Show(this, "无法打开目录：" + ex.Message, "打开失败"); }
    }
}
