using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace CleanC.App;
public sealed record QqContact(string Kind,string Id,string Name);
public sealed record QqAttachment(string Type,string Name,string MessageId,string LocalPath,string State);
public sealed class QqReadWindow:Window
{
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    readonly DataGrid contacts=Table(),attachments=Table();
    readonly TextBox config=new(){MinWidth=480,Text=""};
    readonly Button connect=new(){Content="只读连接"},history=new(){Content="读取选中会话的附件（最近100条）"};
    HttpClient? client;string? self;
    static DataGrid Table()=>new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,SelectionMode=DataGridSelectionMode.Single};
    static void Col(DataGrid t,string name,string field,double w)=>t.Columns.Add(new DataGridTextColumn{Header=name,Binding=new Binding(field),Width=w});
    public QqReadWindow()
    {
        Title="CleanC · QQ 只读会话验证";Width=1100;Height=760;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=new DockPanel{Margin=new Thickness(24)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);p.Children.Add(top);
        top.Children.Add(new TextBlock{Text="QQ 联系人与附件 · 只读验证",FontSize=24,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock{Text="复用已运行的 SnowLuma 本机服务。只读取身份、联系人与明确选择会话的历史；不发送消息、不下载附件、不修改桥接配置。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,12)});
        var bar=new WrapPanel();bar.Children.Add(config);var choose=new Button{Content="选择连接配置"};bar.Children.Add(choose);bar.Children.Add(connect);bar.Children.Add(history);top.Children.Add(bar);top.Children.Add(status);
        history.IsEnabled=false;
        var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="联系人与群",Content=contacts});tabs.Items.Add(new TabItem{Header="选中会话附件",Content=attachments});p.Children.Add(tabs);
        Col(contacts,"类型","Kind",90);Col(contacts,"真实QQ号 / 群号","Id",210);Col(contacts,"名称","Name",650);
        Col(attachments,"类型","Type",80);Col(attachments,"文件名","Name",220);Col(attachments,"消息ID","MessageId",150);Col(attachments,"本地路径","LocalPath",300);Col(attachments,"核对状态","State",230);
        Content=p;status.Text="请先自行启动原有 QQ 连接服务，再点击只读连接。此窗口不自动启动机器人。";
        choose.Click+=(_,_)=>{var d=new OpenFileDialog{Filter="连接配置|*.json"};if(d.ShowDialog(this)==true)config.Text=d.FileName;};
        connect.Click+=async(_,_)=>{
            connect.IsEnabled=history.IsEnabled=false;contacts.ItemsSource=null;attachments.ItemsSource=null;
            try{
                using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(config.Text));
                var server=doc.RootElement.GetProperty("networks").GetProperty("httpServers").EnumerateArray().First();
                if(server.TryGetProperty("enabled",out var enabled)&&enabled.ValueKind==JsonValueKind.False)throw new IOException("配置中的 HTTP 服务未启用。");
                var host=Text(server,"host");if(host is not ("127.0.0.1" or "localhost" or "0.0.0.0" or "::1" or "::"))throw new IOException("仅支持本机回环连接。");
                var port=server.GetProperty("port").GetInt32();if(port<1||port>65535)throw new IOException("端口无效。");
                var path=server.TryGetProperty("path",out var pv)?pv.GetString()??"/":"/";
                if(!path.StartsWith('/')||path.Contains("..")||path.Contains('?')||path.Contains('#')||path.Contains('\\'))throw new IOException("服务路径格式不支持。");
                client?.Dispose();client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri($"http://127.0.0.1:{port}"+path.TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(10)};
                var token=Text(server,"accessToken");if(!string.IsNullOrEmpty(token))client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
                var login=await Call("get_login_info",new{});self=Text(login,"user_id");
                var list=new List<QqContact>();
                var friends=await Call("get_friend_list",new{});foreach(var e in friends.EnumerateArray())list.Add(new("私聊",Text(e,"user_id"),Text(e,"remark") is {Length:>0} r?r:Text(e,"nickname")));
                var groups=await Call("get_group_list",new{});foreach(var e in groups.EnumerateArray())list.Add(new("群聊",Text(e,"group_id"),Text(e,"group_name")));
                contacts.ItemsSource=list;history.IsEnabled=true;status.Text=$"已连接 QQ {self}，读取 {list.Count} 个联系人/群。请选择一个会话再读取附件；此列表不表示已统计完整磁盘占用。";
            }catch(Exception ex){client?.Dispose();client=null;status.Text="连接未成功："+SafeError(ex)+"。请确认原有服务正在运行、QQ 已登录且配置匹配。";}
            finally{connect.IsEnabled=true;}
        };
        history.Click+=async(_,_)=>{
            if(contacts.SelectedItem is not QqContact selected){status.Text="请先选中一个联系人或群。";return;}
            if(selected.Kind=="私聊"&&selected.Id==self){status.Text="目标是当前连接账号自身，请连接测试发送方账号后选择对方。";return;}
            if(!long.TryParse(selected.Id,out var id)){status.Text="无效会话号。";return;}
            history.IsEnabled=connect.IsEnabled=false;attachments.ItemsSource=null;
            try{
                var data=selected.Kind=="群聊"?await Call("get_group_msg_history",new{group_id=id,count=100}):await Call("get_friend_msg_history",new{user_id=id,count=100});
                var rows=new List<QqAttachment>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long localBytes=0;
                foreach(var msg in data.GetProperty("messages").EnumerateArray()){
                    if(!msg.TryGetProperty("message",out var segments)||segments.ValueKind!=JsonValueKind.Array)continue;
                    foreach(var segment in segments.EnumerateArray()){
                        var type=Text(segment,"type");if(type is not ("file" or "image" or "record" or "video"))continue;
                        var d=segment.GetProperty("data");var name=Text(d,"name");if(name.Length==0)name=Text(d,"file_name");
                        var candidate=Text(d,"path");if(candidate.Length==0)candidate=Text(d,"file");
                        var local="";bool exists=false;try{if(Path.IsPathFullyQualified(candidate)&&!candidate.StartsWith(@"\\")&&File.Exists(candidate)){local=candidate;exists=true;if(paths.Add(local))localBytes+=new FileInfo(local).Length;}}catch(IOException){}
                        rows.Add(new(type,name.Length>0?name:"未提供文件名",Text(msg,"message_id"),local,exists?"本地文件存在（仅此消息范围）":"未取得本地文件路径，不计入占用"));
                    }
                }
                attachments.ItemsSource=rows;tabs.SelectedIndex=1;status.Text=$"{selected.Kind} {selected.Id}：本批 {rows.Count} 条附件记录，核对到 {paths.Count} 个本地文件，共 {CleanC.Core.ByteSizeFormatter.Format(localBytes)}。仅最近100条消息，不能代表该会话总占用。";
            }catch(Exception ex){status.Text="读取附件失败："+SafeError(ex);}
            finally{history.IsEnabled=client!=null;connect.IsEnabled=true;}
        };
        Closed+=(_,_)=>client?.Dispose();
    }
    static string Text(JsonElement e,string name)=>e.TryGetProperty(name,out var v)?v.ValueKind==JsonValueKind.String?v.GetString()??"":v.ToString():"";
    static string SafeError(Exception e)=>e is HttpRequestException?"本地服务不可用或请求被拒绝":e is TaskCanceledException?"请求超时":e is JsonException or FormatException?"服务或配置格式不匹配":e is IOException?e.Message:"连接或响应格式不匹配";
    async Task<JsonElement> Call(string action,object parameters)
    {
        if(action is not ("get_login_info" or "get_friend_list" or "get_group_list" or "get_friend_msg_history" or "get_group_msg_history"))throw new InvalidOperationException("不支持此操作");
        using var response=await client!.PostAsJsonAsync(action,parameters);response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if(Text(doc.RootElement,"status")!="ok"||Text(doc.RootElement,"retcode")!="0")throw new IOException("服务返回失败，代码："+Text(doc.RootElement,"retcode"));
        return doc.RootElement.GetProperty("data").Clone();
    }
}
