# Administration Guide

This guide describes runtime administration for the service/suppression
pipeline:

`CMDBuild webhook -> raw Kafka event -> rule engine -> canonical aggregation commands -> target appliers`.

## Services

| Service | Responsibility |
| --- | --- |
| `cmdbwebhooks2kafka` | Accepts CMDBuild webhooks, normalizes payloads, and publishes `CmdbRawEvent` messages. |
| `cmdbconfigbuilder` | Reads raw events, evaluates conversion rules, and publishes canonical `AggregationCommand` messages. |
| `zabbixconfig2api` | Reads canonical aggregation commands and applies the Zabbix side. |
| `cmdbaggregation2cmdbuild` | Reads canonical aggregation commands and applies CMDBuild aggregation objects and relations. |
| `monitoring-ui-api` | Provides schema management, rule editing, source synchronization, and operator UI. |

## Health Checks

Every .NET service exposes the configured `Service:HealthRoute`, default
`/health`.

Example:

```bash
curl -f http://service-host:5182/health
```

The route returns the service name and `status=ok`. Integration-specific checks
remain separate, for example `/zabbix/check` and `/cmdbuild/check`.

The Monitoring UI `Панель` calls `monitoring-ui-api` `/api/health/services`.
That BFF endpoint checks only services listed in UI `healthChecks`, so the
dashboard inventory remains explicit and can be adjusted per environment.

Example UI config:

```json
"healthChecks": [
  { "id": "cmdbwebhooks2kafka", "name": "CMDBuild webhooks -> Kafka", "url": "http://cmdbwebhooks2kafka:5180/health" },
  { "id": "cmdbconfigbuilder", "name": "Rule engine", "url": "http://cmdbconfigbuilder:5182/health" },
  { "id": "zabbixconfig2api", "name": "Zabbix applier", "url": "http://zabbixconfig2api:5183/health" },
  { "id": "cmdbaggregation2cmdbuild", "name": "CMDBuild applier", "url": "http://cmdbaggregation2cmdbuild:5181/health" }
]
```

## Configuration Reload Token

Reloadable web services use the same Bearer token when the Monitoring UI sends
the configuration reload signal. The current reloadable appliers are
`zabbixconfig2api` and `cmdbaggregation2cmdbuild`; both expose
`ConfigurationReload:Route`, default `/configuration/reload`. The UI calls the
configured `reloadUrl` with:

```text
Authorization: Bearer <shared reload token>
```

Configure one identical value in all three places:

| Component | Setting |
| --- | --- |
| `zabbixconfig2api` | `ConfigurationReload:BearerToken` or `ConfigurationReload:BearerTokenSecret` |
| `cmdbaggregation2cmdbuild` | `ConfigurationReload:BearerToken` or `ConfigurationReload:BearerTokenSecret` |
| `monitoring-ui-api` | `appliers.reloadBearerToken` or `appliers.reloadBearerTokenSecret` |

If PAM/AAPM is used, the three `*Secret` settings must reference the same PAM
account. If literal configuration is used in development, the three literal
tokens must be byte-for-byte identical. A mismatch returns `401` from the
applier and the UI must keep the old running configuration version displayed.

## Zabbix Readiness Attribute

The CMDBuild attribute that marks a source object as ready for Zabbix processing
is `zabbix_hostid`. The Monitoring UI shows this value in
`Администрирование -> Основные`.

Operational rule:

| Event | `zabbix_hostid` | Expected behavior |
| --- | --- | --- |
| `CREATE` | empty | Do not create service/suppression membership yet. Keep the object pending/unready. |
| `CREATE` | present | Treat as ready and produce normal ensure/upsert commands. |
| `UPDATE` | empty, no previous managed membership | No-op. |
| `UPDATE` | empty, previous managed membership exists | Remove this source object from managed Zabbix structures. |
| `UPDATE` | present | Recalculate desired state and apply idempotently. |
| `DELETE` | any | Remove only previously recorded managed memberships; if none exist, no-op. |

This prevents a race with a neighboring monitoring application that receives the
same CMDBuild webhooks and creates or modifies the real Zabbix host. The service
must not add a source object into the service model or suppression dependencies
until the CMDBuild card contains `zabbix_hostid`.

## CMDBuild Webhook Feedback Control

CMDBuild webhooks do not provide reliable origin metadata for distinguishing a
customer edit from a write made by this service. The pipeline therefore keeps
the raw webhook intake complete and suppresses repeated work later, where the
service can compare semantic state.

The active algorithm has two layers:

| Layer | Behavior |
| --- | --- |
| `cmdbwebhooks2kafka` | Publishes every accepted webhook to the raw Kafka topic. It does not drop events by class, user, timestamp, or guessed origin. |
| `cmdbconfigbuilder` | For each matched rule, computes a semantic fingerprint keyed by `rule_id`, source class, source card, and event kind. The fingerprint includes the conversion rule, source fields used by conditions/templates, rendered target idempotency key, and rendered managed attributes. If the same semantic fingerprint is seen again within `SemanticDeduplication:WindowSeconds`, no duplicate aggregation command is published. |
| `cmdbaggregation2cmdbuild` | Before updating an existing managed target card, reads the current card and compares only values it intends to manage. If values already match, it reports `targetAction=unchanged` and skips the CMDBuild `PUT`. Relation creation remains idempotent and duplicate relation responses are treated as skipped. |

Operational consequences:

- A burst such as source card update, relation webhook, and a neighboring
  `zabbix_hostid` update can still appear in the raw topic, but repeated
  semantic duplicates stop before the aggregation-command topic.
- Do not suppress raw webhooks by time window alone; a real user edit can occur
  immediately after an automated write.
- Keep target-object fields deterministic. Updating a timestamp on every
  no-op, for example `last_populated_at`, defeats diff-before-write and creates
  avoidable CMDBuild `UPDATE` webhooks.
- The in-memory deduplication cache is per `cmdbconfigbuilder` process. After a
  restart the first event for a source card is evaluated again, but downstream
  appliers remain idempotent.

Configure deduplication in `cmdbconfigbuilder`:

```json
"SemanticDeduplication": {
  "Enabled": true,
  "WindowSeconds": 3600,
  "MaxEntries": 50000
}
```

## Conversion Configuration Storage

The Monitoring UI persists conversion rules and rule templates through
`monitoring-ui-api`. The storage folder is shown in
`Администрирование -> Основные` and configured in the UI BFF settings:

```json
"conversionConfig": {
  "storageFolder": "state/conversion-config",
  "serviceRulesFile": "service-rules.json",
  "suppressionRulesFile": "suppression-rules.json",
  "serviceTemplatesFile": "service-templates.json",
  "suppressionTemplatesFile": "suppression-templates.json",
  "sharedTemplatesFile": "shared-templates.json",
  "manifestFile": "manifest.json"
}
```

The `Синхронизация с источниками данных -> Конфигурации конвертации` screen has
three explicit actions:

| Action | Result |
| --- | --- |
| `Сохранить в папку` | Validates current service/suppression rule documents, writes service/suppression/shared JSON files with templates and managed relations to the configured server folder, and refreshes the browser cache. |
| `Загрузить из папки` | Reads the configured server folder, applies the rule/template documents and managed relations to the editors and previews, and refreshes the browser cache. |
| `Загрузить локальный кэш` | Restores the last browser IndexedDB snapshot without touching the server folder. |

After `Создать/обновить правила по шаблонам и связям`, use
`Сохранить в папку` before reloading appliers: generated rules, templates, and
their `managed_relations` are persisted as one conversion configuration set.
That action does not execute the rules against existing CMDBuild cards and does
not create service/suppression target cards. Target cards appear only after a
matching source-class webhook is processed by the rule engine, or after an
explicit current-card apply is run from `Верификация и применение ->
Запросить применение текущих карточек`.

The folder is intentionally server-side configuration, not a free browser input.
This keeps write scope controlled and allows the same folder to be mounted from
a volume now or moved under Git control later.

`monitoring-ui-api` is the only writer for this folder. A save request carries
the `baseVersion`/`baseEtag` loaded by the UI; if the manifest changed, the API
returns `409 conversion_config_conflict` and the operator must reload the
folder before saving. Files are written through temporary files and atomic
rename; `manifest.json` is written last and contains `version`, `etag`,
`savedAt`, `writer`, and the file map. Applier services and runtime converters
only read published configuration and must not edit these files directly.

## Rule and Model Editor Operations

The rule/model editor workflow is documented in
[RULES_AND_MODELS_EDITOR_GUIDE.md](RULES_AND_MODELS_EDITOR_GUIDE.md). Keep that
guide available to operators who prepare CMDBuild schemas, create conversion
rules, and materialize auto-population templates.

Administrative constraints for that workflow:

- The Monitoring UI is the control point for schema preview, conversion rule
  editing, template editing, and conversion-config storage.
- The CMDBuild catalog must be synchronized before editing rules or templates;
  the source-class attribute chooser is built from the latest loaded
  `/cmdbuild/classes/schema` catalog.
- When `Source class regex` in a template matches several classes, the UI builds
  one union of available source fields. Duplicate field identifiers are shown
  once. Common inherited attributes appear once if the CMDBuild class schema
  endpoint returns them for the concrete classes.
- The UI does not synthesize inherited source attributes by walking the
  CMDBuild parent chain. If an inherited field is missing from the chooser,
  check the live CMDBuild class schema response and refresh the CMDBuild cache.
- One template produces generated rules for all candidate classes. Selection
  filters from the template are copied to every generated rule, so operators
  should use fields common to all matched classes or split the template into
  narrower regex blocks.
- New materialized templates also require a population dimension. The UI
  expands `candidate source class x dimension value` into static generated
  rules. Dimension values can come from lookup/bool catalogs, source-card
  distinct values, reference/domain paths, regex capture, range/list
  generators, or static lists.
- Population dimension fields are intentionally conditional. Operators first
  select the dimension type; then the UI shows only the fields that affect that
  type. `Source attribute/path` is edited for distinct, lookup, bool, and
  regex-capture dimensions. `Value extraction regex` and capture group are
  edited only for regex capture. `Dimension values/range` is edited only for
  range/list or static-list dimensions. `Selection field` is required for
  range/list and static-list dimensions and is usually the same as the source
  field for regex capture.
- The UI generates `dimension.*` during template materialization.
  `dimension.key` is the stable technical identifier of one generated value,
  `dimension.value` is the value used for comparison, `dimension.name` is the
  rendered display name, and `dimension.regexKey` is an escaped key for regex
  conditions. Inside the dimension name template, `dimension.name` means the
  base display name before rendering. Operators should use `dimension.key` for
  managed/idempotency keys and `dimension.name` for human-readable target
  names.
- The Monitoring UI help block for `dimension.*` includes a live preview of the
  first calculated dimension values and target keys. For distinct-field and
  regex-capture dimensions the UI tries to load the needed candidate and
  reference cards automatically when the preview has enough field information.
- When a population field is a CMDB path, for example
  `locationFloorBuildingCity`, template application loads the intermediate
  reference classes and resolves the final leaf attribute before creating
  generated rules.
- The population `Key template` and `Dimension name template` are advanced
  fields with safe defaults. Keep `${template.id}:${dimension.key}` when all
  matched source classes should share one target object per dimension value.
  Use `${template.id}:${class.code}:${dimension.key}` only when targets must be
  separated per source class. Name templates may use `class.*`, `dimension.*`,
  and `vars.*`, but not `source.*`.
- The unresolved reference/domain population type is diagnostic. It means the
  CMDBuild path traversal stopped on an object link instead of a final leaf
  attribute. Do not publish such a template; refresh the catalog, increase
  recursion depth, or choose a final leaf field.
- Target `name`, initial `description`, and idempotency key in materialized
  templates must use `template.*`, `class.*`, `dimension.*`, and `vars.*`
  values only. They must not use `${source.*}` because no concrete source card
  exists during template application.
- Target attributes in materialized templates follow the same rule. Service
  templates may set `is_critical`, `aggregation_type`, `threshold`, and `n` when
  those attributes exist on the selected target class; suppression templates may
  set `is_critical`. `aggregation_type=threshold` requires `threshold` 0..100
  and empty `n`; `aggregation_type=n_of_m` requires integer `n >= 1` and empty
  `threshold`.
- Template saves create immutable `templateVersions` snapshots. Applying
  templates writes `templateApplications` snapshots and reconciles generated
  artifacts by stable `managed_key` plus `artifact_fingerprint`; unchanged
  rules are preserved without rewriting, changed rules are updated, and
  obsolete rules are moved to `templateDeletionPlans`.
- Template `population_source_key` is an internal source-origin key. The normal
  UI shows it read-only. In materialized templates it follows the dimension
  key template, normally `${template.id}:${dimension.key}`.
- The same ownership rule applies to generated relations between templates and
  relations from a template to a static CMDBuild class/card: version is audit
  metadata only, while create/update/delete decisions are based on managed key
  and payload fingerprint.
- Template-managed links are materialized into ordinary generated-rule
  `relations` before publication. Runtime appliers do not read templates
  directly: `cmdbconfigbuilder` emits target relation instructions in
  `AggregationCommand.target.relations`, and `cmdbaggregation2cmdbuild` creates
  the CMDBuild domain relation after the rule-owned target card is ensured. If
  the related target card is not found by card id or lookup/Code, the relation
  is skipped and will be retried when the source object is processed again.
  For service-layer links between two `ServiceNetworkAccessZone` objects, use
  the standard `ServiceNetworkZoneDependsOnNetworkZone` domain.
- For template-to-template links, operators can filter generated rules on both
  sides before variable matching. The left and right filter blocks use the same
  include/exclude regex semantics as template selection filters: include rows
  are AND, exclude rows subtract matches. These filters only limit candidate
  generated rules; the actual relation pair is still chosen by matching the
  selected source and target template variables, optionally after regex
  extraction. For template-to-rule links, source and target selectors can each
  contain templates and rules, but the UI requires exactly one template and one
  rule. Direction is significant: `template -> rule` materializes relations
  from generated rules to the concrete rule, while `rule -> template`
  materializes relations from the concrete rule to matching generated rules of
  the template. The generated-rule filter is shown on whichever side contains
  the template.
- Use stable attribute `Code` values for source fields. Localized Russian names
  belong in description/help text; the rule engine and UI field identifiers
  rely on stable attribute codes.
- After changing rules or templates, save the conversion configuration through
  the UI, reload appliers with the shared Bearer token, and verify managed
  webhooks online before testing CMDBuild card changes.

## Debug Mode

Debug mode is controlled per service:

```json
"Debug": {
  "Enabled": false,
  "Level": "Basic"
}
```

Supported levels:

| Level | Purpose |
| --- | --- |
| `Basic` | Pipeline milestones, counters, command counts, source class/card ids, and high-level apply decisions. |
| `Verbose` | Basic plus detailed per-rule and per-object diagnostics. Use temporarily because it may contain source object values. |

Environment variable form:

```bash
Debug__Enabled=true
Debug__Level=Basic
```

or:

```bash
Debug__Enabled=true
Debug__Level=Verbose
```

Debug events are written through the normal `ILogger` pipeline at
`Information`. They are not written with `LogDebug`, so operators do not need to
raise global .NET log level to see debug diagnostics.

## Log Routing

The application writes normal .NET logs to stdout/stderr. Additional sinks are
optional and configured externally.

### Docker stdout/stderr

Always available. This is the baseline source for Docker, Kubernetes, or a
platform log collector.

### Kafka log topic

Use this mode when ELK is configured to consume logs from Kafka.

```json
"Kafka": {
  "Enabled": true,
  "BootstrapServers": "kafka:29092",
  "SecurityProtocol": "Plaintext",
  "Username": "",
  "Password": ""
},
"KafkaLogging": {
  "Enabled": true,
  "Topic": "service-suppression.logs",
  "MinimumLevel": "Information",
  "ServiceName": "cmdbconfigbuilder",
  "Environment": "Production"
}
```

Requirements:

- Kafka topics are created and managed externally.
- The service account has `WRITE`/`DESCRIBE` permissions on the log topic.
- `Kafka:Enabled=true` is required for `KafkaLogging`.

### Direct ELK logging

Direct ELK logging is optional. If the target architecture collects logs from
Kafka, keep this disabled and use `KafkaLogging` instead.

```json
"ElkLogging": {
  "Enabled": false,
  "Endpoint": "https://elastic.example.local:9200",
  "Index": "cmdbconfigbuilder-logs",
  "ApiKey": "secret://AAA.LOCAL/PROD/elk-api-key",
  "MinimumLevel": "Information",
  "ServiceName": "cmdbconfigbuilder",
  "Environment": "Production"
}
```

Logging failures in Kafka or ELK sinks are intentionally non-fatal and must not
break the main pipeline.

### Syslog

The services do not implement syslog protocol directly. Use the Docker logging
driver to forward stdout/stderr:

```bash
docker run \
  --log-driver=syslog \
  --log-opt syslog-address=udp://syslog.example.local:514 \
  ...
```

For TCP/TLS syslog use:

```bash
--log-opt syslog-address=tcp://syslog.example.local:514
--log-opt syslog-address=tcp+tls://syslog.example.local:6514
```

## Secrets and Auth Modes

No production credentials should be stored in repository files. Use external
configuration, environment variables, mounted secret files, or PAM/AAPM
references.

Supported auth mode values for CMDBuild and Zabbix:

| AuthMode | Meaning |
| --- | --- |
| `Login` | Username/password from config or secret reference. |
| `Token` | API token from config or secret reference. |
| `IndeedPam` | Secret values are resolved from Indeed PAM/AAPM before options validation. |
| `None` | No authorization header. Intended only for controlled dev/test endpoints. |

Example:

```json
"Cmdbuild": {
  "BaseUrl": "https://cmdbuild.example.local/cmdbuild/services/rest/v3",
  "AuthMode": "Login",
  "Username": "svc-monitoring",
  "Password": "secret://AAA.LOCAL/PROD/cmdbuild-password"
}
```

Example Zabbix token:

```json
"Zabbix": {
  "ApiEndpoint": "https://zabbix.example.local/api_jsonrpc.php",
  "AuthMode": "Token",
  "ApiToken": "secret://AAA.LOCAL/PROD/zabbix-api-token"
}
```

To enable PAM/AAPM, configure the shared `Secrets` section or set compatibility
environment variables:

```bash
PAMURL=https://pam.example.local
PAMUSERNAME=APP_ACCOUNT
PAMPASSWORD='bootstrap-secret'
```

or:

```bash
PAMURL=https://pam.example.local
PAMTOKEN='bootstrap-token'
```

If PAM compatibility variables are present and `Secrets:Provider=None`, the
provider is treated as `IndeedPamAapm`.

## Runtime Operations

Recommended operations:

1. Keep `Debug:Enabled=false` in normal production operation.
2. Enable `Debug:Enabled=true`, `Debug:Level=Basic` during incident triage or
   initial rollout.
3. Use `Verbose` only for short diagnostic windows.
4. Prefer Kafka log sink when ELK collects from Kafka.
5. Do not rotate Kafka topics by deleting them while services are running;
   create new topics externally and update service configuration during a
   controlled restart.
6. Keep rule files and appsettings changes under change control.

## Debug Event Examples

Basic debug events currently include:

- webhook normalized: event id, class code, card id, attribute count;
- rule engine processed: event id, source class/card, command count;
- Zabbix applier accepted: command id, command type, rule id;
- CMDBuild applier accepted: command id, command type, rule id.

These messages are intentionally written as `Information` and include the
`Debug Basic` or `Debug Verbose` prefix in the message.

## Kafka Topic Ownership

Customer Kafka clusters can contain topics owned by CMDBuild, other monitoring
integrations, ELK, or unrelated systems. The service must not browse or expose
all cluster topics.

Managed topics are identified by both an explicit owner and a prefix:

```json
"KafkaTopics": {
  "ManagedIdentifier": "cmdb2monitoring-service-suppression",
  "ManagedPrefix": "service-suppression.",
  "CmdbWebhookEvents": "service-suppression.cmdb.events.raw",
  "AggregationCommands": "service-suppression.monitoring.aggregation.commands",
  "DebugLogs": "service-suppression.logs"
}
```

`cmdbconfigbuilder` exposes `/kafka/topics` and
`/kafka/topics/{topic}/events?limit=5` for the Monitoring UI. These endpoints
return only configured topics that match `KafkaTopics:ManagedPrefix`; a request
for a foreign topic is rejected.

## Webhook Ownership

CMDBuild can contain webhooks, topics, or integration artifacts owned by other
systems. The monitoring service must not infer ownership only from endpoint or
topic names.

The UI/BFF configuration uses an explicit `webhooks.managedIdentifier`. The
Webhooks sync view counts only definitions whose `identifier` matches that
value, whose CMDBuild webhook code matches the configured prefix, and whose URL
matches `webhooks.targetUrl`. Definitions with another owner or target URL are
treated as foreign and ignored by this inventory.

The same Webhooks sync view separates three actions:
`Перечитать из CMDBuild` reloads the live `etl/webhook` inventory,
`Опубликовать webhooks в CMDBuild` creates or updates managed CMDBuild
webhooks for source classes used by the loaded conversion rules, and
`Сверить правила онлайн` compares managed webhook definitions with source
classes used by the currently loaded conversion rules. The check does not use
the browser cache. For each source class the expected event coverage is
`CREATE`, `UPDATE`, and `DELETE`.
If a managed webhook definition has no class code, the UI treats it as a global
webhook covering all classes for that event type. If a managed webhook
definition explicitly lists payload fields, the UI also checks that the fields
required by rule conditions, population dimensions, `${source.*}` mappings, and
idempotency keys are present. A webhook definition without a field list is
treated as full-payload. If the live endpoint does not return class-specific
definitions, keep the managed webhook list in UI configuration up to date;
otherwise the UI can verify endpoint availability online but cannot prove
per-class CMDBuild webhook creation.

This check is not tied to individual conversion rules. The UI groups the
current rules by source class and verifies the managed CMDBuild webhook set for
that class/event combination. Rule IDs in the details are diagnostic only, so
the same source class used by both service and suppression rules still requires
one class-level webhook set, not one webhook per generated rule. Publishing
conversion configuration for operators is `Конфигурации конвертации` ->
`Сохранить в папку`; applier services reread that shared folder on reload, and
the target scheme uses the same model with a Git-backed folder. This save does
not create or update CMDBuild webhook definitions.

When service and suppression rule documents are assembled for runtime reading,
`rule_id` values must be unique in that combined view. Newly generated template
rules include their layer in the ID. For older generated rules that have the
same ID in service and suppression documents, the BFF exposes layer-scoped
runtime IDs such as `service-rule-arm-city04` and
`suppression-rule-arm-city04`; same-layer duplicates are still rejected because
they are ambiguous.

Example:

```json
"webhooks": {
  "managedIdentifier": "cmdb2monitoring-service-suppression",
  "rawTopic": "service-suppression.cmdb.events.raw",
  "events": [
    { "eventType": "CREATE", "identifier": "cmdb2monitoring-service-suppression" },
    { "eventType": "UPDATE", "identifier": "cmdb2monitoring-service-suppression" },
    { "eventType": "DELETE", "identifier": "cmdb2monitoring-service-suppression" }
  ]
}
```
