# Incident Response Runbook

## Kafka Poison Message

1. Check `KafkaTopics:DeadLetterTopic` for records with `consumer`, `topic`, `partition`, `offset`, `error`, and `correlationId`.
2. Fix the source payload, conversion rules, or downstream dependency.
3. Replay manually only after confirming the original topic offset has already been committed by the consumer.

## CMDBuild or Zabbix Degradation

1. Check `/metrics` for `http_client_retries_total`, `http_client_failures_total`, and `http_client_circuit_breaks_total`.
2. Check `/cmdbuild/check` or `/zabbix/check` from the owning service.
3. Increase request timeout or reduce batch size only if the dependency is healthy but slow.

## Reload Disabled

1. Confirm `ConfigurationReload:Enabled=true` on every target service.
2. Configure the same token through `ConfigurationReload:BearerToken`, `ConfigurationReload:BearerTokenSecret`, `appliers.reloadBearerToken`, or `appliers.reloadBearerTokenSecret`.
3. Re-run `node src/monitoring-ui-api/scripts/validate-config.mjs`.

## zabbixconfig2api State

1. Keep a single active `zabbixconfig2api` writer for one state volume.
2. Back up `state/cmdb2m.db` and `state/zabbixconfig2api/apply-membership.json` before cleanup or migration actions.
3. Use `/runtime-storage/status` and `/apply/status` before and after operational changes.
