namespace CleanC.Core;

public enum RiskLevel
{
    A,
    B,
    C
}

public sealed class RuleDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Root { get; set; } = "";
    public string[] Patterns { get; set; } = ["*"];
    public int MinAgeDays { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.A;
    public bool RequiresAdmin { get; set; }
    public bool DefaultSelected { get; set; } = true;
    public bool RequiresBrowserClosed { get; set; }
    public string? BrowserProcess { get; set; }
    public string Reason { get; set; } = "";
    public string Impact { get; set; } = "";
    public bool Recursive { get; set; } = true;
}

public sealed record ScannedFile(string Path, long Length, DateTime LastWriteTimeUtc);

public sealed class ScanItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string RootPath { get; init; } = "";
    public IReadOnlyList<ScannedFile> Files { get; set; } = [];
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
    public int SkippedFileCount { get; set; }
    public RiskLevel Risk { get; init; }
    public bool RequiresAdmin { get; init; }
    public bool DefaultSelected { get; init; }
    public bool IsSkipped { get; set; }
    public string? SkipReason { get; set; }
    public string Reason { get; init; } = "";
    public string Impact { get; init; } = "";
    public int MinAgeDays { get; init; }
    public bool Recursive { get; init; } = true;
    public string[] Patterns { get; init; } = ["*"];
    public DateTime ScanTime { get; init; }
    public RuleDefinition Rule { get; init; } = new();
}

public sealed class ScanResult
{
    public required IReadOnlyList<ScanItem> Items { get; init; }
    public DateTime CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public long TotalBytes => Items.Sum(item => item.TotalBytes);
    public int TotalFiles => Items.Sum(item => item.FileCount);
}

public sealed record ScanProgress(
    string ItemName,
    string CurrentPath,
    int FilesScanned,
    long BytesScanned);

public sealed record CleanProgress(
    string ItemName,
    int ProcessedFiles,
    int TotalFiles,
    long ProcessedBytes);

public sealed record CleanError(string Path, string Message);

public sealed class CleanResult
{
    public bool DryRun { get; init; }
    public bool Aborted { get; set; }
    public string? AbortReason { get; set; }
    public int PlannedFiles { get; set; }
    public long PlannedBytes { get; set; }
    public int DeletedFiles { get; set; }
    public long DeletedBytes { get; set; }
    public int SkippedFiles { get; set; }
    public int FailedFiles { get; set; }
    public List<CleanError> Errors { get; } = [];
}

public sealed class CleanLimits
{
    public long MaxBytes { get; init; } = 5L * 1024 * 1024 * 1024;
    public int MaxFiles { get; init; } = 20_000;
}

public sealed record OperationLogEntry(
    DateTimeOffset Timestamp,
    string OperationId,
    string Action,
    string ItemId,
    string Path,
    long Bytes,
    string Result,
    string? Error = null,
    bool DryRun = false);

public sealed record ProtectedPath(string Path, bool IncludeChildren, string Reason);
