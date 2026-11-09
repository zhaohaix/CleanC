using System.Text;
using System.Text.Json;

namespace CleanC.Core;

public sealed class OperationLogger : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private OperationLogger(string logPath)
    {
        LogPath = logPath;
        var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string LogPath { get; }
    public string OperationId { get; } = Guid.NewGuid().ToString("N");

    public static bool IsAppDataWritable()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanC",
            "logs");
        return TryPrepareDirectory(directory, out _);
    }

    public static OperationLogger CreateDefault()
    {
        var directory = ResolveDefaultDirectory();
        var logPath = Path.Combine(directory, $"operations-{DateTime.Now:yyyyMMdd}.jsonl");
        return new OperationLogger(logPath);
    }

    public static string ResolveDefaultDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CleanC",
                "logs"),
            Path.Combine(Path.GetTempPath(), "CleanC", "logs"),
            Path.Combine(AppContext.BaseDirectory, "logs")
        };

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (TryPrepareDirectory(candidate, out var error))
            {
                return candidate;
            }

            errors.Add(candidate + "：" + error);
        }

        throw new InvalidOperationException("无法创建日志目录。已尝试：" + string.Join("；", errors));
    }

    private static bool TryPrepareDirectory(string directory, out string error)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static OperationLogger CreateAt(string logPath)
    {
        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new OperationLogger(logPath);
    }

    public async Task WriteAsync(
        string action,
        string itemId,
        string path,
        long bytes,
        string result,
        string? error = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var entry = new OperationLogEntry(
            DateTimeOffset.Now,
            OperationId,
            action,
            itemId,
            Redact(path),
            bytes,
            result,
            error,
            dryRun);

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(json).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }

    private static string Redact(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            path = path.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        return path;
    }
}
