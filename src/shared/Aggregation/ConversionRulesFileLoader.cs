using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed class ConversionRulesFileLoader(
    IOptions<ConversionRulesOptions> options,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private ConversionRulesDocument? cached;
    private DateTimeOffset? cachedLoadedAtUtc;

    public async Task<ConversionRulesDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (cached is not null && !options.Value.ReloadOnEachEvent)
        {
            return cached;
        }

        var path = ResolvePath(options.Value.FilePath);
        await using var stream = File.OpenRead(path);
        cached = await JsonSerializer.DeserializeAsync<ConversionRulesDocument>(stream, JsonOptions, cancellationToken)
            ?? new ConversionRulesDocument();
        cachedLoadedAtUtc = DateTimeOffset.UtcNow;
        return cached;
    }

    public async Task<ConversionRulesFileStatus> StatusAsync(
        ConversionRulesValidator validator,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(options.Value.FilePath);
        var document = await LoadAsync(cancellationToken);
        var validation = validator.Validate(document);
        var rules = document.Rules ?? [];
        var file = new FileInfo(path);

        return new ConversionRulesFileStatus(
            Version: document.Version,
            RuleCount: rules.Count,
            ServiceRuleCount: rules.Count(rule => string.Equals(rule.Layer, "service", StringComparison.OrdinalIgnoreCase)),
            SuppressionRuleCount: rules.Count(rule => string.Equals(rule.Layer, "suppression", StringComparison.OrdinalIgnoreCase)),
            FilePath: path,
            ReloadOnEachEvent: options.Value.ReloadOnEachEvent,
            FileLastModifiedAtUtc: file.Exists ? file.LastWriteTimeUtc : null,
            LoadedAtUtc: cachedLoadedAtUtc,
            IsValid: validation.IsValid,
            Errors: validation.Errors,
            Warnings: validation.Warnings);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        foreach (var basePath in CandidateBasePaths())
        {
            var candidate = Path.GetFullPath(Path.Combine(basePath, path));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
    }

    private IEnumerable<string> CandidateBasePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { environment.ContentRootPath, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (seen.Add(directory.FullName))
                {
                    yield return directory.FullName;
                }

                directory = directory.Parent;
            }
        }
    }
}

public sealed record ConversionRulesFileStatus(
    string Version,
    int RuleCount,
    int ServiceRuleCount,
    int SuppressionRuleCount,
    string FilePath,
    bool ReloadOnEachEvent,
    DateTimeOffset? FileLastModifiedAtUtc,
    DateTimeOffset? LoadedAtUtc,
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
