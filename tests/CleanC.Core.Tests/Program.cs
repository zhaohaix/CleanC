using CleanC.Core;

namespace CleanC.Core.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var harness = new TestHarness();

        await harness.RunAsync("PathGuard allows normal temp file", TestPathGuardAllowsTempFile);
        await harness.RunAsync("PathGuard rejects rule root", TestPathGuardRejectsRoot);
        await harness.RunAsync("PathGuard rejects traversal", TestPathGuardRejectsTraversal);
        await harness.RunAsync("PathGuard rejects DeepSeek names", TestPathGuardRejectsDeepSeekName);
        await harness.RunAsync("PathGuard rejects protected paths", TestPathGuardRejectsProtectedPaths);
        await harness.RunAsync("PathGuard validates existing file", TestPathGuardValidatesExistingFile);
        await harness.RunAsync("Rules are A-level only", TestRules);
        await harness.RunAsync("Byte size formatter", TestByteSizeFormatter);
        await harness.RunAsync("Pattern matching", TestPatternMatching);
        await harness.RunAsync("Scanner finds old file", TestScanner);
        await harness.RunAsync("Cleaner dry-run does not delete", TestCleanerDryRun);
        await harness.RunAsync("Cleaner deletes old file", TestCleanerRealDelete);
        await harness.RunAsync("Cleaner aborts before deleting over limit", TestCleanerAbort);
        await harness.RunAsync("Cleaner excludes files added after scan", TestAddedAfterScan);
        await harness.RunAsync("Cleaner skips files changed after scan", TestChangedAfterScan);
        await harness.RunAsync("Cleaner rechecks browser process", TestBrowserRecheck);
        await harness.RunAsync("Operation logger writes JSONL", TestLogger);
        await harness.RunAsync("Log directory resolves with fallback", TestLogDirectoryFallback);

        Console.WriteLine();
        Console.WriteLine($"SUMMARY: {harness.Passed} passed, {harness.Failures} failed");
        return harness.Failures == 0 ? 0 : 1;
    }

    private static Task TestPathGuardAllowsTempFile()
    {
        var guard = PathGuard.CreateDefault();
        var path = Path.Combine(Path.GetTempPath(), $"cleanc-normal-{Guid.NewGuid():N}.tmp");
        var rule = RuleFor("%TEMP%");
        Check(guard.IsPathAllowed(path, rule, out var reason), reason);
        return Task.CompletedTask;
    }

    private static Task TestPathGuardRejectsRoot()
    {
        var guard = PathGuard.CreateDefault();
        var rule = RuleFor("%TEMP%");
        Check(!guard.IsPathAllowed(Path.GetTempPath(), rule, out var reason), "root should be rejected");
        Check(reason.Contains("根目录", StringComparison.Ordinal), "reason should mention root: " + reason);
        return Task.CompletedTask;
    }

    private static Task TestPathGuardRejectsTraversal()
    {
        var guard = PathGuard.CreateDefault();
        var path = Path.Combine(Path.GetTempPath(), "..", "Desktop", "canary.txt");
        var rule = RuleFor("%TEMP%");
        Check(!guard.IsPathAllowed(path, rule, out _), "traversal should be rejected");
        return Task.CompletedTask;
    }

    private static Task TestPathGuardRejectsDeepSeekName()
    {
        var guard = PathGuard.CreateDefault();
        var path = Path.Combine(Path.GetTempPath(), "DeepSeek", "canary.txt");
        var rule = RuleFor("%TEMP%");
        Check(!guard.IsPathAllowed(path, rule, out var reason), "DeepSeek path should be rejected");
        Check(reason.Contains("受保护名称", StringComparison.Ordinal), "reason should mention protected name: " + reason);
        return Task.CompletedTask;
    }

    private static Task TestPathGuardRejectsProtectedPaths()
    {
        var guard = PathGuard.CreateDefault();
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var paths = new[]
        {
            Path.Combine(windows, "notepad.exe"),
            Path.Combine(user, ".dsh", "canary.txt"),
            Path.Combine(desktop, "canary.txt"),
            Path.Combine(documents, "canary.txt"),
            Path.Combine(local, "Programs", "DeepSeek Harness", "canary.txt"),
            Path.Combine(local, "Programs", "SomeApp", "canary.txt"),
            Path.Combine(documents, "deepseek-harness", "canary.txt")
        };

        foreach (var path in paths)
        {
            var rule = RuleFor(Path.GetPathRoot(path) ?? @"C:\");
            Check(!guard.IsPathAllowed(path, rule, out var reason), "should reject " + path);
            Check(!string.IsNullOrWhiteSpace(reason), "reason should not be empty for " + path);
        }

        return Task.CompletedTask;
    }

    private static Task TestPathGuardValidatesExistingFile()
    {
        var root = CreateTempRoot();
        try
        {
            var file = CreateOldFile(root, "old.tmp");
            var rule = RuleFor(root);
            var guard = PathGuard.CreateDefault();
            Check(guard.ValidateForDelete(file, rule, out var reason), reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TestRules()
    {
        var rules = RulesLoader.Load();
        Check(rules.Count >= 4, "expected at least 4 rules");
        Check(rules.Select(rule => rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == rules.Count, "rule ids must be unique");
        foreach (var rule in rules)
        {
            Check(rule.Risk == RiskLevel.A, "only A-level rules are allowed: " + rule.Id);
            Check(!rule.RequiresAdmin, "admin rules are not allowed in v1: " + rule.Id);
            Check(!string.IsNullOrWhiteSpace(rule.Root), "rule root is required: " + rule.Id);
        }

        return Task.CompletedTask;
    }

    private static Task TestByteSizeFormatter()
    {
        Check(ByteSizeFormatter.Format(0) == "0 B", "0 B");
        Check(ByteSizeFormatter.Format(512) == "512 B", "512 B");
        Check(ByteSizeFormatter.Format(1024) == "1 KB", "1 KB");
        Check(ByteSizeFormatter.Format(1536) == "1.5 KB", "1.5 KB");
        Check(ByteSizeFormatter.Format(5L * 1024 * 1024 * 1024) == "5 GB", "5 GB");
        return Task.CompletedTask;
    }

    private static Task TestPatternMatching()
    {
        Check(RuleFileEnumerator.Matches("thumbcache_123.db", ["thumbcache_*.db"]), "thumbcache should match");
        Check(!RuleFileEnumerator.Matches("iconcache_456.db", ["thumbcache_*.db"]), "iconcache should not match thumbcache");
        Check(RuleFileEnumerator.Matches("data.tmp", ["*"]), "wildcard should match");
        return Task.CompletedTask;
    }

    private static async Task TestScanner()
    {
        var root = CreateTempRoot();
        try
        {
            CreateOldFile(root, "old.tmp");
            var rule = CreateRule(root);
            var scanner = new Scanner(PathGuard.CreateDefault());
            var result = await scanner.ScanAsync([rule], progress: null, CancellationToken.None);
            Check(result.Items.Count == 1, "expected one item");
            var item = result.Items[0];
            Check(item.FileCount == 1, "expected one file, got " + item.FileCount);
            Check(item.TotalBytes > 0, "expected bytes > 0");
            Check(!item.IsSkipped, "item should not be skipped: " + item.SkipReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestCleanerDryRun()
    {
        var root = CreateTempRoot();
        try
        {
            var file = CreateOldFile(root, "old.tmp");
            var rule = CreateRule(root);
            var item = CreateItem(root, rule, file);
            var logPath = Path.Combine(root, "logs", "dry-run.jsonl");

            await using var logger = OperationLogger.CreateAt(logPath);
            var cleaner = new Cleaner(PathGuard.CreateDefault());
            var result = await cleaner.CleanAsync([item], true, new CleanLimits(), logger, null, CancellationToken.None);

            Check(File.Exists(file), "dry-run must not delete the file");
            Check(result.PlannedFiles == 1, "expected one planned file");
            Check(result.DeletedFiles == 0, "dry-run must delete zero files");
            Check(File.Exists(logPath), "log file should exist");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestCleanerRealDelete()
    {
        var root = CreateTempRoot();
        try
        {
            var file = CreateOldFile(root, "old.tmp");
            var rule = CreateRule(root);
            var item = CreateItem(root, rule, file);
            var logPath = Path.Combine(root, "logs", "real.jsonl");

            await using var logger = OperationLogger.CreateAt(logPath);
            var cleaner = new Cleaner(PathGuard.CreateDefault());
            var result = await cleaner.CleanAsync([item], false, new CleanLimits(), logger, null, CancellationToken.None);

            Check(!File.Exists(file), "real run should delete the file");
            Check(result.DeletedFiles == 1, "expected one deleted file");
            Check(result.FailedFiles == 0, "expected zero failures");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestCleanerAbort()
    {
        var root = CreateTempRoot();
        try
        {
            var first = CreateOldFile(root, "one.tmp");
            var second = CreateOldFile(root, "two.tmp");
            var rule = CreateRule(root);
            var item = CreateItem(root, rule, first, second);
            var logPath = Path.Combine(root, "logs", "abort.jsonl");

            await using var logger = OperationLogger.CreateAt(logPath);
            var cleaner = new Cleaner(PathGuard.CreateDefault());
            var result = await cleaner.CleanAsync(
                [item],
                false,
                new CleanLimits { MaxFiles = 1 },
                logger,
                null,
                CancellationToken.None);

            Check(result.Aborted, "run should abort when over limit");
            Check(File.Exists(first), "first file must remain");
            Check(File.Exists(second), "second file must remain");
            Check(result.DeletedFiles == 0, "aborted run must delete zero files");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestAddedAfterScan()
    {
        var root = CreateTempRoot();
        try
        {
            var original = CreateOldFile(root, "original.tmp");
            var rule = CreateRule(root);
            var scan = await new Scanner(PathGuard.CreateDefault()).ScanAsync([rule], null, CancellationToken.None);
            var added = CreateOldFile(root, "added.tmp");
            await using var logger = OperationLogger.CreateAt(Path.Combine(root, "logs", "added.jsonl"));
            var result = await new Cleaner(PathGuard.CreateDefault()).CleanAsync(scan.Items, false, new CleanLimits(), logger, null, CancellationToken.None);
            Check(!File.Exists(original), "scanned file should be deleted");
            Check(File.Exists(added), "file added after scan must remain");
            Check(result.DeletedFiles == 1, "only one scanned file should be deleted");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task TestChangedAfterScan()
    {
        var root = CreateTempRoot();
        try
        {
            var file = CreateOldFile(root, "changed.tmp");
            var rule = CreateRule(root);
            var scan = await new Scanner(PathGuard.CreateDefault()).ScanAsync([rule], null, CancellationToken.None);
            File.AppendAllText(file, "changed");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-9));
            await using var logger = OperationLogger.CreateAt(Path.Combine(root, "logs", "changed.jsonl"));
            var result = await new Cleaner(PathGuard.CreateDefault()).CleanAsync(scan.Items, false, new CleanLimits(), logger, null, CancellationToken.None);
            Check(File.Exists(file), "changed file must remain even when still old enough");
            Check(result.DeletedFiles == 0, "changed file must not be deleted");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task TestBrowserRecheck()
    {
        var root = CreateTempRoot();
        try
        {
            var file = CreateOldFile(root, "browser.tmp");
            var rule = CreateRule(root);
            rule.RequiresBrowserClosed = true;
            rule.BrowserProcess = "cleanc-missing-" + Guid.NewGuid().ToString("N");
            var scan = await new Scanner(PathGuard.CreateDefault()).ScanAsync([rule], null, CancellationToken.None);
            Check(!scan.Items[0].IsSkipped, "scan must initially succeed");
            // Use this guaranteed running process to model a browser opened after scanning.
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            rule.BrowserProcess = current.ProcessName;
            await using var logger = OperationLogger.CreateAt(Path.Combine(root, "logs", "browser.jsonl"));
            var result = await new Cleaner(PathGuard.CreateDefault()).CleanAsync(scan.Items, false, new CleanLimits(), logger, null, CancellationToken.None);
            Check(File.Exists(file), "running browser cache must remain");
            Check(result.DeletedFiles == 0 && result.SkippedFiles == 1, "browser cache must be skipped");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task TestLogger()
    {
        var root = CreateTempRoot();
        try
        {
            var logPath = Path.Combine(root, "logs", "logger.jsonl");
            await using (var logger = OperationLogger.CreateAt(logPath))
            {
                await logger.WriteAsync("test", "item", Path.Combine(root, "x.tmp"), 12, "ok");
            }

            Check(File.Exists(logPath), "log file should exist");
            var text = await File.ReadAllTextAsync(logPath);
            Check(text.Contains("\"Action\":\"test\"", StringComparison.Ordinal), "log should contain action");
            Check(text.Contains("\"Bytes\":12", StringComparison.Ordinal), "log should contain bytes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task TestLogDirectoryFallback()
    {
        var directory = OperationLogger.ResolveDefaultDirectory();
        Check(!string.IsNullOrWhiteSpace(directory), "a writable log directory should be resolved");
        Console.WriteLine("LOG_DIR=" + directory);
        return Task.CompletedTask;
    }

    private static RuleDefinition RuleFor(string root)
    {
        return new RuleDefinition
        {
            Id = "test.rule",
            Name = "test",
            Root = root,
            Patterns = ["*"],
            MinAgeDays = 7,
            Risk = RiskLevel.A
        };
    }

    private static RuleDefinition CreateRule(string root)
    {
        return new RuleDefinition
        {
            Id = "test.temp",
            Name = "测试临时文件",
            Description = "测试用",
            Root = root,
            Patterns = ["*"],
            MinAgeDays = 7,
            Risk = RiskLevel.A,
            DefaultSelected = true,
            Recursive = true
        };
    }

    private static ScanItem CreateItem(string root, RuleDefinition rule, params string[] files)
    {
        return new ScanItem
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            RootPath = root,
            Files = files.Select(file => new ScannedFile(file, new FileInfo(file).Length, File.GetLastWriteTimeUtc(file))).ToArray(),
            TotalBytes = files.Sum(file => new FileInfo(file).Length),
            FileCount = files.Length,
            Risk = rule.Risk,
            DefaultSelected = true,
            MinAgeDays = rule.MinAgeDays,
            ScanTime = DateTime.UtcNow,
            Rule = rule
        };
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "CleanCTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateOldFile(string root, string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "test-data");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10));
        return path;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestHarness
    {
        public int Passed { get; private set; }
        public int Failures { get; private set; }

        public async Task RunAsync(string name, Func<Task> test)
        {
            try
            {
                await test();
                Passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                Failures++;
                Console.WriteLine("FAIL  " + name + ": " + ex.Message);
            }
        }
    }
}
