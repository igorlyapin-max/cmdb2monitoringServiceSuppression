# ADR 0002: zabbixconfig2api State Scaling

## Status

Accepted

## Context

`zabbixconfig2api` stores membership and runtime state in SQLite/file-backed storage. Redis is used for runtime coordination, locks, progress, deduplication, and cache, but it is not the source of truth for membership state.

## Decision

- The supported production topology for `zabbixconfig2api` is active-passive with one active writer.
- Docker Compose starts one `zabbixconfig2api` instance.
- Horizontal active-active scaling is deferred until durable/apply state moves to a shared transactional backend or a leader-election design is implemented.

## Consequences

- Operators must not scale `zabbixconfig2api` above one active writer against the same state volume.
- Other stateless or read-mostly services can be scaled after Kafka groups, Redis, and external dependencies are sized.
