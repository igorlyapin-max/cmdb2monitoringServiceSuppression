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
  "manifestFile": "manifest.json"
}
```

The `Синхронизация с источниками данных -> Конфигурации конвертации` screen has
three explicit actions:

| Action | Result |
| --- | --- |
| `Сохранить в папку` | Validates current service/suppression rule documents, writes JSON files to the configured server folder, and refreshes the browser cache. |
| `Загрузить из папки` | Reads the configured server folder, applies the rule/template documents to the editors and previews, and refreshes the browser cache. |
| `Загрузить локальный кэш` | Restores the last browser IndexedDB snapshot without touching the server folder. |

The folder is intentionally server-side configuration, not a free browser input.
This keeps write scope controlled and allows the same folder to be mounted from
a volume now or moved under Git control later.

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
value, and reports separate `CREATE`, `UPDATE`, and `DELETE` counts. Definitions
with another identifier are treated as foreign and ignored by this inventory.

The same Webhooks sync view has an online action
`Проверить правила онлайн`. It calls the live webhooks check endpoint and
compares managed webhook definitions with source classes used by the currently
loaded conversion rules. The check does not use the browser cache. For each
source class the expected event coverage is `CREATE`, `UPDATE`, and `DELETE`.
If a managed webhook definition has no class code, the UI treats it as a global
webhook covering all classes for that event type. If the live endpoint does not
return class-specific definitions, keep the managed webhook list in UI
configuration up to date; otherwise the UI can verify endpoint availability
online but cannot prove per-class CMDBuild webhook creation.

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
