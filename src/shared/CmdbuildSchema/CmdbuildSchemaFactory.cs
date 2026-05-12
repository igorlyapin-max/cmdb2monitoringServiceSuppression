namespace Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;

public sealed class CmdbuildSchemaFactory
{
    private const string ServiceAggregationLookupCode = "ServiceAggregationType";
    private const string ServiceTypeLookupCode = "ServiceType";

    public CmdbuildSchemaDefinition Build(CmdbuildSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var prefix = options.Prefix.Trim();
        var classes = new List<CmdbuildClassDefinition>();

        classes.AddRange(BuildServiceClasses(prefix, options));
        classes.AddRange(BuildSuppressionClasses(prefix, options));
        classes.AddRange(BuildCustomClasses(prefix, options));
        var existingModelClasses = BuildExistingModelClasses(prefix, options).ToArray();
        var existingModelClassByCode = existingModelClasses
            .GroupBy(definition => definition.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        classes.AddRange(existingModelClasses);
        classes = classes
            .GroupBy(definition => definition.Code, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(definition => WithSchemaStatus(definition, existingModelClassByCode, options.Language))
            .ToList();

        var byBaseCode = classes
            .GroupBy(RemovePrefix(prefix), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var domains = BuildDomains(prefix, options.Language, byBaseCode)
            .Concat(BuildSourceLinkDomains(prefix, options, classes))
            .ToArray();
        var suggestedDomains = BuildSuggestedDomains(prefix, options, byBaseCode);
        var modelRoots = BuildModelRoots(options);

        return new CmdbuildSchemaDefinition
        {
            Prefix = prefix,
            Language = options.Language,
            BuilderVersion = options.BuilderVersion,
            Lookups = BuildLookups(options.Language),
            ModelRoots = modelRoots,
            Classes = classes,
            Domains = domains,
            SuggestedDomains = suggestedDomains
        };
    }

    private static Func<CmdbuildClassDefinition, string> RemovePrefix(string prefix)
    {
        return definition => RemoveManagedPrefix(prefix, definition.Code);
    }

    private static IEnumerable<CmdbuildClassDefinition> BuildServiceClasses(string prefix, CmdbuildSchemaOptions options)
    {
        var rootClasses = BuildModelRootClasses(prefix, BuilderLayer.Service, options).ToArray();
        foreach (var rootClass in rootClasses)
        {
            yield return rootClass;
        }

        yield return BuildSuperclass(prefix, "ServiceManagedObject", BuilderLayer.Service, options, rootClasses.LastOrDefault()?.Code ?? "");
        yield return BuildClass(prefix, "ServiceResource", BuilderLayer.Service, options, "service_resource");
        yield return BuildClass(prefix, "ServiceNetworkAccessZone", BuilderLayer.Service, options, "network_zone");
        yield return BuildClass(prefix, "ServiceComputeCluster", BuilderLayer.Service, options, "compute_cluster");
        yield return BuildClass(prefix, "ServiceUserEndpointFleet", BuilderLayer.Service, options, "endpoint_fleet");
        yield return BuildClass(prefix, "ServiceWorkplaceGroup", BuilderLayer.Service, options, "workplace_group");
        yield return BuildClass(prefix, "ServicePlatformService", BuilderLayer.Service, options, "platform_service");
        yield return BuildClass(prefix, "ServiceDatabaseService", BuilderLayer.Service, options, "database_service");
        yield return BuildClass(prefix, "ServiceStoragePool", BuilderLayer.Service, options, "storage_pool");
    }

    private static IEnumerable<CmdbuildClassDefinition> BuildSuppressionClasses(string prefix, CmdbuildSchemaOptions options)
    {
        var rootClasses = BuildModelRootClasses(prefix, BuilderLayer.Suppression, options).ToArray();
        foreach (var rootClass in rootClasses)
        {
            yield return rootClass;
        }

        yield return BuildSuperclass(prefix, "SuppressionManagedObject", BuilderLayer.Suppression, options, rootClasses.LastOrDefault()?.Code ?? "");
        yield return BuildClass(prefix, "SuppressionResource", BuilderLayer.Suppression, options, "suppression_resource");
        yield return BuildClass(prefix, "SuppressionNetworkAccessZone", BuilderLayer.Suppression, options, "network_zone");
        yield return BuildClass(prefix, "SuppressionComputeCluster", BuilderLayer.Suppression, options, "compute_cluster");
        yield return BuildClass(prefix, "SuppressionStoragePool", BuilderLayer.Suppression, options, "storage_pool");
        yield return BuildClass(prefix, "SuppressionProxyGroup", BuilderLayer.Suppression, options, "proxy_group");
    }

    private static IEnumerable<CmdbuildClassDefinition> BuildModelRootClasses(
        string prefix,
        BuilderLayer layer,
        CmdbuildSchemaOptions options)
    {
        var rootPath = NormalizeModelRoot(
            layer == BuilderLayer.Service ? options.ServiceModelRoot : options.SuppressionModelRoot,
            options.Language);

        foreach (var rootClass in CmdbuildSchemaClassCodes.ModelRootClassCodes(prefix, layer, rootPath))
        {
            yield return new CmdbuildClassDefinition
            {
                Code = rootClass.Code,
                DisplayName = rootClass.DisplayName,
                Layer = layer,
                Purpose = Text.ModelRootSuperclassPurpose(layer, rootClass.RootPath, options.Language),
                Help = Text.ModelRootSuperclassHelp(layer, rootClass.RootPath, options.Language),
                IsSuperclass = true,
                ParentClassCode = rootClass.ParentClassCode,
                Origin = "model_root_superclass",
                ModelRoot = rootClass.RootPath,
                Attributes = []
            };
        }
    }

    private static IEnumerable<CmdbuildClassDefinition> BuildCustomClasses(
        string prefix,
        CmdbuildSchemaOptions options)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in options.CustomEntities)
        {
            var baseCode = NormalizeCustomBaseCode(prefix, entity.Code, entity.Layer);
            if (string.IsNullOrWhiteSpace(baseCode) || !seen.Add($"{entity.Layer}:{baseCode}"))
            {
                continue;
            }

            var purpose = string.IsNullOrWhiteSpace(entity.Purpose)
                ? Text.CustomClassPurpose(entity.Layer, options.Language)
                : entity.Purpose.Trim();
            var displayName = string.IsNullOrWhiteSpace(entity.DisplayName)
                ? baseCode
                : entity.DisplayName.Trim();

            yield return new CmdbuildClassDefinition
            {
                Code = ApplyPrefix(prefix, baseCode),
                DisplayName = displayName,
                Layer = entity.Layer,
                Purpose = purpose,
                Help = Text.ClassHelp(purpose, options.Language),
                ParentClassCode = ApplyPrefix(prefix, ManagedObjectBaseCode(entity.Layer)),
                Attributes = CustomEntityAttributes(entity.Layer, options.Language)
                    .ToArray()
            };
        }
    }

    private static IEnumerable<CmdbuildClassDefinition> BuildExistingModelClasses(
        string prefix,
        CmdbuildSchemaOptions options)
    {
        _ = prefix;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in options.ExistingModelClasses)
        {
            var code = (entity.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code)
                || !entity.ManagedByBuilder
                || !entity.AutoPopulationEnabled
                || !seen.Add($"{entity.Layer}:{code}"))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(entity.DisplayName)
                ? code
                : entity.DisplayName.Trim();
            var modelRoot = NormalizeModelRoot(entity.ModelRoot, options.Language);
            var parentClassCode = (entity.ParentClassCode ?? "").Trim();

            yield return new CmdbuildClassDefinition
            {
                Code = code,
                DisplayName = displayName,
                Layer = entity.Layer,
                Purpose = Text.ExistingModelClassPurpose(entity.Layer, modelRoot, options.Language),
                Help = Text.ExistingModelClassHelp(entity.Layer, modelRoot, options.Language),
                ParentClassCode = parentClassCode,
                Origin = "existing_managed_descendant",
                ExistingInModelRoot = true,
                ModelRoot = modelRoot,
                ManagedByBuilder = true,
                AutoPopulationEnabled = true,
                Attributes = []
            };
        }
    }

    private static CmdbuildClassDefinition WithSchemaStatus(
        CmdbuildClassDefinition definition,
        IReadOnlyDictionary<string, CmdbuildClassDefinition> existingModelClassByCode,
        SchemaLanguage language)
    {
        if (existingModelClassByCode.TryGetValue(definition.Code, out var existingClass))
        {
            return definition with
            {
                ExistingInModelRoot = true,
                ModelRoot = existingClass.ModelRoot,
                ManagedByBuilder = existingClass.ManagedByBuilder,
                AutoPopulationEnabled = existingClass.AutoPopulationEnabled,
                SchemaStatus = "ready_to_work",
                SchemaStatusLabel = Text.SchemaStatusLabel("ready_to_work", language)
            };
        }

        return definition with
        {
            ExistingInModelRoot = false,
            SchemaStatus = "recommended_to_create",
            SchemaStatusLabel = Text.SchemaStatusLabel("recommended_to_create", language)
        };
    }

    private static CmdbuildClassDefinition BuildSuperclass(
        string prefix,
        string baseCode,
        BuilderLayer layer,
        CmdbuildSchemaOptions options,
        string parentClassCode)
    {
        var language = options.Language;
        var purpose = Text.ClassPurpose(ManagedObjectKind(layer), layer, language);

        return new CmdbuildClassDefinition
        {
            Code = prefix + baseCode,
            DisplayName = Text.ClassName(baseCode, language),
            Layer = layer,
            Purpose = purpose,
            Help = Text.ClassHelp(purpose, language),
            IsSuperclass = true,
            ParentClassCode = parentClassCode,
            Attributes = CommonAttributes(layer, language, options.BuilderVersion)
                .ToArray()
        };
    }

    private static CmdbuildClassDefinition BuildClass(
        string prefix,
        string baseCode,
        BuilderLayer layer,
        CmdbuildSchemaOptions options,
        string kind)
    {
        var language = options.Language;
        var displayName = Text.ClassName(baseCode, language);
        var purpose = Text.ClassPurpose(kind, layer, language);
        var attributes = SpecificAttributes(kind, layer, language)
            .ToArray();

        return new CmdbuildClassDefinition
        {
            Code = prefix + baseCode,
            DisplayName = displayName,
            Layer = layer,
            Purpose = purpose,
            Help = Text.ClassHelp(kind, purpose, language),
            ParentClassCode = ApplyPrefix(prefix, ManagedObjectBaseCode(layer)),
            Attributes = attributes
        };
    }

    private static IReadOnlyList<CmdbuildDomainDefinition> BuildDomains(
        string prefix,
        SchemaLanguage language,
        IReadOnlyDictionary<string, CmdbuildClassDefinition> classes)
    {
        return
        [
            Domain(prefix, language, BuilderLayer.Service, "ServiceResourceMemberOfFleet", "member_of", classes["ServiceResource"], classes["ServiceUserEndpointFleet"]),
            Domain(prefix, language, BuilderLayer.Service, "ServiceFleetAggregatesToWorkplaceGroup", "aggregates_to", classes["ServiceUserEndpointFleet"], classes["ServiceWorkplaceGroup"]),
            Domain(prefix, language, BuilderLayer.Service, "ServiceWorkplaceGroupAggregatesToPlatformService", "aggregates_to", classes["ServiceWorkplaceGroup"], classes["ServicePlatformService"]),
            Domain(prefix, language, BuilderLayer.Service, "ServicePlatformDependsOnDatabase", "service_depends_on", classes["ServicePlatformService"], classes["ServiceDatabaseService"]),
            Domain(prefix, language, BuilderLayer.Service, "ServicePlatformDependsOnStoragePool", "service_depends_on", classes["ServicePlatformService"], classes["ServiceStoragePool"]),
            Domain(prefix, language, BuilderLayer.Service, "ServicePlatformDependsOnNetworkZone", "service_depends_on", classes["ServicePlatformService"], classes["ServiceNetworkAccessZone"]),
            Domain(prefix, language, BuilderLayer.Service, "ServiceFleetDependsOnNetworkZone", "service_depends_on", classes["ServiceUserEndpointFleet"], classes["ServiceNetworkAccessZone"]),
            Domain(prefix, language, BuilderLayer.Service, "ServiceNetworkZoneDependsOnNetworkZone", "service_depends_on", classes["ServiceNetworkAccessZone"], classes["ServiceNetworkAccessZone"]),
            Domain(prefix, language, BuilderLayer.Service, "ServiceDatabaseDependsOnComputeCluster", "service_depends_on", classes["ServiceDatabaseService"], classes["ServiceComputeCluster"]),

            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionResourceDependsOnNetwork", "depends_on_network", classes["SuppressionResource"], classes["SuppressionNetworkAccessZone"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionResourceRunsOnCompute", "runs_on_compute", classes["SuppressionResource"], classes["SuppressionComputeCluster"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionResourceDependsOnStoragePool", "depends_on", classes["SuppressionResource"], classes["SuppressionStoragePool"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionResourceMonitoredViaProxyGroup", "monitored_via", classes["SuppressionResource"], classes["SuppressionProxyGroup"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionComputeDependsOnStoragePool", "depends_on", classes["SuppressionComputeCluster"], classes["SuppressionStoragePool"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionNetworkZoneDependsOnNetworkZone", "depends_on_network", classes["SuppressionNetworkAccessZone"], classes["SuppressionNetworkAccessZone"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionResourceSuppressesResource", "depends_on", classes["SuppressionResource"], classes["SuppressionResource"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionNetworkZoneSuppressesResource", "depends_on", classes["SuppressionNetworkAccessZone"], classes["SuppressionResource"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionComputeSuppressesResource", "depends_on", classes["SuppressionComputeCluster"], classes["SuppressionResource"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionStoragePoolSuppressesResource", "depends_on", classes["SuppressionStoragePool"], classes["SuppressionResource"]),
            Domain(prefix, language, BuilderLayer.Suppression, "SuppressionProxyGroupSuppressesResource", "depends_on", classes["SuppressionProxyGroup"], classes["SuppressionResource"])
        ];
    }

    private static IReadOnlyList<CmdbuildDomainDefinition> BuildSuggestedDomains(
        string prefix,
        CmdbuildSchemaOptions options,
        IReadOnlyDictionary<string, CmdbuildClassDefinition> classes)
    {
        var result = new List<CmdbuildDomainDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in options.CustomEntities.Where(entity => entity.SuggestDomains))
        {
            var baseCode = NormalizeCustomBaseCode(prefix, entity.Code, entity.Layer);
            if (string.IsNullOrWhiteSpace(baseCode) || !classes.TryGetValue(baseCode, out var customClass))
            {
                continue;
            }

            foreach (var domain in SuggestDomainsForCustomClass(prefix, options.Language, entity.Layer, baseCode, customClass, classes))
            {
                if (seen.Add(domain.Code))
                {
                    result.Add(domain);
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<CmdbuildDomainDefinition> BuildSourceLinkDomains(
        string prefix,
        CmdbuildSchemaOptions options,
        IReadOnlyList<CmdbuildClassDefinition> classes)
    {
        var result = new List<CmdbuildDomainDefinition>();
        var classByCode = classes.ToDictionary(definition => definition.Code, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in options.SourceLinks)
        {
            var customerClassCode = NormalizeCustomerClassCode(link.CustomerClassCode);
            if (string.IsNullOrWhiteSpace(customerClassCode)
                || !TryGetManagedClassForSourceLink(prefix, link.ManagedClassCode, classByCode, out var managedClass)
                || managedClass.IsSuperclass)
            {
                continue;
            }

            var customerDomainPart = NormalizeDomainPart(customerClassCode);
            if (string.IsNullOrWhiteSpace(customerDomainPart))
            {
                continue;
            }

            var domainBaseCode = $"{RemoveManagedPrefix(prefix, managedClass.Code)}PopulatedFrom{customerDomainPart}";
            var domainCode = prefix + domainBaseCode;
            if (!seen.Add(domainCode))
            {
                continue;
            }

            result.Add(SourceLinkDomain(prefix, options.Language, domainBaseCode, managedClass, customerClassCode));
        }

        return result;
    }

    private static bool TryGetManagedClassForSourceLink(
        string prefix,
        string managedClassCode,
        IReadOnlyDictionary<string, CmdbuildClassDefinition> classByCode,
        out CmdbuildClassDefinition managedClass)
    {
        var requestedCode = (managedClassCode ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(requestedCode)
            && classByCode.TryGetValue(requestedCode, out managedClass!))
        {
            return true;
        }

        var prefixedCode = ApplyPrefix(prefix, RemoveManagedPrefix(prefix, requestedCode));
        if (!string.IsNullOrWhiteSpace(prefixedCode)
            && classByCode.TryGetValue(prefixedCode, out managedClass!))
        {
            return true;
        }

        managedClass = null!;
        return false;
    }

    private static IEnumerable<CmdbuildDomainDefinition> SuggestDomainsForCustomClass(
        string prefix,
        SchemaLanguage language,
        BuilderLayer layer,
        string baseCode,
        CmdbuildClassDefinition customClass,
        IReadOnlyDictionary<string, CmdbuildClassDefinition> classes)
    {
        if (layer == BuilderLayer.Service)
        {
            if (classes.TryGetValue("ServiceResource", out var serviceResource))
            {
                yield return SuggestedDomain(
                    prefix,
                    language,
                    layer,
                    $"ServiceResourceMemberOf{baseCode}",
                    "member_of",
                    serviceResource,
                    customClass,
                    Text.SuggestedDomainReason("service_member_of", language));
            }

            if (classes.TryGetValue("ServicePlatformService", out var platformService))
            {
                yield return SuggestedDomain(
                    prefix,
                    language,
                    layer,
                    $"{baseCode}AggregatesToPlatformService",
                    "aggregates_to",
                    customClass,
                    platformService,
                    Text.SuggestedDomainReason("service_aggregates_to", language));

                yield return SuggestedDomain(
                    prefix,
                    language,
                    layer,
                    $"ServicePlatformDependsOn{baseCode}",
                    "service_depends_on",
                    platformService,
                    customClass,
                    Text.SuggestedDomainReason("service_depends_on", language));
            }

            yield break;
        }

        if (classes.TryGetValue("SuppressionResource", out var suppressionResource))
        {
            var relationType = InferSuppressionRelationType(baseCode);
            yield return SuggestedDomain(
                prefix,
                language,
                layer,
                $"SuppressionResource{DomainVerb(relationType)}{baseCode}",
                relationType,
                suppressionResource,
                customClass,
                Text.SuggestedDomainReason("suppression_resource_dependency", language));
        }

        if (classes.TryGetValue("SuppressionNetworkAccessZone", out var networkZone)
            && !string.Equals(baseCode, "SuppressionNetworkAccessZone", StringComparison.Ordinal))
        {
            yield return SuggestedDomain(
                prefix,
                language,
                layer,
                $"{baseCode}DependsOnSuppressionNetworkAccessZone",
                "depends_on_network",
                customClass,
                networkZone,
                Text.SuggestedDomainReason("suppression_network_dependency", language));
        }

        if (classes.TryGetValue("SuppressionResource", out suppressionResource)
            && !string.Equals(baseCode, "SuppressionResource", StringComparison.Ordinal))
        {
            yield return SuggestedDomain(
                prefix,
                language,
                layer,
                $"{baseCode}SuppressesSuppressionResource",
                "depends_on",
                customClass,
                suppressionResource,
                Text.SuggestedDomainReason("suppression_suppresses_resource", language));
        }
    }

    private static CmdbuildDomainDefinition Domain(
        string prefix,
        SchemaLanguage language,
        BuilderLayer layer,
        string baseCode,
        string relationType,
        CmdbuildClassDefinition source,
        CmdbuildClassDefinition target)
    {
        return new CmdbuildDomainDefinition
        {
            Code = prefix + baseCode,
            DisplayName = Text.DomainName(relationType, language),
            Layer = layer,
            SourceClassCode = source.Code,
            TargetClassCode = target.Code,
            RelationType = relationType,
            DeleteRelationOnCardDelete = true,
            Help = Text.DomainHelp(relationType, language),
            Attributes = DomainAttributes(relationType, language)
        };
    }

    private static CmdbuildDomainDefinition SuggestedDomain(
        string prefix,
        SchemaLanguage language,
        BuilderLayer layer,
        string baseCode,
        string relationType,
        CmdbuildClassDefinition source,
        CmdbuildClassDefinition target,
        string reason)
    {
        return Domain(prefix, language, layer, baseCode, relationType, source, target) with
        {
            Suggested = true,
            Reason = reason
        };
    }

    private static CmdbuildDomainDefinition SourceLinkDomain(
        string prefix,
        SchemaLanguage language,
        string baseCode,
        CmdbuildClassDefinition managedClass,
        string customerClassCode)
    {
        return new CmdbuildDomainDefinition
        {
            Code = prefix + baseCode,
            DisplayName = Text.DomainName("populated_from", language),
            Layer = managedClass.Layer,
            SourceClassCode = managedClass.Code,
            TargetClassCode = customerClassCode,
            RelationType = "populated_from",
            DeleteRelationOnCardDelete = true,
            Help = Text.DomainHelp("populated_from", language),
            Attributes = DomainAttributes("populated_from", language),
            IsSourceLink = true
        };
    }

    private static IReadOnlyList<CmdbuildLookupDefinition> BuildLookups(SchemaLanguage language)
    {
        return
        [
            new CmdbuildLookupDefinition
            {
                Code = ServiceAggregationLookupCode,
                DisplayName = Text.LookupName(ServiceAggregationLookupCode, language),
                Values = ServiceAggregationLookupValues(language).ToArray()
            },
            new CmdbuildLookupDefinition
            {
                Code = ServiceTypeLookupCode,
                DisplayName = Text.LookupName(ServiceTypeLookupCode, language),
                Values = ServiceTypeLookupValues(language).ToArray()
            }
        ];
    }

    private static IEnumerable<CmdbuildLookupValueDefinition> ServiceAggregationLookupValues(SchemaLanguage language)
    {
        foreach (var code in new[] { "all", "any", "threshold", "n_of_m" })
        {
            yield return new CmdbuildLookupValueDefinition
            {
                Code = code,
                DisplayName = Text.LookupValueName(ServiceAggregationLookupCode, code, language),
                Help = Text.LookupValueHelp(ServiceAggregationLookupCode, code, language)
            };
        }
    }

    private static IEnumerable<CmdbuildLookupValueDefinition> ServiceTypeLookupValues(SchemaLanguage language)
    {
        foreach (var code in new[] { "business", "application", "platform", "integration", "infrastructure" })
        {
            yield return new CmdbuildLookupValueDefinition
            {
                Code = code,
                DisplayName = Text.LookupValueName(ServiceTypeLookupCode, code, language),
                Help = Text.LookupValueHelp(ServiceTypeLookupCode, code, language)
            };
        }
    }

    private static IReadOnlyList<CmdbuildModelRootDefinition> BuildModelRoots(CmdbuildSchemaOptions options)
    {
        return
        [
            ModelRoot(
                BuilderLayer.Service,
                NormalizeModelRoot(options.ServiceModelRoot, options.Language),
                options.Language),
            ModelRoot(
                BuilderLayer.Suppression,
                NormalizeModelRoot(options.SuppressionModelRoot, options.Language),
                options.Language)
        ];
    }

    private static CmdbuildModelRootDefinition ModelRoot(
        BuilderLayer layer,
        string rootPath,
        SchemaLanguage language)
    {
        return new CmdbuildModelRootDefinition
        {
            Layer = layer,
            RootPath = rootPath,
            Help = Text.ModelRootHelp(layer, language)
        };
    }

    private static string NormalizeModelRoot(string rootPath, SchemaLanguage language)
    {
        var normalized = (rootPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultModelRoot(language);
        }

        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : "/" + normalized;
    }

    private static string DefaultModelRoot(SchemaLanguage language)
    {
        return language == SchemaLanguage.En
            ? "/Monitoring"
            : "/Мониторинг";
    }

    private static IEnumerable<CmdbuildAttributeDefinition> CommonAttributes(
        BuilderLayer layer,
        SchemaLanguage language,
        string builderVersion)
    {
        _ = builderVersion;

        yield return Attribute("name", Text.AttrName("name", language), "string", true, Text.AttrHelp("name", language));
        yield return Attribute("description", Text.AttrName("description", language), "text", false, Text.AttrHelp("description", language));
        yield return Attribute("is_active", Text.AttrName("is_active", language), "boolean", true, Text.AttrHelp("is_active", language));
        yield return Attribute("is_critical", Text.AttrName("is_critical", language), "boolean", false, Text.AttrHelp("is_critical", language));
        yield return Attribute("managed_by_builder", Text.AttrName("managed_by_builder", language), "boolean", true, Text.AttrHelp("managed_by_builder", language));
        yield return Attribute("auto_population_enabled", Text.AttrName("auto_population_enabled", language), "boolean", true, Text.AttrHelp("auto_population_enabled", language));
        yield return Attribute("population_rule_id", Text.AttrName("population_rule_id", language), "string", false, Text.AttrHelp("population_rule_id", language));
        yield return Attribute("population_source_key", Text.AttrName("population_source_key", language), "string", false, Text.AttrHelp("population_source_key", language));
        yield return Attribute("last_populated_at", Text.AttrName("last_populated_at", language), "datetime", false, Text.AttrHelp("last_populated_at", language));
        yield return Attribute("builder_version", Text.AttrName("builder_version", language), "string", false, Text.AttrHelp("builder_version", language));

        if (layer is BuilderLayer.Service or BuilderLayer.Suppression)
        {
            yield return Attribute(
                "aggregation_type",
                Text.AttrName("aggregation_type", language),
                "lookup",
                false,
                Text.AttrHelp("aggregation_type", language),
                ServiceAggregationLookupCode,
                ServiceAggregationValidationScript(language));
            yield return Attribute("threshold", Text.AttrName("threshold", language), "decimal", false, Text.AttrHelp("threshold", language));
            yield return Attribute("n", Text.AttrName("n", language), "integer", false, Text.AttrHelp("n", language));
        }
    }

    private static IEnumerable<CmdbuildAttributeDefinition> SpecificAttributes(
        string kind,
        BuilderLayer layer,
        SchemaLanguage language)
    {
        switch (kind, layer)
        {
            case ("platform_service", BuilderLayer.Service):
                yield return Attribute(
                    "service_type",
                    Text.AttrName("service_type", language),
                    "lookup",
                    false,
                    Text.AttrHelp("service_type", language),
                    ServiceTypeLookupCode,
                    ServiceTypeValidationScript(language));
                yield return Attribute("sla_target", Text.AttrName("sla_target", language), "decimal", false, Text.AttrHelp("sla_target", language));
                break;
            case ("proxy_group", BuilderLayer.Suppression):
                yield return Attribute("fallback_supported", Text.AttrName("fallback_supported", language), "boolean", false, Text.AttrHelp("fallback_supported", language));
                break;
        }
    }

    private static IEnumerable<CmdbuildAttributeDefinition> CustomEntityAttributes(BuilderLayer layer, SchemaLanguage language)
    {
        _ = layer;
        _ = language;

        yield break;
    }

    private static IReadOnlyList<CmdbuildAttributeDefinition> DomainAttributes(string relationType, SchemaLanguage language)
    {
        var attributes = new List<CmdbuildAttributeDefinition>
        {
            Attribute("is_active", Text.AttrName("is_active", language), "boolean", true, Text.AttrHelp("domain_is_active", language))
        };

        if (relationType is "depends_on_network" or "runs_on_compute" or "depends_on" or "monitored_via")
        {
            attributes.Add(Attribute("priority", Text.AttrName("priority", language), "integer", false, Text.AttrHelp("priority", language)));
            attributes.Add(Attribute("source", Text.AttrName("source", language), "string", false, Text.AttrHelp("source", language)));
        }

        if (relationType == "populated_from")
        {
            attributes.Add(Attribute("source", Text.AttrName("source", language), "string", false, Text.AttrHelp("source", language)));
            attributes.Add(Attribute("population_rule_id", Text.AttrName("population_rule_id", language), "string", false, Text.AttrHelp("population_rule_id", language)));
        }

        return attributes;
    }

    private static CmdbuildAttributeDefinition Attribute(
        string code,
        string displayName,
        string type,
        bool required,
        string help,
        string lookupTypeCode = "",
        string validationRules = "")
    {
        return new CmdbuildAttributeDefinition
        {
            Code = code,
            DisplayName = displayName,
            Type = type,
            LookupTypeCode = lookupTypeCode,
            Required = required,
            Help = help,
            ValidationRules = validationRules
        };
    }

    private static string ServiceAggregationValidationScript(SchemaLanguage language)
    {
        var message = language == SchemaLanguage.En
            ? "aggregation_type must match its parameters: all/any use no threshold or n, threshold requires threshold 0..100 and empty n, n_of_m requires n >= 1 and empty threshold."
            : "aggregation_type должен соответствовать параметрам: all/any не используют threshold и n, threshold требует threshold 0..100 и пустой n, n_of_m требует n >= 1 и пустой threshold.";

        return $$"""
var read = function (name) {
  if (typeof api !== 'undefined' && api.getValue) {
    return api.getValue(name);
  }
  if (typeof record !== 'undefined' && record.get) {
    return record.get(name);
  }
  if (typeof values !== 'undefined') {
    return values[name];
  }
  if (typeof data !== 'undefined') {
    return data[name];
  }
  return null;
};
var lookupCode = function (value) {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  if (typeof value === 'object') {
    return String(value.code || value._code || value.name || value.value || value.description || value._id || '').toLowerCase();
  }
  return String(value).toLowerCase();
};
var empty = function (value) {
  return value === null || value === undefined || String(value).trim() === '';
};
var numberValue = function (value) {
  if (empty(value)) {
    return null;
  }
  return Number(String(value).replace(',', '.'));
};
var type = lookupCode(read('aggregation_type'));
var threshold = read('threshold');
var n = read('n');
if (type === '' || type === 'all' || type === 'any') {
  return empty(threshold) && empty(n) ? true : '{{message}}';
}
if (type === 'threshold') {
  var thresholdValue = numberValue(threshold);
  return thresholdValue !== null && thresholdValue >= 0 && thresholdValue <= 100 && empty(n) ? true : '{{message}}';
}
if (type === 'n_of_m') {
  var nValue = numberValue(n);
  return nValue !== null && Math.floor(nValue) === nValue && nValue >= 1 && empty(threshold) ? true : '{{message}}';
}
return true;
""";
    }

    private static string ServiceTypeValidationScript(SchemaLanguage language)
    {
        var message = language == SchemaLanguage.En
            ? "sla_target, when filled, must be a percentage from 0 to 100. Use 99.9 for 99.9%, not 0.999."
            : "sla_target, если заполнен, должен быть процентом от 0 до 100. Используйте 99.9 для 99.9%, не 0.999.";

        return $$"""
var read = function (name) {
  if (typeof api !== 'undefined' && api.getValue) {
    return api.getValue(name);
  }
  if (typeof record !== 'undefined' && record.get) {
    return record.get(name);
  }
  if (typeof values !== 'undefined') {
    return values[name];
  }
  if (typeof data !== 'undefined') {
    return data[name];
  }
  return null;
};
var value = read('sla_target');
if (value === null || value === undefined || String(value).trim() === '') {
  return true;
}
var parsed = Number(String(value).replace(',', '.'));
return parsed >= 0 && parsed <= 100 ? true : '{{message}}';
""";
    }

    private static string NormalizeCustomBaseCode(string prefix, string code, BuilderLayer layer)
    {
        var normalized = new string((code ?? "")
            .Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        if (!string.IsNullOrEmpty(prefix)
            && normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            normalized = normalized[prefix.Length..];
        }

        var layerPrefix = layer == BuilderLayer.Service ? "Service" : "Suppression";
        if (!normalized.StartsWith(layerPrefix, StringComparison.Ordinal))
        {
            normalized = layerPrefix + normalized;
        }

        return normalized;
    }

    private static string ApplyPrefix(string prefix, string baseCode)
    {
        return string.IsNullOrWhiteSpace(prefix) || baseCode.StartsWith(prefix, StringComparison.Ordinal)
            ? baseCode
            : prefix + baseCode;
    }

    private static string RemoveManagedPrefix(string prefix, string code)
    {
        var normalized = (code ?? "").Trim();
        return !string.IsNullOrWhiteSpace(prefix) && normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : normalized;
    }

    private static string NormalizeCustomerClassCode(string code)
    {
        return (code ?? "").Trim();
    }

    private static string NormalizeDomainPart(string code)
    {
        return new string((code ?? "")
            .Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string ManagedObjectBaseCode(BuilderLayer layer)
    {
        return layer == BuilderLayer.Service
            ? "ServiceManagedObject"
            : "SuppressionManagedObject";
    }

    private static string ManagedObjectKind(BuilderLayer layer)
    {
        return layer == BuilderLayer.Service
            ? "service_managed_object"
            : "suppression_managed_object";
    }

    private static string InferSuppressionRelationType(string baseCode)
    {
        if (baseCode.Contains("Proxy", StringComparison.OrdinalIgnoreCase))
        {
            return "monitored_via";
        }

        if (baseCode.Contains("Compute", StringComparison.OrdinalIgnoreCase)
            || baseCode.Contains("Cluster", StringComparison.OrdinalIgnoreCase)
            || baseCode.Contains("Hypervisor", StringComparison.OrdinalIgnoreCase))
        {
            return "runs_on_compute";
        }

        if (baseCode.Contains("Network", StringComparison.OrdinalIgnoreCase)
            || baseCode.Contains("Zone", StringComparison.OrdinalIgnoreCase)
            || baseCode.Contains("Subnet", StringComparison.OrdinalIgnoreCase)
            || baseCode.Contains("Vlan", StringComparison.OrdinalIgnoreCase))
        {
            return "depends_on_network";
        }

        return "depends_on";
    }

    private static string DomainVerb(string relationType)
    {
        return relationType switch
        {
            "depends_on_network" => "DependsOnNetwork",
            "runs_on_compute" => "RunsOn",
            "monitored_via" => "MonitoredVia",
            _ => "DependsOn"
        };
    }
}
