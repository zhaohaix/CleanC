using CleanC.Core;
var root = Path.Combine(AppContext.BaseDirectory, "chat-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void FileAt(string relative, int bytes) { var p = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, new byte[bytes]); }
try
{
    FileAt("account/FileStorage/MsgAttach/" + new string('a',32) + "/Image/photo.dat", 11);
    FileAt("account/FileStorage/MsgAttach/" + new string('b',32) + "/Video/video.mp4", 23);
    FileAt("account/Msg/message.db", 37);
    FileAt("account/Backup/photo.jpg", 41);
    FileAt("account/FileStorage/File/report.pdf", 47);
    FileAt("account/FileStorage/Cache/temp.jpg", 53);
    var downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
    var desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    var allowed=new[]{downloads,desktop};
    Check(InstallerPolicy.IsCandidate(Path.Combine(downloads,"setup.exe"),allowed),"downloaded exe candidate");
    Check(InstallerPolicy.IsCandidate(Path.Combine(desktop,"setup.msi"),allowed),"desktop installer candidate");
    Check(!InstallerPolicy.IsCandidate(Path.Combine(desktop,"PortableApp","app.exe"),allowed),"nested desktop portable programs must not be candidates");
    Check(!InstallerPolicy.IsCandidate(Path.Combine(downloads,"notes.txt"),allowed),"ordinary documents must not be candidates");
    Check(!InstallerPolicy.IsCandidate(Path.Combine(desktop,"deepseek-tool","setup.msi"),allowed),"development environment excluded");
    Check(!InstallerPolicy.IsCandidate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Installer","test.msi"),allowed),"system installer excluded");
    var disk=DiskInventory.Scan(root,CancellationToken.None);
    Check(disk.ReadFiles==6&&disk.ReadBytes==212&&disk.Directories.Sum(r=>r.Bytes)==212,"disk inventory reconciles file totals");
    Console.WriteLine("PASS installer candidate boundaries and disk inventory totals");
    var roots = new[] { new ChatStorageRoot("微信", root), new ChatStorageRoot("重复", Path.Combine(root, "account")) };
    var result = ChatStorageAnalyzer.Analyze(roots, CancellationToken.None);
    Check(result.Files == 6 && result.TotalBytes == 212, "overlapping roots must not double count");
    Check(result.Categories.Sum(r => r.Bytes) == result.TotalBytes, "category totals must reconcile");
    Check(result.AttachmentFolders.Sum(r => r.Bytes) == result.TotalBytes, "attachment totals must reconcile");
    Check(result.AttachmentFolders.Count(r => r.Category == "未识别会话附件目录") == 2, "attachment identifiers must stay separate");
    Check(result.AttachmentFolders.Any(r => r.Category == "共享或未确认归属" && r.Bytes == 178), "shared data must not be attributed to a conversation");
    Check(result.Categories.Single(r => r.Category == "备份").Bytes == 41, "backup photos must remain backup data");
    Check(result.Categories.Single(r => r.Category == "缓存目录").Bytes == 53, "cached photos must remain cache data");
    Check(result.LargestFiles.First().Bytes == 53, "largest files must sort by numeric size");
    Check(result.LargestFiles.All(r => r.Path.StartsWith(root)), "file paths must match inspected roots");
    Console.WriteLine("PASS classification, totals, shared data, attachment buckets, overlap deduplication and size sorting");
    for (var i=0;i<110;i++) FileAt("account/large/" + i + ".bin", i+100);
    result = ChatStorageAnalyzer.Analyze([new("微信",root)], CancellationToken.None);
    Check(result.LargestFiles.Count == 100 && result.LargestFiles.First().Bytes == 209 && result.LargestFiles.Last().Bytes == 110, "top 100 must retain the largest files");
    Console.WriteLine("PASS bounded top-100 selection");
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { ChatStorageAnalyzer.Analyze([new("微信",root)], cts.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) {}
    Console.WriteLine("PASS cancellation");
    result = ChatStorageAnalyzer.Analyze([new("微信",Path.Combine(root,"missing"))], CancellationToken.None);
    Check(result.Files == 0 && result.Notices.Count > 0, "missing paths must be explained");
    Console.WriteLine("PASS missing root reporting");
}
finally { Directory.Delete(root, true); }
