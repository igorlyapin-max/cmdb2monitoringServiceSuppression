using Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;

var factory = new CmdbuildSchemaFactory();
var schema = factory.Build(new CmdbuildSchemaOptions
{
    Prefix = "C2M_",
    Language = SchemaLanguage.Ru,
    BuilderVersion = "test",
    CustomEntities =
    [
        new CmdbuildCustomEntityOptions
        {
            Code = "ApplicationCluster",
            Layer = BuilderLayer.Service,
            DisplayName = "Кластер приложений"
        },
        new CmdbuildCustomEntityOptions
        {
            Code = "FirewallGroup",
            Layer = BuilderLayer.Suppression,
            DisplayName = "Группа firewall"
        }
    ],
    SourceLinks =
    [
        new CmdbuildSourceLinkOptions
        {
            ManagedClassCode = "C2M_ServiceWorkplaceGroup",
            CustomerClassCode = "CustomerWorkplace"
        },
        new CmdbuildSourceLinkOptions
        {
            ManagedClassCode = "C2M_SuppressionResource",
            CustomerClassCode = "CustomerServer"
        },
        new CmdbuildSourceLinkOptions
        {
            ManagedClassCode = "C2M_SuppressionManagedObject",
            CustomerClassCode = "CustomerSuperclassMustBeIgnored"
        }
    ]
});

Assert(schema.Classes.Any(c => c.Code == "C2M_ServiceNetworkAccessZone"), "service network zone class is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_SuppressionNetworkAccessZone"), "suppression network zone class is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_ServiceApplicationCluster"), "custom service class is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_SuppressionFirewallGroup"), "custom suppression class is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_ServiceManagedObject" && c.IsSuperclass), "service superclass is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_SuppressionManagedObject" && c.IsSuperclass), "suppression superclass is missing");
Assert(schema.Classes.Count(c => c.Code == "C2M_Monitoring" && c.IsSuperclass) == 1, "shared model root superclass is missing");
Assert(!schema.Classes.Any(c => c.Code is "C2M_ServiceMonitoring" or "C2M_SuppressionMonitoring"), "model root superclass must not be duplicated per layer");
Assert(schema.Classes.All(c => c.Help.Contains("Класс управляется автоматизировано", StringComparison.Ordinal)), "class automation help is missing");
Assert(schema.Classes.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count() == schema.Classes.Count, "class codes must be unique");
AssertManagedInheritance(schema, BuilderLayer.Service, "C2M_ServiceManagedObject", "C2M_Monitoring");
AssertManagedInheritance(schema, BuilderLayer.Suppression, "C2M_SuppressionManagedObject", "C2M_Monitoring");
AssertClassApplyOrder(schema);
AssertModelRoots(schema, "/Мониторинг", "/Мониторинг");
Assert(schema.Domains.Count > 0, "domains are missing");
Assert(schema.Domains.All(d => d.DeleteRelationOnCardDelete), "all domains must delete relation on linked card delete");
Assert(schema.Domains.All(d => d.Help.Contains("удалять связь", StringComparison.Ordinal)), "domain delete help is missing");
Assert(schema.SuggestedDomains.Count >= 3, "suggested domains are missing");
Assert(schema.SuggestedDomains.All(d => d.Suggested), "suggested domains must be marked as suggested");
Assert(schema.SuggestedDomains.All(d => !string.IsNullOrWhiteSpace(d.Reason)), "suggested domains must explain the reason");
Assert(schema.SuggestedDomains.Any(d => d.TargetClassCode == "C2M_ServiceApplicationCluster"), "custom service suggested domain is missing");
Assert(schema.SuggestedDomains.Any(d => d.TargetClassCode == "C2M_SuppressionFirewallGroup"), "custom suppression suggested domain is missing");
AssertServiceAggregationLookup(schema);
AssertServiceTypeLookup(schema);
AssertAttributeValidationRules(schema);

AssertAttributes(
    schema,
    "C2M_ServiceManagedObject",
    present: CommonAttributeCodes().Concat(ServiceAggregationAttributeCodes()).ToArray(),
    absent: RemovedSourceAttributeCodes().Concat(["fallback_supported", "priority", "is_dynamic"]).ToArray());
AssertAttributes(
    schema,
    "C2M_SuppressionManagedObject",
    present: CommonAttributeCodes(),
    absent: RemovedSourceAttributeCodes().Concat(["aggregation_type", "threshold", "n", "fallback_supported", "priority", "is_dynamic"]).ToArray());
AssertAttributes(
    schema,
    "C2M_ServiceNetworkAccessZone",
    present: [],
    absent: ServiceInheritedAttributeCodes().Concat(["zone_id", "subnet_list", "site"]).ToArray());
AssertAttributes(
    schema,
    "C2M_ServiceComputeCluster",
    present: [],
    absent: ServiceInheritedAttributeCodes().Concat(["cluster_id", "is_ha_enabled"]).ToArray());
AssertAttributes(
    schema,
    "C2M_ServiceUserEndpointFleet",
    present: [],
    absent: ServiceInheritedAttributeCodes());
AssertAttributes(
    schema,
    "C2M_ServiceWorkplaceGroup",
    present: [],
    absent: ServiceInheritedAttributeCodes().Concat(["group_id", "location"]).ToArray());
AssertAttributes(
    schema,
    "C2M_ServicePlatformService",
    present: ["service_type", "sla_target"],
    absent: ServiceInheritedAttributeCodes());
AssertAttributes(
    schema,
    "C2M_ServiceDatabaseService",
    present: [],
    absent: ServiceInheritedAttributeCodes());
AssertAttributes(
    schema,
    "C2M_ServiceStoragePool",
    present: [],
    absent: ServiceInheritedAttributeCodes().Concat(["storage_type", "redundancy_level"]).ToArray());
AssertAttributes(
    schema,
    "C2M_SuppressionNetworkAccessZone",
    present: [],
    absent: CommonAttributeCodes().Concat(["zone_id", "subnet_list", "site"]).ToArray());
AssertAttributes(
    schema,
    "C2M_SuppressionProxyGroup",
    present: ["fallback_supported"],
    absent: CommonAttributeCodes());
AssertAttributes(
    schema,
    "C2M_ServiceApplicationCluster",
    present: [],
    absent: ServiceInheritedAttributeCodes());
AssertAttributes(
    schema,
    "C2M_SuppressionFirewallGroup",
    present: [],
    absent: CommonAttributeCodes().Concat(["priority", "is_dynamic"]).ToArray());
AssertDomainAttributes(
    schema,
    "C2M_SuppressionResourceMonitoredViaProxyGroup",
    present: ["is_active", "priority", "source"],
    absent: ["fallback_supported", "is_critical", "is_dynamic"]);
AssertDomainAttributes(
    schema,
    "C2M_ServicePlatformDependsOnDatabase",
    present: ["is_active"],
    absent: ["aggregation_type", "threshold", "n", "is_critical", "is_dynamic", "priority", "source"]);
AssertOperationalFlagHelp(schema);
Assert(schema.Domains
    .Where(domain => domain.Layer == BuilderLayer.Suppression)
    .All(domain => domain.Attributes.All(attribute => attribute.Code != "is_dynamic")),
    "suppression domains must not expose dynamic relation flag.");
AssertSourceLinkDomain(
    schema,
    "C2M_ServiceWorkplaceGroupPopulatedFromCustomerWorkplace",
    "C2M_ServiceWorkplaceGroup",
    "CustomerWorkplace");
AssertSourceLinkDomain(
    schema,
    "C2M_SuppressionResourcePopulatedFromCustomerServer",
    "C2M_SuppressionResource",
    "CustomerServer");
Assert(!schema.Domains.Any(domain => domain.TargetClassCode == "CustomerSuperclassMustBeIgnored"),
    "source links must not be generated for superclasses.");

var englishSchema = factory.Build(new CmdbuildSchemaOptions
{
    Prefix = "C2M_",
    Language = SchemaLanguage.En,
    BuilderVersion = "test"
});
AssertModelRoots(englishSchema, "/Monitoring", "/Monitoring");

var customRootSchema = factory.Build(new CmdbuildSchemaOptions
{
    Prefix = "C2M_",
    Language = SchemaLanguage.Ru,
    BuilderVersion = "test",
    ServiceModelRoot = "ServiceRoot",
    SuppressionModelRoot = "/SuppressionRoot"
});
AssertModelRoots(customRootSchema, "/ServiceRoot", "/SuppressionRoot");

var existingModelSchema = factory.Build(new CmdbuildSchemaOptions
{
    Prefix = "C2M_",
    Language = SchemaLanguage.Ru,
    BuilderVersion = "test",
    ExistingModelClasses =
    [
        new CmdbuildExistingModelClassOptions
        {
            Code = "C2M_ServicePlatformService",
            Layer = BuilderLayer.Service,
            DisplayName = "Existing platform",
            ModelRoot = "/Мониторинг",
            ParentClassCode = "C2M_ServiceManagedObject",
            ManagedByBuilder = true,
            AutoPopulationEnabled = true
        },
        new CmdbuildExistingModelClassOptions
        {
            Code = "CustomerManagedService",
            Layer = BuilderLayer.Service,
            DisplayName = "Customer managed service",
            ModelRoot = "ServiceRoot",
            ParentClassCode = "C2M_ServiceManagedObject",
            ManagedByBuilder = true,
            AutoPopulationEnabled = true
        },
        new CmdbuildExistingModelClassOptions
        {
            Code = "CustomerSuppressionObject",
            Layer = BuilderLayer.Suppression,
            DisplayName = "Customer suppression object",
            ModelRoot = "/SuppressionRoot",
            ParentClassCode = "C2M_SuppressionManagedObject",
            ManagedByBuilder = true,
            AutoPopulationEnabled = true
        },
        new CmdbuildExistingModelClassOptions
        {
            Code = "CustomerUnmanagedService",
            Layer = BuilderLayer.Service,
            DisplayName = "Customer unmanaged service",
            ModelRoot = "ServiceRoot",
            ParentClassCode = "C2M_ServiceManagedObject",
            ManagedByBuilder = false,
            AutoPopulationEnabled = true
        }
    ],
    SourceLinks =
    [
        new CmdbuildSourceLinkOptions
        {
            ManagedClassCode = "CustomerManagedService",
            CustomerClassCode = "CustomerBusinessService"
        }
    ]
});
AssertExistingModelClasses(existingModelSchema);

var validation = new ConversionRulesValidator().Validate(new ConversionRulesDocument
{
    Rules =
    [
        new ConversionRule
        {
            RuleId = "service-workplace-group",
            Name = "Create service workplace group",
            Layer = "service",
            Source = new SourceSelector { ClassCode = "CustomerWorkplace" },
            Target = new TargetObject
            {
                ClassCode = "C2M_ServiceWorkplaceGroup",
                IdempotencyKey = "${source.department}"
            }
        }
    ]
});

Assert(validation.IsValid, "sample conversion rule must be valid");

Console.WriteLine("Shared contract checks passed.");

static void AssertManagedInheritance(
    CmdbuildSchemaDefinition schema,
    BuilderLayer layer,
    string superclassCode,
    string rootSuperclassCode)
{
    var superclass = schema.Classes.Single(c => c.Code == superclassCode);
    Assert(superclass.Layer == layer, $"{superclassCode}: superclass layer is invalid.");
    Assert(superclass.ParentClassCode == rootSuperclassCode, $"{superclassCode}: superclass must inherit the model root superclass.");

    var rootSuperclass = schema.Classes.Single(c => c.Code == rootSuperclassCode);
    Assert(rootSuperclass.IsSuperclass, $"{rootSuperclassCode}: root class must be a superclass.");
    Assert(string.IsNullOrWhiteSpace(rootSuperclass.ParentClassCode), $"{rootSuperclassCode}: root superclass must inherit CMDBuild Class.");

    foreach (var classDefinition in schema.Classes.Where(c => c.Layer == layer && !c.IsSuperclass))
    {
        Assert(
            classDefinition.ParentClassCode == superclassCode,
            $"{classDefinition.Code}: managed class must inherit {superclassCode}.");
    }
}

static void AssertClassApplyOrder(CmdbuildSchemaDefinition schema)
{
    var indexByCode = schema.Classes
        .Select((classDefinition, index) => new { classDefinition.Code, Index = index })
        .ToDictionary(item => item.Code, item => item.Index, StringComparer.Ordinal);

    Assert(indexByCode["C2M_Monitoring"] < indexByCode["C2M_ServiceManagedObject"],
        "shared model root must be listed before the service managed superclass.");
    Assert(indexByCode["C2M_Monitoring"] < indexByCode["C2M_SuppressionManagedObject"],
        "shared model root must be listed before the suppression managed superclass.");
    Assert(indexByCode["C2M_ServiceManagedObject"] < indexByCode["C2M_ServiceResource"],
        "service managed superclass must be listed before ordinary service classes.");
    Assert(indexByCode["C2M_SuppressionManagedObject"] < indexByCode["C2M_SuppressionResource"],
        "suppression managed superclass must be listed before ordinary suppression classes.");
}

static void AssertModelRoots(
    CmdbuildSchemaDefinition schema,
    string expectedServiceRoot,
    string expectedSuppressionRoot)
{
    var serviceRoot = schema.ModelRoots.Single(root => root.Layer == BuilderLayer.Service);
    var suppressionRoot = schema.ModelRoots.Single(root => root.Layer == BuilderLayer.Suppression);
    Assert(serviceRoot.RootPath == expectedServiceRoot, "service model root is invalid.");
    Assert(suppressionRoot.RootPath == expectedSuppressionRoot, "suppression model root is invalid.");
    Assert(!string.IsNullOrWhiteSpace(serviceRoot.Help), "service model root help is missing.");
    Assert(!string.IsNullOrWhiteSpace(suppressionRoot.Help), "suppression model root help is missing.");
}

static void AssertExistingModelClasses(CmdbuildSchemaDefinition schema)
{
    var existingPlatform = schema.Classes.Single(c => c.Code == "C2M_ServicePlatformService");
    Assert(existingPlatform.SchemaStatus == "ready_to_work", "existing planned class must be ready to work.");
    Assert(existingPlatform.SchemaStatusLabel == "Готовы к работе", "ready status label is invalid.");
    Assert(existingPlatform.ExistingInModelRoot, "existing planned class marker is missing.");

    var missingPlanned = schema.Classes.Single(c => c.Code == "C2M_ServiceDatabaseService");
    Assert(missingPlanned.SchemaStatus == "recommended_to_create", "missing planned class must be recommended to create.");
    Assert(missingPlanned.SchemaStatusLabel == "Рекомендовано к созданию", "recommended status label is invalid.");

    var customerService = schema.Classes.Single(c => c.Code == "CustomerManagedService");
    Assert(customerService.Origin == "existing_managed_descendant", "customer-created service class origin is invalid.");
    Assert(customerService.Layer == BuilderLayer.Service, "customer-created service class layer is invalid.");
    Assert(customerService.SchemaStatus == "ready_to_work", "customer-created service class must be ready.");
    Assert(customerService.ModelRoot == "/ServiceRoot", "customer-created service class root must be normalized.");
    Assert(customerService.ParentClassCode == "C2M_ServiceManagedObject", "customer-created service parent is invalid.");
    Assert(customerService.ManagedByBuilder, "customer-created service must be marked as builder-managed.");
    Assert(customerService.AutoPopulationEnabled, "customer-created service must be marked for auto population.");

    var customerSuppression = schema.Classes.Single(c => c.Code == "CustomerSuppressionObject");
    Assert(customerSuppression.Layer == BuilderLayer.Suppression, "customer-created suppression class layer is invalid.");
    Assert(customerSuppression.SchemaStatus == "ready_to_work", "customer-created suppression class must be ready.");

    Assert(!schema.Classes.Any(c => c.Code == "CustomerUnmanagedService"),
        "customer-created class without the builder-management checkbox must not be shown.");

    AssertSourceLinkDomain(
        schema,
        "C2M_CustomerManagedServicePopulatedFromCustomerBusinessService",
        "CustomerManagedService",
        "CustomerBusinessService");
}

static void AssertAttributes(
    CmdbuildSchemaDefinition schema,
    string classCode,
    IReadOnlyList<string> present,
    IReadOnlyList<string> absent)
{
    var classDefinition = schema.Classes.Single(c => c.Code == classCode);
    var attributes = classDefinition.Attributes
        .Select(attribute => attribute.Code)
        .ToHashSet(StringComparer.Ordinal);

    foreach (var code in present)
    {
        Assert(attributes.Contains(code), $"{classCode}: expected attribute '{code}' is missing.");
    }

    foreach (var code in absent)
    {
        Assert(!attributes.Contains(code), $"{classCode}: source CMDB attribute '{code}' must not be generated.");
    }
}

static void AssertDomainAttributes(
    CmdbuildSchemaDefinition schema,
    string domainCode,
    IReadOnlyList<string> present,
    IReadOnlyList<string> absent)
{
    var domainDefinition = schema.Domains.Single(domain => domain.Code == domainCode);
    var attributes = domainDefinition.Attributes
        .Select(attribute => attribute.Code)
        .ToHashSet(StringComparer.Ordinal);

    foreach (var code in present)
    {
        Assert(attributes.Contains(code), $"{domainCode}: expected domain attribute '{code}' is missing.");
    }

    foreach (var code in absent)
    {
        Assert(!attributes.Contains(code), $"{domainCode}: domain attribute '{code}' must not be generated.");
    }
}

static void AssertServiceAggregationLookup(CmdbuildSchemaDefinition schema)
{
    var lookup = schema.Lookups.Single(lookup => lookup.Code == "ServiceAggregationType");
    Assert(lookup.Values.Select(value => value.Code).SequenceEqual(["all", "any", "threshold", "n_of_m"]),
        "service aggregation lookup values are invalid.");

    var serviceSuperclass = schema.Classes.Single(c => c.Code == "C2M_ServiceManagedObject");
    var aggregationType = serviceSuperclass.Attributes.Single(attribute => attribute.Code == "aggregation_type");
    Assert(aggregationType.Type == "lookup", "aggregation_type must be a lookup attribute.");
    Assert(aggregationType.LookupTypeCode == "ServiceAggregationType", "aggregation_type must reference ServiceAggregationType lookup.");
    Assert(!aggregationType.Required, "aggregation_type must not block leaf service cards.");
    Assert(aggregationType.Help.Contains("all", StringComparison.Ordinal)
        && aggregationType.Help.Contains("any", StringComparison.Ordinal)
        && aggregationType.Help.Contains("threshold", StringComparison.Ordinal)
        && aggregationType.Help.Contains("n_of_m", StringComparison.Ordinal)
        && aggregationType.Help.Contains("поле threshold", StringComparison.Ordinal)
        && aggregationType.Help.Contains("поле n", StringComparison.Ordinal),
        "aggregation_type help must explain modes and parameters.");

    var lookupValueHelp = lookup.Values.ToDictionary(value => value.Code, value => value.Help, StringComparer.Ordinal);
    Assert(lookupValueHelp["all"].Contains("threshold и n не используются", StringComparison.Ordinal),
        "all aggregation help must explain ignored parameters.");
    Assert(lookupValueHelp["any"].Contains("threshold и n не используются", StringComparison.Ordinal),
        "any aggregation help must explain ignored parameters.");
    Assert(lookupValueHelp["threshold"].Contains("Заполните threshold", StringComparison.Ordinal)
        && lookupValueHelp["threshold"].Contains("0 до 100", StringComparison.Ordinal)
        && lookupValueHelp["threshold"].Contains("n не используется", StringComparison.Ordinal),
        "threshold aggregation help must explain threshold parameter.");
    Assert(lookupValueHelp["n_of_m"].Contains("Заполните n", StringComparison.Ordinal)
        && lookupValueHelp["n_of_m"].Contains("threshold не используется", StringComparison.Ordinal),
        "n-of-m aggregation help must explain n parameter.");

    var threshold = serviceSuperclass.Attributes.Single(attribute => attribute.Code == "threshold");
    Assert(threshold.Help.Contains("Процентный порог", StringComparison.Ordinal)
        && threshold.Help.Contains("0 до 100", StringComparison.Ordinal)
        && threshold.Help.Contains("80%", StringComparison.Ordinal)
        && threshold.Help.Contains("региональные настройки", StringComparison.Ordinal)
        && threshold.Help.Contains("80.5", StringComparison.Ordinal)
        && threshold.Help.Contains("80,5", StringComparison.Ordinal)
        && threshold.Help.Contains("Zabbix", StringComparison.Ordinal),
        "threshold help must explain the percentage scale.");
}

static void AssertServiceTypeLookup(CmdbuildSchemaDefinition schema)
{
    var lookup = schema.Lookups.Single(lookup => lookup.Code == "ServiceType");
    Assert(lookup.Values.Select(value => value.Code).SequenceEqual(["business", "application", "platform", "integration", "infrastructure"]),
        "service type lookup values are invalid.");

    var platformService = schema.Classes.Single(c => c.Code == "C2M_ServicePlatformService");
    var serviceType = platformService.Attributes.Single(attribute => attribute.Code == "service_type");
    Assert(serviceType.Type == "lookup", "service_type must be a lookup attribute.");
    Assert(serviceType.LookupTypeCode == "ServiceType", "service_type must reference ServiceType lookup.");
    Assert(serviceType.Help.Contains("business", StringComparison.Ordinal)
        && serviceType.Help.Contains("application", StringComparison.Ordinal)
        && serviceType.Help.Contains("platform", StringComparison.Ordinal)
        && serviceType.Help.Contains("integration", StringComparison.Ordinal)
        && serviceType.Help.Contains("infrastructure", StringComparison.Ordinal)
        && serviceType.Help.Contains("aggregation_type", StringComparison.Ordinal)
        && serviceType.Help.Contains("threshold", StringComparison.Ordinal)
        && serviceType.Help.Contains("n", StringComparison.Ordinal),
        "service_type help must explain types and state calculation fields.");

    var valueHelp = lookup.Values.ToDictionary(value => value.Code, value => value.Help, StringComparer.Ordinal);
    Assert(valueHelp["business"].Contains("SLA", StringComparison.Ordinal), "business service type help is incomplete.");
    Assert(valueHelp["application"].Contains("приложения", StringComparison.Ordinal), "application service type help is incomplete.");
    Assert(valueHelp["platform"].Contains("middleware", StringComparison.Ordinal), "platform service type help is incomplete.");
    Assert(valueHelp["integration"].Contains("API", StringComparison.Ordinal), "integration service type help is incomplete.");
    Assert(valueHelp["infrastructure"].Contains("инфраструктурную зависимость", StringComparison.Ordinal), "infrastructure service type help is incomplete.");

    var slaTarget = platformService.Attributes.Single(attribute => attribute.Code == "sla_target");
    Assert(slaTarget.Type == "decimal", "sla_target must be a decimal percentage.");
    Assert(!slaTarget.Required, "sla_target must be optional.");
    Assert(slaTarget.Help.Contains("процент от 0 до 100", StringComparison.Ordinal)
        && slaTarget.Help.Contains("99.9", StringComparison.Ordinal)
        && slaTarget.Help.Contains("99,9", StringComparison.Ordinal)
        && slaTarget.Help.Contains("0.999", StringComparison.Ordinal)
        && slaTarget.Help.Contains("региональные настройки", StringComparison.Ordinal)
        && slaTarget.Help.Contains("Zabbix", StringComparison.Ordinal)
        && slaTarget.Help.Contains("фактической доступностью", StringComparison.Ordinal)
        && slaTarget.Help.Contains("не меняет расчет текущего состояния", StringComparison.Ordinal),
        "sla_target help must explain percentage values and purpose.");
}

static void AssertAttributeValidationRules(CmdbuildSchemaDefinition schema)
{
    var serviceSuperclass = schema.Classes.Single(c => c.Code == "C2M_ServiceManagedObject");
    var aggregationType = serviceSuperclass.Attributes.Single(attribute => attribute.Code == "aggregation_type");
    Assert(!string.IsNullOrWhiteSpace(aggregationType.ValidationRules),
        "aggregation_type must carry the CMDBuild attribute validationRules script.");
    Assert(aggregationType.ValidationRules.Contains("aggregation_type", StringComparison.Ordinal)
        && aggregationType.ValidationRules.Contains("threshold", StringComparison.Ordinal)
        && aggregationType.ValidationRules.Contains("thresholdValue >= 0", StringComparison.Ordinal)
        && aggregationType.ValidationRules.Contains("thresholdValue <= 100", StringComparison.Ordinal)
        && aggregationType.ValidationRules.Contains("n_of_m", StringComparison.Ordinal)
        && aggregationType.ValidationRules.Contains("nValue >= 1", StringComparison.Ordinal),
        "aggregation_type validationRules script is incomplete.");

    var platformService = schema.Classes.Single(c => c.Code == "C2M_ServicePlatformService");
    var serviceType = platformService.Attributes.Single(attribute => attribute.Code == "service_type");
    Assert(!string.IsNullOrWhiteSpace(serviceType.ValidationRules),
        "service_type must carry the CMDBuild attribute validationRules script.");
    Assert(serviceType.ValidationRules.Contains("sla_target", StringComparison.Ordinal)
        && serviceType.ValidationRules.Contains("parsed >= 0", StringComparison.Ordinal)
        && serviceType.ValidationRules.Contains("parsed <= 100", StringComparison.Ordinal)
        && serviceType.ValidationRules.Contains("99.9", StringComparison.Ordinal)
        && serviceType.ValidationRules.Contains("0.999", StringComparison.Ordinal),
        "service_type validationRules script is incomplete.");
}

static void AssertOperationalFlagHelp(CmdbuildSchemaDefinition schema)
{
    var serviceSuperclass = schema.Classes.Single(c => c.Code == "C2M_ServiceManagedObject");
    var objectIsActiveHelp = serviceSuperclass.Attributes.Single(attribute => attribute.Code == "is_active").Help;
    Assert(objectIsActiveHelp.Contains("исключается из желаемой модели Zabbix", StringComparison.Ordinal)
        && objectIsActiveHelp.Contains("all/any/threshold/n-of-m", StringComparison.Ordinal),
        "object is_active help must explain generation and aggregation behavior.");

    var isCriticalHelp = serviceSuperclass.Attributes.Single(attribute => attribute.Code == "is_critical").Help;
    Assert(isCriticalHelp.Contains("не делает неактивный объект активным", StringComparison.Ordinal)
        && isCriticalHelp.Contains("ранжировании первопричины/подавления", StringComparison.Ordinal),
        "is_critical help must explain impact behavior.");

    var suppressionDomain = schema.Domains.Single(domain => domain.Code == "C2M_SuppressionResourceMonitoredViaProxyGroup");
    var relationIsActiveHelp = suppressionDomain.Attributes.Single(attribute => attribute.Code == "is_active").Help;
    Assert(relationIsActiveHelp.Contains("ребро исключается", StringComparison.Ordinal)
        && relationIsActiveHelp.Contains("suppression dependency", StringComparison.Ordinal),
        "domain is_active help must explain edge reconciliation.");

    var priorityHelp = suppressionDomain.Attributes.Single(attribute => attribute.Code == "priority").Help;
    Assert(priorityHelp.Contains("Целочисленный ранг связи", StringComparison.Ordinal)
        && priorityHelp.Contains("1 - самый высокий приоритет", StringComparison.Ordinal)
        && priorityHelp.Contains("чем больше число, тем ниже приоритет", StringComparison.Ordinal)
        && priorityHelp.Contains("Пустое значение", StringComparison.Ordinal),
        "domain priority help must explain units and ordering.");

    Assert(suppressionDomain.Attributes.All(attribute => attribute.Code != "is_dynamic"),
        "domain is_dynamic flag must not be generated; managed relations are always reconciled by desired state.");
}

static void AssertSourceLinkDomain(
    CmdbuildSchemaDefinition schema,
    string domainCode,
    string sourceClassCode,
    string targetClassCode)
{
    var domain = schema.Domains.Single(d => d.Code == domainCode);
    Assert(domain.IsSourceLink, $"{domainCode}: source link marker is missing.");
    Assert(domain.RelationType == "populated_from", $"{domainCode}: invalid relation type.");
    Assert(domain.SourceClassCode == sourceClassCode, $"{domainCode}: invalid managed class.");
    Assert(domain.TargetClassCode == targetClassCode, $"{domainCode}: invalid customer class.");
    AssertDomainAttributes(
        schema,
        domainCode,
        present: ["is_active", "source", "population_rule_id"],
        absent: ["priority", "is_dynamic", "fallback_supported", "is_critical"]);
}

static string[] CommonAttributeCodes()
{
    return
    [
        "name",
        "description",
        "is_active",
        "is_critical",
        "managed_by_builder",
        "auto_population_enabled",
        "population_rule_id",
        "population_source_key",
        "last_populated_at",
        "builder_version"
    ];
}

static string[] ServiceAggregationAttributeCodes()
{
    return
    [
        "aggregation_type",
        "threshold",
        "n"
    ];
}

static string[] ServiceInheritedAttributeCodes()
{
    return CommonAttributeCodes()
        .Concat(ServiceAggregationAttributeCodes())
        .ToArray();
}

static string[] RemovedSourceAttributeCodes()
{
    return
    [
        "source_class",
        "source_card_id",
        "source_external_id"
    ];
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
