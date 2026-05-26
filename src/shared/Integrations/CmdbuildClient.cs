using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class CmdbuildClient(
    HttpClient httpClient,
    IOptionsMonitor<CmdbuildOptions> options,
    IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record ModelRootClassResolution(
        IReadOnlyList<string> Codes,
        bool RootFound,
        string Error);

    private sealed record CmdbuildClassMetadata(
        string Parent,
        string Description,
        string Help);

    private sealed record CmdbuildAttributeMetadata(
        string Description,
        string Help,
        string ValidationRules);

    private sealed record CmdbuildLookupValueMetadata(
        string Id,
        string Code,
        string Description,
        string Note,
        int Index);

    public async Task<CmdbuildSchemaApplyResult> ApplySchemaAsync(
        CmdbuildSchemaDefinition schema,
        CmdbuildSchemaSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(selection);

        var items = new List<CmdbuildSchemaApplyItemResult>();
        var selectedClassCodes = ExpandSelectedClasses(schema, selection);
        var selectedDomainCodes = selection.Domains
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);
        var selectedLookupCodes = ExpandSelectedLookups(schema, selection, selectedClassCodes, selectedDomainCodes);
        var selectedClasses = OrderClassesForApply(schema.Classes
            .Where(classDefinition => selectedClassCodes.Contains(classDefinition.Code))
            .ToArray());
        var selectedLookups = schema.Lookups
            .Where(lookup => selectedLookupCodes.Contains(lookup.Code))
            .ToArray();
        var selectedDomains = schema.Domains
            .Concat(schema.SuggestedDomains)
            .Where(domain => selectedDomainCodes.Contains(domain.Code))
            .ToArray();
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        var operationTimeout = SchemaApplyOperationTimeout(CurrentOptions().RequestTimeoutMs, selectedClasses, selectedLookups, selectedDomains);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operationTimeout);

        var selectedClassCodeSet = selectedClasses
            .Select(classDefinition => classDefinition.Code)
            .ToHashSet(StringComparer.Ordinal);
        try
        {
            var existingClasses = selectedClasses.Count > 0
                ? await ListAllClassesAsync(endpoint, timeout.Token)
                : [];
            var resolvedClassCodes = ResolveSelectedModelRootClasses(selectedClasses, existingClasses, items);
            if (items.Any(item => !item.Success))
            {
                return ApplyResult(items);
            }

            foreach (var lookup in selectedLookups)
            {
                await ApplyLookupAsync(endpoint, lookup, items, timeout.Token);
            }

            var classAvailableByCode = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var classDefinition in selectedClasses)
            {
                if (!string.IsNullOrWhiteSpace(classDefinition.ParentClassCode)
                    && selectedClassCodeSet.Contains(classDefinition.ParentClassCode)
                    && classAvailableByCode.TryGetValue(classDefinition.ParentClassCode, out var parentAvailable)
                    && !parentAvailable)
                {
                    items.Add(Failed(
                        "class",
                        classDefinition.Code,
                        $"Parent class '{classDefinition.ParentClassCode}' was not created or found successfully; dependent class was not sent to CMDBuild."));
                    classAvailableByCode[classDefinition.Code] = false;
                    continue;
                }

                var resolvedCode = ResolveClassCode(classDefinition.Code, resolvedClassCodes);
                if (!string.Equals(resolvedCode, classDefinition.Code, StringComparison.Ordinal))
                {
                    items.Add(Skipped(
                        "class",
                        classDefinition.Code,
                        $"Model root superclass already exists as '{resolvedCode}' with the same display name."));
                    classAvailableByCode[classDefinition.Code] = true;
                    continue;
                }

                var expectedParent = ResolveExpectedParent(classDefinition, resolvedClassCodes);
                classAvailableByCode[classDefinition.Code] = await ApplyClassAsync(endpoint, classDefinition, expectedParent, items, timeout.Token);
            }

            foreach (var domain in selectedDomains)
            {
                if (ClassDependencyFailed(domain.SourceClassCode, selectedClassCodeSet, classAvailableByCode)
                    || ClassDependencyFailed(domain.TargetClassCode, selectedClassCodeSet, classAvailableByCode))
                {
                    items.Add(Failed(
                        "domain",
                        domain.Code,
                        "Source or target class was not created or found successfully; domain was not sent to CMDBuild."));
                    continue;
                }

                await ApplyDomainAsync(endpoint, domain, items, timeout.Token);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"CMDBuild schema apply exceeded operation timeout {FormatDuration(operationTimeout)}. "
                + $"The run includes {selectedClasses.Count} classes, {selectedDomains.Length} domains and {selectedLookups.Length} lookups. "
                + "Apply a smaller selection or increase CMDBuild RequestTimeoutMs for schema operations.",
                ex);
        }

        return ApplyResult(items);
    }

    private static CmdbuildSchemaApplyResult ApplyResult(IReadOnlyList<CmdbuildSchemaApplyItemResult> items)
    {
        return new CmdbuildSchemaApplyResult
        {
            Items = items,
            Created = items.Count(item => item.Action == "created"),
            Updated = items.Count(item => item.Action == "updated"),
            Skipped = items.Count(item => item.Action == "skipped"),
            Failed = items.Count(item => !item.Success)
        };
    }

    private static TimeSpan SchemaApplyOperationTimeout(
        int requestTimeoutMs,
        IReadOnlyCollection<CmdbuildClassDefinition> selectedClasses,
        IReadOnlyCollection<CmdbuildLookupDefinition> selectedLookups,
        IReadOnlyCollection<CmdbuildDomainDefinition> selectedDomains)
    {
        var safeRequestTimeoutMs = Math.Max(1000, requestTimeoutMs);
        var estimatedCmdbuildCalls = 1
            + selectedLookups.Sum(lookup => 2 + lookup.Values.Count)
            + selectedClasses.Sum(classDefinition => 2 + (classDefinition.Attributes.Count * 2))
            + selectedDomains.Sum(domain => 2 + (domain.Attributes.Count * 2));
        var estimatedMs = (long)safeRequestTimeoutMs * Math.Clamp(estimatedCmdbuildCalls, 1, 180);
        return TimeSpan.FromMilliseconds(Math.Clamp(estimatedMs, 120_000L, 1_800_000L));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:0.#} minutes"
            : $"{duration.TotalSeconds:0.#} seconds";
    }

    private static HashSet<string> ExpandSelectedClasses(
        CmdbuildSchemaDefinition schema,
        CmdbuildSchemaSelection selection)
    {
        var selected = selection.Classes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);
        if (selection.IncludeDependencies)
        {
            var selectedDomainCodes = selection.Domains
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var domain in schema.Domains.Concat(schema.SuggestedDomains).Where(domain => selectedDomainCodes.Contains(domain.Code)))
            {
                selected.Add(domain.SourceClassCode);
                selected.Add(domain.TargetClassCode);
            }
        }

        if (!selection.IncludeDependencies)
        {
            return selected;
        }

        var classByCode = schema.Classes.ToDictionary(classDefinition => classDefinition.Code, StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var classCode in selected.ToArray())
            {
                if (!classByCode.TryGetValue(classCode, out var classDefinition)
                    || string.IsNullOrWhiteSpace(classDefinition.ParentClassCode)
                    || !classByCode.ContainsKey(classDefinition.ParentClassCode)
                    || selected.Contains(classDefinition.ParentClassCode))
                {
                    continue;
                }

                selected.Add(classDefinition.ParentClassCode);
                changed = true;
            }
        }

        return selected;
    }

    private static IReadOnlyList<CmdbuildClassDefinition> OrderClassesForApply(IReadOnlyList<CmdbuildClassDefinition> classes)
    {
        var byCode = classes
            .GroupBy(classDefinition => classDefinition.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var stateByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = new List<CmdbuildClassDefinition>();

        foreach (var classDefinition in classes)
        {
            Visit(classDefinition.Code);
        }

        return ordered;

        void Visit(string classCode)
        {
            if (!byCode.TryGetValue(classCode, out var classDefinition))
            {
                return;
            }

            if (stateByCode.TryGetValue(classCode, out var state))
            {
                if (state == 2)
                {
                    return;
                }

                return;
            }

            stateByCode[classCode] = 1;
            if (!string.IsNullOrWhiteSpace(classDefinition.ParentClassCode)
                && byCode.ContainsKey(classDefinition.ParentClassCode))
            {
                Visit(classDefinition.ParentClassCode);
            }

            stateByCode[classCode] = 2;
            ordered.Add(classDefinition);
        }
    }

    private static Dictionary<string, string> ResolveSelectedModelRootClasses(
        IReadOnlyList<CmdbuildClassDefinition> selectedClasses,
        IReadOnlyList<CmdbuildClassCatalogItem> existingClasses,
        List<CmdbuildSchemaApplyItemResult> items)
    {
        var resolved = selectedClasses
            .ToDictionary(classDefinition => classDefinition.Code, classDefinition => classDefinition.Code, StringComparer.Ordinal);
        var existingByCode = existingClasses
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var classDefinition in selectedClasses.Where(classDefinition => classDefinition.Origin == "model_root_superclass"))
        {
            var expectedParent = ResolveExpectedParent(classDefinition, resolved);
            if (existingByCode.TryGetValue(classDefinition.Code, out var exactClass))
            {
                var actualParent = NormalizeClassParent(exactClass.Parent);
                if (!string.Equals(actualParent, expectedParent, StringComparison.Ordinal))
                {
                    items.Add(Failed(
                        "class",
                        classDefinition.Code,
                        $"Model root superclass already exists with parent '{actualParent}', expected '{expectedParent}'. Reparent the existing class manually or recreate it in the correct branch before applying dependent objects."));
                }

                continue;
            }

            var matchesByDisplayName = FindModelRootClassesByDisplayName(existingClasses, classDefinition.DisplayName, expectedParent)
                .ToArray();
            if (matchesByDisplayName.Length > 1)
            {
                items.Add(Failed(
                    "class",
                    classDefinition.Code,
                    $"Multiple prototype superclasses named '{classDefinition.DisplayName}' exist under parent '{expectedParent}': {string.Join(", ", matchesByDisplayName.Select(item => item.Code))}. CMDBuild model roots are matched by display name, so remove or rename duplicates before applying dependent classes."));
                continue;
            }

            if (matchesByDisplayName.Length == 1)
            {
                resolved[classDefinition.Code] = matchesByDisplayName[0].Code;
            }
        }

        return resolved;
    }

    private static string ResolveExpectedParent(
        CmdbuildClassDefinition classDefinition,
        IReadOnlyDictionary<string, string> resolvedClassCodes)
    {
        return string.IsNullOrWhiteSpace(classDefinition.ParentClassCode)
            ? "Class"
            : ResolveClassCode(classDefinition.ParentClassCode, resolvedClassCodes);
    }

    private static string ResolveClassCode(string classCode, IReadOnlyDictionary<string, string> resolvedClassCodes)
    {
        return resolvedClassCodes.TryGetValue(classCode, out var resolvedCode)
            ? resolvedCode
            : classCode;
    }

    private static bool ClassDependencyFailed(
        string classCode,
        IReadOnlySet<string> selectedClassCodes,
        IReadOnlyDictionary<string, bool> classAvailableByCode)
    {
        return selectedClassCodes.Contains(classCode)
            && classAvailableByCode.TryGetValue(classCode, out var available)
            && !available;
    }

    private static HashSet<string> ExpandSelectedLookups(
        CmdbuildSchemaDefinition schema,
        CmdbuildSchemaSelection selection,
        IReadOnlySet<string> selectedClassCodes,
        IReadOnlySet<string> selectedDomainCodes)
    {
        var selected = selection.Lookups
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);
        if (!selection.IncludeDependencies)
        {
            return selected;
        }

        foreach (var classDefinition in schema.Classes.Where(item => selectedClassCodes.Contains(item.Code)))
        {
            foreach (var attribute in classDefinition.Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.LookupTypeCode)))
            {
                selected.Add(attribute.LookupTypeCode);
            }
        }

        foreach (var domain in schema.Domains.Concat(schema.SuggestedDomains).Where(item => selectedDomainCodes.Contains(item.Code)))
        {
            foreach (var attribute in domain.Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.LookupTypeCode)))
            {
                selected.Add(attribute.LookupTypeCode);
            }
        }

        return selected;
    }

    private async Task ApplyLookupAsync(
        string endpoint,
        CmdbuildLookupDefinition lookup,
        List<CmdbuildSchemaApplyItemResult> items,
        CancellationToken cancellationToken)
    {
        var lookupUri = $"{endpoint}/lookup_types/{Uri.EscapeDataString(lookup.Code)}";
        if (await ResourceExistsAsync(lookupUri, cancellationToken))
        {
            items.Add(Skipped("lookup", lookup.Code, "Lookup type already exists."));
        }
        else
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = lookup.Code,
                ["parent"] = null,
                ["speciality"] = "default",
                ["accessType"] = "default"
            };
            items.Add(await PostJsonItemAsync($"{endpoint}/lookup_types", "lookup", lookup.Code, payload, cancellationToken));
        }

        var existingValues = await ListLookupValueMetadataAsync(endpoint, lookup.Code, cancellationToken);
        var index = 1;
        foreach (var value in lookup.Values)
        {
            var valueKey = $"{lookup.Code}/{value.Code}";
            if (existingValues.TryGetValue(value.Code, out var existingValue))
            {
                if (LookupValueMetadataNeedsUpdate(existingValue, value, index))
                {
                    var valueUri = $"{endpoint}/lookup_types/{Uri.EscapeDataString(lookup.Code)}/values/{Uri.EscapeDataString(existingValue.Id)}";
                    items.Add(await PutJsonItemAsync(valueUri, "lookup_value", valueKey, LookupValuePayload(value, index), cancellationToken));
                }
                else
                {
                    items.Add(Skipped("lookup_value", valueKey, "Lookup value already exists."));
                }

                index++;
                continue;
            }

            var payload = LookupValuePayload(value, index);
            items.Add(await PostJsonItemAsync($"{endpoint}/lookup_types/{Uri.EscapeDataString(lookup.Code)}/values", "lookup_value", valueKey, payload, cancellationToken));
            index++;
        }
    }

    private static Dictionary<string, object?> LookupValuePayload(CmdbuildLookupValueDefinition value, int index)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = value.Code,
            ["description"] = value.DisplayName,
            ["index"] = index,
            ["active"] = true,
            ["default"] = false,
            ["note"] = value.Help
        };
    }

    private static bool LookupValueMetadataNeedsUpdate(
        CmdbuildLookupValueMetadata existingValue,
        CmdbuildLookupValueDefinition expectedValue,
        int expectedIndex)
    {
        return !string.Equals(NormalizeMetadataText(existingValue.Description), NormalizeMetadataText(expectedValue.DisplayName), StringComparison.Ordinal)
            || !string.Equals(NormalizeMetadataText(existingValue.Note), NormalizeMetadataText(expectedValue.Help), StringComparison.Ordinal)
            || existingValue.Index != expectedIndex;
    }

    private async Task<bool> ApplyClassAsync(
        string endpoint,
        CmdbuildClassDefinition classDefinition,
        string expectedParent,
        List<CmdbuildSchemaApplyItemResult> items,
        CancellationToken cancellationToken)
    {
        var classUri = $"{endpoint}/classes/{Uri.EscapeDataString(classDefinition.Code)}";
        var payload = ClassPayload(classDefinition, expectedParent);
        var existingClass = await ReadClassMetadataAsync(classUri, cancellationToken);
        if (existingClass is not null)
        {
            var existingParent = NormalizeClassParent(existingClass.Parent);
            if (!string.Equals(existingParent, expectedParent, StringComparison.Ordinal))
            {
                items.Add(Failed(
                    "class",
                    classDefinition.Code,
                    $"Class already exists with parent '{existingParent}', expected '{expectedParent}'. Reparent the existing class manually or recreate it in the correct branch before applying dependent objects."));
                return false;
            }
            else
            {
                if (ClassMetadataNeedsUpdate(existingClass, classDefinition))
                {
                    items.Add(await PutJsonItemAsync(classUri, "class", classDefinition.Code, payload, cancellationToken));
                }
                else
                {
                    items.Add(Skipped("class", classDefinition.Code, "Class already exists."));
                }
            }
        }
        else
        {
            var result = await PostJsonItemAsync($"{endpoint}/classes", "class", classDefinition.Code, payload, cancellationToken);
            items.Add(result);
            if (!result.Success)
            {
                return false;
            }
        }

        var index = 1;
        foreach (var attribute in classDefinition.Attributes)
        {
            await ApplyAttributeAsync(
                endpoint,
                ownerKind: "class_attribute",
                ownerCode: classDefinition.Code,
                attributesEndpoint: $"{endpoint}/classes/{Uri.EscapeDataString(classDefinition.Code)}/attributes",
                attribute,
                index,
                items,
                cancellationToken);
            index++;
        }

        return true;
    }

    private static Dictionary<string, object?> ClassPayload(CmdbuildClassDefinition classDefinition, string expectedParent)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = classDefinition.Code,
            ["description"] = classDefinition.DisplayName,
            ["parent"] = expectedParent,
            ["prototype"] = classDefinition.IsSuperclass,
            ["active"] = true,
            ["type"] = "standard",
            ["speciality"] = "default",
            ["help"] = classDefinition.Help
        };
    }

    private static bool ClassMetadataNeedsUpdate(CmdbuildClassMetadata existingClass, CmdbuildClassDefinition classDefinition)
    {
        return !string.Equals(existingClass.Description.Trim(), classDefinition.DisplayName.Trim(), StringComparison.Ordinal)
            || !string.Equals(existingClass.Help.Trim(), classDefinition.Help.Trim(), StringComparison.Ordinal);
    }

    private async Task<CmdbuildClassMetadata?> ReadClassMetadataAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("success", out var success)
            || success.ValueKind != JsonValueKind.True
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CmdbuildClassMetadata(
            Parent: ReadString(data, "parent") ?? "",
            Description: ReadString(data, "description") ?? ReadString(data, "_description") ?? "",
            Help: ReadString(data, "help") ?? ReadString(data, "_help_translation") ?? "");
    }

    private async Task ApplyDomainAsync(
        string endpoint,
        CmdbuildDomainDefinition domain,
        List<CmdbuildSchemaApplyItemResult> items,
        CancellationToken cancellationToken)
    {
        var domainUri = $"{endpoint}/domains/{Uri.EscapeDataString(domain.Code)}";
        if (await ResourceExistsAsync(domainUri, cancellationToken))
        {
            items.Add(Skipped("domain", domain.Code, "Domain already exists."));
        }
        else
        {
            var cascadeAction = domain.DeleteRelationOnCardDelete ? "setnull" : "restrict";
            var payload = new Dictionary<string, object?>
            {
                ["name"] = domain.Code,
                ["description"] = domain.DisplayName,
                ["source"] = domain.SourceClassCode,
                ["destination"] = domain.TargetClassCode,
                ["cardinality"] = "N:N",
                ["descriptionDirect"] = domain.DisplayName,
                ["descriptionInverse"] = domain.DisplayName,
                ["active"] = true,
                ["sourceProcess"] = false,
                ["destinationProcess"] = false,
                ["cascadeActionDirect"] = cascadeAction,
                ["cascadeActionInverse"] = cascadeAction,
                ["cascadeActionDirect_askConfirm"] = false,
                ["cascadeActionInverse_askConfirm"] = false
            };
            var result = await PostJsonItemAsync($"{endpoint}/domains", "domain", domain.Code, payload, cancellationToken);
            items.Add(result);
            if (!result.Success)
            {
                return;
            }
        }

        var index = 1;
        foreach (var attribute in domain.Attributes)
        {
            await ApplyAttributeAsync(
                endpoint,
                ownerKind: "domain_attribute",
                ownerCode: domain.Code,
                attributesEndpoint: $"{endpoint}/domains/{Uri.EscapeDataString(domain.Code)}/attributes",
                attribute,
                index,
                items,
                cancellationToken);
            index++;
        }
    }

    private async Task ApplyAttributeAsync(
        string endpoint,
        string ownerKind,
        string ownerCode,
        string attributesEndpoint,
        CmdbuildAttributeDefinition attribute,
        int index,
        List<CmdbuildSchemaApplyItemResult> items,
        CancellationToken cancellationToken)
    {
        _ = endpoint;
        var code = $"{ownerCode}.{attribute.Code}";
        var attributeUri = $"{attributesEndpoint}/{Uri.EscapeDataString(attribute.Code)}";
        var existingAttribute = await ReadAttributeMetadataAsync(attributeUri, cancellationToken);
        if (existingAttribute is not null)
        {
            if (AttributeMetadataNeedsUpdate(existingAttribute, attribute))
            {
                var updatePayload = AttributePayload(attribute, index);
                items.Add(await PutJsonItemAsync(attributeUri, ownerKind, code, updatePayload, cancellationToken));
                return;
            }

            items.Add(Skipped(ownerKind, code, "Attribute already exists."));
            return;
        }

        var payload = AttributePayload(attribute, index);
        items.Add(await PostJsonItemAsync(attributesEndpoint, ownerKind, code, payload, cancellationToken));
    }

    private static Dictionary<string, object?> AttributePayload(CmdbuildAttributeDefinition attribute, int index)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = attribute.Code,
            ["description"] = attribute.DisplayName,
            ["type"] = attribute.Type,
            ["mandatory"] = attribute.Required,
            ["active"] = true,
            ["index"] = index,
            ["mode"] = "write",
            ["showInGrid"] = false,
            ["showInReducedGrid"] = false,
            ["help"] = attribute.Help
        };

        if (!string.IsNullOrWhiteSpace(attribute.ValidationRules))
        {
            payload["validationRules"] = attribute.ValidationRules;
        }

        if (!string.IsNullOrWhiteSpace(attribute.LookupTypeCode))
        {
            payload["lookupType"] = attribute.LookupTypeCode;
        }

        if (attribute.Type == "string")
        {
            payload["metadata"] = new Dictionary<string, string>
            {
                ["cm_multiline"] = "false",
                ["cm_length"] = "250"
            };
            payload["maxLength"] = 250;
        }
        else if (attribute.Type == "text")
        {
            payload["metadata"] = new Dictionary<string, string>
            {
                ["cm_multiline"] = "true"
            };
        }
        else if (attribute.Type == "decimal")
        {
            payload["precision"] = 8;
            payload["scale"] = 3;
        }

        return payload;
    }

    private async Task<CmdbuildAttributeMetadata?> ReadAttributeMetadataAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("success", out var success)
            || success.ValueKind != JsonValueKind.True
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CmdbuildAttributeMetadata(
            Description: ReadString(data, "description") ?? ReadString(data, "_description_translation") ?? "",
            Help: ReadString(data, "help") ?? ReadString(data, "_help_translation") ?? "",
            ValidationRules: ReadString(data, "validationRules") ?? "");
    }

    private static bool AttributeMetadataNeedsUpdate(
        CmdbuildAttributeMetadata existingAttribute,
        CmdbuildAttributeDefinition expectedAttribute)
    {
        return !string.Equals(NormalizeMetadataText(existingAttribute.Description), NormalizeMetadataText(expectedAttribute.DisplayName), StringComparison.Ordinal)
            || !string.Equals(NormalizeMetadataText(existingAttribute.Help), NormalizeMetadataText(expectedAttribute.Help), StringComparison.Ordinal)
            || !string.Equals(existingAttribute.ValidationRules ?? "", expectedAttribute.ValidationRules ?? "", StringComparison.Ordinal);
    }

    private static string NormalizeMetadataText(string? value)
    {
        return (value ?? "").Trim();
    }

    private async Task<IReadOnlyDictionary<string, CmdbuildLookupValueMetadata>> ListLookupValueMetadataAsync(
        string endpoint,
        string lookupCode,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, $"{endpoint}/lookup_types/{Uri.EscapeDataString(lookupCode)}/values?limit=1000");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new Dictionary<string, CmdbuildLookupValueMetadata>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(text);
        if (!TryReadDataArray(document.RootElement, out var data))
        {
            return new Dictionary<string, CmdbuildLookupValueMetadata>(StringComparer.Ordinal);
        }

        var values = new Dictionary<string, CmdbuildLookupValueMetadata>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            var code = ReadString(item, "code")
                ?? ReadString(item, "name")
                ?? ReadRaw(item, "_id")
                ?? "";
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            values[code] = new CmdbuildLookupValueMetadata(
                Id: ReadRaw(item, "_id") ?? ReadRaw(item, "id") ?? code,
                Code: code,
                Description: ReadString(item, "description") ?? ReadString(item, "_description_translation") ?? "",
                Note: ReadString(item, "note") ?? ReadString(item, "_note_translation") ?? "",
                Index: ReadInt(item, "index") ?? 0);
        }

        return values;
    }

    public async Task<CmdbuildLookupValueCatalogResult> ListLookupValuesCatalogAsync(
        string lookupCode,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        return new CmdbuildLookupValueCatalogResult
        {
            LookupCode = lookupCode,
            Values = await ListLookupValuesCatalogAsync(endpoint, lookupCode, timeout.Token)
        };
    }

    private async Task<IReadOnlyList<CmdbuildLookupValueCatalogItem>> ListLookupValuesCatalogAsync(
        string endpoint,
        string lookupCode,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, $"{endpoint}/lookup_types/{Uri.EscapeDataString(lookupCode)}/values?limit=1000");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var document = JsonDocument.Parse(text);
        if (!TryReadDataArray(document.RootElement, out var data))
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(ReadLookupValueCatalogItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .ToArray();
    }

    private async Task<bool> ResourceExistsAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Get, requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True;
    }

    private async Task<CmdbuildSchemaApplyItemResult> PostJsonItemAsync(
        string requestUri,
        string kind,
        string code,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Post, requestUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Failed(kind, code, $"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Created(kind, code);
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True)
        {
            return Created(kind, code);
        }

        return Failed(kind, code, Trim(text));
    }

    private async Task<CmdbuildSchemaApplyItemResult> PutJsonItemAsync(
        string requestUri,
        string kind,
        string code,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Put, requestUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Failed(kind, code, $"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Updated(kind, code);
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True)
        {
            return Updated(kind, code);
        }

        return Failed(kind, code, Trim(text));
    }

    private static CmdbuildSchemaApplyItemResult Created(string kind, string code)
    {
        return new CmdbuildSchemaApplyItemResult
        {
            Kind = kind,
            Code = code,
            Action = "created",
            Success = true
        };
    }

    private static CmdbuildSchemaApplyItemResult Updated(string kind, string code)
    {
        return new CmdbuildSchemaApplyItemResult
        {
            Kind = kind,
            Code = code,
            Action = "updated",
            Success = true
        };
    }

    private static CmdbuildSchemaApplyItemResult Skipped(string kind, string code, string message)
    {
        return new CmdbuildSchemaApplyItemResult
        {
            Kind = kind,
            Code = code,
            Action = "skipped",
            Success = true,
            Message = message
        };
    }

    private static CmdbuildSchemaApplyItemResult Failed(string kind, string code, string message)
    {
        return new CmdbuildSchemaApplyItemResult
        {
            Kind = kind,
            Code = code,
            Action = "failed",
            Success = false,
            Message = message
        };
    }

    public async Task<IReadOnlyList<CmdbuildClassCatalogItem>> ListClassesAsync(CancellationToken cancellationToken)
    {
        var result = await ListClassesAsync(rootPath: "", managedFilter: null, includePrototypes: false, cancellationToken);
        return result.Classes;
    }

    public async Task<CmdbuildCreatedCardResult> CreateClassCardAsync(
        string classCode,
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(classCode))
        {
            throw new InvalidOperationException("CMDBuild class code is required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards");
        request.Content = new StringContent(
            JsonSerializer.Serialize(values ?? new Dictionary<string, JsonElement>(), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new CmdbuildCreatedCardResult
            {
                ClassCode = classCode,
                Id = "",
                Description = "",
                Values = values ?? new Dictionary<string, JsonElement>()
            };
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(Trim(text));
        }

        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;

        return new CmdbuildCreatedCardResult
        {
            ClassCode = classCode,
            Id = ReadRaw(data, "_id") ?? ReadRaw(data, "id") ?? "",
            Description = ReadString(data, "_description")
                ?? ReadString(data, "Description")
                ?? ReadString(data, "description")
                ?? "",
            Values = values ?? new Dictionary<string, JsonElement>()
        };
    }

    public async Task<CmdbuildCreatedCardResult> UpdateClassCardAsync(
        string classCode,
        string cardId,
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(classCode))
        {
            throw new InvalidOperationException("CMDBuild class code is required.");
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw new InvalidOperationException("CMDBuild card id is required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        using var request = AuthorizedRequest(
            HttpMethod.Put,
            $"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards/{Uri.EscapeDataString(cardId)}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(values ?? new Dictionary<string, JsonElement>(), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new CmdbuildCreatedCardResult
            {
                ClassCode = classCode,
                Id = cardId,
                Description = "",
                Values = values ?? new Dictionary<string, JsonElement>()
            };
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(Trim(text));
        }

        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;

        return new CmdbuildCreatedCardResult
        {
            ClassCode = classCode,
            Id = ReadRaw(data, "_id") ?? ReadRaw(data, "id") ?? cardId,
            Description = ReadString(data, "_description")
                ?? ReadString(data, "Description")
                ?? ReadString(data, "description")
                ?? "",
            Values = values ?? new Dictionary<string, JsonElement>()
        };
    }

    public async Task<CmdbuildDeleteCardResult> DeleteClassCardAsync(
        string classCode,
        string cardId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(classCode))
        {
            throw new InvalidOperationException("CMDBuild class code is required.");
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw new InvalidOperationException("CMDBuild card id is required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        using var request = AuthorizedRequest(
            HttpMethod.Delete,
            $"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards/{Uri.EscapeDataString(cardId)}");
        using var response = await httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        return new CmdbuildDeleteCardResult
        {
            ClassCode = classCode,
            Id = cardId,
            Action = response.StatusCode == System.Net.HttpStatusCode.NotFound ? "skipped" : "deleted",
            Message = response.StatusCode == System.Net.HttpStatusCode.NotFound ? "card was not found" : ""
        };
    }

    public async Task<CmdbuildRelationApplyResult> CreateDomainRelationAsync(
        string domainCode,
        CmdbuildCreateRelationRequest relation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domainCode))
        {
            throw new InvalidOperationException("CMDBuild domain code is required.");
        }

        if (string.IsNullOrWhiteSpace(relation.SourceClassCode)
            || string.IsNullOrWhiteSpace(relation.SourceCardId)
            || string.IsNullOrWhiteSpace(relation.DestinationClassCode)
            || string.IsNullOrWhiteSpace(relation.DestinationCardId))
        {
            throw new InvalidOperationException("CMDBuild relation source and destination cards are required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var domain = await ReadDomainAsync(endpoint, domainCode, timeout.Token)
            ?? throw new InvalidOperationException($"CMDBuild domain {domainCode} was not found.");
        var sourceClassCode = relation.SourceClassCode.Trim();
        var destinationClassCode = relation.DestinationClassCode.Trim();
        var sourceCardId = relation.SourceCardId.Trim();
        var destinationCardId = relation.DestinationCardId.Trim();

        string sourceType;
        string sourceId;
        string destinationType;
        string destinationId;
        if (domain.SourceClassCode.Equals(sourceClassCode, StringComparison.Ordinal)
            && domain.TargetClassCode.Equals(destinationClassCode, StringComparison.Ordinal))
        {
            sourceType = sourceClassCode;
            sourceId = sourceCardId;
            destinationType = destinationClassCode;
            destinationId = destinationCardId;
        }
        else if (domain.SourceClassCode.Equals(destinationClassCode, StringComparison.Ordinal)
            && domain.TargetClassCode.Equals(sourceClassCode, StringComparison.Ordinal))
        {
            sourceType = destinationClassCode;
            sourceId = destinationCardId;
            destinationType = sourceClassCode;
            destinationId = sourceCardId;
        }
        else
        {
            throw new InvalidOperationException(
                $"CMDBuild domain {domain.Code} does not match relation classes {sourceClassCode} -> {destinationClassCode}.");
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["_type"] = domain.Code,
            ["_sourceType"] = sourceType,
            ["_sourceId"] = sourceId,
            ["_destinationType"] = destinationType,
            ["_destinationId"] = destinationId
        };
        foreach (var attribute in relation.Attributes)
        {
            payload[attribute.Key] = attribute.Value;
        }
        payload.TryAdd("is_active", true);

        return await CreateDomainRelationAsync(endpoint, domain.Code, payload, timeout.Token);
    }

    public async Task<CmdbuildRelationApplyResult> DeleteDomainRelationAsync(
        string domainCode,
        string relationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domainCode))
        {
            throw new InvalidOperationException("CMDBuild domain code is required.");
        }

        if (string.IsNullOrWhiteSpace(relationId))
        {
            throw new InvalidOperationException("CMDBuild relation id is required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        using var request = AuthorizedRequest(
            HttpMethod.Delete,
            $"{endpoint}/domains/{Uri.EscapeDataString(domainCode)}/relations/{Uri.EscapeDataString(relationId)}");
        using var response = await httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        return new CmdbuildRelationApplyResult(
            domainCode,
            relationId,
            response.StatusCode == System.Net.HttpStatusCode.NotFound ? "skipped" : "deleted",
            response.StatusCode == System.Net.HttpStatusCode.NotFound ? "relation was not found" : "");
    }

    public async Task<CmdbuildAggregationApplyResult> ApplyAggregationCommandAsync(
        AggregationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CommandType.Equals(AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
        {
            return await RemoveAggregationMembershipAsync(command, cancellationToken);
        }

        if (!command.CommandType.Equals(AggregationCommandTypes.EnsureMembership, StringComparison.OrdinalIgnoreCase))
        {
            return new CmdbuildAggregationApplyResult
            {
                Success = true,
                CommandId = command.CommandId,
                TargetAction = "skipped",
                RelationAction = "skipped",
                Message = $"Command type '{command.CommandType}' is not applied by CMDBuild applier yet."
            };
        }

        if (string.IsNullOrWhiteSpace(command.Target.ClassCode))
        {
            throw new InvalidOperationException("Aggregation command target.class_code is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Source.ClassCode) || string.IsNullOrWhiteSpace(command.Source.CardId))
        {
            throw new InvalidOperationException("Aggregation command source class/card id are required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var values = ManagedTargetValues(command);
        var targetCardId = await ResolveTargetCardIdAsync(endpoint, command, values, timeout.Token);
        var targetAction = "updated";
        if (string.IsNullOrWhiteSpace(targetCardId))
        {
            var created = await CreateClassCardAsync(endpoint, command.Target.ClassCode, values, timeout.Token);
            targetCardId = created.Id;
            targetAction = "created";
        }
        else
        {
            var currentValues = await ReadClassCardValuesAsync(endpoint, command.Target.ClassCode, targetCardId, timeout.Token);
            if (currentValues is not null && DesiredValuesEqual(currentValues, values))
            {
                targetAction = "unchanged";
            }
            else
            {
                await UpdateClassCardAsync(endpoint, command.Target.ClassCode, targetCardId, values, timeout.Token);
            }
        }

        var relationResult = await EnsureSourceLinkRelationAsync(endpoint, command, targetCardId, timeout.Token);
        var targetRelations = new List<CmdbuildRelationApplyResult>();
        foreach (var relation in command.Target.Relations)
        {
            targetRelations.Add(await EnsureManagedTargetRelationAsync(endpoint, command, targetCardId, relation, timeout.Token));
        }

        return new CmdbuildAggregationApplyResult
        {
            Success = true,
            CommandId = command.CommandId,
            TargetCardId = targetCardId,
            TargetAction = targetAction,
            RelationDomain = relationResult.DomainCode,
            RelationId = relationResult.RelationId,
            RelationAction = relationResult.Action,
            TargetRelations = targetRelations,
            Message = relationResult.Message
        };
    }

    private async Task<string> ResolveTargetCardIdAsync(
        string endpoint,
        AggregationCommand command,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.Target.CardId)
            && await ClassCardExistsAsync(endpoint, command.Target.ClassCode, command.Target.CardId, cancellationToken))
        {
            return command.Target.CardId;
        }

        var code = StringValue(values, "Code") ?? StringValue(values, "code");
        return string.IsNullOrWhiteSpace(code)
            ? ""
            : await FindClassCardIdByCodeAsync(endpoint, command.Target.ClassCode, code, cancellationToken) ?? "";
    }

    private static Dictionary<string, object?> ManagedTargetValues(AggregationCommand command)
    {
        var values = new Dictionary<string, object?>(command.Target.Attributes, StringComparer.Ordinal);
        var code = StringValue(values, "Code") ?? StringValue(values, "code") ?? command.Target.IdempotencyKey;
        var name = StringValue(values, "name") ?? code;
        var description = StringValue(values, "Description")
            ?? StringValue(values, "description")
            ?? command.Target.CardDescription
            ?? name;

        values["Code"] = code;
        values["Description"] = description;
        values["name"] = name;
        values["is_active"] = ValueOrDefault(values, "is_active", true);
        values["managed_by_builder"] = ValueOrDefault(values, "managed_by_builder", true);
        values["auto_population_enabled"] = ValueOrDefault(values, "auto_population_enabled", true);
        values["population_rule_id"] = command.RuleId;
        return values;
    }

    private async Task<bool> ClassCardExistsAsync(
        string endpoint,
        string classCode,
        string cardId,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedGet($"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards/{Uri.EscapeDataString(cardId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>?> ReadClassCardValuesAsync(
        string endpoint,
        string classCode,
        string cardId,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedGet($"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards/{Uri.EscapeDataString(cardId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        using var document = JsonDocument.Parse(text);
        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;
        if (data.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in data.EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        return values;
    }

    private async Task<string?> FindClassCardIdByCodeAsync(
        string endpoint,
        string classCode,
        string code,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var offset = 0;
        int? total = null;

        while (true)
        {
            using var request = AuthorizedGet($"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards?limit={pageSize}&offset={offset}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
            }

            using var document = JsonDocument.Parse(text);
            total ??= ReadTotalInt(document.RootElement);
            if (!TryReadDataArray(document.RootElement, out var data))
            {
                return null;
            }

            var pageCount = 0;
            foreach (var card in data.EnumerateArray())
            {
                pageCount++;
                if (string.Equals(ReadString(card, "Code") ?? "", code, StringComparison.OrdinalIgnoreCase))
                {
                    return ReadRaw(card, "_id") ?? ReadRaw(card, "Id") ?? ReadRaw(card, "id");
                }
            }

            offset += pageCount;
            if (pageCount == 0 || (total is not null && offset >= total.Value))
            {
                return null;
            }
        }
    }

    private async Task<CmdbuildAggregationApplyResult> RemoveAggregationMembershipAsync(
        AggregationCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target.ClassCode))
        {
            throw new InvalidOperationException("Aggregation command target.class_code is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Source.ClassCode) || string.IsNullOrWhiteSpace(command.Source.CardId))
        {
            throw new InvalidOperationException("Aggregation command source class/card id are required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var values = ManagedTargetValues(command);
        var targetCardId = await ResolveTargetCardIdAsync(endpoint, command, values, timeout.Token);
        if (string.IsNullOrWhiteSpace(targetCardId))
        {
            return new CmdbuildAggregationApplyResult
            {
                Success = true,
                CommandId = command.CommandId,
                TargetAction = "skipped",
                RelationAction = "skipped",
                Message = "target card was not found"
            };
        }

        var relationResult = await RemoveSourceLinkRelationAsync(endpoint, command, targetCardId, timeout.Token);
        return new CmdbuildAggregationApplyResult
        {
            Success = true,
            CommandId = command.CommandId,
            TargetCardId = targetCardId,
            TargetAction = "unchanged",
            RelationDomain = relationResult.DomainCode,
            RelationId = relationResult.RelationId,
            RelationAction = relationResult.Action,
            Message = relationResult.Message
        };
    }

    private async Task<CmdbuildCreatedCardResult> CreateClassCardAsync(
        string endpoint,
        string classCode,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards");
        request.Content = new StringContent(JsonSerializer.Serialize(values, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        using var document = JsonDocument.Parse(text);
        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;
        return new CmdbuildCreatedCardResult
        {
            ClassCode = classCode,
            Id = ReadRaw(data, "_id") ?? ReadRaw(data, "id") ?? "",
            Description = ReadString(data, "_description") ?? ReadString(data, "Description") ?? ReadString(data, "description") ?? "",
            Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        };
    }

    private async Task UpdateClassCardAsync(
        string endpoint,
        string classCode,
        string cardId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Put,
            $"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/cards/{Uri.EscapeDataString(cardId)}");
        request.Content = new StringContent(JsonSerializer.Serialize(values, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }
    }

    private static bool DesiredValuesEqual(
        IReadOnlyDictionary<string, JsonElement> currentValues,
        IReadOnlyDictionary<string, object?> desiredValues)
    {
        foreach (var (key, desiredValue) in desiredValues)
        {
            if (!currentValues.TryGetValue(key, out var currentValue))
            {
                return false;
            }

            using var desiredDocument = JsonDocument.Parse(JsonSerializer.Serialize(desiredValue, JsonOptions));
            if (!JsonValuesEqual(currentValue, desiredDocument.RootElement))
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonValuesEqual(JsonElement left, JsonElement right)
    {
        if (IsJsonNull(left) && IsJsonNull(right))
        {
            return true;
        }

        if (TryReadBool(left, out var leftBool) && TryReadBool(right, out var rightBool))
        {
            return leftBool == rightBool;
        }

        if (TryReadDecimal(left, out var leftDecimal) && TryReadDecimal(right, out var rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        return string.Equals(ComparableJsonString(left), ComparableJsonString(right), StringComparison.Ordinal);
    }

    private static bool IsJsonNull(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));
    }

    private static bool TryReadBool(JsonElement value, out bool result)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = value.GetBoolean();
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out result))
        {
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryReadDecimal(JsonElement value, out decimal result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out result))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse((value.GetString() ?? "").Replace(',', '.'), out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static string ComparableJsonString(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "code", "Code", "_id", "id", "value", "name", "description" })
            {
                if (value.TryGetProperty(propertyName, out var property)
                    && property.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    return property.ValueKind == JsonValueKind.String
                        ? property.GetString() ?? ""
                        : property.GetRawText();
                }
            }
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };
    }

    private async Task<CmdbuildRelationApplyResult> EnsureSourceLinkRelationAsync(
        string endpoint,
        AggregationCommand command,
        string targetCardId,
        CancellationToken cancellationToken)
    {
        var expectedDomainCode = $"{command.Target.ClassCode}PopulatedFrom{command.Source.ClassCode}";
        var domain = await ReadDomainAsync(endpoint, expectedDomainCode, cancellationToken);
        if (domain is null || !IsSourceLinkDomain(domain, command))
        {
            var domains = await ListDomainsAsync(null, cancellationToken);
            domain = domains.FirstOrDefault(item => IsSourceLinkDomain(item, command));
        }

        if (domain is null)
        {
            return new CmdbuildRelationApplyResult("", "", "skipped", "source-link domain is not configured");
        }

        var payload = new Dictionary<string, object?>
        {
            ["_type"] = domain.Code,
            ["_sourceType"] = command.Target.ClassCode,
            ["_sourceId"] = targetCardId,
            ["_destinationType"] = command.Source.ClassCode,
            ["_destinationId"] = command.Source.CardId,
            ["is_active"] = true,
            ["source"] = "cmdb2monitoring",
            ["population_rule_id"] = command.RuleId
        };

        using var request = AuthorizedRequest(HttpMethod.Post, $"{endpoint}/domains/{Uri.EscapeDataString(domain.Code)}/relations");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (IsDuplicateResponse(text))
            {
                return new CmdbuildRelationApplyResult(domain.Code, "", "skipped", "relation already exists");
            }

            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        if (IsDuplicateResponse(text))
        {
            return new CmdbuildRelationApplyResult(domain.Code, "", "skipped", "relation already exists");
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var duplicate = text.Contains("duplicate value", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\"code\":\"201\"", StringComparison.OrdinalIgnoreCase);
            if (duplicate)
            {
                return new CmdbuildRelationApplyResult(domain.Code, "", "skipped", "relation already exists");
            }

            throw new InvalidOperationException(Trim(text));
        }

        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;
        return new CmdbuildRelationApplyResult(
            domain.Code,
            ReadRaw(data, "_id") ?? "",
            "created",
            "");
    }

    private async Task<CmdbuildRelationApplyResult> EnsureManagedTargetRelationAsync(
        string endpoint,
        AggregationCommand command,
        string targetCardId,
        AggregationTargetRelation relation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relation.DomainCode)
            || string.IsNullOrWhiteSpace(relation.TargetClassCode)
            || string.IsNullOrWhiteSpace(relation.TargetLookup))
        {
            return new CmdbuildRelationApplyResult("", "", "skipped", "managed target relation is incomplete");
        }

        var domain = await ReadDomainAsync(endpoint, relation.DomainCode, cancellationToken);
        if (domain is null)
        {
            return new CmdbuildRelationApplyResult(relation.DomainCode, "", "skipped", "managed target relation domain is not configured");
        }

        var relatedCardId = await ResolveRelationTargetCardIdAsync(endpoint, relation, cancellationToken);
        if (string.IsNullOrWhiteSpace(relatedCardId))
        {
            return new CmdbuildRelationApplyResult(domain.Code, "", "skipped", $"target lookup '{relation.TargetLookup}' was not found");
        }

        var orientation = ResolveManagedRelationOrientation(domain, command, relation, targetCardId, relatedCardId);
        if (orientation is null)
        {
            return new CmdbuildRelationApplyResult(domain.Code, "", "skipped", "managed target relation classes do not match domain orientation");
        }

        var payload = new Dictionary<string, object?>(relation.AttributeMappings, StringComparer.Ordinal)
        {
            ["_type"] = domain.Code,
            ["_sourceType"] = orientation.SourceType,
            ["_sourceId"] = orientation.SourceId,
            ["_destinationType"] = orientation.DestinationType,
            ["_destinationId"] = orientation.DestinationId
        };
        payload.TryAdd("is_active", true);

        return await CreateDomainRelationAsync(endpoint, domain.Code, payload, cancellationToken);
    }

    private async Task<string> ResolveRelationTargetCardIdAsync(
        string endpoint,
        AggregationTargetRelation relation,
        CancellationToken cancellationToken)
    {
        if (await ClassCardExistsAsync(endpoint, relation.TargetClassCode, relation.TargetLookup, cancellationToken))
        {
            return relation.TargetLookup;
        }

        return await FindClassCardIdByCodeAsync(endpoint, relation.TargetClassCode, relation.TargetLookup, cancellationToken) ?? "";
    }

    private static ManagedRelationOrientation? ResolveManagedRelationOrientation(
        CmdbuildDomainCatalogItem domain,
        AggregationCommand command,
        AggregationTargetRelation relation,
        string targetCardId,
        string relatedCardId)
    {
        if (domain.SourceClassCode.Equals(command.Target.ClassCode, StringComparison.Ordinal)
            && domain.TargetClassCode.Equals(relation.TargetClassCode, StringComparison.Ordinal))
        {
            return new ManagedRelationOrientation(
                command.Target.ClassCode,
                targetCardId,
                relation.TargetClassCode,
                relatedCardId);
        }

        if (domain.TargetClassCode.Equals(command.Target.ClassCode, StringComparison.Ordinal)
            && domain.SourceClassCode.Equals(relation.TargetClassCode, StringComparison.Ordinal))
        {
            return new ManagedRelationOrientation(
                relation.TargetClassCode,
                relatedCardId,
                command.Target.ClassCode,
                targetCardId);
        }

        return null;
    }

    private async Task<CmdbuildRelationApplyResult> CreateDomainRelationAsync(
        string endpoint,
        string domainCode,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Post, $"{endpoint}/domains/{Uri.EscapeDataString(domainCode)}/relations");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (IsDuplicateResponse(text))
            {
                return new CmdbuildRelationApplyResult(domainCode, "", "skipped", "relation already exists");
            }

            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        if (IsDuplicateResponse(text))
        {
            return new CmdbuildRelationApplyResult(domainCode, "", "skipped", "relation already exists");
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var duplicate = text.Contains("duplicate value", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\"code\":\"201\"", StringComparison.OrdinalIgnoreCase);
            if (duplicate)
            {
                return new CmdbuildRelationApplyResult(domainCode, "", "skipped", "relation already exists");
            }

            throw new InvalidOperationException(Trim(text));
        }

        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;
        return new CmdbuildRelationApplyResult(
            domainCode,
            ReadRaw(data, "_id") ?? "",
            "created",
            "");
    }

    private async Task<CmdbuildRelationApplyResult> RemoveSourceLinkRelationAsync(
        string endpoint,
        AggregationCommand command,
        string targetCardId,
        CancellationToken cancellationToken)
    {
        var domain = await ResolveSourceLinkDomainAsync(endpoint, command, cancellationToken);
        if (domain is null)
        {
            return new CmdbuildRelationApplyResult("", "", "skipped", "source-link domain is not configured");
        }

        var relationId = await FindSourceLinkRelationIdAsync(endpoint, domain.Code, command, targetCardId, cancellationToken);
        if (string.IsNullOrWhiteSpace(relationId))
        {
            return new CmdbuildRelationApplyResult(domain.Code, "", "skipped", "relation was not found");
        }

        using var request = AuthorizedRequest(
            HttpMethod.Delete,
            $"{endpoint}/domains/{Uri.EscapeDataString(domain.Code)}/relations/{Uri.EscapeDataString(relationId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        return new CmdbuildRelationApplyResult(
            domain.Code,
            relationId,
            response.StatusCode == System.Net.HttpStatusCode.NotFound ? "skipped" : "deleted",
            response.StatusCode == System.Net.HttpStatusCode.NotFound ? "relation was not found" : "");
    }

    private async Task<CmdbuildDomainCatalogItem?> ResolveSourceLinkDomainAsync(
        string endpoint,
        AggregationCommand command,
        CancellationToken cancellationToken)
    {
        var expectedDomainCode = $"{command.Target.ClassCode}PopulatedFrom{command.Source.ClassCode}";
        var domain = await ReadDomainAsync(endpoint, expectedDomainCode, cancellationToken);
        if (domain is not null && IsSourceLinkDomain(domain, command))
        {
            return domain;
        }

        var domains = await ListDomainsAsync(null, cancellationToken);
        return domains.FirstOrDefault(item => IsSourceLinkDomain(item, command));
    }

    private async Task<string> FindSourceLinkRelationIdAsync(
        string endpoint,
        string domainCode,
        AggregationCommand command,
        string targetCardId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var offset = 0;
        int? total = null;

        while (true)
        {
            using var request = AuthorizedGet($"{endpoint}/domains/{Uri.EscapeDataString(domainCode)}/relations?limit={pageSize}&offset={offset}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
            }

            using var document = JsonDocument.Parse(text);
            total ??= ReadTotalInt(document.RootElement);
            if (!TryReadDataArray(document.RootElement, out var data))
            {
                return "";
            }

            var pageCount = 0;
            foreach (var relation in data.EnumerateArray())
            {
                pageCount++;
                if (RelationMatchesSourceLink(relation, command, targetCardId))
                {
                    return ReadRaw(relation, "_id") ?? ReadRaw(relation, "id") ?? "";
                }
            }

            offset += pageCount;
            if (pageCount == 0 || (total is not null && offset >= total.Value))
            {
                return "";
            }
        }
    }

    private static bool RelationMatchesSourceLink(
        JsonElement relation,
        AggregationCommand command,
        string targetCardId)
    {
        return string.Equals(ReadRaw(relation, "_sourceType") ?? "", command.Target.ClassCode, StringComparison.Ordinal)
            && string.Equals(ReadRaw(relation, "_destinationType") ?? "", command.Source.ClassCode, StringComparison.Ordinal)
            && string.Equals(ReadRaw(relation, "_sourceId") ?? "", targetCardId, StringComparison.Ordinal)
            && string.Equals(ReadRaw(relation, "_destinationId") ?? "", command.Source.CardId, StringComparison.Ordinal);
    }

    private static bool IsSourceLinkDomain(CmdbuildDomainCatalogItem domain, AggregationCommand command)
    {
        return domain.Active
            && domain.SourceClassCode.Equals(command.Target.ClassCode, StringComparison.Ordinal)
            && domain.TargetClassCode.Equals(command.Source.ClassCode, StringComparison.Ordinal)
            && domain.Code.Contains("PopulatedFrom", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CmdbuildDomainCatalogItem?> ReadDomainAsync(
        string endpoint,
        string domainCode,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedGet($"{endpoint}/domains/{Uri.EscapeDataString(domainCode)}?includeModel=true");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        var data = TryReadDataObject(document.RootElement, out var dataObject)
            ? dataObject
            : document.RootElement;
        return ReadDomainCatalogItem(data);
    }

    private static object? ValueOrDefault(IReadOnlyDictionary<string, object?> values, string key, object? defaultValue)
    {
        return values.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(value))
            ? value
            : defaultValue;
    }

    private static string? StringValue(IReadOnlyDictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var value) ? Convert.ToString(value)?.Trim() : null;
    }

    private static bool IsDuplicateResponse(string text)
    {
        return text.Contains("duplicate value", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\"code\":\"201\"", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<CmdbuildClassSchemaCatalogItem>> ListClassSchemasAsync(CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var classes = await ListAllClassesAsync(endpoint, timeout.Token);
        var result = new List<CmdbuildClassSchemaCatalogItem>();
        foreach (var classItem in classes.Where(item => !item.Prototype))
        {
            var attributes = await ListClassAttributeCatalogAsync(endpoint, classItem.Code, timeout.Token);
            result.Add(new CmdbuildClassSchemaCatalogItem
            {
                Code = classItem.Code,
                Name = classItem.Name,
                Description = classItem.Description,
                Active = classItem.Active,
                Prototype = classItem.Prototype,
                Parent = classItem.Parent,
                Attributes = attributes
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<CmdbuildDomainCatalogItem>> ListDomainsAsync(
        string? prefix,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        using var request = AuthorizedGet($"{endpoint}/domains?limit=1000");
        using var response = await httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(Trim(text));
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var normalizedPrefix = (prefix ?? "").Trim();
        return data.EnumerateArray()
            .Select(ReadDomainCatalogItem)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => item.Active)
            .Where(item => string.IsNullOrWhiteSpace(normalizedPrefix)
                || item.Code.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CmdbuildDomainRelationCatalogResult> ListDomainRelationsAsync(
        string? prefix,
        CancellationToken cancellationToken)
    {
        return await ListDomainRelationsAsync(prefix, includeDomain: null, cancellationToken);
    }

    public async Task<CmdbuildDomainRelationCatalogResult> ListDomainRelationsAsync(
        string? prefix,
        Func<CmdbuildDomainCatalogItem, bool>? includeDomain,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var domains = await ListDomainsAsync(prefix, timeout.Token);
        var relations = new List<CmdbuildDomainRelationCatalogItem>();
        foreach (var domain in domains.Where(domain => includeDomain?.Invoke(domain) ?? true))
        {
            relations.AddRange(await ListDomainRelationsAsync(endpoint, domain, timeout.Token));
        }

        return new CmdbuildDomainRelationCatalogResult
        {
            Relations = relations
                .OrderBy(item => item.DomainCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelationId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private async Task<IReadOnlyList<CmdbuildDomainRelationCatalogItem>> ListDomainRelationsAsync(
        string endpoint,
        CmdbuildDomainCatalogItem domain,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var relations = new List<CmdbuildDomainRelationCatalogItem>();
        var offset = 0;
        int? total = null;

        while (true)
        {
            using var request = AuthorizedGet($"{endpoint}/domains/{Uri.EscapeDataString(domain.Code)}/relations?limit={pageSize}&offset={offset}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
            }

            using var document = JsonDocument.Parse(text);
            total ??= ReadTotalInt(document.RootElement);
            if (!TryReadDataArray(document.RootElement, out var data))
            {
                break;
            }

            var pageCount = 0;
            foreach (var relation in data.EnumerateArray())
            {
                relations.Add(ReadDomainRelationCatalogItem(domain.Code, relation));
                pageCount++;
            }

            offset += pageCount;
            if (pageCount == 0 || pageCount < pageSize || (total is not null && offset >= total.Value))
            {
                break;
            }
        }

        return relations;
    }

    public async Task<CmdbuildManagedInstanceCatalogResult> ListManagedClassInstancesAsync(
        string? prefix,
        string? serviceRootPath,
        string? suppressionRootPath,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var classes = new List<CmdbuildClassInstanceCatalogItem>();
        await AddManagedLayerInstancesAsync(endpoint, classes, "Service", prefix, serviceRootPath, timeout.Token);
        await AddManagedLayerInstancesAsync(endpoint, classes, "Suppression", prefix, suppressionRootPath, timeout.Token);

        return new CmdbuildManagedInstanceCatalogResult
        {
            Classes = classes
                .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ClassCode, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public async Task<CmdbuildManagedInstanceCatalogResult> ListManagedLayerClassInstancesAsync(
        string? prefix,
        string layer,
        string? rootPath,
        Func<CmdbuildClassCatalogItem, bool>? includeClass,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var classes = new List<CmdbuildClassInstanceCatalogItem>();
        await AddManagedLayerInstancesAsync(
            endpoint,
            classes,
            layer,
            prefix,
            rootPath,
            timeout.Token,
            includeClass);

        return new CmdbuildManagedInstanceCatalogResult
        {
            Classes = classes
                .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ClassCode, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public async Task<CmdbuildClassInstanceCatalogItem> ListClassCardsCatalogAsync(
        string classCode,
        string? layer,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var allClasses = await ListAllClassesAsync(endpoint, timeout.Token);
        var classItem = allClasses.FirstOrDefault(item =>
            string.Equals(item.Code, classCode, StringComparison.Ordinal));
        if (classItem is null)
        {
            throw new InvalidOperationException($"CMDBuild class {classCode} was not found.");
        }

        var attributes = await ListClassAttributeCatalogAsync(endpoint, classItem.Code, timeout.Token);
        var normalizedLayer = string.IsNullOrWhiteSpace(layer) ? "Source" : layer.Trim();
        var cards = await ListClassCardsAsync(endpoint, normalizedLayer, classItem, attributes, timeout.Token);
        return new CmdbuildClassInstanceCatalogItem
        {
            Layer = normalizedLayer,
            ClassCode = classItem.Code,
            ClassName = classItem.Name,
            ClassDescription = classItem.Description,
            Attributes = attributes,
            Cards = cards
        };
    }

    public async Task<CmdbuildClassCardCatalogItem?> GetClassCardCatalogItemAsync(
        string classCode,
        string cardId,
        string? layer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(classCode))
        {
            throw new InvalidOperationException("CMDBuild class code is required.");
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw new InvalidOperationException("CMDBuild card id is required.");
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var allClasses = await ListAllClassesAsync(endpoint, timeout.Token);
        var classItem = allClasses.FirstOrDefault(item =>
            string.Equals(item.Code, classCode, StringComparison.Ordinal));
        if (classItem is null)
        {
            throw new InvalidOperationException($"CMDBuild class {classCode} was not found.");
        }

        var values = await ReadClassCardValuesAsync(endpoint, classItem.Code, cardId, timeout.Token);
        if (values is null)
        {
            return null;
        }

        var attributes = await ListClassAttributeCatalogAsync(endpoint, classItem.Code, timeout.Token);
        var lookupValuesByType = await ListLookupValueCatalogByTypeAsync(endpoint, attributes, timeout.Token);
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, values, JsonOptions, timeout.Token);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var normalizedLayer = string.IsNullOrWhiteSpace(layer) ? "Source" : layer.Trim();
        return ReadCardCatalogItem(normalizedLayer, classItem, attributes, lookupValuesByType, document.RootElement);
    }

    public async Task<string> ResolveCardPathValueAsync(
        string classCode,
        string cardId,
        string cmdbPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(classCode)
            || string.IsNullOrWhiteSpace(cardId)
            || string.IsNullOrWhiteSpace(cmdbPath))
        {
            return "";
        }

        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var segments = cmdbPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (segments.Count == 0)
        {
            return "";
        }

        var currentClassCode = classCode.Trim();
        var currentCardId = cardId.Trim();
        var currentAttributes = await ListClassAttributeCatalogAsync(endpoint, currentClassCode, timeout.Token);
        if (segments.Count > 1 && currentAttributes.All(attribute =>
                !attribute.Code.Equals(segments[0], StringComparison.OrdinalIgnoreCase)))
        {
            segments.RemoveAt(0);
        }

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var values = await ReadClassCardValuesAsync(endpoint, currentClassCode, currentCardId, timeout.Token);
            if (values is null || !values.TryGetValue(segment, out var value))
            {
                return "";
            }

            if (index == segments.Count - 1)
            {
                var finalText = ReadStringValue(value) ?? "";
                return finalText.Trim();
            }

            var text = ReadReferenceIdValue(value) ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var attribute = currentAttributes.FirstOrDefault(item =>
                item.Code.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (attribute is null)
            {
                return "";
            }

            var targetClassCode = await ResolveReferenceTargetClassCodeAsync(endpoint, currentClassCode, attribute, timeout.Token);
            if (string.IsNullOrWhiteSpace(targetClassCode))
            {
                return "";
            }

            currentClassCode = targetClassCode;
            currentCardId = text;
            currentAttributes = await ListClassAttributeCatalogAsync(endpoint, currentClassCode, timeout.Token);
        }

        return "";
    }

    private async Task<string> ResolveReferenceTargetClassCodeAsync(
        string endpoint,
        string currentClassCode,
        CmdbuildAttributeCatalogItem attribute,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(attribute.TargetClassCode))
        {
            return attribute.TargetClassCode;
        }

        if (string.IsNullOrWhiteSpace(attribute.DomainCode))
        {
            return "";
        }

        var domain = await ReadDomainAsync(endpoint, attribute.DomainCode, cancellationToken);
        if (domain is null)
        {
            return "";
        }

        if (domain.SourceClassCode.Equals(currentClassCode, StringComparison.OrdinalIgnoreCase))
        {
            return domain.TargetClassCode;
        }

        if (domain.TargetClassCode.Equals(currentClassCode, StringComparison.OrdinalIgnoreCase))
        {
            return domain.SourceClassCode;
        }

        return "";
    }

    private async Task AddManagedLayerInstancesAsync(
        string endpoint,
        List<CmdbuildClassInstanceCatalogItem> result,
        string layer,
        string? prefix,
        string? rootPath,
        CancellationToken cancellationToken,
        Func<CmdbuildClassCatalogItem, bool>? includeClass = null)
    {
        var catalog = await ListClassesAsync(
            rootPath,
            new CmdbuildManagedClassFilter
            {
                Prefix = prefix ?? "",
                Layer = layer
            },
            includePrototypes: false,
            cancellationToken);

        foreach (var classItem in catalog.Classes
            .Where(item => !item.Prototype)
            .Where(item => includeClass?.Invoke(item) ?? true))
        {
            var attributes = await ListClassAttributeCatalogAsync(endpoint, classItem.Code, cancellationToken);
            var cards = await ListClassCardsAsync(endpoint, layer, classItem, attributes, cancellationToken);
            result.Add(new CmdbuildClassInstanceCatalogItem
            {
                Layer = layer,
                ClassCode = classItem.Code,
                ClassName = classItem.Name,
                ClassDescription = classItem.Description,
                Attributes = attributes,
                Cards = cards
            });
        }
    }

    private async Task<IReadOnlyList<CmdbuildClassCardCatalogItem>> ListClassCardsAsync(
        string endpoint,
        string layer,
        CmdbuildClassCatalogItem classItem,
        IReadOnlyList<CmdbuildAttributeCatalogItem> attributes,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var cards = new List<CmdbuildClassCardCatalogItem>();
        var lookupValuesByType = await ListLookupValueCatalogByTypeAsync(endpoint, attributes, cancellationToken);
        var offset = 0;
        int? total = null;

        while (true)
        {
            using var request = AuthorizedGet($"{endpoint}/classes/{Uri.EscapeDataString(classItem.Code)}/cards?limit={pageSize}&offset={offset}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
            }

            using var document = JsonDocument.Parse(text);
            total ??= ReadTotalInt(document.RootElement);
            if (!TryReadDataArray(document.RootElement, out var data))
            {
                break;
            }

            var pageCount = 0;
            foreach (var card in data.EnumerateArray())
            {
                cards.Add(ReadCardCatalogItem(layer, classItem, attributes, lookupValuesByType, card));
                pageCount++;
            }

            offset += pageCount;
            if (pageCount == 0 || pageCount < pageSize || (total is not null && offset >= total.Value))
            {
                break;
            }
        }

        return cards;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, CmdbuildLookupValueCatalogItem>>> ListLookupValueCatalogByTypeAsync(
        string endpoint,
        IReadOnlyList<CmdbuildAttributeCatalogItem> attributes,
        CancellationToken cancellationToken)
    {
        var lookupTypes = attributes
            .Select(attribute => attribute.LookupTypeCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (lookupTypes.Length == 0)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, CmdbuildLookupValueCatalogItem>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, IReadOnlyDictionary<string, CmdbuildLookupValueCatalogItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookupType in lookupTypes)
        {
            var values = await ListLookupValuesCatalogAsync(endpoint, lookupType, cancellationToken);
            var byToken = new Dictionary<string, CmdbuildLookupValueCatalogItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                AddLookupValueToken(byToken, value.Id, value);
                AddLookupValueToken(byToken, value.Code, value);
                AddLookupValueToken(byToken, value.Description, value);
            }

            result[lookupType] = byToken;
        }

        return result;
    }

    public async Task<CmdbuildClassCatalogResult> ListClassesAsync(string? rootPath, CancellationToken cancellationToken)
    {
        return await ListClassesAsync(rootPath, managedFilter: null, includePrototypes: false, cancellationToken);
    }

    public async Task<CmdbuildClassCatalogResult> ListClassesAsync(
        string? rootPath,
        CmdbuildManagedClassFilter? managedFilter,
        bool includePrototypes,
        CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

        var allClasses = await ListAllClassesAsync(endpoint, timeout.Token);
        var normalizedRootPath = NormalizeRootPath(rootPath);
        if (string.IsNullOrWhiteSpace(normalizedRootPath))
        {
            var catalogClasses = managedFilter is null
                ? allClasses.Where(item => includePrototypes || !item.Prototype).ToArray()
                : FilterManagedClasses(allClasses, allClasses, managedFilter);

            return new CmdbuildClassCatalogResult
            {
                RootPath = "",
                RootFound = true,
                Classes = catalogClasses
            };
        }

        var menuClasses = await ListMenuClassesAsync(endpoint, normalizedRootPath, timeout.Token);
        if (!menuClasses.RootFound && managedFilter is not null)
        {
            return ListManagedClassesFromClassRoot(allClasses, normalizedRootPath, managedFilter);
        }

        var classesByCode = allClasses.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var classes = menuClasses.Classes
            .Where(item => classesByCode.ContainsKey(item.Code))
            .Select(item => classesByCode[item.Code] with
            {
                InRequestedRoot = true,
                RootPath = normalizedRootPath,
                MenuPath = item.MenuPath
            })
            .Where(item => managedFilter is not null || includePrototypes || !item.Prototype)
            .OrderBy(item => item.MenuPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (managedFilter is not null)
        {
            classes = FilterManagedClasses(classes, allClasses, managedFilter);
        }

        return new CmdbuildClassCatalogResult
        {
            RootPath = normalizedRootPath,
            RootFound = menuClasses.RootFound,
            Classes = classes
        };
    }

    private async Task<IReadOnlyList<CmdbuildClassCatalogItem>> ListAllClassesAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var request = AuthorizedGet($"{endpoint}/classes?limit=1000");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(Trim(text));
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(ReadClassCatalogItem)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => item.Active)
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<CmdbuildAttributeCatalogItem>> ListClassAttributeCatalogAsync(
        string endpoint,
        string classCode,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedGet($"{endpoint}/classes/{Uri.EscapeDataString(classCode)}/attributes?limit=1000");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var document = JsonDocument.Parse(text);
        if (!TryReadDataArray(document.RootElement, out var data))
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(ReadAttributeCatalogItem)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => item.Active)
            .OrderBy(item => item.Index)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CmdbuildClassCatalogItem[] FilterManagedClasses(
        IReadOnlyList<CmdbuildClassCatalogItem> candidates,
        IReadOnlyList<CmdbuildClassCatalogItem> allClasses,
        CmdbuildManagedClassFilter managedFilter)
    {
        var managedBaseClassCode = ManagedBaseClassCode(managedFilter);
        if (string.IsNullOrWhiteSpace(managedBaseClassCode))
        {
            return [];
        }

        var parentByCode = allClasses
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Parent, StringComparer.Ordinal);

        return candidates
            .Where(item => string.Equals(item.Code, managedBaseClassCode, StringComparison.Ordinal)
                || IsDescendantOf(item.Code, managedBaseClassCode, parentByCode))
            .Select(item => item with
            {
                ManagedBaseClass = managedBaseClassCode,
                ManagedDescendant = !string.Equals(item.Code, managedBaseClassCode, StringComparison.Ordinal),
                ManagedByBuilder = true,
                AutoPopulationEnabled = true
            })
            .OrderBy(item => item.MenuPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CmdbuildClassCatalogResult ListManagedClassesFromClassRoot(
        IReadOnlyList<CmdbuildClassCatalogItem> allClasses,
        string normalizedRootPath,
        CmdbuildManagedClassFilter managedFilter)
    {
        if (!TryParseBuilderLayer(managedFilter.Layer, out var layer))
        {
            return new CmdbuildClassCatalogResult
            {
                RootPath = normalizedRootPath,
                RootFound = false,
                Classes = []
            };
        }

        var rootResolution = ResolveModelRootClassCodes(managedFilter.Prefix, layer, normalizedRootPath, allClasses);
        if (!string.IsNullOrWhiteSpace(rootResolution.Error))
        {
            throw new InvalidOperationException(rootResolution.Error);
        }

        var rootClassCodes = rootResolution.Codes.ToArray();
        var lastRootClassCode = rootClassCodes.LastOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(lastRootClassCode))
        {
            return new CmdbuildClassCatalogResult
            {
                RootPath = normalizedRootPath,
                RootFound = false,
                Classes = []
            };
        }

        var parentByCode = allClasses
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Parent, StringComparer.Ordinal);
        var managedBaseClassCode = ManagedBaseClassCode(managedFilter);
        var classes = allClasses
            .Where(item => rootClassCodes.Contains(item.Code, StringComparer.Ordinal)
                || (string.Equals(item.Code, managedBaseClassCode, StringComparison.Ordinal)
                    || IsDescendantOf(item.Code, managedBaseClassCode, parentByCode))
                    && IsDescendantOf(item.Code, lastRootClassCode, parentByCode))
            .Select(item => item with
            {
                InRequestedRoot = true,
                RootPath = normalizedRootPath,
                MenuPath = normalizedRootPath,
                ManagedBaseClass = managedBaseClassCode,
                ManagedDescendant = !string.Equals(item.Code, managedBaseClassCode, StringComparison.Ordinal)
                    && !rootClassCodes.Contains(item.Code, StringComparer.Ordinal),
                ManagedByBuilder = true,
                AutoPopulationEnabled = true
            })
            .OrderBy(item => Array.IndexOf(rootClassCodes, item.Code) < 0 ? int.MaxValue : Array.IndexOf(rootClassCodes, item.Code))
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CmdbuildClassCatalogResult
        {
            RootPath = normalizedRootPath,
            RootFound = rootResolution.RootFound,
            Classes = classes
        };
    }

    private static ModelRootClassResolution ResolveModelRootClassCodes(
        string prefix,
        BuilderLayer layer,
        string normalizedRootPath,
        IReadOnlyList<CmdbuildClassCatalogItem> allClasses)
    {
        var plannedRootClasses = CmdbuildSchemaClassCodes
            .ModelRootClassCodes(prefix, layer, normalizedRootPath)
            .ToArray();
        if (plannedRootClasses.Length == 0)
        {
            return new ModelRootClassResolution([], false, "");
        }

        var existingByCode = allClasses
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var resolvedByPlannedCode = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolvedCodes = new List<string>();
        var allFound = true;

        foreach (var plannedRootClass in plannedRootClasses)
        {
            var expectedParent = string.IsNullOrWhiteSpace(plannedRootClass.ParentClassCode)
                ? "Class"
                : ResolveClassCode(plannedRootClass.ParentClassCode, resolvedByPlannedCode);
            if (existingByCode.TryGetValue(plannedRootClass.Code, out var exactClass)
                && string.Equals(NormalizeClassParent(exactClass.Parent), expectedParent, StringComparison.Ordinal))
            {
                resolvedByPlannedCode[plannedRootClass.Code] = plannedRootClass.Code;
                resolvedCodes.Add(plannedRootClass.Code);
                continue;
            }

            var matchesByDisplayName = FindModelRootClassesByDisplayName(allClasses, plannedRootClass.DisplayName, expectedParent)
                .ToArray();
            if (matchesByDisplayName.Length > 1)
            {
                return new ModelRootClassResolution(
                    resolvedCodes,
                    false,
                    $"Multiple prototype superclasses named '{plannedRootClass.DisplayName}' exist under parent '{expectedParent}': {string.Join(", ", matchesByDisplayName.Select(item => item.Code))}. CMDBuild model roots are matched by display name; remove or rename duplicates before loading this root.");
            }

            if (matchesByDisplayName.Length == 1)
            {
                resolvedByPlannedCode[plannedRootClass.Code] = matchesByDisplayName[0].Code;
                resolvedCodes.Add(matchesByDisplayName[0].Code);
                continue;
            }

            allFound = false;
            resolvedByPlannedCode[plannedRootClass.Code] = plannedRootClass.Code;
            resolvedCodes.Add(plannedRootClass.Code);
        }

        return new ModelRootClassResolution(resolvedCodes, allFound, "");
    }

    private static IEnumerable<CmdbuildClassCatalogItem> FindModelRootClassesByDisplayName(
        IReadOnlyList<CmdbuildClassCatalogItem> classes,
        string displayName,
        string expectedParent)
    {
        return classes.Where(item => item.Prototype
            && string.Equals(NormalizeClassParent(item.Parent), expectedParent, StringComparison.Ordinal)
            && ClassDisplayMatches(item, displayName));
    }

    private static bool ClassDisplayMatches(CmdbuildClassCatalogItem item, string displayName)
    {
        return SameDisplayText(item.Description, displayName)
            || SameDisplayText(item.Name, displayName);
    }

    private static bool SameDisplayText(string left, string right)
    {
        return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeClassParent(string? parent)
    {
        return string.IsNullOrWhiteSpace(parent)
            ? "Class"
            : parent.Trim();
    }

    private static bool IsDescendantOf(
        string classCode,
        string baseClassCode,
        IReadOnlyDictionary<string, string> parentByCode)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = classCode;
        while (parentByCode.TryGetValue(current, out var parent)
            && !string.IsNullOrWhiteSpace(parent)
            && seen.Add(parent))
        {
            if (string.Equals(parent, baseClassCode, StringComparison.Ordinal))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static string ManagedBaseClassCode(CmdbuildManagedClassFilter managedFilter)
    {
        var prefix = (managedFilter.Prefix ?? "").Trim();
        var baseCode = managedFilter.Layer.Trim().ToLowerInvariant() switch
        {
            "service" => "ServiceManagedObject",
            "suppression" => "SuppressionManagedObject",
            _ => ""
        };

        return string.IsNullOrWhiteSpace(baseCode)
            ? ""
            : prefix + baseCode;
    }

    private static bool TryParseBuilderLayer(string layer, out BuilderLayer result)
    {
        if (string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase))
        {
            result = BuilderLayer.Service;
            return true;
        }

        if (string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase))
        {
            result = BuilderLayer.Suppression;
            return true;
        }

        result = default;
        return false;
    }

    private async Task<CmdbuildMenuClassList> ListMenuClassesAsync(
        string endpoint,
        string normalizedRootPath,
        CancellationToken cancellationToken)
    {
        using var menuRequest = AuthorizedGet($"{endpoint}/menu");
        using var menuResponse = await httpClient.SendAsync(menuRequest, cancellationToken);
        var menuText = await menuResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!menuResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)menuResponse.StatusCode}: {Trim(menuText)}");
        }

        using var menuDocument = JsonDocument.Parse(menuText);
        if (!TryReadDataArray(menuDocument.RootElement, out var menus))
        {
            return new CmdbuildMenuClassList(false, []);
        }

        foreach (var menu in menus.EnumerateArray())
        {
            var menuId = ReadString(menu, "_id") ?? ReadRaw(menu, "_id");
            if (string.IsNullOrWhiteSpace(menuId))
            {
                continue;
            }

            using var treeRequest = AuthorizedGet($"{endpoint}/menu/{Uri.EscapeDataString(menuId)}");
            using var treeResponse = await httpClient.SendAsync(treeRequest, cancellationToken);
            var treeText = await treeResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!treeResponse.IsSuccessStatusCode)
            {
                continue;
            }

            using var treeDocument = JsonDocument.Parse(treeText);
            if (!TryReadDataObject(treeDocument.RootElement, out var tree))
            {
                continue;
            }

            var segments = SplitRootPath(normalizedRootPath);
            if (!TryFindMenuRoot(tree, segments, out var rootNode))
            {
                continue;
            }

            var classes = new List<CmdbuildMenuClassItem>();
            CollectMenuClasses(rootNode, segments.ToList(), classes);
            return new CmdbuildMenuClassList(true, classes);
        }

        return new CmdbuildMenuClassList(false, []);
    }

    private static bool TryReadDataArray(JsonElement root, out JsonElement data)
    {
        data = default;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var dataElement)
            && dataElement.ValueKind == JsonValueKind.Array)
        {
            data = dataElement;
            return true;
        }

        return false;
    }

    private static bool TryReadDataObject(JsonElement root, out JsonElement data)
    {
        data = default;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var dataElement)
            && dataElement.ValueKind == JsonValueKind.Object)
        {
            data = dataElement;
            return true;
        }

        return false;
    }

    private static bool TryFindMenuRoot(JsonElement node, IReadOnlyList<string> segments, out JsonElement rootNode)
    {
        if (segments.Count == 0)
        {
            rootNode = node;
            return true;
        }

        return TryFindMenuRoot(node, segments, index: 0, out rootNode);
    }

    private static bool TryFindMenuRoot(JsonElement node, IReadOnlyList<string> segments, int index, out JsonElement rootNode)
    {
        rootNode = default;
        if (index >= segments.Count)
        {
            rootNode = node;
            return true;
        }

        if (node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("children", out var children)
            || children.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (!IsFolder(child) || !MenuLabels(child).Any(label => SameMenuSegment(label, segments[index])))
            {
                continue;
            }

            if (TryFindMenuRoot(child, segments, index + 1, out rootNode))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectMenuClasses(
        JsonElement node,
        List<string> pathSegments,
        List<CmdbuildMenuClassItem> classes)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var menuType = ReadString(node, "menuType");
        if (string.Equals(menuType, "class", StringComparison.OrdinalIgnoreCase))
        {
            var classCode = ReadString(node, "objectTypeName");
            if (!string.IsNullOrWhiteSpace(classCode))
            {
                classes.Add(new CmdbuildMenuClassItem(classCode, "/" + string.Join('/', pathSegments)));
            }
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            var childPath = pathSegments;
            if (IsFolder(child))
            {
                childPath = [.. pathSegments, PrimaryMenuLabel(child)];
            }

            CollectMenuClasses(child, childPath, classes);
        }
    }

    private static bool IsFolder(JsonElement item)
    {
        return string.Equals(ReadString(item, "menuType"), "folder", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> MenuLabels(JsonElement item)
    {
        foreach (var propertyName in new[] { "_actualDescription_translation", "_actualDescription", "objectDescription", "_objectDescription_translation", "_targetDescription_translation", "_targetDescription" })
        {
            var value = ReadString(item, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static string PrimaryMenuLabel(JsonElement item)
    {
        return MenuLabels(item).FirstOrDefault() ?? "";
    }

    private static bool SameMenuSegment(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRootPath(string? rootPath)
    {
        var normalized = (rootPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        normalized = normalized.Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(normalized)
            ? ""
            : "/" + normalized;
    }

    private static string[] SplitRootPath(string normalizedRootPath)
    {
        return normalizedRootPath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<IntegrationCheckResult> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        var endpoint = CurrentOptions().BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Failed(endpoint, "CMDBuild base URL is not configured.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(CurrentOptions().RequestTimeoutMs));

            using var request = AuthorizedGet($"{endpoint}/classes?limit=1");
            using var response = await httpClient.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(endpoint, $"HTTP {(int)response.StatusCode}: {Trim(text)}");
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("success", out var success)
                && success.ValueKind == JsonValueKind.False)
            {
                return Failed(endpoint, Trim(text));
            }

            var total = ReadTotal(root);
            return new IntegrationCheckResult
            {
                System = "CMDBuild",
                Endpoint = endpoint,
                Success = true,
                Summary = total is null
                    ? "CMDBuild REST API is reachable."
                    : $"CMDBuild REST API is reachable; classes total: {total}."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Failed(endpoint, ex.Message);
        }
    }

    private HttpRequestMessage AuthorizedGet(string requestUri)
    {
        return AuthorizedRequest(HttpMethod.Get, requestUri);
    }

    private HttpRequestMessage AuthorizedRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyAuthorization(request);
        return request;
    }

    private CmdbuildOptions CurrentOptions()
    {
        var configured = options.CurrentValue;
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return configured;
        }

        var baseUrl = HeaderValue(request, "x-cmdb2monitoring-cmdbuild-base-url");
        var authMode = HeaderValue(request, "x-cmdb2monitoring-cmdbuild-auth-mode");
        var username = HeaderValue(request, "x-cmdb2monitoring-cmdbuild-username");
        var password = HeaderValue(request, "x-cmdb2monitoring-cmdbuild-password");
        var apiToken = HeaderValue(request, "x-cmdb2monitoring-cmdbuild-api-token");
        var timeoutText = HeaderValue(request, "x-cmdb2monitoring-cmdbuild-timeout-ms");
        if (string.IsNullOrWhiteSpace(baseUrl)
            && string.IsNullOrWhiteSpace(authMode)
            && string.IsNullOrWhiteSpace(username)
            && string.IsNullOrWhiteSpace(password)
            && string.IsNullOrWhiteSpace(apiToken)
            && string.IsNullOrWhiteSpace(timeoutText))
        {
            return configured;
        }

        return new CmdbuildOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? configured.BaseUrl : baseUrl,
            AuthMode = string.IsNullOrWhiteSpace(authMode)
                ? !string.IsNullOrWhiteSpace(apiToken) ? "Token" : configured.AuthMode
                : authMode,
            Username = string.IsNullOrWhiteSpace(username) ? configured.Username : username,
            Password = string.IsNullOrWhiteSpace(password) ? configured.Password : password,
            ApiToken = string.IsNullOrWhiteSpace(apiToken) ? configured.ApiToken : apiToken,
            RequestTimeoutMs = int.TryParse(timeoutText, out var timeoutMs) && timeoutMs > 0
                ? timeoutMs
                : configured.RequestTimeoutMs
        };
    }

    private static string HeaderValue(HttpRequest request, string name)
    {
        return request.Headers.TryGetValue(name, out var values)
            ? values.ToString().Trim()
            : "";
    }

    private void ApplyAuthorization(HttpRequestMessage request)
    {
        var currentOptions = CurrentOptions();
        var authMode = currentOptions.AuthMode;
        if (authMode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (authMode.Equals("Token", StringComparison.OrdinalIgnoreCase)
            || (authMode.Equals("IndeedPam", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(currentOptions.ApiToken)))
        {
            if (string.IsNullOrWhiteSpace(currentOptions.ApiToken))
            {
                throw new InvalidOperationException("CMDBuild API token is required for Token auth mode.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentOptions.ApiToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(currentOptions.Username) || string.IsNullOrWhiteSpace(currentOptions.Password))
        {
            throw new InvalidOperationException("CMDBuild username/password are required for Login auth mode.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{currentOptions.Username}:{currentOptions.Password}")));
    }

    private static CmdbuildClassCatalogItem? ReadClassCatalogItem(JsonElement item)
    {
        var code = ReadString(item, "_id") ?? ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return new CmdbuildClassCatalogItem
        {
            Code = code,
            Name = ReadString(item, "name") ?? code,
            Description = ReadString(item, "description")
                ?? ReadString(item, "_description_translation")
                ?? code,
            Active = ReadBool(item, "active", defaultValue: true),
            Prototype = ReadBool(item, "prototype", defaultValue: false),
            Parent = ReadString(item, "parent") ?? ""
        };
    }

    private static CmdbuildAttributeCatalogItem? ReadAttributeCatalogItem(JsonElement item)
    {
        var code = ReadString(item, "_id") ?? ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return new CmdbuildAttributeCatalogItem
        {
            Code = code,
            Name = ReadString(item, "name") ?? code,
            Description = ReadString(item, "description")
                ?? ReadString(item, "_description_translation")
                ?? code,
            Type = ReadString(item, "type") ?? "",
            LookupTypeCode = ReadString(item, "lookupType") ?? "",
            TargetClassCode = ReadFirstString(item, "targetClass", "targetClassName", "target", "_targetClass", "_targetType", "targetType") ?? "",
            DomainCode = ReadFirstString(item, "domain", "domainName", "domainCode", "referenceDomain") ?? "",
            Help = ReadString(item, "help") ?? "",
            Required = ReadBool(item, "mandatory", defaultValue: false),
            Active = ReadBool(item, "active", defaultValue: true),
            Index = ReadInt(item, "index") ?? 0
        };
    }

    private static CmdbuildDomainCatalogItem? ReadDomainCatalogItem(JsonElement item)
    {
        var code = ReadString(item, "_id") ?? ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return new CmdbuildDomainCatalogItem
        {
            Code = code,
            Name = ReadString(item, "name") ?? code,
            Description = ReadString(item, "description")
                ?? ReadString(item, "_description_translation")
                ?? code,
            Active = ReadBool(item, "active", defaultValue: true),
            SourceClassCode = ReadFirstString(item, "source", "sources", "sourceClass", "sourceClassName") ?? "",
            TargetClassCode = ReadFirstString(item, "destination", "destinations", "target", "targetClass", "targetClassName") ?? ""
        };
    }

    private static CmdbuildDomainRelationCatalogItem ReadDomainRelationCatalogItem(string domainCode, JsonElement item)
    {
        return new CmdbuildDomainRelationCatalogItem
        {
            DomainCode = domainCode,
            RelationId = ReadRaw(item, "_id") ?? ReadRaw(item, "id") ?? "",
            SourceType = ReadRaw(item, "_sourceType") ?? ReadRaw(item, "sourceType") ?? "",
            SourceId = ReadRaw(item, "_sourceId") ?? ReadRaw(item, "sourceId") ?? "",
            DestinationType = ReadRaw(item, "_destinationType") ?? ReadRaw(item, "destinationType") ?? "",
            DestinationId = ReadRaw(item, "_destinationId") ?? ReadRaw(item, "destinationId") ?? "",
            Attributes = ReadRelationAttributes(item)
        };
    }

    private static IReadOnlyDictionary<string, string> ReadRelationAttributes(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return item.EnumerateObject()
            .Where(property => !property.Name.StartsWith('_'))
            .ToDictionary(
                property => property.Name,
                property => ReadStringValue(property.Value) ?? property.Value.GetRawText(),
                StringComparer.Ordinal);
    }

    private static CmdbuildClassCardCatalogItem ReadCardCatalogItem(
        string layer,
        CmdbuildClassCatalogItem classItem,
        IReadOnlyList<CmdbuildAttributeCatalogItem> attributes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CmdbuildLookupValueCatalogItem>> lookupValuesByType,
        JsonElement item)
    {
        var id = ReadRaw(item, "_id") ?? "";
        var description = ReadString(item, "_description")
            ?? ReadString(item, "Description")
            ?? ReadString(item, "description")
            ?? "";
        var values = new List<CmdbuildCardAttributeValue>();
        var attributeByCode = attributes
            .GroupBy(attribute => attribute.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in attributes)
        {
            seen.Add(attribute.Code);
            values.Add(ReadCardAttributeValue(item, attribute, lookupValuesByType));
        }

        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name.StartsWith('_') || seen.Contains(property.Name))
                {
                    continue;
                }

                var attribute = attributeByCode.TryGetValue(property.Name, out var knownAttribute)
                    ? knownAttribute
                    : new CmdbuildAttributeCatalogItem
                    {
                        Code = property.Name,
                        Name = property.Name,
                        Description = property.Name,
                        Type = "",
                        Active = true
                    };
                values.Add(ReadCardAttributeValue(item, attribute, lookupValuesByType));
            }
        }

        return new CmdbuildClassCardCatalogItem
        {
            Layer = layer,
            ClassCode = classItem.Code,
            Id = id,
            Description = description,
            Attributes = values
        };
    }

    private static CmdbuildCardAttributeValue ReadCardAttributeValue(
        JsonElement item,
        CmdbuildAttributeCatalogItem attribute,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CmdbuildLookupValueCatalogItem>> lookupValuesByType)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(attribute.Code, out var value)
            || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new CmdbuildCardAttributeValue
            {
                Code = attribute.Code,
                Name = attribute.Name,
                Description = attribute.Description,
                Type = attribute.Type,
                LookupTypeCode = attribute.LookupTypeCode,
                ValueKind = "null",
                Value = null,
                RawValue = null
            };
        }

        var rawValue = ReadCardValue(value);
        var resolvedLookup = ResolveLookupAttributeValue(value, rawValue, attribute, lookupValuesByType);
        return new CmdbuildCardAttributeValue
        {
            Code = attribute.Code,
            Name = attribute.Name,
            Description = attribute.Description,
            Type = attribute.Type,
            LookupTypeCode = attribute.LookupTypeCode,
            ValueKind = value.ValueKind.ToString().ToLowerInvariant(),
            Value = resolvedLookup?.Code ?? rawValue,
            RawValue = rawValue,
            LookupLabel = resolvedLookup?.Description ?? ""
        };
    }

    private static CmdbuildLookupValueCatalogItem? ResolveLookupAttributeValue(
        JsonElement value,
        string? rawValue,
        CmdbuildAttributeCatalogItem attribute,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CmdbuildLookupValueCatalogItem>> lookupValuesByType)
    {
        if (string.IsNullOrWhiteSpace(attribute.LookupTypeCode)
            || !lookupValuesByType.TryGetValue(attribute.LookupTypeCode, out var values))
        {
            return null;
        }

        foreach (var candidate in LookupValueCandidates(value, rawValue))
        {
            if (values.TryGetValue(candidate, out var lookupValue))
            {
                return lookupValue;
            }
        }

        return null;
    }

    private static IEnumerable<string> LookupValueCandidates(JsonElement value, string? rawValue)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "code", "_code", "name", "value", "_id", "id", "description", "_description_translation" })
            {
                var candidate = ReadRaw(value, propertyName) ?? ReadString(value, propertyName);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    yield return candidate.Trim().Trim('"');
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            yield return rawValue.Trim().Trim('"');
        }
    }

    private static CmdbuildLookupValueCatalogItem ReadLookupValueCatalogItem(JsonElement item)
    {
        var code = ReadString(item, "code")
            ?? ReadString(item, "name")
            ?? ReadRaw(item, "_id")
            ?? "";
        return new CmdbuildLookupValueCatalogItem
        {
            Id = ReadRaw(item, "_id") ?? ReadRaw(item, "id") ?? code,
            Code = code,
            Description = ReadString(item, "description")
                ?? ReadString(item, "_description_translation")
                ?? code,
            Active = ReadBool(item, "active", defaultValue: true),
            Index = ReadInt(item, "index") ?? 0
        };
    }

    private static void AddLookupValueToken(
        Dictionary<string, CmdbuildLookupValueCatalogItem> values,
        string? token,
        CmdbuildLookupValueCatalogItem value)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        values.TryAdd(token.Trim().Trim('"'), value);
    }

    private static string? ReadCardValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            _ => null
        };
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string? ReadFirstString(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            var text = ReadStringValue(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ReadStringValue(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            return value.ToString();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "code", "_code", "value", "Value", "_id", "id", "name", "className", "class", "type", "_type", "description", "_description" })
            {
                if (value.TryGetProperty(propertyName, out var nested))
                {
                    var text = ReadStringValue(nested);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in value.EnumerateArray())
            {
                var text = ReadStringValue(nested);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? ReadReferenceIdValue(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            return value.ToString();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "_id", "id", "Id", "code", "Code" })
            {
                if (value.TryGetProperty(propertyName, out var nested))
                {
                    var text = ReadReferenceIdValue(nested);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return ReadStringValue(value);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in value.EnumerateArray())
            {
                var text = ReadReferenceIdValue(nested);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? ReadRaw(JsonElement item, string propertyName)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool ReadBool(JsonElement item, string propertyName, bool defaultValue)
    {
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : defaultValue;
    }

    private static int? ReadInt(JsonElement item, string propertyName)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static string? ReadTotal(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("meta", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("total", out var total))
        {
            return total.ValueKind == JsonValueKind.Number ? total.GetRawText() : total.GetString();
        }

        return null;
    }

    private static int? ReadTotalInt(JsonElement root)
    {
        var total = ReadTotal(root);
        return int.TryParse(total, out var value) ? value : null;
    }

    private static IntegrationCheckResult Failed(string endpoint, string error)
    {
        return new IntegrationCheckResult
        {
            System = "CMDBuild",
            Endpoint = endpoint,
            Success = false,
            Error = error
        };
    }

    private static string Trim(string value)
    {
        const int maxLength = 300;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

public sealed record CmdbuildClassCatalogItem
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required bool Active { get; init; }

    public required bool Prototype { get; init; }

    public string Parent { get; init; } = "";

    public bool InRequestedRoot { get; init; }

    public string RootPath { get; init; } = "";

    public string MenuPath { get; init; } = "";

    public string ManagedBaseClass { get; init; } = "";

    public bool ManagedDescendant { get; init; }

    public bool ManagedByBuilder { get; init; }

    public bool AutoPopulationEnabled { get; init; }
}

public sealed record CmdbuildClassCatalogResult
{
    public required string RootPath { get; init; }

    public required bool RootFound { get; init; }

    public required IReadOnlyList<CmdbuildClassCatalogItem> Classes { get; init; }
}

public sealed record CmdbuildClassSchemaCatalogItem
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required bool Active { get; init; }

    public required bool Prototype { get; init; }

    public string Parent { get; init; } = "";

    public required IReadOnlyList<CmdbuildAttributeCatalogItem> Attributes { get; init; }
}

public sealed record CmdbuildAttributeCatalogItem
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public string Type { get; init; } = "";

    public string LookupTypeCode { get; init; } = "";

    public string TargetClassCode { get; init; } = "";

    public string DomainCode { get; init; } = "";

    public string Help { get; init; } = "";

    public bool Required { get; init; }

    public bool Active { get; init; }

    public int Index { get; init; }
}

public sealed record CmdbuildDomainCatalogItem
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required bool Active { get; init; }

    public string SourceClassCode { get; init; } = "";

    public string TargetClassCode { get; init; } = "";
}

public sealed record CmdbuildManagedInstanceCatalogResult
{
    public required IReadOnlyList<CmdbuildClassInstanceCatalogItem> Classes { get; init; }
}

public sealed record CmdbuildDomainRelationCatalogResult
{
    public required IReadOnlyList<CmdbuildDomainRelationCatalogItem> Relations { get; init; }
}

public sealed record CmdbuildDomainRelationCatalogItem
{
    public required string DomainCode { get; init; }

    public required string RelationId { get; init; }

    public required string SourceType { get; init; }

    public required string SourceId { get; init; }

    public required string DestinationType { get; init; }

    public required string DestinationId { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record CmdbuildClassInstanceCatalogItem
{
    public required string Layer { get; init; }

    public required string ClassCode { get; init; }

    public string ClassName { get; init; } = "";

    public string ClassDescription { get; init; } = "";

    public required IReadOnlyList<CmdbuildAttributeCatalogItem> Attributes { get; init; }

    public required IReadOnlyList<CmdbuildClassCardCatalogItem> Cards { get; init; }
}

public sealed record CmdbuildClassCardCatalogItem
{
    public required string Layer { get; init; }

    public required string ClassCode { get; init; }

    public required string Id { get; init; }

    public string Description { get; init; } = "";

    public required IReadOnlyList<CmdbuildCardAttributeValue> Attributes { get; init; }
}

public sealed record CmdbuildCreateCardRequest
{
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record CmdbuildCreateRelationRequest
{
    public string SourceClassCode { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public string DestinationClassCode { get; init; } = "";

    public string DestinationCardId { get; init; } = "";

    public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record CmdbuildCreatedCardResult
{
    public required string ClassCode { get; init; }

    public required string Id { get; init; }

    public string Description { get; init; } = "";

    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record CmdbuildDeleteCardResult
{
    public required string ClassCode { get; init; }

    public required string Id { get; init; }

    public required string Action { get; init; }

    public string Message { get; init; } = "";
}

public sealed record CmdbuildAggregationApplyResult
{
    public bool Success { get; init; }

    public string CommandId { get; init; } = "";

    public string TargetCardId { get; init; } = "";

    public string TargetAction { get; init; } = "";

    public string RelationDomain { get; init; } = "";

    public string RelationId { get; init; } = "";

    public string RelationAction { get; init; } = "";

    public IReadOnlyList<CmdbuildRelationApplyResult> TargetRelations { get; init; } = [];

    public string Message { get; init; } = "";
}

public sealed record CmdbuildRelationApplyResult(
    string DomainCode,
    string RelationId,
    string Action,
    string Message);

public sealed record ManagedRelationOrientation(
    string SourceType,
    string SourceId,
    string DestinationType,
    string DestinationId);

public sealed record CmdbuildCardAttributeValue
{
    public required string Code { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Type { get; init; } = "";

    public string LookupTypeCode { get; init; } = "";

    public string ValueKind { get; init; } = "";

    public string? Value { get; init; }

    public string? RawValue { get; init; }

    public string LookupLabel { get; init; } = "";
}

public sealed record CmdbuildLookupValueCatalogResult
{
    public required string LookupCode { get; init; }

    public required IReadOnlyList<CmdbuildLookupValueCatalogItem> Values { get; init; }
}

public sealed record CmdbuildLookupValueCatalogItem
{
    public string Id { get; init; } = "";

    public string Code { get; init; } = "";

    public string Description { get; init; } = "";

    public bool Active { get; init; }

    public int Index { get; init; }
}

internal sealed record CmdbuildMenuClassList(bool RootFound, IReadOnlyList<CmdbuildMenuClassItem> Classes);

internal sealed record CmdbuildMenuClassItem(string Code, string MenuPath);

public sealed record CmdbuildManagedClassFilter
{
    public string Prefix { get; init; } = "";

    public string Layer { get; init; } = "";
}
