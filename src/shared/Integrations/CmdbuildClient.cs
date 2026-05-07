using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class CmdbuildClient(HttpClient httpClient, IOptions<CmdbuildOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record ModelRootClassResolution(
        IReadOnlyList<string> Codes,
        bool RootFound,
        string Error);

    public async Task<CmdbuildSchemaApplyResult> ApplySchemaAsync(
        CmdbuildSchemaDefinition schema,
        CmdbuildSchemaSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(selection);

        var endpoint = options.Value.BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

        var items = new List<CmdbuildSchemaApplyItemResult>();
        var selectedClassCodes = ExpandSelectedClasses(schema, selection);
        var selectedDomainCodes = selection.Domains
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal);
        var selectedLookupCodes = ExpandSelectedLookups(schema, selection, selectedClassCodes, selectedDomainCodes);
        var selectedClasses = OrderClassesForApply(schema.Classes
            .Where(classDefinition => selectedClassCodes.Contains(classDefinition.Code))
            .ToArray());
        var selectedClassCodeSet = selectedClasses
            .Select(classDefinition => classDefinition.Code)
            .ToHashSet(StringComparer.Ordinal);
        var existingClasses = selectedClasses.Count > 0
            ? await ListAllClassesAsync(endpoint, timeout.Token)
            : [];
        var resolvedClassCodes = ResolveSelectedModelRootClasses(selectedClasses, existingClasses, items);
        if (items.Any(item => !item.Success))
        {
            return ApplyResult(items);
        }

        foreach (var lookup in schema.Lookups.Where(lookup => selectedLookupCodes.Contains(lookup.Code)))
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

        var allDomains = schema.Domains.Concat(schema.SuggestedDomains);
        foreach (var domain in allDomains.Where(domain => selectedDomainCodes.Contains(domain.Code)))
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

        var existingValues = await ListLookupValueCodesAsync(endpoint, lookup.Code, cancellationToken);
        var index = 1;
        foreach (var value in lookup.Values)
        {
            var valueKey = $"{lookup.Code}/{value.Code}";
            if (existingValues.Contains(value.Code))
            {
                items.Add(Skipped("lookup_value", valueKey, "Lookup value already exists."));
                index++;
                continue;
            }

            var payload = new Dictionary<string, object?>
            {
                ["code"] = value.Code,
                ["description"] = value.DisplayName,
                ["index"] = index,
                ["active"] = true,
                ["default"] = false,
                ["note"] = value.Help
            };
            items.Add(await PostJsonItemAsync($"{endpoint}/lookup_types/{Uri.EscapeDataString(lookup.Code)}/values", "lookup_value", valueKey, payload, cancellationToken));
            index++;
        }
    }

    private async Task<bool> ApplyClassAsync(
        string endpoint,
        CmdbuildClassDefinition classDefinition,
        string expectedParent,
        List<CmdbuildSchemaApplyItemResult> items,
        CancellationToken cancellationToken)
    {
        var classUri = $"{endpoint}/classes/{Uri.EscapeDataString(classDefinition.Code)}";
        var existingParent = await ReadClassParentAsync(classUri, cancellationToken);
        if (existingParent is not null)
        {
            existingParent = NormalizeClassParent(existingParent);
            if (!string.Equals(existingParent, expectedParent, StringComparison.Ordinal))
            {
                if (await CanReuseLegacySuperclassAsync(endpoint, classDefinition, existingParent, expectedParent, cancellationToken))
                {
                    items.Add(Skipped(
                        "class",
                        classDefinition.Code,
                        $"Class already exists with parent '{existingParent}', while expected parent '{expectedParent}' exists. CMDBuild does not change superclass parent after creation; apply will reuse the existing superclass."));
                }
                else
                {
                    items.Add(Failed(
                        "class",
                        classDefinition.Code,
                        $"Class already exists with parent '{existingParent}', expected '{expectedParent}'. Reparent the existing class manually or recreate it in the correct branch before applying dependent objects."));
                    return false;
                }
            }
            else
            {
                items.Add(Skipped("class", classDefinition.Code, "Class already exists."));
            }
        }
        else
        {
            var payload = new Dictionary<string, object?>
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

    private async Task<bool> CanReuseLegacySuperclassAsync(
        string endpoint,
        CmdbuildClassDefinition classDefinition,
        string existingParent,
        string expectedParent,
        CancellationToken cancellationToken)
    {
        return classDefinition.IsSuperclass
            && classDefinition.Origin != "model_root_superclass"
            && string.Equals(existingParent, "Class", StringComparison.Ordinal)
            && !string.Equals(expectedParent, "Class", StringComparison.Ordinal)
            && await ResourceExistsAsync($"{endpoint}/classes/{Uri.EscapeDataString(expectedParent)}", cancellationToken);
    }

    private async Task<string?> ReadClassParentAsync(string requestUri, CancellationToken cancellationToken)
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

        return ReadString(data, "parent") ?? "";
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
        if (await ResourceExistsAsync(attributeUri, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(attribute.ValidationRules))
            {
                var currentValidationRules = await ReadAttributeValidationRulesAsync(attributeUri, cancellationToken);
                if (!string.Equals(currentValidationRules ?? "", attribute.ValidationRules, StringComparison.Ordinal))
                {
                    var updatePayload = AttributePayload(attribute, index);
                    items.Add(await PutJsonItemAsync(attributeUri, ownerKind, code, updatePayload, cancellationToken));
                    return;
                }
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

    private async Task<string?> ReadAttributeValidationRulesAsync(
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
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("validationRules", out var validationRules))
        {
            return null;
        }

        return validationRules.ValueKind == JsonValueKind.String
            ? validationRules.GetString()
            : null;
    }

    private async Task<HashSet<string>> ListLookupValueCodesAsync(
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
            .Select(item => ReadString(item, "code"))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .ToHashSet(StringComparer.Ordinal);
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

    public async Task<IReadOnlyList<CmdbuildClassSchemaCatalogItem>> ListClassSchemasAsync(CancellationToken cancellationToken)
    {
        var endpoint = options.Value.BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

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
        var endpoint = options.Value.BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

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

    public async Task<CmdbuildManagedInstanceCatalogResult> ListManagedClassInstancesAsync(
        string? prefix,
        string? serviceRootPath,
        string? suppressionRootPath,
        CancellationToken cancellationToken)
    {
        var endpoint = options.Value.BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

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

    private async Task AddManagedLayerInstancesAsync(
        string endpoint,
        List<CmdbuildClassInstanceCatalogItem> result,
        string layer,
        string? prefix,
        string? rootPath,
        CancellationToken cancellationToken)
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

        foreach (var classItem in catalog.Classes.Where(item => !item.Prototype))
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
                cards.Add(ReadCardCatalogItem(layer, classItem, attributes, card));
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
        var endpoint = options.Value.BaseUrl.TrimEnd('/');
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

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
        var allowLegacyManagedTree = rootResolution.RootFound
            && parentByCode.TryGetValue(managedBaseClassCode, out var managedBaseParent)
            && string.Equals(NormalizeClassParent(managedBaseParent), "Class", StringComparison.Ordinal);
        var classes = allClasses
            .Where(item => rootClassCodes.Contains(item.Code, StringComparer.Ordinal)
                || (string.Equals(item.Code, managedBaseClassCode, StringComparison.Ordinal)
                    || IsDescendantOf(item.Code, managedBaseClassCode, parentByCode))
                    && (allowLegacyManagedTree || IsDescendantOf(item.Code, lastRootClassCode, parentByCode)))
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
        var endpoint = options.Value.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Failed(endpoint, "CMDBuild base URL is not configured.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

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
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.Username}:{options.Value.Password}")));
        return request;
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
            SourceClassCode = ReadString(item, "source") ?? "",
            TargetClassCode = ReadString(item, "destination") ?? ""
        };
    }

    private static CmdbuildClassCardCatalogItem ReadCardCatalogItem(
        string layer,
        CmdbuildClassCatalogItem classItem,
        IReadOnlyList<CmdbuildAttributeCatalogItem> attributes,
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
            values.Add(ReadCardAttributeValue(item, attribute));
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
                values.Add(ReadCardAttributeValue(item, attribute));
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

    private static CmdbuildCardAttributeValue ReadCardAttributeValue(JsonElement item, CmdbuildAttributeCatalogItem attribute)
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
                ValueKind = "null",
                Value = null
            };
        }

        return new CmdbuildCardAttributeValue
        {
            Code = attribute.Code,
            Name = attribute.Name,
            Description = attribute.Description,
            Type = attribute.Type,
            ValueKind = value.ValueKind.ToString().ToLowerInvariant(),
            Value = ReadCardValue(value)
        };
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
            foreach (var propertyName in new[] { "name", "_id", "id", "className", "class", "type", "_type", "description", "_description" })
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

public sealed record CmdbuildCardAttributeValue
{
    public required string Code { get; init; }

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Type { get; init; } = "";

    public string ValueKind { get; init; } = "";

    public string? Value { get; init; }
}

internal sealed record CmdbuildMenuClassList(bool RootFound, IReadOnlyList<CmdbuildMenuClassItem> Classes);

internal sealed record CmdbuildMenuClassItem(string Code, string MenuPath);

public sealed record CmdbuildManagedClassFilter
{
    public string Prefix { get; init; } = "";

    public string Layer { get; init; } = "";
}
