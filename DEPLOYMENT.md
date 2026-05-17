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

## Prerequisites

Required infrastructure:

- CMDBuild REST API reachable from `cmdbwebhooks2kafka`,
  `cmdbconfigbuilder`, and `cmdbaggregation2cmdbuild`.
- Zabbix JSON-RPC API reachable from `cmdbconfigbuilder` and
  `zabbixconfig2api`.
- Kafka bootstrap servers reachable from all .NET pipeline services.
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
| `KafkaTopics:DebugLogs` / `KafkaLogging:Topic` | `service-suppression.logs` | any service with `KafkaLogging:Enabled=true` | ELK/log collector |

Minimal ACLs:

| Service | Write | Read |
| --- | --- | --- |
| `cmdbwebhooks2kafka` | raw event topic, optional log topic | none |
| `cmdbconfigbuilder` | aggregation command topic, Zabbix service/suppression apply topics, optional log topic | raw event topic |
| `zabbixconfig2api` | optional log topic | Zabbix service/suppression apply topics |
| `cmdbaggregation2cmdbuild` | optional log topic | aggregation command topic |

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
  "AutoOffsetReset": "Earliest"
},
"KafkaTopics": {
  "ManagedIdentifier": "cmdb2monitoring-service-suppression",
  "ManagedPrefix": "service-suppression.",
  "CmdbWebhookEvents": "service-suppression.cmdb.events.raw",
  "AggregationCommands": "service-suppression.monitoring.aggregation.commands",
  "ZabbixServiceApplyPlans": "service-suppression.zabbix.service.apply-plans",
  "ZabbixSuppressionApplyPlans": "service-suppression.zabbix.suppression.apply-plans",
  "DebugLogs": "service-suppression.logs"
},
"Debug": {
  "Enabled": false,
  "Level": "Basic"
},
"Readiness": {
  "ZabbixHostIdAttribute": "zabbix_main_hostid"
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
Readiness__ZabbixHostIdAttribute=zabbix_main_hostid
Debug__Enabled=false
Debug__Level=Basic
```

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

`zabbixconfig2api` and `cmdbaggregation2cmdbuild` reload external configuration
from disk through their configured `ConfigurationReload:Route`, default
`/configuration/reload`. The Monitoring UI calls each configured `reloadUrl`
with one shared Bearer token:

```text
Authorization: Bearer <shared reload token>
```

Use the same token source for the two appliers and the UI BFF:

```json
// zabbixconfig2api and cmdbaggregation2cmdbuild
"ConfigurationReload": {
  "Route": "/configuration/reload",
  "BearerToken": "",
  "BearerTokenSecret": "secret://AAA.LOCAL/PROD/cmdb2monitoring-reload-token"
},

// monitoring-ui-api
"appliers": {
  "reloadBearerToken": "",
  "reloadBearerTokenSecret": "secret://AAA.LOCAL/PROD/cmdb2monitoring-reload-token"
}
```

For local development a literal value is acceptable, but it still has to be the
same in all three configs. In production prefer the shared PAM/AAPM reference
and do not store the token in git.

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
6. `cmdbconfigbuilder`.
7. `cmdbwebhooks2kafka`.
8. `monitoring-ui-api`.

The appliers can start before producers because Kafka consumers wait for
messages.

## Validation Checklist

After deployment:

1. Check each service health route:

```bash
curl -f http://cmdbwebhooks2kafka:5180/health
curl -f http://cmdbconfigbuilder:5182/health
curl -f http://zabbixconfig2api:5183/health
curl -f http://cmdbaggregation2cmdbuild:5181/health
curl -f http://monitoring-ui-api:8091/api/health/services
```

For `cmdbaggregation2cmdbuild`, also check apply mode. `health` can be OK while
Kafka auto-apply is intentionally disabled for manual checks:

```bash
curl -f http://cmdbaggregation2cmdbuild:5181/apply/status
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
9. Disable debug after validation unless the rollout plan requires it.

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
