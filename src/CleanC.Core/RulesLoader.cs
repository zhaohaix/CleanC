using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanC.Core;

public static class RulesLoader
{
    public const string RulesVersion = "2026.11.07";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<RuleDefinition> Load()
    {
        var assembly = typeof(RulesLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("rules.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("找不到内置 rules.json。");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("无法读取内置 rules.json。");
        var rules = JsonSerializer.Deserialize<List<RuleDefinition>>(stream, JsonOptions) ?? [];
        Validate(rules);
        return rules;
    }

    private static void Validate(IReadOnlyList<RuleDefinition> rules)
    {
        if (rules.Count == 0)
        {
            throw new InvalidOperationException("规则集为空。");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
            {
                throw new InvalidOperationException("规则 Id 为空或重复：" + rule.Id);
            }

            if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.Root))
            {
                throw new InvalidOperationException("规则缺少名称或根目录：" + rule.Id);
            }

            if (rule.Risk != RiskLevel.A)
            {
                throw new InvalidOperationException("第一版只允许 A 级规则：" + rule.Id);
            }

            if (rule.RequiresAdmin)
            {
                throw new InvalidOperationException("第一版不允许需要管理员的规则：" + rule.Id);
            }

            if (rule.Patterns.Length == 0)
            {
                rule.Patterns = ["*"];
            }
        }
    }
}
