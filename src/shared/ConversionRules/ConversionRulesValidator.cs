namespace Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;

public sealed class ConversionRulesValidator
{
    private static readonly HashSet<string> Layers = new(StringComparer.OrdinalIgnoreCase)
    {
        "service",
        "suppression"
    };

    public ConversionRulesValidationResult Validate(ConversionRulesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<string>();
        var warnings = new List<string>();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);

        if (document.Rules.Count == 0)
        {
            warnings.Add("rules: no conversion rules are configured.");
        }

        foreach (var rule in document.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId))
            {
                errors.Add("rule_id is required.");
            }
            else if (!ruleIds.Add(rule.RuleId))
            {
                errors.Add($"rule_id '{rule.RuleId}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add($"{rule.RuleId}: name is required.");
            }

            if (!Layers.Contains(rule.Layer))
            {
                errors.Add($"{rule.RuleId}: layer must be service or suppression.");
            }

            if (string.IsNullOrWhiteSpace(rule.Source.ClassCode))
            {
                errors.Add($"{rule.RuleId}: source.class_code is required.");
            }

            if (string.IsNullOrWhiteSpace(rule.Target.ClassCode))
            {
                errors.Add($"{rule.RuleId}: target.class_code is required.");
            }

            if (string.IsNullOrWhiteSpace(rule.Target.IdempotencyKey)
                && string.IsNullOrWhiteSpace(rule.Target.CardId))
            {
                errors.Add($"{rule.RuleId}: target.idempotency_key or target.card_id is required.");
            }

            foreach (var relation in rule.Relations)
            {
                if (string.IsNullOrWhiteSpace(relation.DomainCode))
                {
                    errors.Add($"{rule.RuleId}: relation.domain_code is required.");
                }

                if (string.IsNullOrWhiteSpace(relation.TargetClassCode))
                {
                    errors.Add($"{rule.RuleId}: relation.target_class_code is required.");
                }

                if (string.IsNullOrWhiteSpace(relation.TargetLookup))
                {
                    errors.Add($"{rule.RuleId}: relation.target_lookup is required.");
                }
            }
        }

        return new ConversionRulesValidationResult(errors.Count == 0, errors, warnings);
    }
}

public sealed record ConversionRulesValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
