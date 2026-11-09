namespace CleanC.Core;

public sealed class PathGuard
{
    private static readonly char Sep = Path.DirectorySeparatorChar;
    private static readonly char AltSep = Path.AltDirectorySeparatorChar;
    private readonly IReadOnlyList<ProtectedPath> _protected;

    public PathGuard(IEnumerable<ProtectedPath> protectedPaths)
    {
        _protected = protectedPaths
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .Select(item => item with { Path = Normalize(item.Path) })
            .GroupBy(item => (item.Path, item.IncludeChildren))
            .Select(group => group.First())
            .ToArray();
    }

    public IReadOnlyList<ProtectedPath> ProtectedPaths => _protected;

    public static PathGuard CreateDefault()
    {
        var user = SafeFolder(Environment.SpecialFolder.UserProfile);
        var local = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
        var roaming = SafeFolder(Environment.SpecialFolder.ApplicationData);
        var windows = SafeFolder(Environment.SpecialFolder.Windows);
        var programFiles = SafeFolder(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = SafeFolder(Environment.SpecialFolder.ProgramFilesX86);
        var common = SafeFolder(Environment.SpecialFolder.CommonApplicationData);
        var desktop = SafeFolder(Environment.SpecialFolder.DesktopDirectory);
        var documents = SafeFolder(Environment.SpecialFolder.MyDocuments);
        var pictures = SafeFolder(Environment.SpecialFolder.MyPictures);
        var videos = SafeFolder(Environment.SpecialFolder.MyVideos);
        var music = SafeFolder(Environment.SpecialFolder.MyMusic);

        var protectedPaths = new List<ProtectedPath>();

        void Add(string? path, bool includeChildren, string reason)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var full = Normalize(path);
            if (string.IsNullOrWhiteSpace(full))
            {
                return;
            }

            if (protectedPaths.Any(item =>
                    item.Path.Equals(full, StringComparison.OrdinalIgnoreCase) &&
                    item.IncludeChildren == includeChildren))
            {
                return;
            }

            protectedPaths.Add(new ProtectedPath(full, includeChildren, reason));
        }

        var driveRoot = string.IsNullOrWhiteSpace(user) ? @"C:\" : Path.GetPathRoot(user) ?? @"C:\";
        Add(driveRoot, false, "磁盘根目录");
        Add(Path.Combine(driveRoot, "Users"), false, "用户目录根");
        Add(user, false, "当前用户目录");
        Add(local, false, "LocalAppData 根目录");
        Add(roaming, true, "Roaming 用户配置");
        Add(windows, true, "Windows 系统目录");
        Add(programFiles, true, "Program Files");
        Add(programFilesX86, true, "Program Files (x86)");
        Add(common, true, "ProgramData");
        Add(desktop, true, "桌面");
        Add(documents, true, "文档");
        Add(Path.Combine(user, "Downloads"), true, "下载");
        Add(pictures, true, "图片");
        Add(videos, true, "视频");
        Add(music, true, "音乐");
        Add(Path.Combine(user, "OneDrive"), true, "OneDrive");

        Add(Path.Combine(user, ".dsh"), true, "DeepSeek Harness 运行环境");
        Add(Path.Combine(local, "Programs"), true, "用户级程序目录");
        Add(Path.Combine(local, "Programs", "DeepSeek Harness"), true, "DeepSeek Harness 安装目录");
        Add(Path.Combine(local, "Packages"), true, "应用包数据");
        Add(Path.Combine(local, "CleanC"), true, "CleanC 自身日志与配置");
        Add(Path.Combine(documents, "deepseek-harness"), true, "当前工作区");
        Add(AppContext.BaseDirectory, true, "程序自身目录");

        Add(Path.Combine(driveRoot, "Users", "Public"), true, "公共用户目录");
        Add(Path.Combine(driveRoot, "$Recycle.Bin"), true, "回收站");
        Add(Path.Combine(driveRoot, "System Volume Information"), true, "系统卷信息");
        Add(Path.Combine(driveRoot, "Recovery"), true, "恢复分区目录");
        Add(Path.Combine(driveRoot, "Boot"), true, "启动目录");
        Add(Path.Combine(driveRoot, "EFI"), true, "EFI 目录");
        Add(Path.Combine(driveRoot, "Windows.old"), true, "旧版 Windows");

        try
        {
            var usersRoot = Path.Combine(driveRoot, "Users");
            if (Directory.Exists(usersRoot))
            {
                foreach (var otherUser in Directory.GetDirectories(usersRoot))
                {
                    if (!user.Equals(otherUser, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(otherUser, true, "其他用户目录");
                    }
                }
            }
        }
        catch
        {
            // 权限不足时忽略，当前用户的保护已经覆盖。
        }

        return new PathGuard(protectedPaths);
    }

    public bool IsPathAllowed(string path, RuleDefinition rule)
        => IsPathAllowed(path, rule, out _);

    public bool IsPathAllowed(string path, RuleDefinition rule, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rule.Root))
        {
            reason = "路径或规则为空";
            return false;
        }

        string full;
        string root;
        try
        {
            full = Normalize(path);
            root = Normalize(rule.Root);
        }
        catch (Exception ex)
        {
            reason = "路径无法解析：" + ex.Message;
            return false;
        }

        if (!IsUnderOrEqual(full, root))
        {
            reason = "不在规则允许的根目录内";
            return false;
        }

        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            reason = "拒绝删除规则根目录本身";
            return false;
        }

        if (IsProtectedPath(full, out var protectedReason))
        {
            reason = protectedReason;
            return false;
        }

        if (IsAncestorOfProtected(full))
        {
            reason = "该路径是受保护目录的父目录";
            return false;
        }

        if (ContainsProtectedName(full, out var nameReason))
        {
            reason = nameReason;
            return false;
        }

        return true;
    }

    public bool ValidateForDelete(string path, RuleDefinition rule, out string reason)
    {
        if (!IsPathAllowed(path, rule, out reason))
        {
            return false;
        }

        string full;
        string root;
        try
        {
            full = Normalize(path);
            root = Normalize(rule.Root);
        }
        catch (Exception ex)
        {
            reason = "路径无法解析：" + ex.Message;
            return false;
        }

        if (!File.Exists(full))
        {
            reason = Directory.Exists(full) ? "目标是目录，不是文件" : "文件不存在";
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(full);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                reason = "目标是链接/重解析点";
                return false;
            }

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                reason = "只读文件，跳过";
                return false;
            }

            if (attributes.HasFlag(FileAttributes.System))
            {
                reason = "系统文件，跳过";
                return false;
            }

            if (attributes.HasFlag(FileAttributes.Hidden))
            {
                reason = "隐藏文件，跳过";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = "无法读取文件属性：" + ex.Message;
            return false;
        }

        if (HasReparsePointInPath(root, full))
        {
            reason = "路径上存在链接/重解析点";
            return false;
        }

        return true;
    }

    public static string Normalize(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return "";
        }

        var full = Path.GetFullPath(expanded);
        if (full.Length > 3)
        {
            full = full.TrimEnd(Sep, AltSep);
        }

        return full;
    }

    private bool IsProtectedPath(string full, out string reason)
    {
        foreach (var item in _protected)
        {
            if (full.Equals(item.Path, StringComparison.OrdinalIgnoreCase) ||
                (item.IncludeChildren && IsUnderOrEqual(full, item.Path)))
            {
                reason = "受保护路径：" + item.Reason;
                return true;
            }
        }

        reason = "";
        return false;
    }

    private bool IsAncestorOfProtected(string full)
    {
        foreach (var item in _protected)
        {
            if (IsAncestorOf(full, item.Path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProtectedName(string full, out string reason)
    {
        var segments = full.Split([Sep, AltSep], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Equals(".dsh", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("deepseek-harness", StringComparison.OrdinalIgnoreCase))
            {
                reason = "路径包含受保护名称：" + segment;
                return true;
            }
        }

        reason = "";
        return false;
    }

    private static bool HasReparsePointInPath(string root, string full)
    {
        try
        {
            if (IsReparsePoint(root))
            {
                return true;
            }

            var relative = Path.GetRelativePath(root, full);
            var current = root;
            foreach (var segment in relative.Split([Sep, AltSep], StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (IsReparsePoint(current))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            // 读不到属性时按“有风险”处理，避免误删。
            return true;
        }
    }

    private static bool IsUnderOrEqual(string candidate, string root)
    {
        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root.EndsWith(Sep) || root.EndsWith(AltSep) ? root : root + Sep;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAncestorOf(string candidate, string child)
    {
        if (candidate.Equals(child, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = candidate.EndsWith(Sep) || candidate.EndsWith(AltSep)
            ? candidate
            : candidate + Sep;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch
        {
            return "";
        }
    }
}
