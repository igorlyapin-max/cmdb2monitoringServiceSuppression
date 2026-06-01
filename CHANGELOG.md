# Changelog

## Unreleased

- Removed tracked placeholder reload tokens and default integration passwords from service configuration.
- Added shared hardening defaults for rate limiting, security headers, Prometheus-style `/metrics`, correlation ids, and HTTP retry/circuit breaker behavior.
- Added Host header validation, trusted proxy handling, protected metrics options, `/ready`, named Compose state volume defaults, and GitLab CI guardrails for static checks and tracked runtime state.
- Documented administrator-owned HTTP/TLS selection for CMDBuild and Zabbix endpoints without application-level certificate bypass.
- Added bounded Kafka processing retries with dead-letter publishing to `KafkaTopics:DeadLetterTopic`.
- Added Dockerfiles, Docker Compose baseline, `.env.example`, and GitLab CI validation/build/security/image smoke jobs.
- Documented active-passive operation for `zabbixconfig2api` runtime state until a dedicated shared state migration is designed.
