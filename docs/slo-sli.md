# SLO / SLI Baseline

## Initial SLIs

- Webhook accept latency: `http_request_duration_seconds` on `cmdbwebhooks2kafka`.
- Rule processing failures: `kafka_message_processing_failures_total` on `cmdbconfigbuilder`.
- Apply failures: `kafka_message_processing_failures_total` on `zabbixconfig2api` and `cmdbaggregation2cmdbuild`.
- Dependency health: `/zabbix/check`, `/cmdbuild/check`, `/redis/check`, and `/kafka/topics`.
- Dead letters: `kafka_messages_dead_lettered_total`.

## Initial SLO Targets

- 99 percent of webhook requests complete under 1 second while Kafka is healthy.
- Zero sustained growth in dead-letter messages after acknowledged incidents.
- Configuration reload and manual apply endpoints return 2xx or explicit 4xx/5xx within configured request timeout.

These are baseline targets for dashboards and alerts; tune them after collecting production traffic for at least one full operational cycle.
