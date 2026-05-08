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
| `KafkaTopics:AggregationCommands` | `service-suppression.monitoring.aggregation.commands` | `cmdbconfigbuilder` | `zabbixconfig2api`, `cmdbaggregation2cmdbuild` |
| `KafkaTopics:DebugLogs` / `KafkaLogging:Topic` | `service-suppression.logs` | any service with `KafkaLogging:Enabled=true` | ELK/log collector |

Minimal ACLs:

| Service | Write | Read |
| --- | --- | --- |
| `cmdbwebhooks2kafka` | raw event topic, optional log topic | none |
| `cmdbconfigbuilder` | aggregation command topic, optional log topic | raw event topic |
| `zabbixconfig2api` | optional log topic | aggregation command topic |
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
  "DebugLogs": "service-suppression.logs"
},
"Debug": {
  "Enabled": false,
  "Level": "Basic"
},
"Readiness": {
  "ZabbixHostIdAttribute": "zabbix_hostid"
}
```

Environment variable equivalent:

```bash
Kafka__Enabled=true
Kafka__BootstrapServers=kafka:29092
KafkaTopics__CmdbWebhookEvents=service-suppression.cmdb.events.raw
KafkaTopics__AggregationCommands=service-suppression.monitoring.aggregation.commands
Readiness__ZabbixHostIdAttribute=zabbix_hostid
Debug__Enabled=false
Debug__Level=Basic
```

`zabbix_hostid` is the CMDBuild card attribute that marks a source object as
ready for the Zabbix side of the pipeline. `CREATE` and `UPDATE` events without
this value must not create service/suppression membership. `DELETE` must remove
only previously recorded managed membership and otherwise be a no-op.

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

The Webhooks sync screen counts only entries with this identifier. This is
required because customer CMDBuild instances can contain webhooks or Kafka
topics from other integrations.

The Webhooks sync screen also provides `Проверить правила онлайн`. This action
must reach the live webhooks endpoint through `backend.webhooksCheckUrl`; it
does not validate against a local cache. It compares managed webhook definitions
with source classes referenced by the loaded conversion rules and reports
classes missing `CREATE`, `UPDATE`, or `DELETE` coverage. Class-specific
webhooks should expose a class code in the webhook definition; definitions
without a class code are treated as global for their event type. If
`backend.webhooksCheckUrl` returns only health status, keep the UI
`webhooks.events` inventory synchronized with CMDBuild so the online check can
still compare rules with the managed webhook set after the live endpoint has
been reached.

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
  "zabbixHostIdAttribute": "zabbix_hostid"
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
for future Git-backed versioning.

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
}
```

The rules file is external text JSON generated by the UI or maintained through
change control.

### zabbixconfig2api

```json
"Zabbix": {
  "ApiEndpoint": "https://zabbix.example.local/api_jsonrpc.php",
  "AuthMode": "Token",
  "ApiToken": "secret://AAA.LOCAL/PROD/zabbix-api-token",
  "User": "",
  "Password": ""
}
```

### cmdbaggregation2cmdbuild

```json
"Cmdbuild": {
  "BaseUrl": "https://cmdbuild.example.local/cmdbuild/services/rest/v3",
  "AuthMode": "Login",
  "Username": "svc-monitoring",
  "Password": "secret://AAA.LOCAL/PROD/cmdbuild-password",
  "ApiToken": ""
}
```

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

## Rollback

Rollback is configuration-first:

- disable CMDBuild webhook or point it away from `cmdbwebhooks2kafka`;
- stop `cmdbconfigbuilder` to stop producing new commands;
- keep appliers running only if they need to drain already produced commands;
- restore the previous external rules file and service config;
- restart services in startup order.
