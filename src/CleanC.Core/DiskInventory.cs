using System.Diagnostics;
namespace CleanC.Core;
public sealed record DiskEntry(string Path, long Bytes, int Files) { public string SizeText => ByteSizeFormatter.Format(Bytes); }
public sealed record DiskReport(IReadOnlyList<DiskEntry> Directories, IReadOnlyList<DiskEntry> LargestFiles, long ReadBytes, int ReadFiles, int Unreadable, int LinksSkipped, bool Limited);
public static class DiskInventory
{
    public static DiskReport Scan(string root, CancellationToken token)
    {
        var dirs = new Dictionary<string,(long Size,int Count)>(StringComparer.OrdinalIgnoreCase);
        var top = new PriorityQueue<DiskEntry,long>();
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(root));
        long bytes=0; int count=0, unreadable=0, links=0; bool limited=false;
        var timer=Stopwatch.StartNew();
        while(pending.Count>0)
        {
            token.ThrowIfCancellationRequested();
            if(count>=2000000 || timer.Elapsed>TimeSpan.FromSeconds(120)){limited=true;break;}
            var path=pending.Pop();
            try
            {
                var attr=File.GetAttributes(path);
                if((attr & (FileAttributes.ReparsePoint|FileAttributes.Offline))!=0){links++;continue;}
                if((attr & FileAttributes.Directory)!=0)
                {
                    foreach(var child in Directory.EnumerateFileSystemEntries(path))
                    {
                        token.ThrowIfCancellationRequested();
                        if(pending.Count>=500000 || timer.Elapsed>TimeSpan.FromSeconds(120)){limited=true;break;}
                        pending.Push(child);
                    }
                    continue;
                }
                var size=new FileInfo(path).Length;
                var parts=Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar);
                int depth=parts[0].Equals("Users",StringComparison.OrdinalIgnoreCase)?Math.Min(3,parts.Length-1):Math.Min(2,parts.Length-1);
                if(parts.Length>3 && parts[0].Equals("Users",StringComparison.OrdinalIgnoreCase) && parts[2].Equals("AppData",StringComparison.OrdinalIgnoreCase))depth=Math.Min(5,parts.Length-1);
                var key=depth==0?path:Path.Combine(root,Path.Combine(parts.Take(depth).ToArray()));
                var old=dirs.GetValueOrDefault(key);dirs[key]=(old.Size+size,old.Count+1);
                bytes+=size;count++;
                top.Enqueue(new(path,size,1),size);if(top.Count>100)top.Dequeue();
            }
            catch(UnauthorizedAccessException){unreadable++;}
            catch(IOException){unreadable++;}
        }
        return new(dirs.Select(p=>new DiskEntry(p.Key,p.Value.Size,p.Value.Count)).OrderByDescending(p=>p.Bytes).ToArray(),
            top.UnorderedItems.Select(p=>p.Element).OrderByDescending(p=>p.Bytes).ToArray(),bytes,count,unreadable,links,limited);
    }
}
public static class InstallerPolicy
{
    private static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".exe",".msi",".msix",".msixbundle",".appx",".appxbundle",".iso"};
    public static bool IsCandidate(string path,IEnumerable<string> roots)
    {
        var full=Path.GetFullPath(path);
        var systemRoots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)};
        if(systemRoots.Where(p=>!string.IsNullOrEmpty(p)).Any(p=>full.StartsWith(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))return false;
        if(!Extensions.Contains(Path.GetExtension(full)))return false;
        if(!roots.Any(r=>full.StartsWith(Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))return false;
        var allowed=roots.Select(Path.GetFullPath).ToArray();
        if(Path.GetExtension(full).Equals(".exe",StringComparison.OrdinalIgnoreCase) &&
           !full.StartsWith(allowed[0].TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) &&
           !allowed.Any(r=>Path.GetDirectoryName(full)!.Equals(r.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)))return false;
        var segments=full.Split(Path.DirectorySeparatorChar);
        if(segments.Any(s=>s.Equals(".dsh",StringComparison.OrdinalIgnoreCase)||s.Equals(".codex",StringComparison.OrdinalIgnoreCase)||s.Contains("deepseek",StringComparison.OrdinalIgnoreCase)))return false;
        var app=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        return !full.StartsWith(app,StringComparison.OrdinalIgnoreCase);
    }
    public static bool CanRecycle(string path,IEnumerable<string> roots,long length,DateTime modified)
    {
        if(!IsCandidate(path,roots))return false;
        var full=Path.GetFullPath(path);
        var current=Path.GetPathRoot(full)!;
        foreach(var part in Path.GetRelativePath(current,full).Split(Path.DirectorySeparatorChar))
        {
            current=Path.Combine(current,part);
            var a=File.GetAttributes(current);
            if((a&(FileAttributes.ReparsePoint|FileAttributes.System|FileAttributes.ReadOnly|FileAttributes.Offline))!=0)return false;
        }
        var info=new FileInfo(full);
        return info.Exists&&info.Length==length&&info.LastWriteTimeUtc==modified;
    }
}
