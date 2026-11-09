using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CleanC.Core;
using CleanC.App.Infrastructure;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace CleanC.App;
public sealed class InstallerRow:ObservableObject
{
    private bool _selected;
    public bool Selected {get=>_selected;set=>SetProperty(ref _selected,value);}
    public required string Path {get;init;}
    public long Bytes {get;init;}
    public DateTime Modified {get;init;}
    public string SizeText=>ByteSizeFormatter.Format(Bytes);
    public string Kind=>System.IO.Path.GetExtension(Path).Equals(".exe",StringComparison.OrdinalIgnoreCase)?"可执行文件（需确认是否安装包）":"安装包或磁盘镜像";
}
public sealed class DiskWindow:Window
{
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    readonly DataGrid dirs=Table(),large=Table(),install=Table();
    readonly Button scan=new(){Content="扫描 C 盘"},cancel=new(){Content="取消扫描"},packages=new(){Content="扫描安装文件候选"},recycle=new(){Content="将勾选项移入回收站"};
    readonly List<InstallerRow> candidates=[];
    readonly TabControl tabs=new();
    readonly string[] roots;
    CancellationTokenSource? cts;
    bool closed;
    static DataGrid Table()=>new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,IsReadOnly=true,Background=Brushes.White,RowHeight=36,HeadersVisibility=DataGridHeadersVisibility.Column};
    static void Column(DataGrid t,string h,string b,double w)=>t.Columns.Add(new DataGridTextColumn{Header=h,Binding=new Binding(b),Width=w,SortMemberPath=b=="SizeText"?"Bytes":b});
    public DiskWindow()
    {
        Title="CleanC · C 盘空间与安装文件";Width=1100;Height=780;MinWidth=900;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(243,246,245));FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
        var download=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders","{374DE290-123F-4565-9164-39C4925E467B}",null) as string;
        roots=[Environment.ExpandEnvironmentVariables(download??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)];
        var panel=new DockPanel{Margin=new Thickness(24)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(new TextBlock{Text="C 盘空间 · 先查看，再选择处理",FontSize=24,FontWeight=FontWeights.SemiBold,Foreground=Brushes.DarkSlateGray});
        top.Children.Add(new TextBlock{Text="目录与大文件仅供定位。安装文件候选来自 C 盘下载目录与桌面，默认不勾选；EXE 也可能是程序本体。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,12)});
        var buttons=new WrapPanel();top.Children.Add(buttons);
        foreach(var b in new[]{scan,cancel,packages,recycle}){b.Margin=new Thickness(0,0,10,0);b.Padding=new Thickness(14,9,14,9);buttons.Children.Add(b);}
        cancel.IsEnabled=false;top.Children.Add(status);
        panel.Children.Add(tabs);
        foreach(var pair in new[]{("目录占用",dirs),("大文件前100项",large),("可选择处理的安装文件",install)})
        {tabs.Items.Add(new TabItem{Header=pair.Item1,Content=pair.Item2,Padding=new Thickness(14,10,14,10)});}
        foreach(var t in new[]{dirs,large}){Column(t,"大小","SizeText",110);Column(t,"文件数","Files",90);Column(t,"完整路径","Path",780);t.MouseDoubleClick+=(_,_)=>Open(t.SelectedItem is DiskEntry e?e.Path:null);}
        install.IsReadOnly=false;
        install.Columns.Add(new DataGridCheckBoxColumn{Header="选择",Binding=new Binding("Selected"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=55});
        Column(install,"大小","SizeText",100);Column(install,"类型","Kind",240);Column(install,"完整路径","Path",600);
        foreach(var col in install.Columns.OfType<DataGridTextColumn>())col.IsReadOnly=true;
        scan.Click+=async(_,_)=>await ScanDisk();
        packages.Click+=async(_,_)=>await ScanInstallers();
        cancel.Click+=(_,_)=>cts?.Cancel();
        recycle.Click+=(_,_)=>Recycle();
        Closed+=(_,_)=>{closed=true;cts?.Cancel();};
        Content=panel;
        var drive=new DriveInfo("C");
        status.Text=$"C 盘已用 {ByteSizeFormatter.Format(drive.TotalSize-drive.TotalFreeSpace)}，可用 {ByteSizeFormatter.Format(drive.AvailableFreeSpace)}。点击扫描查看明细。";
    }
    void Busy(bool value){scan.IsEnabled=packages.IsEnabled=recycle.IsEnabled=!value;cancel.IsEnabled=value;}
    async Task ScanDisk()
    {
        if(cts!=null)return;using var token=new CancellationTokenSource();cts=token;Busy(true);status.Text="正在只读扫描 C 盘，最多 120 秒…";
        try{var r=await Task.Run(()=>DiskInventory.Scan(@"C:\",token.Token),token.Token);if(closed)return;
            dirs.ItemsSource=r.Directories;large.ItemsSource=r.LargestFiles;
            status.Text=$"已统计 {r.ReadFiles:N0} 个文件，逻辑大小合计 {ByteSizeFormatter.Format(r.ReadBytes)}；不可读 {r.Unreadable:N0} 项，跳过链接/离线文件 {r.LinksSkipped:N0} 项。"+(r.Limited?"已达到扫描上限，结果不完整。":"")+"硬链接可能重复计数；系统保留空间未计入，不能与磁盘已用直接相加比较。";}
        catch(OperationCanceledException){status.Text="扫描已取消。";}
        catch(Exception ex){status.Text="扫描失败："+ex.Message;}
        finally{cts=null;Busy(false);}
    }
    async Task ScanInstallers()
    {
        if(cts!=null)return;using var token=new CancellationTokenSource();cts=token;Busy(true);status.Text="正在查找 C 盘下载目录和桌面的安装文件候选…";
        try{
            var result=await Task.Run(()=>{
                var list=new List<InstallerRow>();var pending=new Stack<string>(roots.Where(p=>p.StartsWith(@"C:\",StringComparison.OrdinalIgnoreCase)));int skipped=0,visited=0;var timer=Stopwatch.StartNew();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while(pending.Count>0){
                    token.Token.ThrowIfCancellationRequested();if(visited++>200000||timer.Elapsed>TimeSpan.FromSeconds(45)){skipped++;break;}
                    var path=pending.Pop();if(!seen.Add(Path.GetFullPath(path)))continue;
                    try{var a=File.GetAttributes(path);if((a&(FileAttributes.ReparsePoint|FileAttributes.Offline))!=0){skipped++;continue;}
                        if((a&FileAttributes.Directory)!=0){foreach(var child in Directory.EnumerateFileSystemEntries(path)){if(pending.Count>=200000){skipped++;break;}pending.Push(child);}}
                        else if(InstallerPolicy.IsCandidate(path,roots)){var i=new FileInfo(path);list.Add(new(){Path=path,Bytes=i.Length,Modified=i.LastWriteTimeUtc});}
                    }catch(UnauthorizedAccessException){skipped++;}catch(IOException){skipped++;}
                }return(list,skipped);},token.Token);
            if(closed)return;candidates.Clear();candidates.AddRange(result.list.OrderByDescending(r=>r.Bytes));install.ItemsSource=candidates;tabs.SelectedIndex=2;
            status.Text=$"发现 {candidates.Count} 个候选，共 {ByteSizeFormatter.Format(candidates.Sum(r=>r.Bytes))}。默认不勾选，请核对路径与用途。"+(result.skipped>0?$" 有 {result.skipped} 项未扫描或达到上限，结果不完整。":"");
        }catch(OperationCanceledException){status.Text="安装文件扫描已取消。";}catch(Exception ex){status.Text="扫描失败："+ex.Message;}finally{cts=null;Busy(false);}
    }
    void Recycle()
    {
        install.CommitEdit();install.CommitEdit(DataGridEditingUnit.Row,true);
        var selected=candidates.Where(r=>r.Selected).ToArray();
        if(selected.Length==0){status.Text="请先勾选需要处理的安装文件。";return;}
        var paths=string.Join("\n",selected.Take(12).Select(r=>r.Path))+(selected.Length>12?"\n…其余 "+(selected.Length-12)+" 项":"");
        if(MessageBox.Show(this,$"将以下 {selected.Length} 个候选移入回收站：\n\n{paths}\n\nEXE 可能是程序本体。移入同盘回收站后通常不会立即释放 C 盘空间；可先恢复确认。是否继续？","确认移入回收站",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        int ok=0,failed=0;var errors=new List<string>();
        foreach(var row in selected)try{
            if(!InstallerPolicy.CanRecycle(row.Path,roots,row.Bytes,row.Modified))throw new IOException("文件已变化、路径受保护或属性不允许，请重新扫描。");
            // Windows shell recycling: no permanent-delete fallback.
            FileSystem.DeleteFile(row.Path,UIOption.AllDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException);
            if(File.Exists(row.Path))throw new IOException("文件仍存在，未确认移入回收站。");
            candidates.Remove(row);ok++;
        }catch(Exception ex){failed++;if(errors.Count<3)errors.Add(Path.GetFileName(row.Path)+"："+ex.Message);}
        install.ItemsSource=null;install.ItemsSource=candidates;
        status.Text=$"移入回收站 {ok} 项，失败或取消 {failed} 项。"+string.Join("；",errors);
    }
    void Open(string? path){if(path==null)return;try{var dir=Directory.Exists(path)?path:Path.GetDirectoryName(path);if(dir!=null)Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")){UseShellExecute=true,ArgumentList={dir}});}catch(Exception ex){status.Text="无法打开："+ex.Message;}}
}
