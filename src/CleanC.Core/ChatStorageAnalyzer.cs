using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CleanC.Core;

public sealed record ChatStorageRoot(string App, string Path);
public sealed record ChatStorageRow(string App, string Account, string Category, string Path, long Bytes, int Files)
{
    public string SizeText => ByteSizeFormatter.Format(Bytes);
}
public sealed record ChatStorageAnalysis(IReadOnlyList<ChatStorageRow> Categories,
    IReadOnlyList<ChatStorageRow> AttachmentFolders, IReadOnlyList<ChatStorageRow> LargestFiles,
    long TotalBytes, int Files, IReadOnlyList<string> Notices);

public static class ChatStorageAnalyzer
{
    public static string Classify(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        bool Has(params string[] names) => parts.Any(p => names.Contains(p, StringComparer.OrdinalIgnoreCase));
        var ext = Path.GetExtension(relative).ToLowerInvariant();
        if (Has("Backup", "BackupFiles", "bak")) return "备份";
        if (Has("Msg", "db_storage", "nt_db") || ext is ".db" or ".dbc" or ".db-wal" or ".db-shm" or ".db-journal") return "数据库与消息索引";
        if (Has("Cache", "AppWebCache", "nt_temp", "temp", "Code Cache", "GPUCache")) return "缓存目录";
        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".heic" || Has("Image", "Images", "image", "Thumb")) return "图片及图片目录文件";
        if (ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" || Has("Video")) return "视频及视频目录文件";
        if (ext is ".mp3" or ".wav" or ".amr" or ".silk" or ".ogg" or ".m4a" || Has("Audio", "Voice")) return "语音及音频目录文件";
        if (Has("FileRecv", "File", "Files") || ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".pptx" or ".zip" or ".7z" or ".rar") return "附件与文档";
        return "其他或未确认类型";
    }

    public static ChatStorageAnalysis Analyze(IReadOnlyList<ChatStorageRoot> roots, CancellationToken token)
    {
        var categories = new Dictionary<(string App, string Account, string Type), (long Bytes, int Files)>();
        var folders = new Dictionary<(string App, string Account, string Path), (long Bytes, int Files)>();
        var largest = new PriorityQueue<ChatStorageRow, long>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notices = new HashSet<string>();
        long total = 0; int files = 0, visited = 0;
        var timer = Stopwatch.StartNew();
        foreach (var entry in roots)
        {
            token.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(entry.Path);
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                if (visited++ >= 300000 || timer.Elapsed > TimeSpan.FromSeconds(45))
                { notices.Add("达到扫描上限，结果不完整"); pending.Clear(); break; }
                var path = pending.Pop();
                if (!seen.Add(Path.GetFullPath(path))) continue;
                try
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                    { notices.Add("已跳过链接或离线文件"); continue; }
                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        foreach (var child in Directory.EnumerateFileSystemEntries(path))
                        {
                            token.ThrowIfCancellationRequested();
                            if (pending.Count >= 300000 || timer.Elapsed > TimeSpan.FromSeconds(45))
                            { notices.Add("达到扫描上限，结果不完整"); break; }
                            pending.Push(child);
                        }
                        continue;
                    }
                    var size = new FileInfo(path).Length;
                    var relative = Path.GetRelativePath(root, path);
                    var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var account = parts.Length > 1 ? parts[0] : "根目录或共享数据";
                    var category = Classify(relative);
                    var key = (entry.App, account, category);
                    var previous = categories.GetValueOrDefault(key);
                    categories[key] = (previous.Bytes + size, previous.Files + 1);
                    total += size; files++;
                    // Only bucket known attachment directory structure; identifiers are not group names.
                    var index = Array.FindIndex(parts, p => p.Equals("MsgAttach", StringComparison.OrdinalIgnoreCase));
                    var bucket = "共享或未确认归属";
                    if (index >= 0 && parts.Length > index + 2 && Regex.IsMatch(parts[index + 1], "^[a-fA-F0-9]{32}$"))
                        bucket = Path.Combine(root, Path.Combine(parts.Take(index + 2).ToArray()));
                    var fk = (entry.App, account, bucket);
                    var old = folders.GetValueOrDefault(fk);
                    folders[fk] = (old.Bytes + size, old.Files + 1);
                    var row = new ChatStorageRow(entry.App, account, category, path, size, 1);
                    largest.Enqueue(row, size);
                    if (largest.Count > 100) largest.Dequeue();
                }
                catch (FileNotFoundException) { notices.Add("部分文件已变动或常见目录不存在"); }
                catch (DirectoryNotFoundException) { notices.Add("部分常见目录不存在"); }
                catch (UnauthorizedAccessException) { notices.Add("部分目录权限不足，结果不完整"); }
                catch (IOException) { notices.Add("部分文件访问失败，结果不完整"); }
            }
        }
        return new(
            categories.Select(p => new ChatStorageRow(p.Key.App, p.Key.Account, p.Key.Type, "", p.Value.Bytes, p.Value.Files)).OrderByDescending(r => r.Bytes).ToArray(),
            folders.Select(p => new ChatStorageRow(p.Key.App, p.Key.Account, p.Key.Path == "共享或未确认归属" ? p.Key.Path : "未识别会话附件目录", p.Key.Path, p.Value.Bytes, p.Value.Files)).OrderByDescending(r => r.Bytes).ToArray(),
            largest.UnorderedItems.Select(p => p.Element).OrderByDescending(r => r.Bytes).ToArray(),
            total, files, notices.ToArray());
    }
}
