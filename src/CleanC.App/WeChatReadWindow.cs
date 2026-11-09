using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
namespace CleanC.App;
public sealed record WeChatIdentity(string Type,string Id,string Name,string State);
public sealed class WeChatReadWindow:Window
{
    public WeChatReadWindow()
    {
        Title="CleanC · 微信只读身份验证";Width=1000;Height=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel{Margin=new Thickness(24)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(new TextBlock{Text="微信联系人与群名 · 本地只读验证",FontSize=24,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock{Text="选择现有 WeChatAgent 的 wechat-io-config.json；凭据仅在本次请求内使用。只调用读取服务，不启动 Hook，不访问发送接口。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
        var bar=new WrapPanel();var file=new TextBox{MinWidth=480};var choose=new Button{Content="选择连接配置"};var read=new Button{Content="读取身份与联系人"};bar.Children.Add(file);bar.Children.Add(choose);bar.Children.Add(read);top.Children.Add(bar);
        var status=new TextBlock{Text="需要原有微信读取服务已启动。当前只核对会话身份，不计算单群完整占用。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};top.Children.Add(status);
        var table=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false};panel.Children.Add(table);
        foreach(var v in new[]{("类型","Type",90),("原始会话标识","Id",280),("名称","Name",250),("说明","State",280)})table.Columns.Add(new DataGridTextColumn{Header=v.Item1,Binding=new Binding(v.Item2),Width=v.Item3});
        choose.Click+=(_,_)=>{var d=new OpenFileDialog{Filter="读取连接配置|wechat-io-config.json|JSON|*.json"};if(d.ShowDialog(this)==true)file.Text=d.FileName;};
        read.Click+=async(_,_)=>{
            read.IsEnabled=false;table.ItemsSource=null;
            try{
                using var config=JsonDocument.Parse(await File.ReadAllTextAsync(file.Text));
                var url=config.RootElement.GetProperty("reader_base_url").GetString()!;
                var endpoint=new Uri(url);
                if(endpoint.Scheme!="http"||endpoint.Host is not ("localhost" or "127.0.0.1")||endpoint.UserInfo.Length>0||endpoint.Query.Length>0||endpoint.AbsolutePath!="/")throw new IOException("只支持现有读取服务的本机 HTTP 根地址。");
                using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(10)};
                client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",config.RootElement.GetProperty("reader_token").GetString());
                using var stateReply=await client.GetAsync("api/v1/reader/status");stateReply.EnsureSuccessStatusCode();
                using var state=JsonDocument.Parse(await stateReply.Content.ReadAsStringAsync());
                if(!state.RootElement.GetProperty("success").GetBoolean()||state.RootElement.GetProperty("backend").GetString()!="weflow-cli-nt")throw new IOException("读取服务不匹配。");
                using var reply=await client.GetAsync("api/v1/contacts?limit=500");reply.EnsureSuccessStatusCode();
                using var body=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());if(!body.RootElement.GetProperty("success").GetBoolean())throw new IOException("联系人查询失败。");
                var list=new List<WeChatIdentity>();
                foreach(var row in body.RootElement.GetProperty("contacts").EnumerateArray()){
                    var id=row.GetProperty("username").GetString()??"";
                    var ambiguous=row.TryGetProperty("ambiguous",out var a)&&a.ValueKind==JsonValueKind.True;
                    list.Add(new(id.EndsWith("@chatroom")?"群聊":"私聊",id,row.GetProperty("displayName").GetString()??"",ambiguous?"名称可能重复，请以原始标识核对":"原始微信标识，不是QQ群号"));
                }
                table.ItemsSource=list;status.Text=$"已读取 {list.Count} 项（最多500项，非完整通讯录保证）。群名与原始标识已分开；附件到本地文件的映射仍需验证。";
            }catch(HttpRequestException){status.Text="读取服务不可用或鉴权失败，请先确认原有服务和读取配置。";}
            catch(TaskCanceledException){status.Text="读取超时。";}
            catch(Exception){status.Text="读取配置或服务响应格式不匹配，请核对所选文件和服务版本。";}
            finally{read.IsEnabled=true;}
        };
        Content=panel;
    }
}
