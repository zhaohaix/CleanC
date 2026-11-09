namespace CleanC.Core;

public sealed class Scanner
{
    private readonly PathGuard _guard;

    public Scanner(PathGuard guard)
    {
        _guard = guard;
    }

    public Task<ScanResult> ScanAsync(
        IReadOnlyList<RuleDefinition> rules,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Scan(rules, progress, cancellationToken), cancellationToken);
    }

    private ScanResult Scan(
        IReadOnlyList<RuleDefinition> rules,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var items = new List<ScanItem>();

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(ScanRule(rule, progress, cancellationToken));
        }

        var completed = DateTime.UtcNow;
        return new ScanResult
        {
            Items = items,
            CompletedAt = completed,
            Duration = completed - started
        };
    }

    private ScanItem ScanRule(
        RuleDefinition rule,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanTime = DateTime.UtcNow;
        var root = PathGuard.Normalize(rule.Root);

        var item = new ScanItem
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            RootPath = root,
            Risk = rule.Risk,
            RequiresAdmin = rule.RequiresAdmin,
            DefaultSelected = rule.DefaultSelected,
            Reason = rule.Reason,
            Impact = rule.Impact,
            MinAgeDays = rule.MinAgeDays,
            Recursive = rule.Recursive,
            Patterns = rule.Patterns,
            ScanTime = scanTime,
            Rule = rule
        };

        if (rule.RequiresBrowserClosed && ProcessGuard.IsRunning(rule.BrowserProcess))
        {
            item.IsSkipped = true;
            item.SkipReason = "浏览器正在运行，关闭后再扫描";
            return item;
        }

        if (!Directory.Exists(root))
        {
            item.IsSkipped = true;
            item.SkipReason = "目录不存在或不可访问";
            return item;
        }

        var cutoff = scanTime.AddDays(-rule.MinAgeDays);
        var files = RuleFileEnumerator.GetFiles(root, rule, cutoff, cancellationToken);
        var snapshots = new List<ScannedFile>();
        long totalBytes = 0;
        var fileCount = 0;
        var skippedFiles = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_guard.ValidateForDelete(file, rule, out _))
            {
                skippedFiles++;
                continue;
            }

            long length;
            try
            {
                var info = new FileInfo(file);
                length = info.Length;
                var lastWrite = info.LastWriteTimeUtc;
                if (lastWrite > cutoff)
                {
                    skippedFiles++;
                    continue;
                }
                snapshots.Add(new ScannedFile(file, length, lastWrite));
            }
            catch
            {
                skippedFiles++;
                continue;
            }

            totalBytes += length;
            fileCount++;

            if (fileCount % 200 == 0)
            {
                progress?.Report(new ScanProgress(rule.Name, file, fileCount, totalBytes));
            }
        }

        item.Files = snapshots.AsReadOnly();
        item.TotalBytes = totalBytes;
        item.FileCount = fileCount;
        item.SkippedFileCount = skippedFiles;

        if (fileCount == 0)
        {
            item.IsSkipped = true;
            item.SkipReason = "没有符合条件的文件";
        }

        progress?.Report(new ScanProgress(rule.Name, root, fileCount, totalBytes));
        return item;
    }
}
