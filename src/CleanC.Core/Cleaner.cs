namespace CleanC.Core;

public sealed class Cleaner
{
    private readonly PathGuard _guard;

    public Cleaner(PathGuard guard)
    {
        _guard = guard;
    }

    public Task<CleanResult> CleanAsync(
        IReadOnlyList<ScanItem> items,
        bool dryRun,
        CleanLimits limits,
        OperationLogger logger,
        IProgress<CleanProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => CleanCoreAsync(items, dryRun, limits, logger, progress, cancellationToken),
            cancellationToken);
    }

    private async Task<CleanResult> CleanCoreAsync(
        IReadOnlyList<ScanItem> items,
        bool dryRun,
        CleanLimits limits,
        OperationLogger logger,
        IProgress<CleanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new CleanResult { DryRun = dryRun };
        var plan = new List<PlannedFile>();
        long plannedBytes = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.IsSkipped)
            {
                continue;
            }

            var cutoff = item.ScanTime.AddDays(-item.MinAgeDays);
            if (item.Rule.RequiresBrowserClosed && ProcessGuard.IsRunning(item.Rule.BrowserProcess))
            {
                result.SkippedFiles += item.Files.Count;
                await logger.WriteAsync("skipped", item.Id, item.RootPath, 0,
                    "浏览器正在运行，跳过缓存清理", dryRun: dryRun,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }
            var files = item.Files;

            foreach (var snapshot in files)
            {
                var file = snapshot.Path;
                cancellationToken.ThrowIfCancellationRequested();

                if (!_guard.ValidateForDelete(file, item.Rule, out var reason))
                {
                    result.SkippedFiles++;
                    await logger.WriteAsync(
                        "skipped",
                        item.Id,
                        file,
                        0,
                        reason,
                        dryRun: dryRun,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                long length;
                DateTime lastWriteUtc;
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists)
                    {
                        result.SkippedFiles++;
                        continue;
                    }

                    length = info.Length;
                    lastWriteUtc = info.LastWriteTimeUtc;
                }
                catch (Exception ex)
                {
                    result.SkippedFiles++;
                    await logger.WriteAsync(
                        "skipped",
                        item.Id,
                        file,
                        0,
                        "无法读取文件信息",
                        ex.Message,
                        dryRun,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (lastWriteUtc > cutoff || length != snapshot.Length || lastWriteUtc != snapshot.LastWriteTimeUtc)
                {
                    result.SkippedFiles++;
                    continue;
                }

                if (plan.Count + 1 > limits.MaxFiles || plannedBytes + length > limits.MaxBytes)
                {
                    result.Aborted = true;
                    result.AbortReason =
                        $"本次计划超过安全上限（{limits.MaxFiles:N0} 个文件 / {ByteSizeFormatter.Format(limits.MaxBytes)}）。" +
                        "请缩小清理范围后重试。";
                    result.PlannedFiles = plan.Count;
                    result.PlannedBytes = plannedBytes;
                    await logger.WriteAsync(
                        "aborted",
                        item.Id,
                        "",
                        0,
                        result.AbortReason,
                        dryRun: dryRun,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return result;
                }

                plan.Add(new PlannedFile(item, file, length, lastWriteUtc));
                plannedBytes += length;
            }
        }

        result.PlannedFiles = plan.Count;
        result.PlannedBytes = plannedBytes;

        if (dryRun)
        {
            foreach (var file in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await logger.WriteAsync(
                    "planned",
                    file.Item.Id,
                    file.Path,
                    file.Length,
                    "模拟清理，不删除",
                    dryRun: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new CleanProgress("模拟完成", plan.Count, plan.Count, plannedBytes));
            return result;
        }

        var processed = 0;
        foreach (var file in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Item.Rule.RequiresBrowserClosed && ProcessGuard.IsRunning(file.Item.Rule.BrowserProcess))
            {
                result.SkippedFiles++;
                await logger.WriteAsync("skipped", file.Item.Id, file.Path, file.Length,
                    "浏览器正在运行，跳过缓存清理", cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!_guard.ValidateForDelete(file.Path, file.Item.Rule, out var reason))
            {
                result.SkippedFiles++;
                await logger.WriteAsync(
                    "skipped",
                    file.Item.Id,
                    file.Path,
                    file.Length,
                    reason,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var info = new FileInfo(file.Path);
                if (!info.Exists)
                {
                    result.SkippedFiles++;
                    continue;
                }

                if (info.Length != file.Length || info.LastWriteTimeUtc != file.LastWriteTimeUtc)
                {
                    result.SkippedFiles++;
                    await logger.WriteAsync(
                        "skipped",
                        file.Item.Id,
                        file.Path,
                        file.Length,
                        "文件在扫描后发生变化，跳过",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                File.Delete(file.Path);
                result.DeletedFiles++;
                result.DeletedBytes += file.Length;
                await logger.WriteAsync(
                    "deleted",
                    file.Item.Id,
                    file.Path,
                    file.Length,
                    "已删除",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.FailedFiles++;
                result.Errors.Add(new CleanError(file.Path, ex.Message));
                await logger.WriteAsync(
                    "failed",
                    file.Item.Id,
                    file.Path,
                    file.Length,
                    "删除失败",
                    ex.Message,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            processed++;
            if (processed % 50 == 0 || processed == plan.Count)
            {
                progress?.Report(new CleanProgress(file.Item.Name, processed, plan.Count, result.DeletedBytes));
            }
        }

        return result;
    }

    private sealed record PlannedFile(
        ScanItem Item,
        string Path,
        long Length,
        DateTime LastWriteTimeUtc);
}
