using System.Net;
using System.Reflection;
using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Cmdb2MonitoringServiceSuppression.Shared.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var factory = new CmdbuildSchemaFactory();
var topics = new KafkaTopicsOptions();
Assert(topics.EffectiveZabbixApplyPlans("service").Contains(".zabbix.service.", StringComparison.Ordinal),
    "service Zabbix apply topic must be layer-specific");
Assert(topics.EffectiveZabbixApplyPlans("suppression").Contains(".zabbix.suppression.", StringComparison.Ordinal),
    "suppression Zabbix apply topic must be layer-specific");
Assert(topics.EffectiveZabbixApplyPlans("service") != topics.EffectiveZabbixApplyPlans("suppression"),
    "service and suppression Zabbix apply topics must be different");
Assert(!string.IsNullOrWhiteSpace(topics.DeadLetterTopic), "dead-letter topic must be configured by default");

var kafkaOptions = new KafkaOptions();
Assert(kafkaOptions.DeadLetterEnabled, "Kafka dead-letter publishing must be enabled by default");
Assert(kafkaOptions.HasValidProcessingPolicy(), "Kafka processing retry defaults must be valid");

var resilience = new ResilienceOptions();
Assert(resilience.Enabled, "HTTP resilience must be enabled by default");
Assert(resilience.HasValidRetryPolicy(), "HTTP retry defaults must be valid");
Assert(resilience.HasValidCircuitBreaker(), "HTTP circuit breaker defaults must be valid");

var metricsOptions = new MetricsOptions();
Assert(metricsOptions.HasValidRoute(), "metrics route default must be valid");
Assert(metricsOptions.HasValidAccessPolicy(), "metrics access policy default must be valid");

var readinessOptions = new ReadinessOptions();
Assert(readinessOptions.HasValidRoute(), "readiness route default must be valid");
Assert(!readinessOptions.CheckExternalDependencies, "external dependency readiness checks must be opt-in by default");
Assert(readinessOptions.HasValidCheckTimeout(), "readiness dependency check timeout default must be valid");
var readinessCheckResult = ServiceReadinessCheckResult.NotReady("dependency", "failed");
Assert(readinessCheckResult.Required && !readinessCheckResult.Ready,
    "readiness dependency check results must preserve required not-ready state");

var rateLimitingOptions = new RateLimitingOptions();
Assert(rateLimitingOptions.ExcludedPathPrefixes.Contains("/ready", StringComparer.OrdinalIgnoreCase),
    "readiness route must be excluded from default rate limiting");

var hostValidationOptions = new HostValidationOptions();
Assert(hostValidationOptions.Enabled, "host validation must be enabled by default");
Assert(hostValidationOptions.HasValidAllowedHosts(), "host validation defaults must include allowed hosts");

var trustedProxyOptions = new TrustedProxyOptions();
Assert(trustedProxyOptions.Enabled, "trusted proxy validation must be enabled by default");
Assert(trustedProxyOptions.HasValidNetworks(), "trusted proxy defaults must include at least one network");

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
Assert(schema.Classes.Any(c => c.Code == "C2M_ServiceSlaCalendar"), "service SLA calendar class is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_ServiceSlaPolicy"), "service SLA policy class is missing");
Assert(schema.Classes.Any(c => c.Code == "C2M_ServiceSlaDowntime"), "service SLA downtime class is missing");
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
Assert(schema.SuggestedDomains.Count >= 4, "suggested domains are missing");
Assert(schema.SuggestedDomains.All(d => d.Suggested), "suggested domains must be marked as suggested");
Assert(schema.SuggestedDomains.All(d => !string.IsNullOrWhiteSpace(d.Reason)), "suggested domains must explain the reason");
Assert(schema.SuggestedDomains.Any(d => d.TargetClassCode == "C2M_ServiceApplicationCluster"), "custom service suggested domain is missing");
Assert(schema.SuggestedDomains.Any(d => d.TargetClassCode == "C2M_SuppressionFirewallGroup"), "custom suppression suggested domain is missing");
Assert(schema.SuggestedDomains.Any(d =>
        d.SourceClassCode == "C2M_SuppressionFirewallGroup"
        && d.TargetClassCode == "C2M_SuppressionResource"
        && d.RelationType == "depends_on"),
    "custom suppression entity must be able to point to suppressed resources.");
AssertServiceAggregationLookup(schema);
AssertServiceTypeLookup(schema);
AssertServiceSlaCalendar(schema);
AssertServiceSlaPolicy(schema);
AssertServiceSlaDowntime(schema);
AssertAttributeValidationRules(schema);

AssertAttributes(
    schema,
    "C2M_ServiceManagedObject",
    present: CommonAttributeCodes().Concat(ServiceAggregationAttributeCodes()).ToArray(),
    absent: RemovedSourceAttributeCodes().Concat(["fallback_supported", "priority", "is_dynamic"]).ToArray());
AssertAttributes(
    schema,
    "C2M_SuppressionManagedObject",
    present: CommonAttributeCodes().Concat(ServiceAggregationAttributeCodes()).ToArray(),
    absent: RemovedSourceAttributeCodes().Concat(["fallback_supported", "priority", "is_dynamic"]).ToArray());
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
    "C2M_ServiceSlaCalendar",
    present: ["calendar_code", "calendar_type", "monday_hours", "tuesday_hours", "wednesday_hours", "thursday_hours", "friday_hours", "saturday_hours", "sunday_hours", "timezone", "zabbix_calendar_name", "external_calendar_id"],
    absent: ServiceInheritedAttributeCodes().Concat(["service_type", "sla_target", "reporting_period", "calendar", "zabbix_sla_name"]).ToArray());
AssertAttributes(
    schema,
    "C2M_ServiceSlaPolicy",
    present: ["sla_target", "reporting_period", "calendar", "timezone", "zabbix_sla_name"],
    absent: ServiceInheritedAttributeCodes().Concat(["service_type"]).ToArray());
AssertAttributes(
    schema,
    "C2M_ServiceSlaDowntime",
    present: ["downtime_type", "schedule_type", "start_time", "duration_minutes", "day_of_week", "day_of_month", "valid_from", "valid_to", "reason", "timezone", "zabbix_downtime_name"],
    absent: ServiceInheritedAttributeCodes().Concat(["service_type", "sla_target", "reporting_period", "calendar", "zabbix_sla_name"]).ToArray());
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
foreach (var (domainCode, sourceClassCode) in new[]
{
    ("C2M_SuppressionResourceSuppressesResource", "C2M_SuppressionResource"),
    ("C2M_SuppressionNetworkZoneSuppressesResource", "C2M_SuppressionNetworkAccessZone"),
    ("C2M_SuppressionComputeSuppressesResource", "C2M_SuppressionComputeCluster"),
    ("C2M_SuppressionStoragePoolSuppressesResource", "C2M_SuppressionStoragePool"),
    ("C2M_SuppressionProxyGroupSuppressesResource", "C2M_SuppressionProxyGroup")
})
{
    AssertDomainAttributes(
        schema,
        domainCode,
        present: ["is_active", "priority", "source"],
        absent: ["fallback_supported", "is_critical", "is_dynamic"]);
    Assert(schema.Domains.Any(domain =>
            domain.Code == domainCode
            && domain.SourceClassCode == sourceClassCode
            && domain.TargetClassCode == "C2M_SuppressionResource"
            && domain.RelationType == "depends_on"),
        $"{sourceClassCode} must be able to point to suppressed resources.");
}
var serviceConcreteClasses = ServiceTopologyClassCodes(schema);
foreach (var sourceClassCode in serviceConcreteClasses)
{
    foreach (var targetClassCode in serviceConcreteClasses)
    {
        Assert(schema.Domains.Any(domain =>
                domain.SourceClassCode == sourceClassCode
                && domain.TargetClassCode == targetClassCode
                && domain.RelationType == "service_depends_on"),
            $"{sourceClassCode} must be able to depend on {targetClassCode} through relationType=service_depends_on.");
    }

    Assert(schema.Domains.Any(domain =>
            domain.SourceClassCode == sourceClassCode
            && domain.TargetClassCode == "C2M_ServiceSlaPolicy"
            && domain.RelationType == "has_sla_policy"),
        $"{sourceClassCode} must be able to reference C2M_ServiceSlaPolicy through relationType=has_sla_policy.");

    if (sourceClassCode != "C2M_ServicePlatformService")
    {
        Assert(schema.Domains.Any(domain =>
                domain.SourceClassCode == sourceClassCode
                && domain.TargetClassCode == "C2M_ServicePlatformService"
                && domain.RelationType == "aggregates_to"),
            $"{sourceClassCode} must be containable by C2M_ServicePlatformService through relationType=aggregates_to.");
    }
}
Assert(!schema.Domains.Any(domain =>
        domain.SourceClassCode == "C2M_ServiceSlaPolicy"
        && domain.RelationType == "service_depends_on"),
    "C2M_ServiceSlaPolicy must not participate in service dependency topology as a source.");
Assert(!schema.Domains.Any(domain =>
        domain.SourceClassCode == "C2M_ServiceSlaDowntime"
        && domain.RelationType == "service_depends_on"),
    "C2M_ServiceSlaDowntime must not participate in service dependency topology as a source.");
Assert(!schema.Domains.Any(domain =>
        domain.SourceClassCode == "C2M_ServiceSlaCalendar"
        && domain.RelationType == "service_depends_on"),
    "C2M_ServiceSlaCalendar must not participate in service dependency topology as a source.");
var suppressionConcreteClasses = schema.Classes
    .Where(classDefinition => classDefinition.Layer == BuilderLayer.Suppression && !classDefinition.IsSuperclass)
    .Select(classDefinition => classDefinition.Code)
    .OrderBy(code => code, StringComparer.Ordinal)
    .ToArray();
foreach (var sourceClassCode in suppressionConcreteClasses)
{
    foreach (var targetClassCode in suppressionConcreteClasses)
    {
        var relationType = targetClassCode.Contains("NetworkAccessZone", StringComparison.OrdinalIgnoreCase)
            ? "depends_on_network"
            : "depends_on";
        Assert(schema.Domains.Any(domain =>
                domain.SourceClassCode == sourceClassCode
                && domain.TargetClassCode == targetClassCode
                && domain.RelationType == relationType),
            $"{sourceClassCode} must be able to suppress {targetClassCode} through relationType={relationType}.");
    }
}
Assert(schema.Domains.Select(domain => domain.Code).Distinct(StringComparer.Ordinal).Count() == schema.Domains.Count,
    "domain codes must be unique.");
foreach (var domain in schema.Domains.Concat(schema.SuggestedDomains))
{
    Assert(domain.Code.Length <= 58,
        $"{domain.Code}: CMDBuild domain code must fit PostgreSQL _Map_<domain> identifier length.");
}
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
AssertConversionRulesValidatorRejectsDuplicateRuleIds();
AssertZabbixHostIdReadinessContract();
AssertApplyModeContract();
AssertRouterCoreAggregationContract();
AssertRuleEngineEmitsSourceMembershipTombstones();
AssertZabbixMembershipMovesSourceBetweenTargets();
AssertZabbixMembershipRemovesSourceFromLayer();
AssertZabbixMembershipPrunesMissingHostBindings();
AssertZabbixMembershipPublishesCriticalityTag();
AssertZabbixAppliedGraphDiffContract();
AssertSqliteZabbixApplyStateStorageContract();
AssertSqliteDirtyScopeStoreContract();
AssertSqliteMonitoringCoverageSnapshotStoreContract();
AssertZabbixGraphScopeContract();
await AssertRuntimeCoordinationLocalLockContractAsync();
await AssertRuntimeLookupCacheContractAsync();
AssertZabbixManagedServiceMappingContract();
AssertSemanticFingerprintIncludesDimensionField();
AssertSemanticFingerprintChangesWhenHostIdAppears();
await AssertZabbixManagedServiceClientCreatesTaggedServiceAsync();
await AssertZabbixManagedServiceClientCreatesServiceWithParentsAsync();
await AssertZabbixManagedServiceClientClearsChildrenAsync();
await AssertZabbixManagedServiceClientKeepsFoundChildrenWhenSomeRelationsAreMissingAsync();
await AssertZabbixManagedServiceClientTagsExistingServiceWithoutTopologyMutationAsync();
await AssertZabbixSourceLeafServiceCreatesProblemTagsAsync();
await AssertZabbixClientAppliesSlaAndPreservesManualDowntimeAsync();
await AssertZabbixClientEnsuresHostTagsAsync();
await AssertZabbixClientAppliesTriggerDependenciesAsync();
await AssertZabbixClientAppliesSuppressionAggregateAsync();
await AssertZabbixSuppressionAggregateThresholdUsesSelectedHostsAsync();
await AssertCmdbuildApplyCreatesSourceLinkAsync();

Console.WriteLine("Shared contract checks passed.");

static void AssertApplyModeContract()
{
    Assert(!new ApplyOptions { Mode = "manual", AutoApplyEnabled = false }.EffectiveAutoApplyEnabled(),
        "manual apply mode must not start Kafka auto-apply.");
    Assert(new ApplyOptions { Mode = "auto", AutoApplyEnabled = false }.EffectiveAutoApplyEnabled(),
        "auto apply mode must enable Kafka auto-apply.");
    Assert(new ApplyOptions { Mode = "manual", AutoApplyEnabled = true }.EffectiveAutoApplyEnabled(),
        "AutoApplyEnabled flag must enable Kafka auto-apply.");
    Assert(!new ApplyOptions().CreateSuppressionServices,
        "suppression must not create Zabbix Services by default.");
}

static void AssertConversionRulesValidatorRejectsDuplicateRuleIds()
{
    var validation = new ConversionRulesValidator().Validate(new ConversionRulesDocument
    {
        Version = "test",
        Rules =
        [
            MinimalRule("rule"),
            MinimalRule("rule")
        ]
    });

    Assert(!validation.IsValid, "conversion rule validator must reject duplicated rule_id values.");
    Assert(validation.Errors.Any(error => error.Contains("rule_id 'rule' is duplicated", StringComparison.Ordinal)),
        "conversion rule validator must report the duplicated rule_id explicitly.");
}

static void AssertZabbixHostIdReadinessContract()
{
    var preferred = BuildHostReadinessCommand(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "host-001",
        ["zabbix_main_hostid"] = "main-30011",
        ["zabbix_hostid"] = "legacy-30011"
    });
    Assert(preferred.Source.ZabbixHostId == "main-30011",
        "AggregationRuleEngine must prefer zabbix_main_hostid over legacy zabbix_hostid.");

    var legacyFallback = BuildHostReadinessCommand(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "host-001",
        ["zabbix_hostid"] = "legacy-30011"
    });
    Assert(legacyFallback.Source.ZabbixHostId == "legacy-30011",
        "AggregationRuleEngine must keep zabbix_hostid as a compatibility fallback.");

    var configured = BuildHostReadinessCommand(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Code"] = "host-001",
            ["zabbix_main_hostid"] = "main-30011",
            ["customer_hostid"] = "custom-30011"
        },
        new AggregationRuleEngine("customer_hostid"));
    Assert(configured.Source.ZabbixHostId == "custom-30011",
        "AggregationRuleEngine must honor configured Readiness:ZabbixHostIdAttribute before defaults.");
}

static void AssertRouterCoreAggregationContract()
{
    var commands = BuildRouterCorePlans("City04").Select(plan => plan.Command).ToArray();
    Assert(commands.Length == 2, "routerCore event must produce the static core-router command and the city population command.");

    var coreCommand = commands.Single(command => command.RuleId == "core-router");
    Assert(coreCommand.CommandType == AggregationCommandTypes.EnsureMembership, "core-router command type is invalid.");
    Assert(coreCommand.Source.ClassCode == "routerCore", "core-router source class is invalid.");
    Assert(coreCommand.Source.CardId == "447411", "core-router source card id is invalid.");
    Assert(coreCommand.Source.KeyAttribute == "Code", "core-router source key attribute is invalid.");
    Assert(coreCommand.Source.KeyValue == "ctest2-routerCore-002", "core-router source key value is invalid.");
    Assert(coreCommand.Source.ZabbixHostId == "30011", "core-router source zabbix_main_hostid is invalid.");
    Assert(coreCommand.Target.ClassCode == "C2M_ServiceNetworkAccessZone", "core-router target class is invalid.");
    Assert(coreCommand.Target.CardId == "576100", "core-router target card id is invalid.");
    Assert(!coreCommand.Target.CreateInstance, "core-router command must attach to the existing target card.");
    Assert(coreCommand.Target.IdempotencyKey == "cmdbuild:C2M_ServiceNetworkAccessZone:576100",
        "core-router idempotency key is invalid.");
    Assert(coreCommand.Target.Attributes["Code"]?.ToString() == "CoreRouter", "core-router target Code is invalid.");
    Assert(coreCommand.Target.Attributes["name"]?.ToString() == "Маршрутизаторы ядра", "core-router target name is invalid.");

    var cityCommand = commands.Single(command => command.RuleId == "network-access-zone-by-city-routercore-city04");
    Assert(cityCommand.Source.KeyAttribute == "locationFloorBuildingCity", "city command source key attribute is invalid.");
    Assert(cityCommand.Source.KeyValue == "City04", "city command source key value is invalid.");
    Assert(cityCommand.Target.CreateInstance, "city command must create or resolve a managed city target.");
    Assert(cityCommand.Target.IdempotencyKey == "network-access-zone-by-city:City04", "city command idempotency key is invalid.");
    Assert(cityCommand.Target.Attributes["name"]?.ToString() == "City04", "city command target name is invalid.");
    Assert(cityCommand.Target.Relations.Count == 1, "city command must contain a managed relation to core routers.");
    Assert(cityCommand.Target.Relations[0].DomainCode == "C2M_ServiceNetworkZoneDependsOnNetworkZone",
        "city command managed relation domain is invalid.");
    Assert(cityCommand.Target.Relations[0].TargetLookup == "576100", "city command must link to the core-router target.");
}

static void AssertRuleEngineEmitsSourceMembershipTombstones()
{
    var document = new ConversionRulesDocument
    {
        Version = "test",
        Rules =
        [
            new ConversionRule
            {
                RuleId = "service-city01",
                Name = "Service City01",
                Layer = "service",
                Source = new SourceSelector
                {
                    ClassCode = "Host",
                    KeyAttribute = "city",
                    Conditions =
                    [
                        new SourceCondition { Attribute = "city", Operator = "equals", Value = "City01" }
                    ]
                },
                Target = new TargetObject
                {
                    ClassCode = "C2M_ServiceResource",
                    IdempotencyKey = "service:${source.city}"
                }
            },
            new ConversionRule
            {
                RuleId = "supp-router",
                Name = "Supp router",
                Layer = "suppression",
                Source = new SourceSelector
                {
                    ClassCode = "Host",
                    Conditions =
                    [
                        new SourceCondition { Attribute = "role", Operator = "equals", Value = "router" }
                    ]
                },
                Target = new TargetObject
                {
                    ClassCode = "C2M_SuppressionResource",
                    IdempotencyKey = "supp:${source.role}"
                }
            }
        ]
    };
    var rawEvent = new CmdbRawEvent
    {
        EventId = "tombstone-test",
        Source = "test",
        EventType = "UPDATE",
        ClassCode = "Host",
        CardId = "1001",
        Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["city"] = "City01",
            ["role"] = "workstation"
        }
    };

    var commands = new AggregationRuleEngine().BuildCommands(rawEvent, document);
    Assert(commands.Count(command => command.CommandType == AggregationCommandTypes.EnsureMembership) == 1,
        "matching layer must keep its ensure_membership command.");
    var tombstone = commands.Single(command => command.CommandType == AggregationCommandTypes.RemoveSourceMembership);
    Assert(tombstone.Layer == "suppression", "non-matching candidate layer must receive source membership tombstone.");
    Assert(tombstone.Source.ClassCode == "Host" && tombstone.Source.CardId == "1001",
        "source membership tombstone must keep stable source identity.");
    Assert(string.IsNullOrWhiteSpace(tombstone.Target.ClassCode),
        "source membership tombstone must not target a stale generated object.");
}

static void AssertZabbixMembershipMovesSourceBetweenTargets()
{
    var state = NewZabbixApplyStateStore();
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City01", "City01", "30001"), "suppression", includeSourceLeafManagedKey: false);
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City02", "City02", "30001"), "suppression", includeSourceLeafManagedKey: false);

    var memberships = state.ListMemberships("suppression");
    var city01 = memberships.Single(item => item.TargetManagedKey == "supp:City01");
    var city02 = memberships.Single(item => item.TargetManagedKey == "supp:City02");
    Assert(city01.SourceCount == 0 && city01.PendingSourceCount == 0,
        "moving a source to a new dimension must remove it from the previous target membership.");
    Assert(city02.SourceCount == 1 && city02.Sources.Single().SourceKeyValue == "City02",
        "moving a source to a new dimension must keep it only in the new target membership.");

    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City03", "City03", ""), "suppression", includeSourceLeafManagedKey: false);
    memberships = state.ListMemberships("suppression");
    city02 = memberships.Single(item => item.TargetManagedKey == "supp:City02");
    var city03 = memberships.Single(item => item.TargetManagedKey == "supp:City03");
    Assert(city02.SourceCount == 0 && city02.PendingSourceCount == 0,
        "moving an unready source must remove the old active membership.");
    Assert(city03.SourceCount == 0 && city03.PendingSourceCount == 1,
        "unready moved source must be pending only in the current target.");
}

static void AssertZabbixMembershipRemovesSourceFromLayer()
{
    var state = NewZabbixApplyStateStore();
    state.UpdateMembership(BuildMembershipCommand("service", "svc:City01", "City01", "30001"), "service");
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City01", "City01", "30001"), "suppression", includeSourceLeafManagedKey: false);

    var removal = state.UpdateMembership(BuildSourceMembershipRemovalCommand("suppression"), "suppression", includeSourceLeafManagedKey: false);
    Assert(removal.RemovedSourceMemberships == 1, "source tombstone must report removed suppression membership.");
    Assert(state.ListMemberships("suppression").Single().SourceCount == 0,
        "source tombstone must remove the source from every target in its layer.");
    Assert(state.ListMemberships("service").Single().SourceCount == 1,
        "source tombstone must not remove the same source from another layer.");
}

static void AssertZabbixMembershipPrunesMissingHostBindings()
{
    var state = NewZabbixApplyStateStore();
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City01", "City01", "30001", "1001"), "suppression", includeSourceLeafManagedKey: false);
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City02", "City02", "30002", "1002"), "suppression", includeSourceLeafManagedKey: false);
    state.UpdateMembership(BuildMembershipCommand("service", "svc:City02", "City02", "30002", "1002"), "service");

    var cleanup = state.RemoveSourceHostBindings(
        "suppression",
        new HashSet<string>(["30002", "39999"], StringComparer.Ordinal));

    Assert(cleanup.RequestedHostIds == 2, "stale source-host cleanup must report requested hostids.");
    Assert(cleanup.RemovedSourceMemberships == 1, "stale source-host cleanup must remove matching source memberships.");
    Assert(cleanup.RemovedHostIds.SequenceEqual(["30002"]), "stale source-host cleanup must report removed hostids.");
    var suppression = state.ListMemberships("suppression");
    Assert(suppression.Single(item => item.TargetManagedKey == "supp:City01").SourceCount == 1,
        "stale source-host cleanup must keep suppression memberships with existing hostids.");
    Assert(suppression.Single(item => item.TargetManagedKey == "supp:City02").SourceCount == 0,
        "stale source-host cleanup must remove only sources bound to missing Zabbix hostids.");
    Assert(state.ListMemberships("service").Single().SourceCount == 1,
        "stale source-host cleanup must be layer-scoped.");
}

static void AssertZabbixMembershipPublishesCriticalityTag()
{
    var state = NewZabbixApplyStateStore();
    var command = BuildMembershipCommand(
        "service",
        "svc:Critical",
        "Critical",
        "30001",
        isCritical: "true");

    var update = state.UpdateMembership(command, "service");
    Assert(update.Current.IsCritical == "true",
        "Zabbix persisted membership must keep target is_critical metadata.");

    var method = typeof(ZabbixAggregationApplier).GetMethod(
        "FromMembershipSnapshot",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ZabbixAggregationApplier.FromMembershipSnapshot was not found.");
    var definition = (ZabbixManagedServiceDefinition?)method.Invoke(null, [update.Current, "service"])
        ?? throw new InvalidOperationException("ZabbixAggregationApplier.FromMembershipSnapshot returned null.");

    Assert(definition.Tags.TryGetValue(ZabbixManagedServiceTags.IsCritical, out var isCritical)
        && isCritical == "true",
        "Zabbix service graph publication must include the cmdb2monitoring:is_critical tag.");
}

static void AssertZabbixAppliedGraphDiffContract()
{
    var state = NewZabbixApplyStateStore();
    var command = BuildMembershipCommand("service", "svc:City01", "City01", "30001");
    var desired = ZabbixDesiredGraphBuilder.Build([command], "service", createManagedServices: true);
    var firstDiff = state.DiffAppliedGraph("service", desired.Objects, "changes", sampleLimit: 20);
    Assert(firstDiff.Added == desired.Objects.Count,
        "empty applied graph must mark every desired object as added.");
    Assert(firstDiff.PublishCandidates == desired.Objects.Count,
        "changes mode must publish every added desired object.");

    state.ReplaceAppliedGraph("service", desired.Objects);
    var unchangedDiff = state.DiffAppliedGraph("service", desired.Objects, "changes", sampleLimit: 20);
    Assert(unchangedDiff.Unchanged == desired.Objects.Count && unchangedDiff.PublishCandidates == 0,
        "changes mode must skip unchanged desired graph objects.");

    var fullDiff = state.DiffAppliedGraph("service", desired.Objects, "full", sampleLimit: 20);
    Assert(fullDiff.Unchanged == desired.Objects.Count && fullDiff.PublishCandidates == desired.Objects.Count,
        "full mode must publish unchanged desired graph objects.");

    var changedCommand = BuildMembershipCommand(
        "service",
        "svc:City01",
        "City01",
        "30001",
        isCritical: "true");
    var changedDesired = ZabbixDesiredGraphBuilder.Build([changedCommand], "service", createManagedServices: true);
    var changedDiff = state.DiffAppliedGraph("service", changedDesired.Objects, "changes", sampleLimit: 20);
    Assert(changedDiff.Changed > 0 && changedDiff.PublishCandidates > 0,
        "target attribute changes must be visible in desired graph diff.");

    var removedDiff = state.DiffAppliedGraph("service", [], "changes", sampleLimit: 20);
    Assert(removedDiff.Removed == desired.Objects.Count && removedDiff.PublishCandidates == 0,
        "missing desired graph objects must be reported as removed without automatic publish candidates.");
}

static void AssertSqliteZabbixApplyStateStorageContract()
{
    var directory = Path.Combine(Path.GetTempPath(), "cmdb2monitoring-tests");
    Directory.CreateDirectory(directory);
    var dbPath = Path.Combine(directory, $"zabbix-apply-state-{Guid.NewGuid():N}.db");
    var storage = NewSqliteZabbixApplyStateStorage(dbPath);
    var state = new ZabbixApplyStateStore(storage);
    var command = BuildMembershipCommand("service", "svc:City01", "City01", "30001");
    state.UpdateMembership(command, "service");
    state.ReplaceAppliedGraph("service", ZabbixDesiredGraphBuilder.Build([command], "service", createManagedServices: true).Objects);

    var reloaded = new ZabbixApplyStateStore(NewSqliteZabbixApplyStateStorage(dbPath));
    var memberships = reloaded.ListMemberships("service");
    Assert(memberships.Count == 1 && memberships.Single().SourceCount == 1,
        "SQLite membership-state backend must persist and reload target/source memberships.");

    var status = storage.Status();
    Assert(status.Backend == "sqlite" && status.SchemaVersion == 1,
        "SQLite membership-state backend must report schema version.");
    Assert(status.TargetMembershipCount == 1 && status.SourceMembershipCount == 1,
        "SQLite membership-state backend must expose normalized membership counters.");
    Assert(status.AppliedGraphObjectCount > 0,
        "SQLite membership-state backend must persist applied graph object counters.");
}

static void AssertSqliteDirtyScopeStoreContract()
{
    var directory = Path.Combine(Path.GetTempPath(), "cmdb2monitoring-tests");
    Directory.CreateDirectory(directory);
    var dbPath = Path.Combine(directory, $"zabbix-dirty-scopes-{Guid.NewGuid():N}.db");
    var store = NewSqliteDirtyScopeStore(dbPath);
    var mark = store.Mark(new DirtyScopeMarkRequest
    {
        Layer = "service",
        Reason = "contract test",
        Entries =
        [
            new DirtyScopeMarkEntry
            {
                ScopeType = "target",
                ScopeKey = "svc:City01"
            }
        ]
    });

    Assert(mark.AddedOrUpdated == 1, "SQLite dirty scope store must mark one dirty scope.");
    var reloaded = NewSqliteDirtyScopeStore(dbPath).Snapshot(100);
    var service = reloaded.Layers.Single(item => item.Layer == "service");
    Assert(service.Count == 1 && service.Entries.Single().ScopeKey == "svc:City01",
        "SQLite dirty scope store must persist and reload dirty scopes.");

    var processed = store.MarkResult("service", ["svc:City01"], "processed", "contract apply completed");
    Assert(processed.AddedOrUpdated == 1,
        "SQLite dirty scope store must update dirty scope processing result.");
    var processedEntry = NewSqliteDirtyScopeStore(dbPath)
        .Snapshot(100)
        .Layers.Single(item => item.Layer == "service")
        .Entries.Single();
    Assert(processedEntry.Status == "processed" && processedEntry.LastReconcileResult == "contract apply completed",
        "SQLite dirty scope store must persist processed status and result text.");

    var clear = store.Clear("service");
    Assert(clear.Removed == 1 && NewSqliteDirtyScopeStore(dbPath).Snapshot(100).Layers.All(item => item.Count == 0),
        "SQLite dirty scope store must clear layer-scoped dirty scopes.");
}

static void AssertSqliteMonitoringCoverageSnapshotStoreContract()
{
    var directory = Path.Combine(Path.GetTempPath(), "cmdb2monitoring-tests");
    Directory.CreateDirectory(directory);
    var dbPath = Path.Combine(directory, $"monitoring-coverage-{Guid.NewGuid():N}.db");
    var options = new MonitoringCoverageSnapshotOptions
    {
        SnapshotRetentionDays = 30,
        DefaultExpectedPolicy = "rules_matched",
        HostIdAttribute = "zabbix_main_hostid",
        AllowOperationalDelta = true,
        MaxOperationalDeltaMinutes = 30
    };
    var started = DateTimeOffset.UtcNow.AddSeconds(-2);
    var finished = DateTimeOffset.UtcNow;
    var records = MonitoringCoverageSourceRecord.FromMemberships(
        [
            NewCoverageMembership("service", "svc:ntbook", "NTbook", "1001", "30001"),
            NewCoverageMembership("service", "svc:router", "routerG", "1002", "")
        ],
        [
            NewCoverageMembership("suppression", "supp:ntbook", "NTbook", "1001", "30001")
        ]);
    var snapshot = MonitoringCoverageSnapshot.FromRecords(
        records,
        [new ZabbixHostInfo { HostId = "30001", Host = "host-30001", Name = "host-30001" }],
        options,
        started,
        started.AddSeconds(1),
        finished,
        []);

    var store = NewSqliteMonitoringCoverageSnapshotStore(dbPath);
    var save = store.Save(snapshot, options);
    Assert(save.Backend == "sqlite" && save.SnapshotId == snapshot.SnapshotId,
        "SQLite coverage snapshot store must report saved snapshot id.");

    var history = NewSqliteMonitoringCoverageSnapshotStore(dbPath).List(10);
    Assert(history.Backend == "sqlite" && history.Snapshots.Count == 1,
        "SQLite coverage snapshot store must persist and reload snapshot history.");
    var summary = history.Snapshots.Single();
    Assert(summary.ExpectedObjects == 2 && summary.WithHostId == 1 && summary.ExistingZabbixHosts == 1,
        "SQLite coverage snapshot history must expose coverage counters.");
}

static void AssertZabbixGraphScopeContract()
{
    var root = BuildMembershipCommand("service", "svc:root", "Root", "30001");
    var child = BuildMembershipCommand("service", "svc:child", "Child", "30002", sourceCardId: "1002")
        with
        {
            Target = BuildMembershipCommand("service", "svc:child", "Child", "30002", sourceCardId: "1002").Target
                with { ParentManagedKeys = ["svc:root"] }
        };
    var unrelated = BuildMembershipCommand("service", "svc:other", "Other", "30003", sourceCardId: "1003");
    var serviceScope = ZabbixGraphScopeResolver.Resolve([root, child, unrelated], "service", ["svc:root"], 0);
    Assert(serviceScope.Enabled, "scope resolver must mark explicit scope as enabled.");
    Assert(serviceScope.TargetManagedKeys.SetEquals(["svc:root", "svc:child"]),
        "service scope by root must include the selected target and its descendants, but not unrelated targets.");
    Assert(serviceScope.Commands.Count == 2,
        "service scope must filter commands to scoped target keys.");

    var suppA = BuildSuppressionRelationCommand("supp:A", "A", "supp:B", "1001");
    var suppB = BuildSuppressionRelationCommand("supp:B", "B", "supp:C", "1002");
    var suppC = BuildMembershipCommand("suppression", "supp:C", "C", "30003", sourceCardId: "1003");
    var limitedSuppressionScope = ZabbixGraphScopeResolver.Resolve([suppA, suppB, suppC], "suppression", ["supp:A"], 1);
    Assert(limitedSuppressionScope.TargetManagedKeys.SetEquals(["supp:A", "supp:B"]),
        "suppression scope depth must limit relation-chain traversal.");
    var fullSuppressionScope = ZabbixGraphScopeResolver.Resolve([suppA, suppB, suppC], "suppression", ["A"], 0);
    Assert(fullSuppressionScope.TargetManagedKeys.SetEquals(["supp:A", "supp:B", "supp:C"]),
        "suppression scope with depth 0 must include the connected chain.");
}

static async Task AssertRuntimeCoordinationLocalLockContractAsync()
{
    var store = new LocalRuntimeCoordinationStore(new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions
    {
        Enabled = false,
        LockTtlSeconds = 30
    }));
    var status = store.Status();
    Assert(status.Backend == "local-memory", "disabled Redis must use local-memory runtime coordination.");

    await using var first = await store.TryAcquireLockAsync("zabbix:graph:service:apply", CancellationToken.None);
    Assert(first.Acquired, "first local runtime lock acquisition must succeed.");
    var progress = store.StartOperation("zabbix:graph:service:apply", first.Backend);
    Assert(store.Status().ActiveOperationCount == 1,
        "runtime coordination must expose active operation count.");
    await using var second = await store.TryAcquireLockAsync("zabbix:graph:service:apply", CancellationToken.None);
    Assert(!second.Acquired && second.StatusCode == StatusCodes.Status409Conflict,
        "concurrent local runtime lock acquisition must return busy.");
    store.CompleteOperation(progress.OperationId, "completed");
    var completedStatus = store.Status();
    Assert(completedStatus.ActiveOperationCount == 0 && completedStatus.RecentOperations.Any(item => item.OperationId == progress.OperationId),
        "runtime coordination must move completed operations to recent operation history.");
    var firstDebounce = store.RequestDebouncedOperation("zabbix:dependencies:suppression:auto-reconcile", "reason-a", TimeSpan.FromSeconds(5));
    var secondDebounce = store.RequestDebouncedOperation("zabbix:dependencies:suppression:auto-reconcile", "reason-b", TimeSpan.FromSeconds(5));
    Assert(firstDebounce.ShouldSchedule && !secondDebounce.ShouldSchedule,
        "runtime debounce must schedule only the first request inside the debounce window.");
    var debounceBatch = store.ConsumeDebouncedOperation("zabbix:dependencies:suppression:auto-reconcile");
    Assert(debounceBatch.Reasons.Contains("reason-a") && debounceBatch.Reasons.Contains("reason-b"),
        "runtime debounce must preserve coalesced reconcile reasons.");

    var failStore = new LocalRuntimeCoordinationStore(new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions
    {
        Enabled = true,
        FailureMode = "fail",
        LockTtlSeconds = 30
    }));
    await using var blocked = await failStore.TryAcquireLockAsync("zabbix:graph:service:apply", CancellationToken.None);
    Assert(!blocked.Acquired && blocked.Status == "runtime_coordination_unavailable",
        "Redis FailureMode=fail must block operations until a real Redis backend is active.");

    var redisDisabledStore = new RedisRuntimeCoordinationStore(
        new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions
        {
            Enabled = false,
            LockTtlSeconds = 30
        }),
        new LocalRuntimeCoordinationStore(new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions
        {
            Enabled = false,
            LockTtlSeconds = 30
        })),
        NullLogger<RedisRuntimeCoordinationStore>.Instance);
    Assert(redisDisabledStore.Status().Backend == "local-memory",
        "Redis coordination wrapper must delegate to local-memory when Redis is disabled.");

    var parsed = RedisEndpoint.Parse("redis://user:secret@redis.local:6380/2");
    Assert(parsed.Host == "redis.local" && parsed.Port == 6380 && parsed.UserName == "user" && parsed.Password == "secret" && parsed.Database == 2,
        "Redis endpoint parser must support redis://user:password@host:port/db.");
    var configured = RedisEndpoint.Parse("127.0.0.1:6379,password=secret,defaultDatabase=3");
    Assert(configured.Host == "127.0.0.1" && configured.Port == 6379 && configured.Password == "secret" && configured.Database == 3,
        "Redis endpoint parser must support host:port,password=...,defaultDatabase=... syntax.");
}

static async Task AssertRuntimeLookupCacheContractAsync()
{
    var disabledOptions = new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions
    {
        Enabled = false,
        CacheDefaultTtlSeconds = 60
    });
    var disabledCache = new LocalRuntimeLookupCache(disabledOptions);
    Assert(disabledCache.Status().Backend == "no-cache",
        "disabled Redis lookup cache must report no-cache backend.");
    await disabledCache.SetStringAsync("zabbix:host", "1001", "cached", TimeSpan.FromSeconds(30), CancellationToken.None);
    Assert(await disabledCache.GetStringAsync("zabbix:host", "1001", CancellationToken.None) is null,
        "disabled Redis lookup cache must not retain lookup values.");

    var fallbackOptions = new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions
    {
        Enabled = true,
        CacheDefaultTtlSeconds = 60
    });
    var fallbackCache = new LocalRuntimeLookupCache(fallbackOptions);
    Assert(fallbackCache.Status().Backend == "local-memory-fallback",
        "enabled local lookup fallback must report local-memory-fallback backend.");
    await fallbackCache.SetStringAsync("zabbix:host", "1001", "cached", TimeSpan.FromSeconds(30), CancellationToken.None);
    Assert(await fallbackCache.GetStringAsync("zabbix:host", "1001", CancellationToken.None) == "cached",
        "local lookup fallback must retain lookup values until TTL expires.");

    var redisDisabledCache = new RedisRuntimeLookupCache(
        disabledOptions,
        disabledCache,
        NullLogger<RedisRuntimeLookupCache>.Instance);
    Assert(redisDisabledCache.Status().Backend == "no-cache",
        "Redis lookup cache wrapper must not cache when Redis is disabled.");
}

static void AssertZabbixManagedServiceMappingContract()
{
    var commands = BuildRouterCorePlans("City04").Select(plan => plan.Command).ToArray();
    var coreCommand = commands.Single(command => command.RuleId == "core-router");
    var cityCommand = commands.Single(command => command.RuleId == "network-access-zone-by-city-routercore-city04");

    var coreService = ZabbixManagedServiceMapper.FromAggregationCommand(coreCommand, "service");
    Assert(coreService.ManagedKey == "cmdbuild:C2M_ServiceNetworkAccessZone:576100",
        "static target must use the CMDBuild card idempotency key as Zabbix managed key.");
    Assert(coreService.CardId == "576100", "static target must keep CMDBuild card id tag value.");
    Assert(coreService.Name == "Маршрутизаторы ядра", "static target service name is invalid.");
    Assert(coreService.Tags[ZabbixManagedServiceTags.Managed] == "true", "managed Zabbix service tag is missing.");
    Assert(coreService.Tags[ZabbixManagedServiceTags.Layer] == "service", "Zabbix service layer tag is invalid.");
    Assert(coreService.Tags[ZabbixManagedServiceTags.Class] == "C2M_ServiceNetworkAccessZone", "Zabbix service class tag is invalid.");
    Assert(coreService.Tags[ZabbixManagedServiceTags.CardId] == "576100", "Zabbix service card id tag is invalid.");
    Assert(coreService.Algorithm == ZabbixServiceAlgorithms.MostCriticalOfChildren,
        "aggregation_type=any must map to the Zabbix most-critical-of-children algorithm.");

    var cityService = ZabbixManagedServiceMapper.FromAggregationCommand(cityCommand, "service");
    Assert(cityService.ManagedKey == "network-access-zone-by-city:City04",
        "dynamic target must use the generated idempotency key as Zabbix managed key.");
    Assert(cityService.Name == "Уровень коммутации / City04",
        "dynamic target Zabbix service name must use the user-visible rule name.");
    Assert(cityService.Relations.Count == 1, "dynamic city target must expose one Zabbix child relation.");
    Assert(cityService.Relations[0].TargetLookup == "576100", "Zabbix relation must keep the CMDBuild target lookup.");
    Assert(ZabbixManagedServiceMapper.LookupCandidates("C2M_ServiceNetworkAccessZone", "576100")
        .Contains("cmdbuild:C2M_ServiceNetworkAccessZone:576100", StringComparer.Ordinal),
        "Zabbix relation lookup candidates must include CMDBuild card idempotency key fallback.");
}

static async Task AssertZabbixManagedServiceClientCreatesTaggedServiceAsync()
{
    var command = BuildRouterCorePlans("City04")
        .Select(plan => plan.Command)
        .Single(command => command.RuleId == "core-router");
    var handler = new DiagnosticZabbixHandler();
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplyManagedServiceAsync(
        ZabbixManagedServiceMapper.FromAggregationCommand(command, "service"),
        CancellationToken.None);
    Assert(result.Success, "Zabbix managed service apply result must be successful.");
    Assert(result.Action == "created", "Zabbix managed service action must be created.");
    Assert(result.ServiceId == "9001", "Zabbix service id must be returned from service.create.");

    var payloadText = handler.CreatePayload
        ?? throw new InvalidOperationException("Zabbix service.create payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var payload = document.RootElement;
    Assert(JsonString(payload, "method") == "service.create", "Zabbix method must be service.create.");
    var parameters = payload.GetProperty("params");
    Assert(JsonString(parameters, "name") == "Маршрутизаторы ядра", "Zabbix service.create name is invalid.");
    Assert(parameters.GetProperty("algorithm").GetInt32() == ZabbixServiceAlgorithms.MostCriticalOfChildren,
        "Zabbix service.create algorithm is invalid.");
    Assert(parameters.GetProperty("sortorder").GetInt32() == 0, "Zabbix service.create sortorder is invalid.");

    var tags = parameters.GetProperty("tags")
        .EnumerateArray()
        .ToDictionary(tag => JsonString(tag, "tag"), tag => JsonString(tag, "value"), StringComparer.Ordinal);
    Assert(tags[ZabbixManagedServiceTags.Managed] == "true", "Zabbix service.create managed tag is missing.");
    Assert(tags[ZabbixManagedServiceTags.Key] == "cmdbuild:C2M_ServiceNetworkAccessZone:576100",
        "Zabbix service.create managed key tag is invalid.");
    Assert(tags[ZabbixManagedServiceTags.CardId] == "576100", "Zabbix service.create card id tag is invalid.");
    Assert(tags[ZabbixManagedServiceTags.SourceKeyAttribute] == "Code",
        "Zabbix service.create source key attribute tag is invalid.");
    Assert(tags[ZabbixManagedServiceTags.SourceKeyValue] == "ctest2-routerCore-002",
        "Zabbix service.create source key value tag is invalid.");
    Assert(tags[ZabbixManagedServiceTags.SourceZabbixHostId] == "30011",
        "Zabbix service.create source hostid tag is invalid.");
}

static async Task AssertZabbixManagedServiceClientCreatesServiceWithParentsAsync()
{
    var handler = new DiagnosticZabbixHandler();
    handler.ManagedServiceIdsByKey["cmdbuild:C2M_ServicePlatformService:100"] = "8001";
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplyManagedServiceAsync(
        new ZabbixManagedServiceDefinition
        {
            Layer = "service",
            ManagedKey = "workplaces:City04",
            ClassCode = "C2M_ServiceWorkplaceGroup",
            Name = "Рабочие места / City04",
            ParentManagedKeys = ["cmdbuild:C2M_ServicePlatformService:100"]
        },
        CancellationToken.None);

    Assert(result.Success, "Zabbix managed service parent-link result must be successful.");
    Assert(result.RelationsApplied == 1, "Zabbix managed service parent relation must be counted as applied.");
    var payloadText = handler.CreatePayload
        ?? throw new InvalidOperationException("Zabbix service.create payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var parameters = document.RootElement.GetProperty("params");
    var parents = parameters.GetProperty("parents").EnumerateArray().ToArray();
    Assert(parents.Length == 1, "Zabbix service.create must include one parent reference.");
    Assert(JsonString(parents[0], "serviceid") == "8001",
        "Zabbix service.create must attach the service to the resolved parent service.");
}

static async Task AssertZabbixManagedServiceClientClearsChildrenAsync()
{
    var handler = new DiagnosticZabbixHandler { ExistingManagedService = true };
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplyManagedServiceAsync(
        new ZabbixManagedServiceDefinition
        {
            Layer = "suppression",
            ManagedKey = "rule:City04",
            ClassCode = "C2M_SuppressionResource",
            RuleId = "suppression-rule-arm-city04",
            RuleName = "Рабочие места / City04",
            Name = "Рабочие места / City04"
        },
        CancellationToken.None);

    Assert(result.Success, "Zabbix managed service clear-children result must be successful.");
    Assert(result.Action == "updated", "Zabbix managed service clear-children action must be updated.");
    var payloadText = handler.UpdatePayload
        ?? throw new InvalidOperationException("Zabbix service.update payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var payload = document.RootElement;
    Assert(JsonString(payload, "method") == "service.update", "Zabbix method must be service.update.");
    var parameters = payload.GetProperty("params");
    Assert(parameters.TryGetProperty("children", out var children)
        && children.ValueKind == JsonValueKind.Array
        && !children.EnumerateArray().Any(),
        "Zabbix service.update must send an empty children array when desired relations are empty.");
}

static async Task AssertZabbixManagedServiceClientKeepsFoundChildrenWhenSomeRelationsAreMissingAsync()
{
    var handler = new DiagnosticZabbixHandler();
    handler.ManagedServiceIdsByKey["workplaces:City31"] = "9001";
    handler.ManagedServiceIdsByKey["source:service:NTbook:434731"] = "9002";
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplyManagedServiceAsync(
        new ZabbixManagedServiceDefinition
        {
            Layer = "service",
            ManagedKey = "workplaces:City31",
            ClassCode = "C2M_ServiceUserEndpointFleet",
            Name = "Рабочие места / City31",
            Relations =
            [
                new ZabbixManagedServiceRelation
                {
                    DomainCode = "C2M_ServiceFleetDependsOnNetworkZone",
                    TargetClassCode = "C2M_ServiceNetworkAccessZone",
                    TargetLookup = "supp-service-copy:City31"
                }
            ],
            ChildManagedKeys = ["source:service:NTbook:434731"]
        },
        CancellationToken.None);

    Assert(result.Success, "Zabbix managed service partial topology result must be successful.");
    Assert(result.Action == "updated", "Zabbix managed service partial topology action must be updated.");
    Assert(result.RelationsApplied == 1, "Zabbix managed service must still apply found source leaf children.");
    Assert(result.RelationsDeferred == 1, "Zabbix managed service must still report the missing dependency relation.");
    var payloadText = handler.UpdatePayload
        ?? throw new InvalidOperationException("Zabbix service.update payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var parameters = document.RootElement.GetProperty("params");
    var children = parameters.GetProperty("children").EnumerateArray().ToArray();
    Assert(children.Length == 1, "Zabbix service.update must include found children despite missing dependency warnings.");
    Assert(JsonString(children[0], "serviceid") == "9002",
        "Zabbix service.update must attach the source leaf child instead of leaving it in the root.");
}

static async Task AssertZabbixManagedServiceClientTagsExistingServiceWithoutTopologyMutationAsync()
{
    var handler = new DiagnosticZabbixHandler { ExistingManagedService = true };
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplyManagedServiceTagsAsync(
        new ZabbixManagedServiceDefinition
        {
            Layer = "suppression",
            ManagedKey = "rule:City04",
            Tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZabbixManagedServiceTags.SlaPolicy] = "workplace-99",
                [ZabbixManagedServiceTags.SlaTarget] = "99.9"
            }
        },
        CancellationToken.None);

    Assert(result.Success, "Zabbix managed service tag-only update must be successful.");
    Assert(result.Action == "tagged", "Zabbix managed service tag-only action is invalid.");
    Assert(result.ServiceId == "9001", "Zabbix managed service tag-only update must return the existing service id.");
    Assert(handler.CreatePayload is null, "Zabbix tag-only service update must not create an isolated service.");

    var payloadText = handler.UpdatePayload
        ?? throw new InvalidOperationException("Zabbix tag-only service.update payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var payload = document.RootElement;
    Assert(JsonString(payload, "method") == "service.update", "Zabbix method must be service.update.");
    var parameters = payload.GetProperty("params");
    Assert(JsonString(parameters, "serviceid") == "9001", "Zabbix tag-only service.update id is invalid.");
    Assert(!parameters.TryGetProperty("children", out _), "Zabbix tag-only service.update must not send children.");
    Assert(!parameters.TryGetProperty("name", out _), "Zabbix tag-only service.update must not rewrite service name.");
    var tags = parameters.GetProperty("tags")
        .EnumerateArray()
        .ToDictionary(tag => JsonString(tag, "tag"), tag => JsonString(tag, "value"), StringComparer.Ordinal);
    Assert(tags["customer:keep"] == "manual", "Zabbix tag-only service.update must preserve existing custom tags.");
    Assert(tags[ZabbixManagedServiceTags.SlaPolicy] == "workplace-99", "Zabbix tag-only service.update must add SLA policy tag.");
    Assert(tags[ZabbixManagedServiceTags.SlaTarget] == "99.9", "Zabbix tag-only service.update must add SLA target tag.");
}

static async Task AssertZabbixSourceLeafServiceCreatesProblemTagsAsync()
{
    var command = BuildRouterCorePlans("City04")
        .Select(plan => plan.Command)
        .Single(command => command.RuleId == "core-router");
    var leaf = ZabbixManagedServiceMapper.FromSourceBinding(command, "service");
    Assert(leaf.ManagedKey == "source:service:routerCore:447411", "source leaf managed key is invalid.");
    Assert(leaf.Name == "routerCore / ctest2-routerCore-002",
        "source leaf must use a stable source object display attribute instead of the grouping key.");
    Assert(leaf.ProblemTags.Count == 1, "source leaf must expose a problem tag for host binding.");
    Assert(leaf.ProblemTags[0].Tag == ZabbixManagedServiceTags.SourceZabbixHostId,
        "source leaf problem tag must use the managed source hostid tag.");
    Assert(leaf.ProblemTags[0].Value == "30011", "source leaf problem tag value is invalid.");
    var lookupDimensionLeaf = ZabbixManagedServiceMapper.FromSourceBinding(
        command with
        {
            Source = command.Source with
            {
                ClassCode = "NTbook",
                KeyAttribute = "Critical",
                KeyValue = "177140",
                Attributes = new Dictionary<string, string>(command.Source.Attributes, StringComparer.OrdinalIgnoreCase)
                {
                    ["Code"] = "ctest2-NTbook-023"
                }
            }
        },
        "service");
    Assert(lookupDimensionLeaf.Name == "NTbook / ctest2-NTbook-023",
        "source leaf must not expose lookup/reference ids such as Critical=177140 as a Zabbix service name.");

    var handler = new DiagnosticZabbixHandler();
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplyManagedServiceAsync(leaf, CancellationToken.None);
    Assert(result.Success, "Zabbix source leaf service apply result must be successful.");
    Assert(result.ProblemTagsApplied == 1, "Zabbix source leaf service must report applied problem tags.");

    var payloadText = handler.CreatePayload
        ?? throw new InvalidOperationException("Zabbix source leaf service.create payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var parameters = document.RootElement.GetProperty("params");
    var problemTags = parameters.GetProperty("problem_tags")
        .EnumerateArray()
        .ToArray();
    Assert(problemTags.Length == 1, "Zabbix service.create must include one problem tag for source leaf.");
    Assert(JsonString(problemTags[0], "tag") == ZabbixManagedServiceTags.SourceZabbixHostId,
        "Zabbix source leaf problem tag key is invalid.");
    Assert(JsonString(problemTags[0], "value") == "30011",
        "Zabbix source leaf problem tag value is invalid.");
}

static async Task AssertZabbixClientAppliesSlaAndPreservesManualDowntimeAsync()
{
    var handler = new DiagnosticZabbixHandler { ExistingSla = true };
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplySlaAsync(
        new ZabbixSlaDefinition
        {
            PolicyKey = "workplace-99",
            Name = "CMDB2M SLA workplace",
            Slo = 99.9m,
            Period = ZabbixSlaPeriods.Monthly,
            Timezone = "Europe/Moscow",
            EffectiveDate = 1777584000,
            ManagedExcludedDowntimePrefix = "CMDB2M REG:",
            ServiceTags =
            [
                new ZabbixSlaServiceTag(ZabbixManagedServiceTags.SlaPolicy, "workplace-99")
            ],
            Schedule =
            [
                new ZabbixSlaSchedulePeriod(0, 604800)
            ],
            ExcludedDowntimes =
            [
                new ZabbixSlaExcludedDowntime("CMDB2M REG:weekly Sunday [workplace-99]", 1777800000, 1777807200)
            ]
        },
        CancellationToken.None);

    Assert(result.Action == "updated", "Existing Zabbix SLA must be updated.");
    Assert(result.ManagedExcludedDowntimes == 1, "Managed excluded downtime counter is invalid.");
    Assert(result.PreservedManualExcludedDowntimes == 1, "Manual excluded downtime must be preserved.");

    var payloadText = handler.SlaUpdatePayload
        ?? throw new InvalidOperationException("Zabbix sla.update payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var payload = document.RootElement;
    Assert(JsonString(payload, "method") == "sla.update", "Zabbix method must be sla.update.");
    var parameters = payload.GetProperty("params");
    Assert(JsonString(parameters, "slaid") == "71001", "Zabbix sla.update id is invalid.");
    Assert(JsonString(parameters, "status") == "0", "Zabbix SLA must be enabled with status=0.");
    var serviceTags = parameters.GetProperty("service_tags").EnumerateArray().ToArray();
    Assert(serviceTags.Any(tag =>
            JsonString(tag, "tag") == ZabbixManagedServiceTags.SlaPolicy
            && JsonString(tag, "value") == "workplace-99"),
        "Zabbix SLA must select services by managed SLA policy tag.");
    var excludedDowntimes = parameters.GetProperty("excluded_downtimes").EnumerateArray().ToArray();
    Assert(excludedDowntimes.Length == 2, "Zabbix SLA must preserve one manual downtime and add one managed downtime.");
    Assert(excludedDowntimes.Any(item => JsonString(item, "name") == "manual one-time change"),
        "Zabbix SLA update must preserve manual excluded downtime.");
    Assert(!excludedDowntimes.Any(item => JsonString(item, "name") == "CMDB2M REG:old window"),
        "Zabbix SLA update must remove stale managed excluded downtime.");
}

static async Task AssertZabbixClientEnsuresHostTagsAsync()
{
    var handler = new DiagnosticZabbixHandler();
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var tagsApplied = await client.EnsureHostTagsAsync(
        "30011",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.SourceZabbixHostId] = "30011",
            [ZabbixManagedServiceTags.SourceCardId] = "447411"
        },
        CancellationToken.None);

    Assert(tagsApplied == 2, "Zabbix host tag apply counter is invalid.");
    var payloadText = handler.HostUpdatePayload
        ?? throw new InvalidOperationException("Zabbix host.update payload was not captured.");
    using var document = JsonDocument.Parse(payloadText);
    var parameters = document.RootElement.GetProperty("params");
    Assert(JsonString(parameters, "hostid") == "30011", "Zabbix host.update hostid is invalid.");
    var tags = parameters.GetProperty("tags")
        .EnumerateArray()
        .ToDictionary(tag => JsonString(tag, "tag"), tag => JsonString(tag, "value"), StringComparer.Ordinal);
    Assert(tags["customer:tag"] == "keep", "Zabbix host.update must preserve existing customer host tags.");
    Assert(tags[ZabbixManagedServiceTags.SourceZabbixHostId] == "30011",
        "Zabbix host.update managed source hostid tag is invalid.");
    Assert(tags[ZabbixManagedServiceTags.SourceCardId] == "447411",
        "Zabbix host.update managed source card id tag is invalid.");
}

static async Task AssertZabbixClientAppliesTriggerDependenciesAsync()
{
    var handler = new DiagnosticZabbixHandler();
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var triggers = await client.GetTriggersByHostIdsAsync(["30011", "30012"], includeDisabled: false, CancellationToken.None);
    Assert(triggers.Count == 2, "Zabbix trigger.get must return diagnostic triggers.");
    Assert(triggers.Any(trigger => trigger.TriggerId == "60001" && trigger.Hosts.Any(host => host.HostId == "30011")),
        "Zabbix trigger.get must read trigger host binding.");
    Assert(triggers.Single(trigger => trigger.TriggerId == "60001").Expression.Contains("icmpping", StringComparison.Ordinal),
        "Zabbix trigger.get must read expanded trigger expression.");
    Assert(triggers.Single(trigger => trigger.TriggerId == "60001").Tags.Any(tag =>
            tag.Tag == "scope" && tag.Value == "availability"),
        "Zabbix trigger.get must read trigger tags.");
    Assert(triggers.Single(trigger => trigger.TriggerId == "60002").Dependencies.Single().TriggerId == "77777",
        "Zabbix trigger.get must read existing dependencies.");

    var triggerGetPayload = handler.TriggerGetPayload
        ?? throw new InvalidOperationException("Zabbix trigger.get payload was not captured.");
    using var getDocument = JsonDocument.Parse(triggerGetPayload);
    var getParameters = getDocument.RootElement.GetProperty("params");
    Assert(getParameters.TryGetProperty("filter", out var filter)
        && JsonString(filter, "status") == "0",
        "Zabbix trigger.get must filter enabled triggers by default.");
    Assert(getParameters.GetProperty("selectDependencies").EnumerateArray().Any(),
        "Zabbix trigger.get must request existing dependencies.");
    Assert(getParameters.GetProperty("selectTags").EnumerateArray().Any(),
        "Zabbix trigger.get must request trigger tags.");
    Assert(getParameters.GetProperty("expandExpression").GetBoolean(),
        "Zabbix trigger.get must request expanded trigger expressions.");
    Assert(getParameters.GetProperty("output").EnumerateArray().Any(item => item.GetString() == "expression"),
        "Zabbix trigger.get must request trigger expressions.");

    var batchHandler = new DiagnosticZabbixHandler();
    var batchClient = new ZabbixClient(
        new HttpClient(batchHandler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));
    var manyHostIds = Enumerable.Range(1, 55).Select(index => index == 1 ? "30011" : $"host-{index:00}").ToArray();
    var batchedTriggers = await batchClient.GetTriggersByHostIdsAsync(manyHostIds, includeDisabled: false, CancellationToken.None);
    Assert(batchedTriggers.Count == 2, "Batched Zabbix trigger.get must merge duplicate trigger results.");
    Assert(batchHandler.TriggerGetPayloads.Count == 3,
        "Zabbix trigger.get by hostids must be split into batches.");
    Assert(batchHandler.TriggerGetPayloads.All(payload => TriggerGetLookupCount(payload, "hostids") <= 25),
        "Zabbix trigger.get hostid batches must stay within the safe batch size.");

    var manyTriggerIds = Enumerable.Range(1, 55).Select(index => $"trigger-{index:00}").ToArray();
    await batchClient.GetTriggersByIdsAsync(manyTriggerIds, includeDisabled: true, CancellationToken.None);
    Assert(batchHandler.TriggerGetPayloads.Skip(3).Count() == 3,
        "Zabbix trigger.get by triggerids must be split into batches.");
    Assert(batchHandler.TriggerGetPayloads.Skip(3).All(payload => TriggerGetLookupCount(payload, "triggerids") <= 25),
        "Zabbix trigger.get triggerid batches must stay within the safe batch size.");

    await batchClient.GetTriggersByHostIdsAsync(manyHostIds, includeDisabled: false, batchSize: 10, cancellationToken: CancellationToken.None);
    var configuredBatchPayloads = batchHandler.TriggerGetPayloads.Skip(6).ToArray();
    Assert(configuredBatchPayloads.Length == 6,
        "Zabbix trigger.get must honor an explicitly configured batch size.");
    Assert(configuredBatchPayloads.All(payload => TriggerGetLookupCount(payload, "hostids") <= 10),
        "Zabbix trigger.get configured batches must stay within the configured batch size.");

    var batchedHosts = await batchClient.GetHostsByIdsAsync(manyHostIds, batchSize: 10, cancellationToken: CancellationToken.None);
    Assert(batchedHosts.Count == 1 && batchedHosts.Single().HostId == "30011",
        "Zabbix host.get by hostids must return existing hosts.");
    Assert(batchHandler.HostGetPayloads.Count == 6,
        "Zabbix host.get by hostids must be split into batches.");
    Assert(batchHandler.HostGetPayloads.All(payload => TriggerGetLookupCount(payload, "hostids") <= 10),
        "Zabbix host.get batches must stay within the configured batch size.");

    var applied = await client.UpdateTriggerDependenciesAsync(
        "60002",
        ["60001", "77777"],
        CancellationToken.None);
    Assert(applied == 2, "Zabbix trigger dependency apply counter is invalid.");
    var triggerUpdatePayload = handler.TriggerUpdatePayload
        ?? throw new InvalidOperationException("Zabbix trigger.update payload was not captured.");
    using var updateDocument = JsonDocument.Parse(triggerUpdatePayload);
    var updateParameters = updateDocument.RootElement.GetProperty("params");
    Assert(JsonString(updateParameters, "triggerid") == "60002", "Zabbix trigger.update triggerid is invalid.");
    var dependencyIds = updateParameters.GetProperty("dependencies")
        .EnumerateArray()
        .Select(item => JsonString(item, "triggerid"))
        .ToArray();
    Assert(dependencyIds.SequenceEqual(["60001", "77777"]), "Zabbix trigger.update dependencies are invalid.");
}

static int TriggerGetLookupCount(string payload, string lookupField)
{
    using var document = JsonDocument.Parse(payload);
    return document.RootElement
        .GetProperty("params")
        .GetProperty(lookupField)
        .GetArrayLength();
}

static async Task AssertZabbixClientAppliesSuppressionAggregateAsync()
{
    var handler = new DiagnosticZabbixHandler();
    var client = new ZabbixClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));

    var result = await client.ApplySuppressionAggregateAsync(
        new ZabbixSuppressionAggregateDefinition
        {
            TargetManagedKey = "suppression:vpn-hubs:city04",
            TargetClass = "C2M_SuppressionNetworkAccessZone",
            TargetCardId = "611269",
            TargetName = "ВПН Хабы / City04",
            AggregationType = "any",
            HostGroupName = "CMDB2Monitoring",
            HostName = "cmdb2monitoring-suppression-aggregates",
            HostVisibleName = "CMDB2Monitoring suppression aggregates",
            ItemKey = "cmdb2monitoring.suppression.aggregate.abc",
            ItemName = "CMDB2M suppression state: ВПН Хабы / City04",
            CalculationFormula = "max(/cmdb-vpn-hub-001/icmpping,#3)+max(/cmdb-vpn-hub-002/icmpping,#3)",
            TriggerName = "CMDB2M suppression: ВПН Хабы / City04 недоступен как группа",
            TriggerExpression = "last(/cmdb2monitoring-suppression-aggregates/cmdb2monitoring.suppression.aggregate.abc)<1",
            TriggerPriority = 3
        },
        CancellationToken.None);

    Assert(result.HostId == "43001", "Zabbix aggregate host id is invalid.");
    Assert(result.ItemId == "44001", "Zabbix aggregate item id is invalid.");
    Assert(result.TriggerId == "45001", "Zabbix aggregate trigger id is invalid.");
    Assert(handler.HistoryPushPayload is null, "Zabbix aggregate state must not be pushed through history.push.");

    using var itemDocument = JsonDocument.Parse(handler.ItemCreatePayload
        ?? throw new InvalidOperationException("Zabbix item.create payload was not captured."));
    var itemParameters = itemDocument.RootElement.GetProperty("params");
    Assert(JsonString(itemParameters, "key_") == "cmdb2monitoring.suppression.aggregate.abc",
        "Zabbix aggregate item key is invalid.");
    Assert(itemParameters.GetProperty("type").GetInt32() == 15, "Zabbix aggregate item must be a calculated item.");
    Assert(itemParameters.GetProperty("value_type").GetInt32() == 3, "Zabbix aggregate item must be numeric unsigned.");
    Assert(JsonString(itemParameters, "params") == "max(/cmdb-vpn-hub-001/icmpping,#3)+max(/cmdb-vpn-hub-002/icmpping,#3)",
        "Zabbix aggregate item formula is invalid.");

    using var triggerDocument = JsonDocument.Parse(handler.TriggerCreatePayload
        ?? throw new InvalidOperationException("Zabbix trigger.create payload was not captured."));
    var triggerParameters = triggerDocument.RootElement.GetProperty("params");
    Assert(JsonString(triggerParameters, "expression") == "last(/cmdb2monitoring-suppression-aggregates/cmdb2monitoring.suppression.aggregate.abc)<1",
        "Zabbix aggregate trigger expression is invalid.");
    var tags = triggerParameters.GetProperty("tags")
        .EnumerateArray()
        .ToDictionary(tag => JsonString(tag, "tag"), tag => JsonString(tag, "value"), StringComparer.Ordinal);
    Assert(tags[ZabbixManagedServiceTags.Aggregate] == "true", "Zabbix aggregate trigger tag is missing.");
    Assert(tags[ZabbixManagedServiceTags.Key] == "suppression:vpn-hubs:city04",
        "Zabbix aggregate trigger target key tag is invalid.");

    var updateHandler = new DiagnosticZabbixHandler
    {
        ExistingAggregateHost = true,
        ExistingAggregateItem = true
    };
    var updateClient = new ZabbixClient(
        new HttpClient(updateHandler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));
    await updateClient.ApplySuppressionAggregateAsync(
        new ZabbixSuppressionAggregateDefinition
        {
            TargetManagedKey = "suppression:vpn-hubs:city04",
            TargetClass = "C2M_SuppressionNetworkAccessZone",
            TargetCardId = "611269",
            TargetName = "ВПН Хабы / City04",
            AggregationType = "any",
            HostGroupName = "CMDB2Monitoring",
            HostName = "cmdb2monitoring-suppression-aggregates",
            HostVisibleName = "CMDB2Monitoring suppression aggregates",
            ItemKey = "cmdb2monitoring.suppression.aggregate.abc",
            ItemName = "CMDB2M suppression state: ВПН Хабы / City04",
            CalculationFormula = "max(/cmdb-vpn-hub-001/icmpping,#3)+max(/cmdb-vpn-hub-002/icmpping,#3)",
            TriggerName = "CMDB2M suppression: ВПН Хабы / City04 недоступен как группа",
            TriggerExpression = "last(/cmdb2monitoring-suppression-aggregates/cmdb2monitoring.suppression.aggregate.abc)<1",
            TriggerPriority = 3
        },
        CancellationToken.None);
    using var itemUpdateDocument = JsonDocument.Parse(updateHandler.ItemUpdatePayload
        ?? throw new InvalidOperationException("Zabbix item.update payload was not captured."));
    var itemUpdateParameters = itemUpdateDocument.RootElement.GetProperty("params");
    Assert(JsonString(itemUpdateParameters, "itemid") == "44001", "Zabbix aggregate item.update itemid is invalid.");
    Assert(!itemUpdateParameters.TryGetProperty("hostid", out _),
        "Zabbix aggregate item.update must not send immutable hostid.");
    Assert(JsonString(itemUpdateParameters, "params") == "max(/cmdb-vpn-hub-001/icmpping,#3)+max(/cmdb-vpn-hub-002/icmpping,#3)",
        "Zabbix aggregate item.update formula is invalid.");

    var aggregateItems = await updateClient.GetSuppressionAggregateItemsAsync(
        "cmdb2monitoring-suppression-aggregates",
        ["cmdb2monitoring.suppression.aggregate.abc", "cmdb2monitoring.suppression.aggregate.abc", ""],
        CancellationToken.None);
    Assert(aggregateItems.Count == 1, "Zabbix aggregate item diagnostics must de-duplicate requested keys.");
    Assert(aggregateItems.Single().State == "1", "Zabbix aggregate item diagnostics must read unsupported state.");
    Assert(aggregateItems.Single().Error.Contains("bad formula", StringComparison.Ordinal),
        "Zabbix aggregate item diagnostics must read item error.");
    using var itemGetDocument = JsonDocument.Parse(updateHandler.ItemGetPayload
        ?? throw new InvalidOperationException("Zabbix item.get payload was not captured."));
    var itemGetParameters = itemGetDocument.RootElement.GetProperty("params");
    Assert(JsonString(itemGetParameters, "host") == "cmdb2monitoring-suppression-aggregates",
        "Zabbix aggregate item diagnostics must query by aggregate host name.");
    Assert(itemGetParameters.GetProperty("filter").GetProperty("key_").GetArrayLength() == 1,
        "Zabbix aggregate item diagnostics must query unique item keys.");

    var adoptHandler = new DiagnosticZabbixHandler
    {
        ExistingAggregateHost = true,
        ExistingAggregateItem = true,
        ExistingAggregateTriggerByName = true
    };
    var adoptClient = new ZabbixClient(
        new HttpClient(adoptHandler),
        new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
        {
            ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
            AuthMode = "Login",
            User = "Admin",
            Password = "zabbix",
            RequestTimeoutMs = 5000
        }));
    var adopted = await adoptClient.ApplySuppressionAggregateAsync(
        new ZabbixSuppressionAggregateDefinition
        {
            TargetManagedKey = "suppression:vpn-hubs:city04",
            TargetClass = "C2M_SuppressionNetworkAccessZone",
            TargetCardId = "611269",
            TargetName = "ВПН Хабы / City04",
            AggregationType = "any",
            HostGroupName = "CMDB2Monitoring",
            HostName = "cmdb2monitoring-suppression-aggregates",
            HostVisibleName = "CMDB2Monitoring suppression aggregates",
            ItemKey = "cmdb2monitoring.suppression.aggregate.abc",
            ItemName = "CMDB2M suppression state: ВПН Хабы / City04",
            CalculationFormula = "max(/cmdb-vpn-hub-001/icmpping,#3)+max(/cmdb-vpn-hub-002/icmpping,#3)",
            TriggerName = "CMDB2M suppression: ВПН Хабы / City04 недоступен как группа",
            TriggerExpression = "last(/cmdb2monitoring-suppression-aggregates/cmdb2monitoring.suppression.aggregate.abc)<1",
            TriggerPriority = 3
        },
        CancellationToken.None);
    Assert(adopted.TriggerId == "45001", "Zabbix aggregate trigger adoption by name must keep the existing trigger id.");
    Assert(adopted.TriggerAction == "updated", "Zabbix aggregate trigger adoption by name must update the existing trigger.");
    Assert(adoptHandler.TriggerCreatePayload is null,
        "Zabbix aggregate trigger adoption by name must not create a duplicated trigger.");
}

static async Task AssertZabbixSuppressionAggregateThresholdUsesSelectedHostsAsync()
{
    var state = NewZabbixApplyStateStore();
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City31", "City31", "30011", "1001"), "suppression", includeSourceLeafManagedKey: false);
    state.UpdateMembership(BuildMembershipCommand("suppression", "supp:City31", "City31", "39999", "1002"), "suppression", includeSourceLeafManagedKey: false);

    var zabbixOptions = new ZabbixOptions
    {
        ApiEndpoint = "http://zabbix.local/api_jsonrpc.php",
        AuthMode = "Login",
        User = "Admin",
        Password = "zabbix",
        RequestTimeoutMs = 5000
    };
    var client = new ZabbixClient(
        new HttpClient(new DiagnosticZabbixHandler()),
        new StaticOptionsMonitor<ZabbixOptions>(zabbixOptions));
    var applier = new ZabbixTriggerDependencyApplier(
        client,
        state,
        new LocalRuntimeLookupCache(new StaticOptionsMonitor<RuntimeRedisOptions>(new RuntimeRedisOptions())),
        new StaticOptionsMonitor<ZabbixTriggerDependenciesOptions>(new ZabbixTriggerDependenciesOptions
        {
            Enabled = true,
            AggregateStateTriggerIncludeNameRegex = ".*",
            DependencyTriggerIncludeNameRegex = ".*"
        }),
        new StaticOptionsMonitor<ZabbixOptions>(zabbixOptions),
        NullLogger<ZabbixTriggerDependencyApplier>.Instance);

    var result = await applier.RunAsync(dryRun: true, CancellationToken.None);
    Assert(result.Errors.Count == 0, "suppression aggregate dry-run must not fail for missing selected source triggers.");
    var aggregate = result.Aggregates.Single(item => item.TargetManagedKey == "supp:City31");
    Assert(aggregate.HostCount == 2, "suppression aggregate must still report all host bindings.");
    Assert(aggregate.UnknownHostCount == 1, "suppression aggregate must count source-hosts without selected triggers as unknown.");
    Assert(aggregate.RequiredHealthyHostCount == 1,
        "suppression aggregate threshold must be based on hosts with selected supported triggers, not raw CMDBuild membership.");
    Assert(aggregate.OwnProblemExpression.EndsWith("<1", StringComparison.Ordinal),
        "suppression aggregate own trigger expression must use the selected-host threshold.");
}

static void AssertSemanticFingerprintIncludesDimensionField()
{
    var city04Plan = BuildRouterCorePlans("City04")
        .Single(plan => plan.Command.RuleId == "network-access-zone-by-city-routercore-city04");
    var city29Plan = BuildRouterCorePlans("City29")
        .Single(plan => plan.Command.RuleId == "network-access-zone-by-city-routercore-city29");

    Assert(city04Plan.SemanticKey != city29Plan.SemanticKey,
        "semantic key must include the generated city rule id so different city targets are not deduplicated together.");
    Assert(city04Plan.SemanticFingerprint != city29Plan.SemanticFingerprint,
        "semantic fingerprint must change when the population dimension field changes.");
}

static void AssertSemanticFingerprintChangesWhenHostIdAppears()
{
    var withoutHostId = BuildHostReadinessPlan(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "host-001"
    });
    var withHostId = BuildHostReadinessPlan(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "host-001",
        ["zabbix_main_hostid"] = "main-30011"
    });

    Assert(withoutHostId.SemanticKey == withHostId.SemanticKey,
        "semantic key must stay stable when the same source card becomes ready.");
    Assert(withoutHostId.SemanticFingerprint != withHostId.SemanticFingerprint,
        "semantic fingerprint must change when zabbix_main_hostid appears so dedup cannot suppress readiness.");
}

static async Task AssertCmdbuildApplyCreatesSourceLinkAsync()
{
    var command = BuildRouterCorePlans("City04")
        .Select(plan => plan.Command)
        .Single(command => command.RuleId == "core-router");
    var handler = new DiagnosticCmdbuildHandler();
    var client = new CmdbuildClient(
        new HttpClient(handler),
        new StaticOptionsMonitor<CmdbuildOptions>(new CmdbuildOptions
        {
            BaseUrl = "http://cmdbuild.local/services/rest/v3",
            AuthMode = "None",
            RequestTimeoutMs = 5000
        }),
        new HttpContextAccessor());

    var result = await client.ApplyAggregationCommandAsync(command, CancellationToken.None);
    Assert(result.Success, "CMDBuild apply result must be successful.");
    Assert(result.TargetCardId == "576100", "CMDBuild apply must resolve the static core-router target card.");
    Assert(result.TargetAction == "unchanged", "CMDBuild apply must not update an unchanged target card.");
    Assert(result.RelationDomain == "C2M_ServiceNetworkAccessZonePopulatedFromrouterCore",
        "CMDBuild apply must use the source-link domain.");
    Assert(result.RelationId == "rel-core-router-447411", "CMDBuild apply must report created relation id.");
    Assert(result.RelationAction == "created", "CMDBuild apply must create the missing source-link relation.");

    var sourceLinkRelationPayload = handler.SourceLinkRelationPayload
        ?? throw new InvalidOperationException("CMDBuild apply must post a source-link relation payload.");
    using var document = JsonDocument.Parse(sourceLinkRelationPayload);
    var payload = document.RootElement;
    Assert(JsonString(payload, "_sourceType") == "C2M_ServiceNetworkAccessZone", "source-link source type is invalid.");
    Assert(JsonString(payload, "_sourceId") == "576100", "source-link source id is invalid.");
    Assert(JsonString(payload, "_destinationType") == "routerCore", "source-link destination type is invalid.");
    Assert(JsonString(payload, "_destinationId") == "447411", "source-link destination id is invalid.");
    Assert(JsonString(payload, "population_rule_id") == "core-router", "source-link population_rule_id is invalid.");
}

static ConversionRule MinimalRule(string ruleId)
{
    return new ConversionRule
    {
        RuleId = ruleId,
        Name = $"Rule {ruleId}",
        Layer = "service",
        Source = new SourceSelector { ClassCode = "Host", KeyAttribute = "Code" },
        Target = new TargetObject
        {
            ClassCode = "C2M_ServiceResource",
            IdempotencyKey = "${source.Code}"
        }
    };
}

static AggregationCommand BuildHostReadinessCommand(
    IReadOnlyDictionary<string, string> attributes,
    AggregationRuleEngine? engine = null)
{
    return BuildHostReadinessPlan(attributes, engine).Command;
}

static ZabbixApplyStateStore NewZabbixApplyStateStore()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "cmdb2monitoring-tests",
        $"zabbix-apply-state-{Guid.NewGuid():N}.json");
    var storage = new FileZabbixApplyStateStorage(
        Options.Create(new ZabbixApplyStateOptions { FilePath = path }),
        NullLogger<FileZabbixApplyStateStorage>.Instance);
    return new ZabbixApplyStateStore(storage);
}

static SqliteZabbixApplyStateStorage NewSqliteZabbixApplyStateStorage(string dbPath)
{
    return new SqliteZabbixApplyStateStorage(
        new StaticOptionsMonitor<DurableStoreOptions>(new DurableStoreOptions
        {
            Provider = "sqlite",
            ConnectionString = $"Data Source={dbPath}"
        }),
        Options.Create(new ZabbixApplyStateOptions
        {
            FilePath = Path.Combine(
                Path.GetTempPath(),
                "cmdb2monitoring-tests",
                $"unused-bootstrap-{Guid.NewGuid():N}.json")
        }),
        NullLogger<SqliteZabbixApplyStateStorage>.Instance);
}

static ZabbixDirtyScopeStore NewSqliteDirtyScopeStore(string dbPath)
{
    return new ZabbixDirtyScopeStore(
        new StaticOptionsMonitor<DurableStoreOptions>(new DurableStoreOptions
        {
            Provider = "sqlite",
            ConnectionString = $"Data Source={dbPath}"
        }),
        NullLogger<ZabbixDirtyScopeStore>.Instance);
}

static MonitoringCoverageSnapshotStore NewSqliteMonitoringCoverageSnapshotStore(string dbPath)
{
    return new MonitoringCoverageSnapshotStore(
        new StaticOptionsMonitor<DurableStoreOptions>(new DurableStoreOptions
        {
            Provider = "sqlite",
            ConnectionString = $"Data Source={dbPath}"
        }),
        NullLogger<MonitoringCoverageSnapshotStore>.Instance);
}

static ZabbixTargetMembershipSnapshot NewCoverageMembership(
    string layer,
    string targetKey,
    string sourceClass,
    string sourceCardId,
    string zabbixHostId)
{
    return new ZabbixTargetMembershipSnapshot
    {
        Layer = layer,
        TargetManagedKey = targetKey,
        TargetClass = "C2M_Test",
        TargetCardId = targetKey,
        TargetName = targetKey,
        Sources =
        [
            new ZabbixSourceMembership
            {
                SourceClass = sourceClass,
                SourceCardId = sourceCardId,
                SourceKeyAttribute = "Code",
                SourceKeyValue = sourceCardId,
                ZabbixHostId = zabbixHostId,
                SourceLeafManagedKey = $"{sourceClass}:{sourceCardId}"
            }
        ],
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}

static AggregationCommand BuildMembershipCommand(
    string layer,
    string targetKey,
    string city,
    string zabbixHostId,
    string sourceCardId = "1001",
    string isCritical = "")
{
    var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["name"] = city
    };
    if (!string.IsNullOrWhiteSpace(isCritical))
    {
        attributes["is_critical"] = isCritical;
    }

    return new AggregationCommand
    {
        CommandId = Guid.NewGuid().ToString("N"),
        CommandType = AggregationCommandTypes.EnsureMembership,
        Layer = layer,
        RuleId = $"{layer}-{city}",
        RuleName = $"{layer} {city}",
        EventType = "UPDATE",
        Source = new AggregationSourceObject
        {
            ClassCode = "Host",
            CardId = sourceCardId,
            KeyAttribute = "city",
            KeyValue = city,
            ZabbixHostId = zabbixHostId
        },
        Target = new AggregationTargetObject
        {
            ClassCode = layer.Equals("suppression", StringComparison.OrdinalIgnoreCase)
                ? "C2M_SuppressionResource"
                : "C2M_ServiceResource",
            IdempotencyKey = targetKey,
            CardDescription = $"{layer} {city}",
            CreateInstance = true,
            Attributes = attributes
        }
    };
}

static AggregationCommand BuildSuppressionRelationCommand(
    string targetKey,
    string name,
    string relationTargetKey,
    string sourceCardId)
{
    var command = BuildMembershipCommand(
        "suppression",
        targetKey,
        name,
        $"3{sourceCardId}",
        sourceCardId);
    return command with
    {
        Target = command.Target with
        {
            Relations =
            [
                new AggregationTargetRelation
                {
                    DomainCode = "C2M_SuppressionResourceSuppressesResource",
                    TargetClassCode = "C2M_SuppressionResource",
                    TargetLookup = relationTargetKey
                }
            ]
        }
    };
}

static AggregationCommand BuildSourceMembershipRemovalCommand(string layer)
{
    return new AggregationCommand
    {
        CommandId = Guid.NewGuid().ToString("N"),
        CommandType = AggregationCommandTypes.RemoveSourceMembership,
        Layer = layer,
        RuleId = $"source-membership-reconcile:{layer}:Host",
        RuleName = $"Reconcile source membership {layer}/Host",
        EventType = "UPDATE",
        Source = new AggregationSourceObject
        {
            ClassCode = "Host",
            CardId = "1001",
            KeyAttribute = "_id",
            KeyValue = "1001"
        }
    };
}

static AggregationCommandPlan BuildHostReadinessPlan(
    IReadOnlyDictionary<string, string> attributes,
    AggregationRuleEngine? engine = null)
{
    var rawEvent = new CmdbRawEvent
    {
        EventId = "readiness-test",
        Source = "CMDBuild",
        EventType = "UPDATE",
        ClassCode = "Host",
        CardId = "1001",
        Attributes = attributes
    };

    return (engine ?? new AggregationRuleEngine()).BuildCommandPlans(rawEvent, new ConversionRulesDocument
    {
        Version = "test",
        Rules = [MinimalRule("host-readiness")]
    }).Single();
}

static IReadOnlyList<AggregationCommandPlan> BuildRouterCorePlans(string city)
{
    var rawEvent = new CmdbRawEvent
    {
        EventId = $"event-{city}",
        Source = "CMDBuild",
        EventType = "UPDATE",
        ClassCode = "routerCore",
        CardId = "447411",
        Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["className"] = "routerCore",
            ["Code"] = "ctest2-routerCore-002",
            ["zabbix_main_hostid"] = "30011",
            ["locationFloorBuildingCity"] = city
        }
    };

    return new AggregationRuleEngine().BuildCommandPlans(rawEvent, new ConversionRulesDocument
    {
        Version = "test",
        Rules =
        [
            CoreRouterRule(),
            RouterCityRule("City04"),
            RouterCityRule("City29")
        ]
    });
}

static ConversionRule CoreRouterRule()
{
    return new ConversionRule
    {
        RuleId = "core-router",
        Name = "маршрутизаторы ядра",
        Layer = "service",
        Priority = 50,
        Source = new SourceSelector
        {
            ClassCode = "routerCore",
            KeyAttribute = "Code"
        },
        When = new RuleWhen
        {
            FieldExists = "Code",
            AllRegex =
            [
                new RegexMatcher { Field = "className", Pattern = "(?i)^routerCore$" },
                new RegexMatcher { Field = "Code", Pattern = ".*" }
            ]
        },
        Target = new TargetObject
        {
            ClassCode = "C2M_ServiceNetworkAccessZone",
            CardId = "576100",
            CardDescription = "Маршрутизаторы ядра",
            IdempotencyKey = "cmdbuild:C2M_ServiceNetworkAccessZone:576100",
            InitialUserValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Code"] = "CoreRouter",
                ["Description"] = "Маршрутизаторы ядра",
                ["name"] = "Маршрутизаторы ядра",
                ["is_critical"] = "false",
                ["aggregation_type"] = "any"
            }
        }
    };
}

static ConversionRule RouterCityRule(string city)
{
    return new ConversionRule
    {
        RuleId = $"network-access-zone-by-city-routercore-{city.ToLowerInvariant()}",
        Name = $"Уровень коммутации / {city}",
        Layer = "service",
        Priority = 100,
        Source = new SourceSelector
        {
            ClassCode = "routerCore",
            KeyAttribute = "locationFloorBuildingCity"
        },
        When = new RuleWhen
        {
            FieldExists = "locationFloorBuildingCity",
            AllRegex =
            [
                new RegexMatcher { Field = "className", Pattern = "(?i)^routerCore$" },
                new RegexMatcher { Field = "Code", Pattern = ".*" },
                new RegexMatcher { Field = "locationFloorBuildingCity", Pattern = $"(?i)^{city}$" }
            ]
        },
        Target = new TargetObject
        {
            ClassCode = "C2M_ServiceNetworkAccessZone",
            CreateInstance = true,
            IdempotencyKey = $"network-access-zone-by-city:{city}",
            AttributeMappings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = city,
                ["population_source_key"] = $"network-access-zone-by-city:{city}"
            },
            InitialUserValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["description"] = $"Автоматически создано для {city}"
            }
        },
        Relations =
        [
            new TargetRelation
            {
                DomainCode = "C2M_ServiceNetworkZoneDependsOnNetworkZone",
                TargetClassCode = "C2M_ServiceNetworkAccessZone",
                TargetLookup = "576100",
                AttributeMappings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["is_active"] = "true"
                }
            }
        ]
    };
}

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

    var suppressionSuperclass = schema.Classes.Single(c => c.Code == "C2M_SuppressionManagedObject");
    var suppressionAggregationType = suppressionSuperclass.Attributes.Single(attribute => attribute.Code == "aggregation_type");
    Assert(suppressionAggregationType.LookupTypeCode == "ServiceAggregationType",
        "suppression aggregation_type must reuse ServiceAggregationType lookup.");
    Assert(suppressionAggregationType.Help.Contains("trigger dependencies", StringComparison.Ordinal)
        && suppressionAggregationType.Help.Contains("all", StringComparison.Ordinal)
        && suppressionAggregationType.Help.Contains("any", StringComparison.Ordinal),
        "suppression aggregation_type help must explain trigger dependency constraints.");
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

static void AssertServiceSlaPolicy(CmdbuildSchemaDefinition schema)
{
    var lookup = schema.Lookups.Single(lookup => lookup.Code == "ServiceSlaReportingPeriod");
    Assert(lookup.Values.Select(value => value.Code).SequenceEqual(["daily", "weekly", "monthly", "quarterly", "yearly"]),
        "service SLA reporting period lookup values are invalid.");

    var slaPolicy = schema.Classes.Single(c => c.Code == "C2M_ServiceSlaPolicy");
    Assert(slaPolicy.Layer == BuilderLayer.Service, "SLA policy must belong to the service layer.");
    Assert(slaPolicy.ParentClassCode == "C2M_ServiceManagedObject", "SLA policy must inherit the service managed superclass.");
    Assert(slaPolicy.Purpose.Contains("SLA", StringComparison.Ordinal), "SLA policy purpose must explain SLA usage.");
    Assert(slaPolicy.Help.Contains("авторитетную цель SLA", StringComparison.Ordinal)
        && slaPolicy.Help.Contains("has_sla_policy", StringComparison.Ordinal)
        && slaPolicy.Help.Contains("has_sla_calendar", StringComparison.Ordinal)
        && slaPolicy.Help.Contains("has_regular_downtime", StringComparison.Ordinal)
        && slaPolicy.Help.Contains("Zabbix SLA", StringComparison.Ordinal)
        && slaPolicy.Help.Contains("24x7 monthly 99.9", StringComparison.Ordinal),
        "SLA policy help must explain CMDBuild ownership and examples.");

    var slaTarget = slaPolicy.Attributes.Single(attribute => attribute.Code == "sla_target");
    Assert(slaTarget.Type == "decimal", "SLA policy sla_target must be a decimal percentage.");
    Assert(slaTarget.Required, "SLA policy sla_target must be required.");
    Assert(!string.IsNullOrWhiteSpace(slaTarget.ValidationRules), "SLA policy sla_target must validate percentage input.");
    Assert(slaTarget.ValidationRules.Contains("parsed >= 0", StringComparison.Ordinal)
        && slaTarget.ValidationRules.Contains("parsed <= 100", StringComparison.Ordinal)
        && slaTarget.ValidationRules.Contains("99.9", StringComparison.Ordinal)
        && slaTarget.ValidationRules.Contains("0.999", StringComparison.Ordinal),
        "SLA policy sla_target validationRules script is incomplete.");

    var reportingPeriod = slaPolicy.Attributes.Single(attribute => attribute.Code == "reporting_period");
    Assert(reportingPeriod.Type == "lookup", "SLA policy reporting_period must be a lookup.");
    Assert(reportingPeriod.LookupTypeCode == "ServiceSlaReportingPeriod",
        "SLA policy reporting_period must reference ServiceSlaReportingPeriod lookup.");
    Assert(reportingPeriod.Required, "SLA policy reporting_period must be required.");

    var calendar = slaPolicy.Attributes.Single(attribute => attribute.Code == "calendar");
    Assert(calendar.Help.Contains("legacy", StringComparison.Ordinal)
        && calendar.Help.Contains("has_sla_calendar", StringComparison.Ordinal)
        && calendar.Help.Contains("ServiceSlaCalendar", StringComparison.Ordinal),
        "SLA policy calendar help must point to ServiceSlaCalendar relation.");

    AssertDomainAttributes(
        schema,
        "C2M_ServicePlatformServiceHasSlaPolicy",
        present: ["is_active", "source"],
        absent: ["priority", "is_dynamic", "fallback_supported", "is_critical"]);
}

static void AssertServiceSlaCalendar(CmdbuildSchemaDefinition schema)
{
    var calendar = schema.Classes.Single(c => c.Code == "C2M_ServiceSlaCalendar");
    Assert(calendar.Layer == BuilderLayer.Service, "SLA calendar must belong to the service layer.");
    Assert(calendar.ParentClassCode == "C2M_ServiceManagedObject", "SLA calendar must inherit the service managed superclass.");
    Assert(calendar.Purpose.Contains("календар", StringComparison.OrdinalIgnoreCase)
        || calendar.Purpose.Contains("calendar", StringComparison.OrdinalIgnoreCase),
        "SLA calendar purpose must explain calendar usage.");
    Assert(calendar.Help.Contains("переиспользуемые рабочие календари SLA", StringComparison.Ordinal)
        && calendar.Help.Contains("has_sla_calendar", StringComparison.Ordinal)
        && calendar.Help.Contains("calendar_code", StringComparison.Ordinal)
        && calendar.Help.Contains("monday_hours", StringComparison.Ordinal)
        && calendar.Help.Contains("sunday_hours", StringComparison.Ordinal)
        && calendar.Help.Contains("HH:mm-HH:mm", StringComparison.Ordinal)
        && calendar.Help.Contains("внешним/ручным", StringComparison.Ordinal)
        && calendar.Help.Contains("24x7", StringComparison.Ordinal),
        "SLA calendar help must explain CMDBuild ownership, relation and examples.");

    var calendarCode = calendar.Attributes.Single(attribute => attribute.Code == "calendar_code");
    Assert(calendarCode.Type == "string" && calendarCode.Required, "SLA calendar calendar_code must be a required string.");
    Assert(calendarCode.Help.Contains("стабильный ключ", StringComparison.OrdinalIgnoreCase)
        && calendarCode.Help.Contains("Zabbix", StringComparison.Ordinal),
        "SLA calendar calendar_code help must explain the stable key.");

    foreach (var dayAttributeCode in new[]
    {
        "monday_hours",
        "tuesday_hours",
        "wednesday_hours",
        "thursday_hours",
        "friday_hours",
        "saturday_hours",
        "sunday_hours"
    })
    {
        var dayHours = calendar.Attributes.Single(attribute => attribute.Code == dayAttributeCode);
        Assert(dayHours.Type == "string", $"SLA calendar {dayAttributeCode} must be a string.");
        Assert(dayHours.Help.Contains("HH:mm-HH:mm", StringComparison.Ordinal)
            && dayHours.Help.Contains("09:00-18:00", StringComparison.Ordinal)
            && dayHours.Help.Contains("09:00-13:00;14:00-18:00", StringComparison.Ordinal),
            $"SLA calendar {dayAttributeCode} help must include the expected format and examples.");
        Assert(!string.IsNullOrWhiteSpace(dayHours.ValidationRules), $"SLA calendar {dayAttributeCode} must validate time intervals.");
        Assert(dayHours.ValidationRules.Contains(dayAttributeCode, StringComparison.Ordinal)
            && dayHours.ValidationRules.Contains("HH:mm-HH:mm", StringComparison.Ordinal)
            && dayHours.ValidationRules.Contains("09:00-13:00;14:00-18:00", StringComparison.Ordinal)
            && dayHours.ValidationRules.Contains("minutes(bounds[0]) >= minutes(bounds[1])", StringComparison.Ordinal),
            $"SLA calendar {dayAttributeCode} validationRules script is incomplete.");
    }

    var domain = schema.Domains.Single(domain => domain.Code == "C2M_ServiceSlaPolicyHasSlaCalendar");
    Assert(domain.SourceClassCode == "C2M_ServiceSlaPolicy", "SLA calendar domain source is invalid.");
    Assert(domain.TargetClassCode == "C2M_ServiceSlaCalendar", "SLA calendar domain target is invalid.");
    Assert(domain.RelationType == "has_sla_calendar", "SLA calendar domain relation type is invalid.");
    Assert(domain.DisplayName.Contains("календар", StringComparison.OrdinalIgnoreCase)
        || domain.DisplayName.Contains("has_sla_calendar", StringComparison.Ordinal),
        "SLA calendar domain display name must explain calendar usage.");
    Assert(domain.Help.Contains("календарем SLA", StringComparison.Ordinal)
        && domain.Help.Contains("Zabbix SLA", StringComparison.Ordinal),
        "SLA calendar domain help must explain Zabbix SLA usage.");
    AssertDomainAttributes(
        schema,
        "C2M_ServiceSlaPolicyHasSlaCalendar",
        present: ["is_active", "source"],
        absent: ["priority", "is_dynamic", "fallback_supported", "is_critical"]);
}

static void AssertServiceSlaDowntime(CmdbuildSchemaDefinition schema)
{
    var downtimeTypeLookup = schema.Lookups.Single(lookup => lookup.Code == "ServiceSlaDowntimeType");
    Assert(downtimeTypeLookup.Values.Select(value => value.Code).SequenceEqual(["regular"]),
        "service SLA downtime type lookup values are invalid.");
    var scheduleLookup = schema.Lookups.Single(lookup => lookup.Code == "ServiceSlaDowntimeSchedule");
    Assert(scheduleLookup.Values.Select(value => value.Code).SequenceEqual(["daily", "weekly", "monthly"]),
        "service SLA downtime schedule lookup values are invalid.");

    var downtime = schema.Classes.Single(c => c.Code == "C2M_ServiceSlaDowntime");
    Assert(downtime.Layer == BuilderLayer.Service, "SLA downtime must belong to the service layer.");
    Assert(downtime.ParentClassCode == "C2M_ServiceManagedObject", "SLA downtime must inherit the service managed superclass.");
    Assert(downtime.Purpose.Contains("downtime", StringComparison.OrdinalIgnoreCase)
        || downtime.Purpose.Contains("исключения SLA", StringComparison.OrdinalIgnoreCase),
        "SLA downtime purpose must explain downtime usage.");
    Assert(downtime.Help.Contains("регулярные договорные окна", StringComparison.Ordinal)
        && downtime.Help.Contains("has_regular_downtime", StringComparison.Ordinal)
        && downtime.Help.Contains("ручными в Zabbix", StringComparison.Ordinal)
        && downtime.Help.Contains("managed-префикс", StringComparison.Ordinal),
        "SLA downtime help must explain regular CMDBuild ownership and manual Zabbix downtime preservation.");

    var downtimeType = downtime.Attributes.Single(attribute => attribute.Code == "downtime_type");
    Assert(downtimeType.Type == "lookup", "SLA downtime downtime_type must be a lookup.");
    Assert(downtimeType.LookupTypeCode == "ServiceSlaDowntimeType",
        "SLA downtime downtime_type must reference ServiceSlaDowntimeType lookup.");
    Assert(downtimeType.Required, "SLA downtime downtime_type must be required.");

    var scheduleType = downtime.Attributes.Single(attribute => attribute.Code == "schedule_type");
    Assert(scheduleType.Type == "lookup", "SLA downtime schedule_type must be a lookup.");
    Assert(scheduleType.LookupTypeCode == "ServiceSlaDowntimeSchedule",
        "SLA downtime schedule_type must reference ServiceSlaDowntimeSchedule lookup.");
    Assert(scheduleType.Required, "SLA downtime schedule_type must be required.");

    var startTime = downtime.Attributes.Single(attribute => attribute.Code == "start_time");
    Assert(startTime.Type == "string" && startTime.Required, "SLA downtime start_time must be a required string.");
    var duration = downtime.Attributes.Single(attribute => attribute.Code == "duration_minutes");
    Assert(duration.Type == "integer" && duration.Required, "SLA downtime duration_minutes must be a required integer.");

    var domain = schema.Domains.Single(domain => domain.Code == "C2M_ServiceSlaPolicyHasRegularDowntime");
    Assert(domain.SourceClassCode == "C2M_ServiceSlaPolicy", "SLA downtime domain source is invalid.");
    Assert(domain.TargetClassCode == "C2M_ServiceSlaDowntime", "SLA downtime domain target is invalid.");
    Assert(domain.RelationType == "has_regular_downtime", "SLA downtime domain relation type is invalid.");
    AssertDomainAttributes(
        schema,
        "C2M_ServiceSlaPolicyHasRegularDowntime",
        present: ["is_active", "source"],
        absent: ["priority", "is_dynamic", "fallback_supported", "is_critical"]);
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

    var suppressionSuperclass = schema.Classes.Single(c => c.Code == "C2M_SuppressionManagedObject");
    var suppressionAggregationType = suppressionSuperclass.Attributes.Single(attribute => attribute.Code == "aggregation_type");
    Assert(!string.IsNullOrWhiteSpace(suppressionAggregationType.ValidationRules),
        "suppression aggregation_type must carry the CMDBuild attribute validationRules script.");

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
        && objectIsActiveHelp.Contains("service-tree child algorithms", StringComparison.Ordinal)
        && objectIsActiveHelp.Contains("aggregate-политик", StringComparison.Ordinal),
        "object is_active help must explain generation and aggregation behavior.");

    var isCriticalHelp = serviceSuperclass.Attributes.Single(attribute => attribute.Code == "is_critical").Help;
    Assert(isCriticalHelp.Contains("не влияет на расчет доступности", StringComparison.Ordinal)
        && isCriticalHelp.Contains("severity trigger-а", StringComparison.Ordinal)
        && isCriticalHelp.Contains("cmdb2monitoring:is_critical", StringComparison.Ordinal),
        "is_critical help must explain metadata-only behavior.");

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
    var domain = schema.Domains.SingleOrDefault(d => d.Code == domainCode)
        ?? schema.Domains.Single(d =>
            d.IsSourceLink
            && d.RelationType == "populated_from"
            && d.SourceClassCode == sourceClassCode
            && d.TargetClassCode == targetClassCode);
    Assert(domain.IsSourceLink, $"{domain.Code}: source link marker is missing.");
    Assert(domain.RelationType == "populated_from", $"{domain.Code}: invalid relation type.");
    Assert(domain.SourceClassCode == sourceClassCode, $"{domain.Code}: invalid managed class.");
    Assert(domain.TargetClassCode == targetClassCode, $"{domain.Code}: invalid customer class.");
    AssertDomainAttributes(
        schema,
        domain.Code,
        present: ["is_active", "source", "population_rule_id"],
        absent: ["priority", "is_dynamic", "fallback_supported", "is_critical"]);
}

static string[] ServiceTopologyClassCodes(CmdbuildSchemaDefinition schema)
{
    return schema.Classes
        .Where(classDefinition => classDefinition.Layer == BuilderLayer.Service
            && !classDefinition.IsSuperclass
            && classDefinition.Code is not "C2M_ServiceSlaCalendar" and not "C2M_ServiceSlaPolicy" and not "C2M_ServiceSlaDowntime")
        .Select(classDefinition => classDefinition.Code)
        .OrderBy(code => code, StringComparer.Ordinal)
        .ToArray();
}

static string JsonString(JsonElement element, string propertyName)
{
    Assert(element.TryGetProperty(propertyName, out var property), $"JSON property '{propertyName}' is missing.");
    return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.GetRawText();
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

public sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; } = value;

    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        return null;
    }
}

public sealed class DiagnosticCmdbuildHandler : HttpMessageHandler
{
    public string? SourceLinkRelationPayload { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = (request.RequestUri?.AbsolutePath ?? "") + (request.RequestUri?.Query ?? "");
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);

        if (request.Method == HttpMethod.Get
            && path.EndsWith("/classes/C2M_ServiceNetworkAccessZone/cards/576100", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """
            {
              "success": true,
              "data": {
                "_id": "576100",
                "Code": "CoreRouter",
                "Description": "Маршрутизаторы ядра",
                "name": "Маршрутизаторы ядра",
                "is_active": true,
                "managed_by_builder": true,
                "auto_population_enabled": true,
                "population_rule_id": "core-router",
                "is_critical": false,
                "aggregation_type": "any"
              }
            }
            """);
        }

        if (request.Method == HttpMethod.Get
            && path.EndsWith("/domains/C2M_ServiceNetworkAccessZonePopulatedFromrouterCore?includeModel=true", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """
            {
              "success": true,
              "data": {
                "_id": "C2M_ServiceNetworkAccessZonePopulatedFromrouterCore",
                "name": "C2M_ServiceNetworkAccessZonePopulatedFromrouterCore",
                "active": true,
                "source": "C2M_ServiceNetworkAccessZone",
                "destination": "routerCore"
              }
            }
            """);
        }

        if (request.Method == HttpMethod.Post
            && path.EndsWith("/domains/C2M_ServiceNetworkAccessZonePopulatedFromrouterCore/relations", StringComparison.Ordinal))
        {
            SourceLinkRelationPayload = body;
            return Json(HttpStatusCode.OK, """
            {
              "success": true,
              "data": {
                "_id": "rel-core-router-447411"
              }
            }
            """);
        }

        return Json(
            HttpStatusCode.NotFound,
            $$"""
            {
              "success": false,
              "error": "unexpected request",
              "method": "{{request.Method.Method}}",
              "path": "{{path}}"
            }
            """);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
    }
}

public sealed class DiagnosticZabbixHandler : HttpMessageHandler
{
    public bool ExistingManagedService { get; init; }

    public Dictionary<string, string> ManagedServiceIdsByKey { get; } = new(StringComparer.Ordinal);

    public bool ExistingAggregateHost { get; init; }

    public bool ExistingAggregateItem { get; init; }

    public bool ExistingAggregateTriggerByName { get; init; }

    public bool ExistingSla { get; init; }

    public string? CreatePayload { get; private set; }

    public string? UpdatePayload { get; private set; }

    public string? HostUpdatePayload { get; private set; }

    public string? HostCreatePayload { get; private set; }

    public string? ItemCreatePayload { get; private set; }

    public string? ItemGetPayload { get; private set; }

    public string? ItemUpdatePayload { get; private set; }

    public string? TriggerGetPayload { get; private set; }

    public IReadOnlyList<string> TriggerGetPayloads => triggerGetPayloads;

    public IReadOnlyList<string> HostGetPayloads => hostGetPayloads;

    public string? TriggerCreatePayload { get; private set; }

    public string? TriggerUpdatePayload { get; private set; }

    public string? HistoryPushPayload { get; private set; }

    public string? SlaCreatePayload { get; private set; }

    public string? SlaUpdatePayload { get; private set; }

    private readonly List<string> triggerGetPayloads = [];
    private readonly List<string> hostGetPayloads = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var method = JsonStringValue(document.RootElement, "method");

        return method switch
        {
            "user.login" => Json(HttpStatusCode.OK, """
            {
              "jsonrpc": "2.0",
              "result": "diagnostic-token",
              "id": 1
            }
            """),
            "service.get" => ServiceGetResponse(body),
            "service.create" => CaptureCreate(body),
            "service.update" => CaptureUpdate(body),
            "sla.get" => ExistingSla ? ExistingSlaResponse() : EmptyArrayResponse(),
            "sla.create" => CaptureSlaCreate(body),
            "sla.update" => CaptureSlaUpdate(body),
            "hostgroup.get" => EmptyArrayResponse(),
            "hostgroup.create" => Json(HttpStatusCode.OK, """
            {
              "jsonrpc": "2.0",
              "result": {
                "groupids": [
                  "42001"
                ]
              },
              "id": 20
            }
            """),
            "host.get" => CaptureHostGet(body),
            "host.create" => CaptureHostCreate(body),
            "host.update" => CaptureHostUpdate(body),
            "item.get" => CaptureItemGet(body),
            "item.create" => CaptureItemCreate(body),
            "item.update" => CaptureItemUpdate(body),
            "trigger.get" => CaptureTriggerGet(body),
            "trigger.create" => CaptureTriggerCreate(body),
            "trigger.update" => CaptureTriggerUpdate(body),
            "history.push" => CaptureHistoryPush(body),
            _ => Json(
                HttpStatusCode.OK,
                $$"""
                {
                  "jsonrpc": "2.0",
                  "error": {
                    "code": -32601,
                    "message": "unexpected method",
                    "data": "{{method}}"
                  },
                  "id": 99
                }
                """)
        };
    }

    private HttpResponseMessage CaptureCreate(string body)
    {
        CreatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "serviceids": [
              "9001"
            ]
          },
          "id": 3
        }
        """);
    }

    private HttpResponseMessage ServiceGetResponse(string body)
    {
        if (TryReadServiceGetTag(body, ZabbixManagedServiceTags.Key, out var managedKey)
            && ManagedServiceIdsByKey.TryGetValue(managedKey, out var serviceId))
        {
            return ManagedServiceResponse(serviceId, managedKey);
        }

        return ExistingManagedService ? ExistingServiceResponse() : EmptyArrayResponse();
    }

    private static bool TryReadServiceGetTag(string body, string tagName, out string value)
    {
        value = "";
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("tags", out var tags)
            || tags.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var tag in tags.EnumerateArray())
        {
            if (JsonStringValue(tag, "tag") == tagName)
            {
                value = JsonStringValue(tag, "value");
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        return false;
    }

    private HttpResponseMessage CaptureUpdate(string body)
    {
        UpdatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "serviceids": [
              "9001"
            ]
          },
          "id": 4
        }
        """);
    }

    private HttpResponseMessage CaptureSlaCreate(string body)
    {
        SlaCreatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "slaids": [
              "71001"
            ]
          },
          "id": 30
        }
        """);
    }

    private HttpResponseMessage CaptureSlaUpdate(string body)
    {
        SlaUpdatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "slaids": [
              "71001"
            ]
          },
          "id": 31
        }
        """);
    }

    private HttpResponseMessage CaptureHostUpdate(string body)
    {
        HostUpdatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "hostids": [
              "30011"
            ]
          },
          "id": 5
        }
        """);
    }

    private HttpResponseMessage CaptureHostGet(string body)
    {
        hostGetPayloads.Add(body);
        if (!body.Contains("cmdb2monitoring-suppression-aggregates", StringComparison.Ordinal))
        {
            return HostGetResponse(body);
        }

        return ExistingAggregateHost
            ? Json(HttpStatusCode.OK, """
            {
              "jsonrpc": "2.0",
              "result": [
                {
                  "hostid": "43001",
                  "host": "cmdb2monitoring-suppression-aggregates",
                  "name": "CMDB2Monitoring suppression aggregates"
                }
              ],
              "id": 21
            }
            """)
            : EmptyArrayResponse();
    }

    private HttpResponseMessage CaptureHostCreate(string body)
    {
        HostCreatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "hostids": [
              "43001"
            ]
          },
          "id": 21
        }
        """);
    }

    private HttpResponseMessage CaptureItemCreate(string body)
    {
        ItemCreatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "itemids": [
              "44001"
            ]
          },
          "id": 22
        }
        """);
    }

    private HttpResponseMessage CaptureItemGet(string body)
    {
        ItemGetPayload = body;
        return ExistingAggregateItem
            ? Json(HttpStatusCode.OK, """
            {
              "jsonrpc": "2.0",
              "result": [
                {
                  "itemid": "44001",
                  "name": "CMDB2M suppression state: ВПН Хабы / City04",
                  "key_": "cmdb2monitoring.suppression.aggregate.abc",
                  "type": "15",
                  "value_type": "3",
                  "status": "0",
                  "state": "1",
                  "error": "bad formula: unsupported source trigger expression",
                  "lastvalue": "0",
                  "lastclock": "1778760000"
                }
              ],
              "id": 22
            }
            """)
            : EmptyArrayResponse();
    }

    private HttpResponseMessage CaptureItemUpdate(string body)
    {
        ItemUpdatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "itemids": [
              "44001"
            ]
          },
          "id": 22
        }
        """);
    }

    private static HttpResponseMessage HostGetResponse(string body)
    {
        if (!body.Contains("30011", StringComparison.Ordinal))
        {
            return EmptyArrayResponse();
        }

        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": [
            {
              "hostid": "30011",
              "host": "ctest2-routerCore-002",
              "name": "ctest2-routerCore-002",
              "tags": [
                {
                  "tag": "customer:tag",
                  "value": "keep"
                }
              ]
            }
          ],
          "id": 6
        }
        """);
    }

    private HttpResponseMessage CaptureTriggerGet(string body)
    {
        TriggerGetPayload = body;
        triggerGetPayloads.Add(body);
        if (body.Contains("cmdb2monitoring", StringComparison.Ordinal)
            && body.Contains("aggregate", StringComparison.Ordinal)
            && body.Contains("suppression_state", StringComparison.Ordinal))
        {
            return EmptyArrayResponse();
        }

        if (body.Contains("CMDB2M suppression", StringComparison.Ordinal)
            && body.Contains("City04", StringComparison.Ordinal))
        {
            return ExistingAggregateTriggerByName ? AggregateTriggerResponse() : EmptyArrayResponse();
        }

        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": [
            {
              "triggerid": "60001",
              "description": "Core router unavailable",
              "status": "0",
              "priority": "4",
              "value": "1",
              "expression": "max(/ctest2-routerCore-002/icmpping,#3)=0",
              "recovery_expression": "",
              "tags": [
                {
                  "tag": "scope",
                  "value": "availability"
                }
              ],
              "hosts": [
                {
                  "hostid": "30011",
                  "host": "ctest2-routerCore-002",
                  "name": "ctest2-routerCore-002"
                }
              ],
              "dependencies": []
            },
            {
              "triggerid": "60002",
              "description": "VPN HUB unavailable",
              "status": "0",
              "priority": "4",
              "value": "0",
              "expression": "max(/ctest2-vpnhub-001/icmpping,#3)=0",
              "recovery_expression": "",
              "tags": [
                {
                  "tag": "component",
                  "value": "health"
                }
              ],
              "hosts": [
                {
                  "hostid": "30012",
                  "host": "ctest2-vpnhub-001",
                  "name": "ctest2-vpnhub-001"
                }
              ],
              "dependencies": [
                {
                  "triggerid": "77777",
                  "description": "manual dependency"
                }
              ]
            }
          ],
          "id": 7
        }
        """);
    }

    private static HttpResponseMessage AggregateTriggerResponse()
    {
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": [
            {
              "triggerid": "45001",
              "description": "CMDB2M suppression: ВПН Хабы / City04 недоступен как группа",
              "status": "0",
              "priority": "3",
              "value": "0",
              "expression": "last(/cmdb2monitoring-suppression-aggregates/cmdb2monitoring.suppression.aggregate.abc)<1",
              "recovery_expression": "",
              "tags": [],
              "hosts": [
                {
                  "hostid": "43001",
                  "host": "cmdb2monitoring-suppression-aggregates",
                  "name": "CMDB2Monitoring suppression aggregates"
                }
              ],
              "dependencies": []
            }
          ],
          "id": 25
        }
        """);
    }

    private HttpResponseMessage CaptureTriggerCreate(string body)
    {
        TriggerCreatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "triggerids": [
              "45001"
            ]
          },
          "id": 23
        }
        """);
    }

    private HttpResponseMessage CaptureTriggerUpdate(string body)
    {
        TriggerUpdatePayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "triggerids": [
              "60002"
            ]
          },
          "id": 8
        }
        """);
    }

    private HttpResponseMessage CaptureHistoryPush(string body)
    {
        HistoryPushPayload = body;
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": {
            "itemids": [
              "44001"
            ]
          },
          "id": 24
        }
        """);
    }

    private static HttpResponseMessage EmptyArrayResponse()
    {
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": [],
          "id": 25
        }
        """);
    }

    private static HttpResponseMessage ExistingServiceResponse()
    {
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": [
            {
              "serviceid": "9001",
              "name": "Рабочие места / City04",
              "algorithm": "2",
              "sortorder": "0",
              "description": "",
              "tags": [
                {
                  "tag": "cmdb2monitoring:managed",
                  "value": "true"
                },
                {
                  "tag": "cmdb2monitoring:layer",
                  "value": "suppression"
                },
                {
                  "tag": "cmdb2monitoring:key",
                  "value": "rule:City04"
                },
                {
                  "tag": "customer:keep",
                  "value": "manual"
                }
              ],
              "children": [
                {
                  "serviceid": "9002",
                  "name": "МаршрутизаторыSupp / City04"
                }
              ],
              "parents": []
            }
          ],
          "id": 2
        }
        """);
    }

    private static HttpResponseMessage ManagedServiceResponse(string serviceId, string managedKey)
    {
        return Json(
            HttpStatusCode.OK,
            $$"""
            {
              "jsonrpc": "2.0",
              "result": [
                {
                  "serviceid": "{{serviceId}}",
                  "name": "{{managedKey}}",
                  "algorithm": "2",
                  "sortorder": "0",
                  "description": "",
                  "tags": [
                    {
                      "tag": "cmdb2monitoring:managed",
                      "value": "true"
                    },
                    {
                      "tag": "cmdb2monitoring:key",
                      "value": "{{managedKey}}"
                    }
                  ],
                  "children": [],
                  "parents": []
                }
              ],
              "id": 2
            }
            """);
    }

    private static HttpResponseMessage ExistingSlaResponse()
    {
        return Json(HttpStatusCode.OK, """
        {
          "jsonrpc": "2.0",
          "result": [
            {
              "slaid": "71001",
              "name": "CMDB2M SLA workplace",
              "period": "2",
              "slo": "99.9",
              "timezone": "Europe/Moscow",
              "status": "0",
              "description": "",
              "service_tags": [
                {
                  "tag": "cmdb2monitoring:sla_policy",
                  "operator": "0",
                  "value": "workplace-99"
                }
              ],
              "schedule": [
                {
                  "period_from": "0",
                  "period_to": "604800"
                }
              ],
              "excluded_downtimes": [
                {
                  "name": "manual one-time change",
                  "period_from": "1777600000",
                  "period_to": "1777603600"
                },
                {
                  "name": "CMDB2M REG:old window",
                  "period_from": "1777610000",
                  "period_to": "1777613600"
                }
              ]
            }
          ],
          "id": 32
        }
        """);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
    }

    private static string JsonStringValue(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }
}
