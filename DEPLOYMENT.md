# Deployment Guide

This guide describes how to deploy the .NET services and the UI/BFF without
hardcoded runtime settings.

## Deployment Principles

- Keep runtime settings in external text configuration files, environment
  variables, Docker/Kubernetes secrets, or PAM/AAPM.
- Do not store production passwords, API tokens, Kafka SASL passwords, or ELK
  API keys in git.
- Kafka topics are externally managed. Services must not create topics at
  startup.
- Start with Kafka disabled only for local HTTP dry-run checks. Enable Kafka for
  the real event pipeline.
- TLS is administrator-owned. `Cmdbuild:BaseUrl` and `Zabbix:ApiEndpoint`
  keep the configured `http://` or `https://` scheme; the services do not force
  HTTPS and do not provide certificate-validation bypass switches.

## Prerequisites

Required infrastructure:

- CMDBuild REST API reachable from `cmdbwebhooks2kafka`,
  `cmdbconfigbuilder`, and `cmdbaggregation2cmdbuild`.
- Zabbix JSON-RPC API reachable from `cmdbconfigbuilder` and
  `zabbixconfig2api`.
- Kafka bootstrap servers reachable from all .NET pipeline services.
- `monitoring-ui-api` reachable from `cmdbmodelmaterializer` for
  `conversion-config-store` reads and deploy writes.
- Optional PAM/AAPM endpoint for `secret://...` or `aapm://...` values.
- Optional ELK/log collector. Preferred production pattern is ELK collecting
  from Kafka log topics when `KafkaLogging` is enabled.

## Kafka Topics

Create topics before service startup.

`KafkaTopics:ManagedIdentifier` and `KafkaTopics:ManagedPrefix` identify topics
owned by this solution. The Monitoring UI event browser uses the
`cmdbconfigbuilder` Kafka explorer and shows only configured topics matching
that prefix; foreign customer topics are ignored.

| Topic setting | Default | Producer | Consumers |
| --- | --- | --- | --- |
| `KafkaTopics:CmdbWebhookEvents` | `service-suppression.cmdb.events.raw` | `cmdbwebhooks2kafka` | `cmdbconfigbuilder` |
| `KafkaTopics:AggregationCommands` | `service-suppression.monitoring.aggregation.commands` | `cmdbconfigbuilder` | `cmdbaggregation2cmdbuild` |
| `KafkaTopics:ZabbixServiceApplyPlans` | `service-suppression.zabbix.service.apply-plans` | `cmdbconfigbuilder` | `zabbixconfig2api` service contour |
| `KafkaTopics:ZabbixSuppressionApplyPlans` | `service-suppression.zabbix.suppression.apply-plans` | `cmdbconfigbuilder` | `zabbixconfig2api` suppression contour |
| `KafkaTopics:CmdbModelMissingDimensions` | `service-suppression.cmdb.model.missing-dimensions` | `cmdbconfigbuilder` | `cmdbmodelmaterializer` |
| `KafkaTopics:DeadLetterTopic` | `service-suppression.dlq` | Kafka consumers after bounded retry failure | operators / replay tooling |
| `KafkaTopics:DebugLogs` / `KafkaLogging:Topic` | `service-suppression.logs` | any service with `KafkaLogging:Enabled=true` | ELK/log collector |

Minimal ACLs:

| Service | Write | Read |
| --- | --- | --- |
| `cmdbwebhooks2kafka` | raw event topic, optional log topic | none |
| `cmdbconfigbuilder` | aggregation command topic, Zabbix service/suppression apply topics, missing-dimensions topic, DLQ topic, optional log topic | raw event topic |
| `cmdbmodelmaterializer` | DLQ topic, optional log topic | missing-dimensions topic |
| `zabbixconfig2api` | DLQ topic, optional log topic | Zabbix service/suppression apply topics |
| `cmdbaggregation2cmdbuild` | DLQ topic, optional log topic | aggregation command topic |

## External Configuration

Each service ships an `appsettings.json` with safe defaults and empty secrets.
Override using an external production file or environment variables.

Common sections:

```json
"Kafka": {
  "Enabled": true,
  "BootstrapServers": "kafka:29092",
  "ClientId": "service-name",
  "ConsumerGroupId": "service-name",
  "SecurityProtocol": "Plaintext",
  "SaslMechanism": "Plain",
  "Username": "",
  "Password": "",
  "AutoOffsetReset": "Earliest",
  "DeadLetterEnabled": true,
  "MaxProcessingAttempts": 3,
  "ProcessingRetryDelayMs": 2000
},
"KafkaTopics": {
  "ManagedIdentifier": "cmdb2monitoring-service-suppression",
  "ManagedPrefix": "service-suppression.",
  "CmdbWebhookEvents": "service-suppression.cmdb.events.raw",
  "AggregationCommands": "service-suppression.monitoring.aggregation.commands",
  "ZabbixServiceApplyPlans": "service-suppression.zabbix.service.apply-plans",
  "ZabbixSuppressionApplyPlans": "service-suppression.zabbix.suppression.apply-plans",
  "CmdbModelMissingDimensions": "service-suppression.cmdb.model.missing-dimensions",
  "DeadLetterTopic": "service-suppression.dlq",
  "DebugLogs": "service-suppression.logs"
},
"Debug": {
  "Enabled": false,
  "Level": "Basic"
},
"Readiness": {
  "Route": "/ready",
  "ZabbixHostIdAttribute": "zabbix_main_hostid",
  "CheckExternalDependencies": false,
  "CheckTimeoutMs": 2000
}
```

Environment variable equivalent:

```bash
Kafka__Enabled=true
Kafka__BootstrapServers=kafka:29092
KafkaTopics__CmdbWebhookEvents=service-suppression.cmdb.events.raw
KafkaTopics__AggregationCommands=service-suppression.monitoring.aggregation.commands
KafkaTopics__ZabbixServiceApplyPlans=service-suppression.zabbix.service.apply-plans
KafkaTopics__ZabbixSuppressionApplyPlans=service-suppression.zabbix.suppression.apply-plans
KafkaTopics__CmdbModelMissingDimensions=service-suppression.cmdb.model.missing-dimensions
Readiness__Route=/ready
Readiness__ZabbixHostIdAttribute=zabbix_main_hostid
Readiness__CheckExternalDependencies=false
Readiness__CheckTimeoutMs=2000
Debug__Enabled=false
Debug__Level=Basic
```

Common hardening sections are available in every .NET service:

```json
"AllowedHosts": ["monitoring.example.local"],
"HostValidation": { "Enabled": true },
"TrustedProxies": { "Enabled": true, "Networks": ["10.20.0.0/24"] },
"RateLimiting": {
  "Enabled": true,
  "PermitLimit": 600,
  "WindowSeconds": 60,
  "TrustForwardedFor": true
},
"SecurityHeaders": { "Enabled": true, "HstsEnabled": false },
"Metrics": {
  "Enabled": true,
  "Route": "/metrics",
  "RequireBearerToken": true,
  "BearerTokenSecret": "secret://AAA.LOCAL/PROD/cmdb2monitoring-metrics-token",
  "AllowedNetworks": ["10.30.0.0/24"]
},
"Readiness": { "Route": "/ready" },
"Correlation": { "Enabled": true, "HeaderName": "X-Correlation-Id" },
"Resilience": { "Enabled": true, "MaxAttempts": 3, "CircuitBreakerFailures": 5 }
```

Host validation must include every DNS name that clients and other services use
in the HTTP `Host` header. Trusted proxy networks are the only sources whose
`X-Forwarded-For` value is accepted for rate limiting. Protect `/metrics` with
a Bearer token, an allowlisted scrape network, or both. `/ready` is intended for
orchestrator readiness checks; `/health` remains the lightweight liveness route.
By default `/ready` is shallow and does not call CMDBuild, Zabbix, Redis, Kafka,
or other services. Set `Readiness:CheckExternalDependencies=true` only where the
orchestrator should remove the instance when configured dependencies are not
reachable or when required runtime configuration is incomplete.

Enable `SecurityHeaders:HstsEnabled=true` only behind HTTPS termination.

For CMDBuild and Zabbix outbound connections, choose the protocol by the URL:
`Cmdbuild:BaseUrl=http://...` or `https://...`, and
`Zabbix:ApiEndpoint=http://...` or `https://...`. If HTTPS uses a private CA,
install that CA into the host/container trust store or terminate TLS at an
administrator-managed reverse proxy. If mTLS is required, terminate or inject it
at the platform layer. Do not add `IgnoreCertificateErrors`,
`DangerousAcceptAnyServerCertificateValidator`, or equivalent application-level
bypass behavior.

`zabbix_main_hostid` is the CMDBuild card attribute that marks a source object as
ready for the Zabbix side of the pipeline. `CREATE` and `UPDATE` events without
this value must not create service/suppression membership. `DELETE` must remove
only previously recorded managed membership and otherwise be a no-op. If an
`UPDATE` changes the population dimension or makes the source card stop matching
all rules for a layer, the pipeline publishes source-membership reconciliation:
`zabbixconfig2api` removes that source from stale targets in the same layer
before trigger dependencies are recalculated.

## Service-Specific Settings

### cmdbwebhooks2kafka

```json
"CmdbWebhook": {
  "Route": "/webhooks/cmdbuild",
  "Source": "CMDBuild"
},
"CmdbWebhookNormalization": {
  "EventTypeFields": ["event_type", "eventType", "operation", "action", "type"],
  "ClassCodeFields": ["class_code", "classCode", "class", "_class", "className"],
  "CardIdFields": ["card_id", "cardId", "_id", "id"],
  "AttributeObjectFields": ["attributes", "values", "card", "data"]
}
```

Configure CMDBuild webhook target URL to call:

```text
http://cmdbwebhooks2kafka:5180/webhooks/cmdbuild
```

In `monitoring-ui-api` configuration, explicitly identify the webhook
definitions owned by this project:

```json
"webhooks": {
  "managedIdentifier": "cmdb2monitoring-service-suppression",
  "targetUrl": "http://cmdbwebhooks2kafka:5180/webhooks/cmdbuild",
  "route": "/webhooks/cmdbuild",
  "rawTopic": "service-suppression.cmdb.events.raw",
  "events": [
    { "eventType": "CREATE", "identifier": "cmdb2monitoring-service-suppression" },
    { "eventType": "UPDATE", "identifier": "cmdb2monitoring-service-suppression" },
    { "eventType": "DELETE", "identifier": "cmdb2monitoring-service-suppression" }
  ]
}
```

The Webhooks sync screen counts only managed entries with this identifier, the
configured webhook code prefix, and the configured target URL. This is required
because customer CMDBuild instances can contain webhooks or Kafka topics from
other integrations.

The Webhooks sync screen also provides `Перечитать из CMDBuild`,
`Опубликовать webhooks в CMDBuild`, and `Сверить правила онлайн`. Publication
uses the CMDBuild REST endpoint `services/rest/v3/etl/webhook/` through the
`monitoring-ui-api` `cmdbuild` configuration and creates or updates one
managed webhook per source class/event pair. Online verification must also
reach the live webhooks endpoint through `backend.webhooksCheckUrl`; it does
not validate against a local cache. It compares managed webhook definitions
with source classes referenced by the loaded conversion rules and reports
classes missing `CREATE`, `UPDATE`, or `DELETE` coverage. Class-specific
webhooks should expose a class code in the webhook definition; definitions
without a class code are treated as global for their event type. If webhook
definitions include explicit payload fields (`fields`, `attributeCodes`, or
`payloadFields`), the UI verifies that rule fields are present; definitions
without field lists are treated as full-payload. The comparison is per source
class and event, not per `rule_id`; rule IDs are shown only to help find which
generated rules introduced a source class into the check. Conversion
configuration publication for operators is `Конфигурации конвертации` ->
`Сохранить в папку`; applier services pick it up on configuration reload. This
does not create CMDBuild webhooks.

Configure UI health and event browsing endpoints explicitly:

```json
"backend": {
  "kafkaTopicsUrl": "http://cmdbconfigbuilder:5182/kafka/topics"
},
"healthChecks": [
  { "id": "cmdbwebhooks2kafka", "name": "CMDBuild webhooks -> Kafka", "url": "http://cmdbwebhooks2kafka:5180/health" },
  { "id": "cmdbconfigbuilder", "name": "Rule engine", "url": "http://cmdbconfigbuilder:5182/health" },
  { "id": "cmdbmodelmaterializer", "name": "Model materializer", "url": "http://cmdbmodelmaterializer:5184/health" },
  { "id": "zabbixconfig2api", "name": "Zabbix applier", "url": "http://zabbixconfig2api:5183/health" },
  { "id": "cmdbaggregation2cmdbuild", "name": "CMDBuild applier", "url": "http://cmdbaggregation2cmdbuild:5181/health" }
],
"kafka": {
  "managedIdentifier": "cmdb2monitoring-service-suppression",
  "managedTopicPrefix": "service-suppression.",
  "defaultEventLimit": 5
},
"readiness": {
  "zabbixHostIdAttribute": "zabbix_main_hostid"
},
"conversionConfig": {
  "storageFolder": "state/conversion-config",
  "serviceRulesFile": "service-rules.json",
  "suppressionRulesFile": "suppression-rules.json",
  "serviceTemplatesFile": "service-templates.json",
  "suppressionTemplatesFile": "suppression-templates.json",
  "manifestFile": "manifest.json"
}
```

Mount `conversionConfig.storageFolder` on persistent storage. It contains the
rule and template JSON files saved from the UI and is the planned handoff point
for future Git-backed versioning. Managed links created from the UI are saved as
`managed_relations` inside the rule/template JSON files, so the whole folder
must be moved, backed up, reviewed, and restored as one configuration set.
Template documents may contain `templateVersions` immutable snapshots. Rule
documents may contain `templateApplications` application snapshots and
`templateDeletionPlans`; keep these files together because generated artifact
reconciliation relies on the stored managed keys and fingerprints.

### Configuration reload

`zabbixconfig2api`, `cmdbaggregation2cmdbuild`, and `cmdbmodelmaterializer`
reload external configuration from disk through their configured
`ConfigurationReload:Route`, default `/configuration/reload`. The Monitoring UI
and `cmdbmodelmaterializer` call configured `reloadUrl` values with one shared
Bearer token:

```text
Authorization: Bearer <shared reload token>
```

Use the same token source for the appliers, `cmdbmodelmaterializer`, and the UI
BFF:

```json
// zabbixconfig2api, cmdbaggregation2cmdbuild, and cmdbmodelmaterializer
"ConfigurationReload": {
  "Enabled": true,
  "Route": "/configuration/reload",
  "BearerToken": "",
  "BearerTokenSecret": "secret://AAA.LOCAL/PROD/cmdb2monitoring-reload-token"
},

// monitoring-ui-api
"appliers": {
  "reloadEnabled": true,
  "reloadBearerToken": "",
  "reloadBearerTokenSecret": "secret://AAA.LOCAL/PROD/cmdb2monitoring-reload-token"
},

// cmdbmodelmaterializer
"Materializer": {
  "ReloadAppliersOnSave": true,
  "ReloadTargets": [
    {
      "Name": "zabbixconfig2api",
      "Url": "http://zabbixconfig2api:5183/configuration/reload",
      "BearerToken": "",
      "BearerTokenSecret": "secret://AAA.LOCAL/PROD/cmdb2monitoring-reload-token",
      "Enabled": true
    }
  ]
}
```

Tracked defaults keep reload disabled and token values empty. For local
development a literal value is acceptable, but it still has to be the same in
every reload config. In production prefer the shared PAM/AAPM reference and do
not store the token in git.

## Docker Compose Baseline

Use `.env.example` as the template for local or stand deployment:

```bash
cp .env.example .env
docker compose up --build
```

The Compose file builds all service images, maps ports `5180`-`5184` and `8091`,
and mounts the named volume `cmdb2m-state` into containers so conversion-config
files and SQLite state survive restarts without writing runtime state into the
repository checkout. Kafka, CMDBuild, and Zabbix are external by default and are
addressed through `.env`.

For developer-only inspection you can temporarily replace the named volume with
`./state:/app/state`, but do not commit runtime state files. The GitLab
`tracked_state_guard` job fails if database files, Zabbix apply state, or
`src/zabbixconfig2api/state/` files are tracked.

`zabbixconfig2api` must run as one active writer for a shared state directory.
Do not scale it above one replica until durable/apply state is moved to a shared
transactional backend or leader election is introduced.

### cmdbconfigbuilder

```json
"ConversionRules": {
  "FilePath": "/config/conversion-rules.json",
  "ReloadOnEachEvent": true
},
"SemanticDeduplication": {
  "Enabled": true,
  "WindowSeconds": 3600,
  "MaxEntries": 50000
}
```

The conversion configuration is external JSON generated by the UI or maintained
through change control. In the current deployment the UI writes the shared
configuration folder with `Сохранить в папку`; applier services reread that
folder when operators trigger configuration reload. In the target scheme the
same workflow points at a Git-backed folder instead of a manually managed
runtime publish button.

`SemanticDeduplication` suppresses repeated aggregation commands caused by
CMDBuild feedback webhooks. It does not drop raw webhooks. Increase
`MaxEntries` for large CMDBuild catalogs with many active source cards; reduce
`WindowSeconds` only if operators need repeated identical updates to force a
new command during a short troubleshooting window.

### cmdbmodelmaterializer

```json
"ConversionConfigStore": {
  "BaseUrl": "http://monitoring-ui-api:8091",
  "CurrentPath": "/api/conversion-config-store/current",
  "DeployPath": "/api/conversion-config-store/deploy",
  "TimeoutMs": 10000
},
"Materializer": {
  "Enabled": true,
  "DuplicateTtlSeconds": 3600,
  "MaxWriteAttempts": 3,
  "ReloadAppliersOnSave": true
},
"Replay": {
  "Enabled": true,
  "ReprocessUrl": "http://cmdbconfigbuilder:5182/rules/reprocess-card",
  "BackfillDimensionOnSave": false,
  "MaxBackfillCards": 1000,
  "TimeoutMs": 30000
},
"GraphOverlay": {
  "Enabled": false,
  "ApplyCurrentUrl": "http://cmdbconfigbuilder:5182/rules/apply-current",
  "Targets": [ "zabbix-direct" ],
  "CmdbuildPrefix": "C2M_",
  "ServiceModelRoot": "",
  "SuppressionModelRoot": "",
  "ZabbixCommandApplyUrl": "http://zabbixconfig2api:5183/commands/apply-graph",
  "PublishMode": "changes",
  "TopologyReadMode": "rules",
  "ScopeDepth": 0,
  "RequireScopeMatch": false,
  "DryRun": false,
  "TimeoutMs": 60000
}
```

`cmdbmodelmaterializer` consumes `KafkaTopics:CmdbModelMissingDimensions`.
It serializes work by `layer/templateId/dimensionKey`, reads current rules and
templates from `conversion-config-store`, writes append/update-only generated
rules through the deploy endpoint, and reloads configured appliers after a
successful save. It never deletes generated rules, CMDBuild cards, Zabbix
services, detached generated rules, manual rules, or legacy objects. Runtime
relations are added when their `domain_code` can be inferred from existing
generated sibling rules or explicit relation metadata; otherwise the service
saves the rule and records a warning in `/materializer/status`.

When `Replay:Enabled=true`, the materializer calls
`cmdbconfigbuilder /rules/reprocess-card` after a successful deploy. The
builder reads the source card from CMDBuild, reuses the normal
enrich/build/publish rule-engine path, and publishes ordinary aggregation and
Zabbix commands. Keep `BackfillDimensionOnSave=false` for the default source
card replay. Set it to `true` only for controlled scoped backfill: the builder
will scan the request's source class up to `MaxBackfillCards` and process cards
matching the materialized dimension instead of relying on the original event.

`GraphOverlay:Enabled=false` is the default rollout mode: the UI continues to
show a visible scoped graph overlay action after materialization/replay. When
set to `true`, the materializer calls `ApplyCurrentUrl` with
`buildMode=graph-overlay`, the request layer, and scope keys from the generated
rule id, target key, and dimension value. This automatic step may publish
through Kafka with `Targets=["zabbix"]` or call `ZabbixCommandApplyUrl`
directly with `Targets=["zabbix-direct"]`; it does not traverse source cards.
The default `TopologyReadMode=rules` also builds the Zabbix desired graph from
the scoped runtime rules and their relations instead of reading the full
CMDBuild managed-object catalog. Keep `TopologyReadMode=full` only as a
legacy diagnostic mode, because it reads managed cards and domain relations.

For `Администрирование -> Материализация`, configure
`monitoring-ui-api backend.modelMaterializerStatusUrl` as
`http://cmdbmodelmaterializer:5184/materializer/status` and
`backend.modelMaterializerProcessUrl` as
`http://cmdbmodelmaterializer:5184/materializer/process`. The UI uses these
routes through the BFF to show recent jobs and retry failed missing-dimension
requests, and also reads the conversion-config-store audit endpoint and recent
Kafka events from the missing-dimensions topic.

### zabbixconfig2api

```json
"Zabbix": {
  "ApiEndpoint": "https://zabbix.example.local/api_jsonrpc.php",
  "AuthMode": "Token",
  "ApiToken": "secret://AAA.LOCAL/PROD/zabbix-api-token",
  "User": "",
  "Password": "",
  "RequestTimeoutMs": 60000
},
"Apply": {
  "Mode": "auto",
  "AutoApplyEnabled": true,
  "SafeApply": true
},
"ZabbixApplyState": {
  "FilePath": "state/zabbixconfig2api/apply-membership.json"
},
"ZabbixTriggerDependencies": {
  "Enabled": true,
  "IncludeDisabledTriggers": false,
  "AutoReconcileOnMembershipChange": true,
  "AutoReconcileDebounceSeconds": 10,
  "TransitiveGroupDependencyDepth": 2,
  "TriggerGetBatchSize": 25,
  "MaxSourceHostsPerAggregate": 1000,
  "MaxAggregateFormulaLength": 65000,
  "MaxDependenciesPerRun": 10000,
  "SampleLimit": 100,
  "AggregateHostGroupName": "CMDB2Monitoring",
  "AggregateHostName": "cmdb2monitoring-suppression-aggregates",
  "AggregateHostVisibleName": "CMDB2Monitoring suppression aggregates",
  "AggregateItemKeyPrefix": "cmdb2monitoring.suppression.aggregate",
  "AggregateStateTriggerIncludeTags": [
    { "Tag": "scope", "Value": "availability" }
  ],
  "AggregateStateTriggerExcludeTags": [],
  "AggregateStateTriggerIncludeNameRegex": "",
  "AggregateStateTriggerExcludeNameRegex": "",
  "AggregateStateTriggerMinPriority": 3,
  "DependencyTriggerIncludeTags": [],
  "DependencyTriggerExcludeTags": [],
  "DependencyTriggerIncludeNameRegex": "",
  "DependencyTriggerExcludeNameRegex": "",
  "DependencyTriggerMinPriority": 0,
  "SampleSourceTriggersPerAggregate": 20,
  "AggregateTriggerPriority": 3
}
```

In auto mode `zabbixconfig2api` consumes
`KafkaTopics:ZabbixServiceApplyPlans` and
`KafkaTopics:ZabbixSuppressionApplyPlans`. Service-layer commands upsert managed
Zabbix Services; operators can verify them in Zabbix under `Monitoring ->
Services` by service name or by tags such as `cmdb2monitoring:managed=true` and
`cmdb2monitoring:layer=service`. Suppression commands update persisted
membership for aggregate triggers and trigger dependencies; operators verify
that contour in `Каскадное подавление -> Применить в Zabbix -> Зависимости
триггеров` and on the technical aggregate host.

The apply state file keeps source membership for target services, the set of
managed Zabbix trigger dependencies, and the last applied desired graph
snapshot used by `Опубликовать изменения ...`. Keep this path on durable
storage if `zabbixconfig2api` can restart; without it the service can rebuild
desired dependencies from membership, but it cannot distinguish old managed
trigger dependencies from manual Zabbix dependencies and the next
changes-mode publication behaves like the first run for graph objects.
Membership state is keyed by `layer + source class + source card id`, not by the
population dimension. A source moving between dimensions is moved between target
memberships; if it no longer matches any rule in a layer, a
`remove_source_membership` command removes it from every target in that layer.

The UI `Scope из последних изменений` hint is not part of durable service
state. It is a browser-session helper that proposes scope keys after
rule/template changes; operators can paste it into `Scope публикации`.
Restarting the UI or loading the page again loses that hint, while the
persisted Zabbix apply state still controls the real graph diff. During
current-card apply, `cmdbconfigbuilder` uses the provided scope as an optional
preparation prefilter: statically matched rule/target keys reduce the rule list
and source classes before CMDBuild card reads. For service-layer scope that
matches manual service objects, `cmdbconfigbuilder` resolves
service-object-to-template and service-object-to-aggregate relations first and
uses those related targets as rule scope keys. If no related aggregate/template
rules exist, source-card reads for rules are skipped. Unmatched keys keep the
full preparation scan. Operators can call the same matching logic from the UI
with `Проверить scope`; this preview does not read source cards and does not
publish commands. When the UI sends `RequireZabbixScopeMatch=true`, a
non-empty unmatched scope fails before card reads instead of silently falling
back to a full scan. The pending dirty-scope hint is browser-local journal
state (`cmdb2monitoring.serviceSuppression.zabbixDirtyScope.v1`), not durable
microservice state.

`ZabbixTriggerDependencies` controls the suppression dependency reconciliation:
dry-run and apply read suppression membership, find active triggers for
`zabbix_main_hostid` source hosts, create one managed calculated item and one
managed aggregate trigger per suppression object on `AggregateHostName`, and
update `trigger.dependencies` through `trigger.update`. Runtime state is
calculated by Zabbix; the service does not push aggregate state through
`history.push`.

Two trigger selectors are intentionally separate. `AggregateStateTrigger*`
chooses source-host triggers whose expressions are embedded into the calculated
item and therefore define when a suppression group becomes Problem. The default
uses enabled triggers tagged `scope=availability` with priority at least `3`;
`component=health` is not required.
The aggregation denominator is the number of source hosts whose selected,
supported trigger expressions actually enter the calculated item. Source cards
with host bindings but without selected group-state triggers are reported as
unknown/skipped and do not make the group fail by themselves.
For suppression `aggregation_type`, `all` means the group fails when not all
selected source hosts are healthy, `any` means it fails only when none are
healthy, `threshold` compares the healthy-host percentage, and `n_of_m` compares
the healthy-host count. This is intentionally not the same runtime as the
Zabbix Services tree, where service algorithm `all` raises the parent only when
all direct child services are already Problem.
Keep this selector explicit in configuration: `zabbixconfig2api` validates that
it has include tags or an include-name regex. To intentionally select every
enabled source trigger for group state, set
`AggregateStateTriggerIncludeNameRegex` to `.*`; leaving the selector empty is
treated as a configuration error.
`DependencyTrigger*` chooses dependent leaf/source triggers that receive
dependencies on the nearest suppression group; the default keeps all enabled
triggers with priority `0+`. `TransitiveGroupDependencyDepth` (`1..3`) controls
how many upstream group causes are included into aggregate trigger expressions.
`TriggerGetBatchSize` controls how many hostid/triggerid values are sent in one
Zabbix `trigger.get` request during dry-run/apply. `Zabbix:RequestTimeoutMs`
controls the timeout of each JSON-RPC request; the local default is `60000` ms.
Both values are visible in the Monitoring UI read-only. If reconciliation times
out, reduce `TriggerGetBatchSize` or increase `RequestTimeoutMs`, then reload or
restart `zabbixconfig2api`. `MaxSourceHostsPerAggregate` and
`MaxAggregateFormulaLength` protect Zabbix calculated items and aggregate trigger
expressions from oversized suppression groups; dry-run warns at 80% and blocks
above the configured limits. `MaxDependenciesPerRun` is a guard against
accidental many-to-many explosions caused by broad suppression rules.

### cmdbaggregation2cmdbuild

```json
"Cmdbuild": {
  "BaseUrl": "https://cmdbuild.example.local/cmdbuild/services/rest/v3",
  "AuthMode": "Login",
  "Username": "svc-monitoring",
  "Password": "secret://AAA.LOCAL/PROD/cmdbuild-password",
  "ApiToken": ""
},
"Apply": {
  "Mode": "auto",
  "AutoApplyEnabled": true,
  "SafeApply": true
}
```

`cmdbaggregation2cmdbuild` consumes `KafkaTopics:AggregationCommands` only when
auto-apply is enabled, either by `Apply:AutoApplyEnabled=true` or
`Apply:Mode=auto`. With the safe default `manual`/`false`, use HTTP
`POST /commands/apply` for explicit checks; the Kafka consumer is not started,
so queued commands are not silently acknowledged without CMDBuild changes.

## Secrets and PAM/AAPM

Every sensitive field can be supplied directly in dev or by reference in
production:

```json
"Kafka": {
  "Password": "secret://AAA.LOCAL/PROD/kafka-password"
},
"ElkLogging": {
  "ApiKey": "secret://AAA.LOCAL/PROD/elk-api-key"
}
```

PAM/AAPM bootstrap can be configured in the shared `Secrets` section or through
compatibility environment variables:

```bash
PAMURL=https://pam.example.local
PAMUSERNAME=APP_ACCOUNT
PAMPASSWORD='bootstrap-secret'
```

Companion fields are supported: if a section has `Password` and
`PasswordSecret`, an empty `Password` can be filled from
`secret://<PasswordSecret>`.

## Logging Deployment

### Kafka logs for ELK pickup

Preferred when ELK consumes from Kafka:

```json
"KafkaLogging": {
  "Enabled": true,
  "Topic": "service-suppression.logs",
  "MinimumLevel": "Information",
  "ServiceName": "cmdbconfigbuilder",
  "Environment": "Production"
}
```

### Direct ELK sink

Optional:

```json
"ElkLogging": {
  "Enabled": true,
  "Endpoint": "https://elastic.example.local:9200",
  "Index": "cmdbconfigbuilder-logs",
  "ApiKey": "secret://AAA.LOCAL/PROD/elk-api-key"
}
```

### `monitoring-ui-api` structured logs

The Node.js BFF always writes structured JSON logs to stdout/stderr. Enable
debug diagnostics and external sinks through environment variables:

```bash
MONITORING_UI_DEBUG_ENABLED=true
MONITORING_UI_DEBUG_LEVEL=Basic
MONITORING_UI_LOGGING_REQUIRE_EXTERNAL_SINK=true
MONITORING_UI_KAFKA_LOGGING_ENABLED=true
MONITORING_UI_KAFKA_LOGGING_BOOTSTRAP_SERVERS=kafka:29092
MONITORING_UI_KAFKA_LOGGING_TOPIC=service-suppression.logs
MONITORING_UI_ELK_LOGGING_ENABLED=false
MONITORING_UI_ELK_LOGGING_ENDPOINT=https://elastic.example.local:9200
MONITORING_UI_ELK_LOGGING_API_KEY_SECRET=secret://AAA.LOCAL/PROD/elk-api-key
MONITORING_UI_READINESS_CHECK_EXTERNAL_DEPENDENCIES=false
```

Use `MONITORING_UI_DEBUG_LEVEL=Verbose` only temporarily. Runtime logging masks
fields whose names contain password, token, secret, API key, Authorization,
cookie, or connection string.

### Docker syslog

No service code change is required:

```bash
docker run \
  --log-driver=syslog \
  --log-opt syslog-address=udp://syslog.example.local:514 \
  ...
```

## Startup Order

1. Kafka and externally created topics.
2. CMDBuild and Zabbix endpoints.
3. Optional PAM/AAPM.
4. `cmdbaggregation2cmdbuild`.
5. `zabbixconfig2api`.
6. `monitoring-ui-api`.
7. `cmdbmodelmaterializer`.
8. `cmdbconfigbuilder`.
9. `cmdbwebhooks2kafka`.

The appliers can start before producers because Kafka consumers wait for
messages. `monitoring-ui-api` should be reachable before
`cmdbmodelmaterializer` starts consuming missing-dimension requests, because it
owns `conversion-config-store`.

## Validation Checklist

After deployment:

1. Check each service health route:

```bash
curl -f http://cmdbwebhooks2kafka:5180/health
curl -f http://cmdbconfigbuilder:5182/health
curl -f http://cmdbmodelmaterializer:5184/health
curl -f http://zabbixconfig2api:5183/health
curl -f http://cmdbaggregation2cmdbuild:5181/health
curl -f http://monitoring-ui-api:8091/api/health/services
```

For `cmdbaggregation2cmdbuild`, also check apply mode. `health` can be OK while
Kafka auto-apply is intentionally disabled for manual checks:

```bash
curl -f http://cmdbaggregation2cmdbuild:5181/apply/status
curl -f http://cmdbmodelmaterializer:5184/materializer/status
```

2. Check CMDBuild and Zabbix integration routes where configured:

```bash
curl -f http://cmdbaggregation2cmdbuild:5181/cmdbuild/check
curl -f http://zabbixconfig2api:5183/zabbix/check
```

3. Temporarily enable `Debug:Enabled=true`, `Debug:Level=Basic`.
4. Send one CMDBuild test webhook.
5. Verify one raw event in `KafkaTopics:CmdbWebhookEvents`.
6. Verify one or more canonical commands in
   `KafkaTopics:AggregationCommands`.
7. Open Monitoring UI `События` or call
   `http://cmdbconfigbuilder:5182/kafka/topics/{topic}/events?limit=5` for a
   managed topic.
8. Verify logs in stdout and in `KafkaLogging:Topic` if enabled.
9. Run runtime startup smoke for CI or release candidates:

```bash
./scripts/test-runtime-smoke.sh
```

The smoke starts every service in normal, debug Basic, and debug Verbose modes,
then checks `/health`, `/ready`, and `/metrics` on loopback ports. It disables
Kafka consumers for the smoke run with `Kafka__Enabled=false`.

10. Disable debug after validation unless the rollout plan requires it.

Run repository diagnostic autotests separately from deployment smoke checks:

```bash
./scripts/test-diagnostics.sh
```

Use `LIVE=1 ./scripts/test-diagnostics.sh` only when local CMDBuild and Zabbix
test endpoints are available.

## Rollback

Rollback is configuration-first:

- disable CMDBuild webhook or point it away from `cmdbwebhooks2kafka`;
- stop `cmdbconfigbuilder` to stop producing new commands;
- keep appliers running only if they need to drain already produced commands;
- restore the previous external rules file and service config;
- restart services in startup order.
