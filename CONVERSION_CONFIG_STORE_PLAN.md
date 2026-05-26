# Conversion Config Store And Auto Materialization Plan

Status legend: `[ ]` not started, `[~]` in progress, `[x]` done.

## Goal

Keep generated monitoring configuration consistent when new aggregation
dimension values appear in CMDBuild, for example a new `Building.City` followed
by `ARM` and `Маршрутизатор` cards for that city. The runtime must be able to
create the missing generated rules/groups and then run a scoped graph overlay
without turning every new city into a manual operator task.

## Stage 1. Conversion Config Store API Over Current Folder

Status: `[x]`

- [x] Introduce one `conversion-config-store` API owner above the current
  `state/conversion-config` folder backend.
- [x] Route UI saves/loads through the store API instead of treating the folder
  as the direct ownership boundary.
- [x] Keep the existing JSON files and `manifest.json` format for compatibility.
- [x] Enforce `baseVersion`/`baseEtag` optimistic concurrency on every write.
- [x] Serialize writes through a store lock.
- [x] Record audit metadata for each write: actor, change type, reason,
  previous version, new version, etag, and saved time.
- [x] Keep old endpoints as compatibility aliases until all clients move.

## Stage 2. PostgreSQL Backend Refactor

Status: `[x]`

- [x] Add a PostgreSQL backend behind the same store interface.
- [x] Store versioned documents, materialization jobs, materialized dimensions,
  locks, and audit records transactionally.
- [x] Use database advisory locks or row locks for materialization/save
  serialization.
- [x] Keep folder export for current appliers during migration.
- [x] Keep Git as authoring/export/audit baseline, not as the write target for
  every runtime-generated city.

## Stage 3. Missing Dimension Detection

Status: `[x]`

- [x] In `cmdbconfigbuilder`, detect source-card events where a template matches
  and a dimension value is calculated but no generated rule exists yet.
- [x] Publish a request to `cmdb.model.missing-dimensions`.
- [x] Use idempotency key `layer/templateId/dimensionKey`.
- [x] Include source class/card, field, dimension value, and reason in the
  event.
- [x] Do not mutate conversion config inside webhook processing.

## Stage 4. Model Materializer Service

Status: `[x]`

- [x] Add `cmdbmodelmaterializer`.
- [x] Consume `cmdb.model.missing-dimensions`.
- [x] Deduplicate and lock by `layer/templateId/dimensionKey`.
- [x] Read current authoring/materialized config through
  `conversion-config-store`.
- [x] Materialize only missing dimensions in append/update-only mode.
- [x] Save generated rules and managed relations through the store API.
- [x] Reload appliers after a successful save.
- [x] Never delete generated rules, CMDBuild cards, Zabbix services, or
  manual/detached/legacy objects in the automatic flow.

Implementation note: runtime relation `domain_code` is inferred from existing
generated sibling rules or explicit relation metadata. If the domain cannot be
derived without a CMDBuild schema lookup, `cmdbmodelmaterializer` still saves the
generated rule and records a warning instead of deleting or guessing.

## Stage 5. Replay And Backfill

Status: `[x]`

- [x] After materialization, replay the source card that produced the missing
  dimension.
- [x] Prefer a dedicated `cmdb.events.reprocess` topic or
  `cmdbconfigbuilder /rules/reprocess-card` API.
- [x] Keep `cmdbconfigbuilder` as the only component that builds aggregation
  commands from source cards.
- [x] Support scoped backfill by dimension when the original source event is too
  old or multiple source cards already exist.

Implementation note: Stage 5 uses `cmdbconfigbuilder /rules/reprocess-card`
instead of a second Kafka topic. `cmdbmodelmaterializer` calls it after a
successful deploy or when a retry sees the dimension already materialized.
`cmdbconfigbuilder` reads the source card from CMDBuild, reuses the same
enrich/build/publish path as the streaming webhook worker, and can run scoped
dimension backfill for one source class when `backfill_dimension=true`.

## Stage 6. Scoped Zabbix Graph Overlay

Status: `[x]`

- [x] After replay creates/updates CMDBuild managed objects and dirty scopes,
  run scoped `graph-overlay` for the affected service/suppression keys.
- [x] Make automatic Zabbix overlay configurable.
- [x] Default first implementation can leave Zabbix overlay as a visible
  operator action while auto materialization and replay are enabled.
- [x] Keep full source traversal out of this automatic path.

Implementation note: `cmdbmodelmaterializer` now has a `GraphOverlay:*`
section. When enabled, it calls `cmdbconfigbuilder /rules/apply-current` with
`buildMode=graph-overlay`, layer from the missing-dimension request, and scope
keys derived from the generated rule id, target key, and dimension value. The
default config keeps `GraphOverlay:Enabled=false`, so operators can still run
the visible scoped graph overlay manually until automatic Zabbix writes are
explicitly enabled for the stand.

## Stage 7. Rule-Based Zabbix Topology Read

Status: `[x]`

- [x] Make `graph-overlay` build desired service/suppression topology from the
  selected runtime rules and their relations by default.
- [x] Keep the old full CMDBuild managed-card/domain-relation topology read as
  explicit `TopologyReadMode=full` for legacy diagnostics.
- [x] Pass `TopologyReadMode=rules` from the materializer and UI BFF.
- [x] Prevent unmatched scoped graph-overlay requests from silently falling
  back to a full graph run.
- [x] Document the operational default and legacy mode.

Implementation note: `ApplyCurrentRulesRequest.TopologyReadMode` defaults to
`auto`; in `graph-overlay` this resolves to `rules`. This avoids reading every
managed service/suppression card and every topology relation from CMDBuild when
only a scoped aggregation dimension changed.

## Stage 8. UI And Operations

Status: `[x]`

- [x] Show materialization jobs, missing dimensions, audit records, and failed
  jobs in the UI.
- [x] Let operators retry failed materialization/replay jobs.
- [x] Show when a graph overlay is pending because an automatic materialization
  completed.
- [x] Document that automatic materialization is create/update only; cleanup is
  still an administrator workflow.

Implementation note: `monitoring-ui-api` now exposes BFF routes
`/api/materializer/status` and `/api/materializer/retry`. The UI screen
`Администрирование -> Материализация` shows recent `cmdbmodelmaterializer`
jobs, failed jobs, original missing-dimension requests for retry, recent
Kafka missing-dimensions events, conversion-config-store audit entries, and
manual graph-overlay pending work when `GraphOverlay:Enabled=false`.

## Completion Summary

- Stage 1 is implemented on the current folder backend.
- Stage 2 is implemented as an optional PostgreSQL backend in
  `monitoring-ui-api`; folder remains the default backend.
- Stage 3 is implemented in `cmdbconfigbuilder`: webhook processing detects
  missing template dimensions and publishes requests to the configured
  `KafkaTopics:CmdbModelMissingDimensions` topic.
- Stage 4 is implemented as `cmdbmodelmaterializer`: it consumes missing
  dimensions, writes append/update-only generated rules through the
  conversion-config-store deploy API, reconciles managed generated-rule
  relations where relation metadata can be inferred, and reloads appliers after
  a successful save.
- Stage 5 is implemented through `cmdbconfigbuilder /rules/reprocess-card` and
  `cmdbmodelmaterializer Replay:*` settings. The materializer replays the
  source card after successful materialization; the same API supports scoped
  source-class backfill by dimension.
- Stage 6 is implemented as optional `cmdbmodelmaterializer GraphOverlay:*`.
  The default is disabled; when enabled it uses scoped `graph-overlay` and does
  not read source cards.
- Stage 7 is implemented in `cmdbconfigbuilder`, `monitoring-ui-api`, and
  `cmdbmodelmaterializer`: graph-overlay topology reads default to scoped
  runtime rules; full CMDBuild managed-catalog reads are legacy diagnostics.
- Stage 8 is implemented in the UI under
  `Администрирование -> Материализация`. It is an operator surface for recent
  materialization jobs, failed retries, missing-dimensions events, store audit,
  and pending graph overlay.

No implementation stages remain in this plan. The next work is operational:
deploy the updated services, enable `GraphOverlay:Enabled=true` only after a
stand-level decision, and run live smoke checks against CMDBuild, Kafka, and
Zabbix.
