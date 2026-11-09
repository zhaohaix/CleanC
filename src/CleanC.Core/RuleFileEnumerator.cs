using System.IO.Enumeration;

namespace CleanC.Core;

public static class RuleFileEnumerator
{
    public static IReadOnlyList<string> GetFiles(
        string root,
        RuleDefinition rule,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        if (!Directory.Exists(root))
        {
            return files;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = rule.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false
        };

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Matches(path, rule.Patterns))
                {
                    continue;
                }

                DateTime lastWrite;
                try
                {
                    lastWrite = File.GetLastWriteTimeUtc(path);
                }
                catch
                {
                    continue;
                }

                if (lastWrite > cutoffUtc)
                {
                    continue;
                }

                files.Add(path);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 权限不足或目录被占用时，保留已经枚举到的文件。
        }

        return files;
    }

    public static bool Matches(string path, IReadOnlyList<string> patterns)
    {
        var fileName = Path.GetFileName(path);
        foreach (var pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }
}
