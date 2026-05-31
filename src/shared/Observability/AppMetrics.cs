using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Observability;

public sealed class AppMetrics(IOptionsMonitor<MetricsOptions> options)
{
    private readonly ConcurrentDictionary<MetricKey, double> counters = new();

    public bool Enabled => options.CurrentValue.Enabled;

    public void Increment(string name, params (string Key, string Value)[] labels)
    {
        if (!Enabled)
        {
            return;
        }

        counters.AddOrUpdate(
            new MetricKey(name, NormalizeLabelsKey(labels)),
            1,
            (_, current) => current + 1);
    }

    public void ObserveSeconds(string name, TimeSpan elapsed, params (string Key, string Value)[] labels)
    {
        if (!Enabled)
        {
            return;
        }

        var normalized = NormalizeLabelsKey(labels);
        counters.AddOrUpdate(
            new MetricKey($"{name}_count", normalized),
            1,
            (_, current) => current + 1);
        counters.AddOrUpdate(
            new MetricKey($"{name}_sum", normalized),
            elapsed.TotalSeconds,
            (_, current) => current + elapsed.TotalSeconds);
    }

    public string RenderPrometheus()
    {
        var builder = new StringBuilder();
        foreach (var item in counters.OrderBy(item => item.Key.Name, StringComparer.Ordinal))
        {
            builder.Append(SanitizeName(item.Key.Name));
            builder.Append(item.Key.Labels);
            builder.Append(' ');
            builder.Append(item.Value.ToString("0.############", CultureInfo.InvariantCulture));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string RenderLabels(IReadOnlyList<MetricLabel> labels)
    {
        if (labels.Count == 0)
        {
            return string.Empty;
        }

        return "{"
            + string.Join(",", labels.Select(label =>
                $"{SanitizeName(label.Key)}=\"{EscapeLabelValue(label.Value)}\""))
            + "}";
    }

    private static string NormalizeLabelsKey((string Key, string Value)[] labels)
    {
        var normalized = labels
            .Where(label => !string.IsNullOrWhiteSpace(label.Key))
            .Select(label => new MetricLabel(label.Key.Trim(), label.Value ?? string.Empty))
            .OrderBy(label => label.Key, StringComparer.Ordinal)
            .ToArray();
        return RenderLabels(normalized);
    }

    private static string SanitizeName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == ':' ? ch : '_').ToArray();
        return new string(chars);
    }

    private static string EscapeLabelValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed record MetricKey(string Name, string Labels);

    private sealed record MetricLabel(string Key, string Value);
}
