# Administration Guide

This guide describes runtime administration for the service/suppression
pipeline:

`CMDBuild webhook -> raw Kafka event -> rule engine -> canonical aggregation commands -> target appliers`.

## Services

| Service | Responsibility |
| --- | --- |
| `cmdbwebhooks2kafka` | Accepts CMDBuild webhooks, normalizes payloads, and publishes `CmdbRawEvent` messages. |
| `cmdbconfigbuilder` | Reads raw events, evaluates conversion rules, and publishes canonical `AggregationCommand` messages to CMDBuild and layer-specific Zabbix topics. |
| `zabbixconfig2api` | Reads separate service/suppression Zabbix topics and applies the Zabbix side with independent status and counters. |
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

## CMDBuild Credentials In The UI

Schema, class, domain, and managed instance views are served through
`monitoring-ui-api`, but the live CMDBuild reads are executed by
`cmdbaggregation2cmdbuild`. The UI BFF forwards the CMDBuild BaseUrl/auth
settings from `monitoring-ui-api` to that microservice for interactive
requests. If those settings are not enough or an operator needs a different
account, the schema page exposes `CMDBuild доступ`: the entered login/password
is kept only in the browser session and is sent as a request-scoped override.
Kafka/automatic processing does not use that browser session; it continues to
use `cmdbaggregation2cmdbuild` configuration or PAM secrets.

## Zabbix Readiness Attribute

The CMDBuild attribute that marks a source object as ready for Zabbix processing
is `zabbix_main_hostid`. The Monitoring UI shows this value in
`Администрирование -> Основные`.

Operational rule:

| Event | `zabbix_main_hostid` | Expected behavior |
| --- | --- | --- |
| `CREATE` | empty | Do not create service/suppression membership yet. Keep the object pending/unready. |
| `CREATE` | present | Treat as ready and produce normal ensure/upsert commands. |
| `UPDATE` | empty, no previous managed membership | Keep pending/unready diagnostics; do not create active membership. |
| `UPDATE` | empty, previous managed membership exists | Remove this source object from managed Zabbix structures and keep pending diagnostics. |
| `UPDATE` | present | Recalculate desired state and apply idempotently. |
| `DELETE` | any | Remove only previously recorded managed memberships; if none exist, no-op. |

This prevents a race with a neighboring monitoring application that receives the
same CMDBuild webhooks and creates or modifies the real Zabbix host. The service
must not add a source object into the service model or suppression dependencies
until the CMDBuild card contains `zabbix_main_hostid`.

## Zabbix Apply Contours

Zabbix application is split by layer:

| Layer | UI menu | Kafka topic setting | Default topic |
| --- | --- | --- | --- |
| Service | `Сервисный слой -> Применить в Zabbix` | `KafkaTopics:ZabbixServiceApplyPlans` | `service-suppression.zabbix.service.apply-plans` |
| Suppression | `Каскадное подавление -> Применить в Zabbix` | `KafkaTopics:ZabbixSuppressionApplyPlans` | `service-suppression.zabbix.suppression.apply-plans` |

The UI evaluates and publishes each layer in two modes:

- `Проверить изменения ...` / `Опубликовать изменения ...` is the routine mode.
  `cmdbconfigbuilder` still builds the current desired graph from CMDBuild cards
  and rules, but `zabbixconfig2api` compares it with the persisted applied graph
  snapshot in `ZabbixApplyState:FilePath` and sends to Zabbix only added or
  changed graph objects. The report shows `desired`, previously applied objects,
  added, changed, unchanged, stale/removed, and `к публикации`.
- `Проверить полный граф ...` / `Опубликовать полный граф ...` intentionally
  replays the whole desired graph. Use it after applier state loss, manual
  Zabbix repair, deployment recovery, or when an operator wants to force a full
  reconciliation.

Each Zabbix apply screen also has `Scope публикации`. Leave it empty for the
whole layer. To publish a smaller part of the graph, enter one or more managed
keys, rule ids, or display names separated by comma/newline. For the service
layer, scope includes the matched service/aggregate, its parent chain, and its
children. For the suppression layer, scope includes the matched object and the
connected relation chain. `Глубина раскрытия=0` means no depth limit; a positive
value limits relation hops. Scope limits Zabbix writes and the direct-applier
diff. `cmdbconfigbuilder` also tries to prefilter preparation before loading
source cards: if a scope key can be matched statically to a rule id, rule name,
template id, target managed key, target card id, or target class/name fields,
it narrows the selected rules and source classes before reading CMDBuild.
Service-layer scope can also match a manual service object. In that case
`cmdbconfigbuilder` reads service objects and their service-object-to-template
or service-object-to-aggregate relations first, converts related aggregate
targets into rule scope keys, and then reduces source-card reading. If the
manual service object has no linked aggregate/template rules, source-card
reading is skipped and only service topology is prepared. If scope still cannot
be matched, preparation falls back to the full rule/card scan and the later
`zabbixconfig2api` graph scope still limits Zabbix writes.
Before starting a long check or publication, press `Проверить scope`: the UI
calls `cmdbconfigbuilder /rules/apply-current/scope-preview`, shows matched
rules/source classes and the same `scope подготовки` counters, but does not
read source cards and does not send commands to Kafka or Zabbix.
The checkbox `Не запускать, если заполненный scope не найден...` is enabled by
default. With a non-empty scope it makes `cmdbconfigbuilder` stop with
`scope_not_matched` before source-card reads when keys match neither static
rule metadata nor manual service objects. Empty scope still means an intentional
full-layer run.

The UI also keeps a local `Scope из последних изменений` hint for each layer.
It is filled when static rules are changed, when managed rule/template
relations are changed, and when `Создать/обновить правила по шаблонам и
связям` materializes changed generated rules. The operator can press
`Подставить scope из последних изменений`, review the keys, run
`Проверить изменения ...`, and then publish. The hint is persisted in the
browser-local journal `cmdb2monitoring.serviceSuppression.zabbixDirtyScope.v1`;
it is not written to conversion configuration files or microservice state and
can be cleared from the UI.

Removed desired objects are reported as stale in changes mode, but are not
deleted from Zabbix automatically. Use the stale managed objects cleanup actions
when deletion is intended. Current implementation reduces Zabbix writes; it
can reduce CMDBuild card scanning only when scope matches rules statically.
When a template/rule deletion removes objects from desired graph, dirty scope
can show the affected old keys, but the actual removal remains an explicit
stale cleanup or full-graph reconciliation step because scoped diff matches
desired targets.

The real publish action first builds the desired graph, validates it, and only
then writes commands. If the graph has blocking errors, for example a visible
service aggregate without parent, a cycle, a conflicting managed key, or a
source-class read/auth error, publication is stopped before any Zabbix write or
Kafka publish. When `monitoring-ui-api` has `backend.zabbixCommandApplyUrl`
configured, the accepted graph is sent directly to
`zabbixconfig2api /commands/apply-graph` and mutates Zabbix immediately. Without
that URL it falls back to writing only to that layer's Zabbix topic. In both
modes it does not write to `KafkaTopics:AggregationCommands`, so CMDBuild
aggregation objects are not touched by these layer-specific Zabbix actions.

For the service layer the same action also publishes manual service topology:
cards of `ServicePlatformService` and their CMDBuild relations to service
aggregates or other services. SLA support cards (`ServiceSlaPolicy`,
`ServiceSlaCalendar`, `ServiceSlaDowntime`) are not created as Zabbix service
nodes; they are used later by the SLA publisher.

Long dry-run and publish actions receive a client operation id. While
`cmdbconfigbuilder` scans CMDBuild cards and builds commands, the UI polls
`/rules/apply-current/progress/{operationId}` through the BFF and shows current
stage, completed source classes, current class card progress, commands built,
commands published, duplicate skips, remaining class/card counts, and recent
errors. The same progress/result payload includes a planned Zabbix object list:
target class/key/name, action, source cards, rules, sample attributes, and
planned relations. For dry-run this is the object set that would be published;
for a real run it is the object set being published to the layer topic. The UI
shows that list page by page and keeps per-object action, attributes, sources,
and relations under expandable details so large plans remain readable.
The same screen exposes `Отменить операцию`; it calls
`/rules/apply-current/cancel/{operationId}` and the backend stops at the next
safe cancellation point.

Service graph publication is ordered top-down. `cmdbconfigbuilder` enriches
commands with `parent_managed_keys` derived from the full desired graph;
when direct Zabbix apply is configured, it sends the accepted graph to
`zabbixconfig2api /commands/apply-graph` instead of streaming one HTTP request
per command. The UI enables publication only after a successful graph check in
the current session, and the backend repeats blocking graph validation before
publishing even if the UI is bypassed. The applier applies the graph in phases:
membership state update,
service node upsert with parents, source leaf upsert with parent, final
children/relation reconciliation, then post-verify of actual Zabbix services.
`zabbixconfig2api` sends those parents to the Zabbix Service API. Existing
streaming webhook commands that do not carry parent information do not clear
existing parents, so routine source-card updates cannot detach already
published aggregates into the Zabbix root. If a service-object-to-template link
is saved, streaming service commands generated by that template also carry the
saved parent managed key.

`zabbixconfig2api` exposes `/apply/status` with separate service and suppression
status blocks. Each block reports the topic, last command,
dry-run/applied/partial/skipped/manual pending/error counters, recent errors and
warnings, and a limited membership snapshot. Service status reports Zabbix
Service objects, relations, source leaf services, host tags, and problem tags.
Suppression status reports membership targets, source membership, and membership
relations that feed aggregate triggers and trigger dependencies.

When `zabbixconfig2api` has `Apply:Mode=auto` or `Apply:AutoApplyEnabled=true`,
published service-layer commands are applied to Zabbix through the Service API.
The applier performs an idempotent upsert of Zabbix Services and marks them with
managed tags:

- `cmdb2monitoring:managed=true`
- `cmdb2monitoring:layer=service|suppression`
- `cmdb2monitoring:class=<CMDBuild target class>`
- `cmdb2monitoring:key=<target idempotency key>`
- optional `cmdb2monitoring:card_id=<CMDBuild card id>` for existing cards
- `cmdb2monitoring:role=root_service|service_group|aggregate|source_leaf|internal`
- `cmdb2monitoring:visibility=root|child|internal`

Expected place for the service model in Zabbix UI: `Monitoring -> Services` /
the service tree pages. Use the tags above or the generated service name to
identify objects created by this system. The display name is operator-owned:
do not encode topology by adding local suffixes such as `(Сервис)`, `Supp`, or
numeric counters. Topology is defined by CMDBuild relations and by the managed
role/visibility tags.

The root of Zabbix Services should contain only business/root service objects,
normally `ServicePlatformService` cards or objects explicitly marked
`cmdb2monitoring:role=root_service`. Generated aggregates are visible child
nodes and source leaf services are internal technical bindings. Service dry-run
shows root services and visible orphan nodes. The stale report additionally
shows root-level source leaves and root-level non-root managed services. Treat
those lists as topology defects: add or fix CMDBuild relations, then rerun
service publication; do not fix them by renaming objects.

`aggregation_type` on service objects is interpreted through Zabbix Services
algorithms, not through the suppression healthy-host trigger formula. In the
current service-tree publication `all` means "parent becomes Problem only when
all direct child services are already Problem". `any` means "any Problem direct
child propagates to the parent". `threshold` and `n_of_m` are preserved and
validated as aggregation policy metadata, but the Zabbix Services tree does not
calculate percentage/count by itself; use a separate aggregate/business trigger
policy when service availability must be "40 of 50 routers are available" or
"20% of workplaces may fail".

`is_critical` is metadata, not a Zabbix severity or service algorithm switch.
It does not change availability calculation, trigger severity, service
`algorithm`, or suppression aggregate formulas. The Zabbix Services term "most
critical" means the most severe current status among child services. It is not
the CMDBuild `is_critical` attribute. When a managed Zabbix service is
published, the attribute is exported only as a service tag,
`cmdb2monitoring:is_critical=<value>`; in other paths treat it as CMDBuild/UI
metadata until explicit impact logic is added.

Manual service-layer objects that are not produced from source aggregation are
created in `Сервисный слой -> Объекты сервиса`. This page is for concrete
CMDBuild cards such as `C2M_ServicePlatformService`, `C2M_ServiceSlaPolicy`,
`C2M_ServiceSlaCalendar`, and `C2M_ServiceSlaDowntime`. It also creates direct
relations between those manual objects: service-to-service dependency,
service-to-aggregate containment, service-to-aggregate dependency,
service-to-SLA policy, SLA policy-to-calendar, and SLA policy-to-regular
downtime. Use it for direct manual links such as `Сервис рабочих мест ->
Ноутбуки` with relation role `Содержит` and `Сервис рабочих мест ->
Маршрутизаторы филиалов` with relation role `Зависит от`. When the service must
link to all aggregates generated by one template, use `Сервис содержит агрегаты
шаблона` or `Сервис зависит от агрегатов шаблона` and select the template once;
the UI expands it to the current generated aggregate cards and creates the
CMDBuild relations in a batch. If the selected template currently has no
generated aggregate cards, the UI saves the link as a pending
service-object-to-template intent in the service template document instead of
blocking the operator. The pending link appears in `Существующая связь` and in
`Визуализация графа связей`; rerun the same action after aggregates are created
to materialize concrete CMDBuild relations. If existing cards belong to an older
template id that is no longer stored in the template file, the selector also
shows `Шаблон из текущих правил`
entries derived from generated rules; use those to link already-created
aggregates without recreating them. Relations where both
sides are templates/rules must remain in `Сервисный слой -> Управление
связями`, so the rule/template lifecycle remains authoritative for generated
objects. In the service-object relation editor the checkbox `Фильтровать правила
и классы из шаблонов` hides generated aggregate cards by default; if a needed
aggregate such as `Рабочие места / City14` is not visible, clear the checkbox.
The dropdown labels include the generating rule/template name so template
aggregates are distinguishable from generic CMDBuild card descriptions. These
manual service objects are saved as CMDBuild cards, not in the
conversion configuration folder; opening the menu refreshes them from CMDBuild
and updates the local CMDBuild cache used after a browser reload. Direct
`Содержит` links from `ServicePlatformService` to service aggregates use
`aggregates_to` domains in reverse CMDBuild orientation: aggregate -> service.
The service schema creates these containment domains for every concrete service
managed aggregate class, for example `ServiceFleetAggregatesToPlatformService`,
`ServiceNetworkAccessZoneAggregatesToPlatformService`, and
`ServiceComputeClusterAggregatesToPlatformService`, so the service schema must
be applied before creating such links.

SLA settings are owned by CMDBuild in the service schema. The class
`C2M_ServiceSlaPolicy` stores reusable SLA policies: `sla_target`,
`reporting_period`, optional legacy/external `calendar`, optional `timezone`,
and optional `zabbix_sla_name`. Service objects link to a policy through the
`has_sla_policy` domain. Operators should create policies such as
`24x7 monthly 99.9` once and link service objects to them; conversion rules may
select the policy, but they should not become the authoritative SLA store.

SLA calendars are separate CMDBuild objects. The class
`C2M_ServiceSlaCalendar` stores reusable calendars such as `24x7`,
`business-days 09:00-18:00 Europe/Moscow`, or a customer office calendar.
`C2M_ServiceSlaPolicy` links to these cards through `has_sla_calendar`.
Use `calendar_code` as the stable key. When the calendar is managed in
CMDBuild, fill the seven weekday fields (`monday_hours` ... `sunday_hours`) in
`HH:mm-HH:mm` format, for example `09:00-18:00`; leave a day empty when it is
outside SLA time. Several intervals may be separated with semicolon, for
example `09:00-13:00;14:00-18:00`. Use `zabbix_calendar_name` or
`external_calendar_id` when publication must bind to an existing external
calendar. If an SLA policy has no `has_sla_calendar` relation, the calendar is
treated as manual/external and the publisher must not create or modify a
managed CMDBuild calendar for that policy. The policy-level `calendar` text
field remains only as compatibility fallback for already created
configurations.

Regular downtime windows are also owned by CMDBuild. The class
`C2M_ServiceSlaDowntime` stores recurring exclusions, for example weekly
maintenance Sunday 02:00 for 120 minutes. `C2M_ServiceSlaPolicy` links to these
cards through `has_regular_downtime`. The Zabbix SLA publisher expands those
windows only inside the configured horizon and preserves manual one-time Zabbix
downtimes: it reads the existing Zabbix SLA, removes or replaces only excluded
downtime entries with the configured managed prefix, then appends the freshly
generated CMDBuild windows. Non-prefixed manual entries are not touched.

The UI page `Администрирование -> SLA` shows the effective settings
from `zabbixconfig2api`: `ZabbixSla:DefaultPolicyKey`,
`ZabbixSla:DowntimePublicationHorizonMonths`,
`ZabbixSla:ManagedExcludedDowntimePrefix`, `ZabbixSla:SampleLimit`, and
`Zabbix:RequestTimeoutMs`. The SLA-owned fields are editable in the panel:
`monitoring-ui-api` writes the allowlisted values to
`src/zabbixconfig2api/appsettings.json` and calls the normal
`zabbixconfig2api` reload endpoint using the shared Bearer token. The same
pattern is used by `Администрирование -> Микросервисы` for the allowlisted
`ZabbixTriggerDependencies` fields and `Zabbix:RequestTimeoutMs`.
`DefaultPolicyKey` is the fallback policy when a service has no explicit
`has_sla_policy` relation. This administration page is only for settings; the
SLA dry-run and publication actions are located in `Сервисный слой -> Применить
в Zabbix` after the `Опубликовать граф сервиса в Zabbix` action.

Use the SLA dry-run in `Сервисный слой -> Применить в Zabbix` before the real
publication. The plan shows how many CMDBuild service cards were scanned, how
many service objects will be tagged with `cmdb2monitoring:sla_policy`, how many
Zabbix SLA definitions will be created or updated, and how many managed
excluded downtime entries will be published. The required order is:

1. Run `Сервисный слой -> Применить в Zabbix -> Опубликовать граф сервиса в Zabbix`.
2. Confirm that the selected service objects already exist in the Zabbix service
   tree and have at least one parent or child. Manual service objects are
   created by the service-model publication step, not by SLA publication.
3. Run `Сервисный слой -> Применить в Zabbix -> Опубликовать SLA в Zabbix`.

The SLA publisher does not create isolated Zabbix Service nodes. It only adds or
updates SLA tags on already published managed Zabbix Services, then creates or
updates the Zabbix SLA objects selected by those tags. If a service object is
missing from Zabbix, or if a previously created service has no `parents` and no
`children`, dry-run reports a blocking topology problem and apply refuses to
publish SLA until the service model is published/reconciled.

For source objects with `zabbix_main_hostid`, the applier also creates a managed
source leaf service. The parent managed service keeps these source leaf services
as children in addition to model relation children. The source leaf service gets
`problem_tags` matching `cmdb2monitoring:source_hostid=<zabbix_main_hostid>`, and
the applier preserves existing Zabbix host tags while adding the same managed
host tag to the Zabbix host. This is the binding that lets Zabbix Services know
which real host problems belong under a managed service object such as
`ВПН филиалов` in the service model.
The source leaf service name is built from human-readable source-card fields
(`zabbix_service_name`, `monitoring_name`, `Code`, `name`, `Description`,
`hostname`) and only falls back to the CMDBuild card id. It must not be built
from the population key value, so services like `NTbook / 177140` indicate an
old publication and should be renamed by the next service apply.
Source leaf services are technical binding nodes, not standalone business
services. They should be children of an aggregate such as
`Рабочие места (Сервис) / City31`, which in turn can be contained by
`Рабочие места филиала`. If a leaf such as `NTbook / ctest2-NTbook-003` is shown
at the root of `Monitoring -> Services`, refresh the stale report in
`Сервисный слой -> Применить в Zabbix`; the report lists root-level source leaf
services separately. Re-run service publication after fixing any missing child
warnings. Missing relation children are warnings, but resolved source leaf
children are still applied to their aggregate.

Suppression is different. By default `Apply:CreateSuppressionServices=false`,
so suppression commands do not create Zabbix Services and do not create source
leaf services/problem tags. They update only persisted suppression membership in
`ZabbixApplyState:FilePath`; the `Зависимости триггеров` reconcile then creates
or updates the technical aggregate host, calculated items, aggregate triggers,
and source-host trigger dependencies. The compatibility flag
`Apply:CreateSuppressionServices=true` restores the old behavior for a lab
transition, but it is not the normal production path.

The membership set is stored by `zabbixconfig2api` in
`ZabbixApplyState:FilePath` so that multiple source cards mapped into the same
target service do not overwrite each other during repeated applies or service
restarts. If a source object has no `zabbix_main_hostid`, it is treated as unready:
dry-run reports the missing binding, and publish removes any previous active
membership for that card instead of adding it to dependencies. The same source card
remains visible in the membership snapshot as `pendingSources`; this is a
diagnostic state, not a Zabbix service child. When a later `UPDATE` or
current-card reconcile sees `zabbix_main_hostid`, the source is moved from
pending into active membership. In the service layer this also creates
source leaf/problem tags; in suppression it only makes the source eligible for
aggregate trigger and dependency calculation.

Membership is reconciled as current state per source card. The stable identity is
`layer + source class + source card id`; dimension fields and `sourceKeyValue`
only decide which target currently owns that source. When an `UPDATE` moves a
source card to another dimension, `zabbixconfig2api` removes the same source
from other targets in the same layer before storing it in the new target. When
the source card no longer matches any rule for a layer, `cmdbconfigbuilder`
publishes `remove_source_membership`; `zabbixconfig2api` then removes that source
from all targets in that layer. The service and suppression layers are isolated,
so a suppression tombstone does not remove service membership for the same
CMDBuild card.

For a clean test run after manually deleting Zabbix objects, distinguish Zabbix
objects from `zabbixconfig2api` state:

- In Zabbix `Data collection -> Hosts`, delete test source hosts and the
  technical host `cmdb2monitoring-suppression-aggregates`.
- In Zabbix `Services -> Services`, delete managed service-tree nodes if the
  tree must be rebuilt from scratch.
- In Zabbix `Services -> SLA`, delete managed SLA definitions when SLA should
  also be rebuilt. If manual one-time downtime entries must be preserved, do
  not delete the whole SLA; delete only managed excluded downtimes with the
  prefix from `ZabbixSla:ManagedExcludedDowntimePrefix`, by default
  `CMDB2M REG:`.
- The local state file `src/zabbixconfig2api/state/zabbixconfig2api/apply-membership.json`
  is not a Zabbix object. It stores membership, pending sources, applied graph
  snapshot, stale diagnostics, and managed dependency history. During a normal
  test reset it can remain in place if the next run uses `Опубликовать полный
  граф ...`: full-graph publication refreshes the desired graph and state.
- Delete `apply-membership.json` only for a deliberately history-free lab run,
  or when the state file is known to be corrupted. Stop `zabbixconfig2api`
  before deleting it, delete the file rather than the whole state directory,
  start the service again, and then run full-graph publication. The file is
  recreated automatically on the next state write.

After manual Zabbix cleanup, use this order from the Monitoring UI:

1. `Сервисный слой -> Применить в Zabbix -> Опубликовать полный граф сервиса`.
2. `Каскадное подавление -> Применить в Zabbix -> Опубликовать полный граф подавления`.
3. `Каскадное подавление -> Применить в Zabbix -> Зависимости триггеров -> Опубликовать зависимости триггеров в Zabbix`.
4. `Сервисный слой -> Применить в Zabbix -> Опубликовать SLA в Zabbix`.

Webhook payloads may omit `zabbix_main_hostid`. `cmdbconfigbuilder` resolves the
configured readiness attribute (`Readiness:ZabbixHostIdAttribute`, default
`zabbix_main_hostid`) from the current CMDBuild card before rule evaluation and
adds it to the source attributes used by commands. Legacy fields such as
`zabbix_hostid` are still accepted by the shared rule engine as fallback values,
but they do not replace the configured readiness attribute.

The semantic deduplication fingerprint includes the resolved readiness value.
This means repeated CMDBuild noise is still suppressed, but a transition from an
unready card to a card with `zabbix_main_hostid` always produces a new desired
Zabbix command.

This binding builds the Zabbix service impact model. Automatic hiding,
acknowledging, or closing of dependent problem events is not performed by a
separate cmdb2monitoring problem applier. The production suppression mechanism
must be expressed in Zabbix itself through aggregate triggers and trigger
dependencies derived from the suppression model.

The `Зависимости триггеров` block in `Каскадное подавление -> Применить в
Zabbix` performs suppression reconciliation. It uses the persisted suppression
membership and relation graph after `Опубликовать граф подавления в Zabbix`:

- command target is treated as the cause/parent object;
- each related target is treated as the dependent/child object;
- suppression relations form a directed acyclic graph, not one mandatory tree.
  There may be several independent suppression chains, several top-level causes,
  branches that split or converge, and chains that do not share any common root.
  The required invariant is causal direction plus no cycles;
- every suppression object gets one managed calculated item and one managed
  aggregate trigger on the technical Zabbix host
  `cmdb2monitoring-suppression-aggregates`;
- `aggregation_type` controls the trigger expression over the calculated
  healthy-host count: `all` fails when not all source hosts are healthy, `any`
  fails only when none of the source hosts is healthy, `threshold` fails when
  healthy percentage is below `threshold`, and `n_of_m` fails when healthy count
  is below `n`. The healthy count is calculated from the aggregate state
  selector, not from every source-host trigger that may later receive a
  dependency. By default this selector takes enabled triggers tagged
  `scope=availability` with priority at least `3`; `component=health` is not
  required. This keeps common ICMP availability triggers usable while
  low-priority secondary symptoms like time/agent side checks do not make the
  suppression group unsupported or incorrectly healthy. The selector must be
  explicit in `ZabbixTriggerDependencies`: if an operator really wants to use
  every enabled trigger for group state, set
  `AggregateStateTriggerIncludeNameRegex=.*` rather than leaving include
  filters empty;
- do not confuse this suppression meaning with Zabbix Services `all/any`:
  suppression `all` means "all selected source hosts must be healthy", while
  service-tree `all` means "all direct child services are already Problem before
  the parent becomes Problem";
- `aggregation_type` is evaluated against source hosts that actually contributed
  at least one selected, supported trigger to the calculated item. Source cards
  with `zabbix_main_hostid` but no selected group-state trigger are reported as
  unknown/skipped in dry-run/apply diagnostics and are not counted as failed
  children. For example, if a workplace group has 12 CMDBuild source cards but
  only 3 source hosts have selected availability triggers, `all` requires those
  3 contributing hosts to be healthy, not all 12 raw membership records;
- the calculated item stores only the object's own healthy source-host count.
  The managed aggregate trigger is the union of the own failure expression and
  upstream aggregate failure expressions. This lets an intermediate group become
  Problem because of a cause above it while its own source hosts are still
  healthy;
- source trigger dependency coverage uses a separate selector. By default it
  selects all enabled triggers of dependent source hosts (`min priority 0`,
  no include tags), because any real Problem on the leaf host should be blocked
  by the nearest suppression group while the group is in Problem;
- `ZabbixTriggerDependencies:TransitiveGroupDependencyDepth` limits how many
  upstream group levels are included into aggregate trigger expressions. The
  default is `2`; valid values are `1..3`. This setting lives only in
  `zabbixconfig2api`; `Администрирование -> Микросервисы` reads it from the
  microservice config file, can update it, and applies the change by calling the
  Bearer-protected configuration reload endpoint. If the value is changed in
  the UI but not applied yet, manual dependency dry-run/apply uses the UI value
  as a one-run override, while automatic reconcile keeps using the saved
  microservice value;
- Zabbix API execution limits are also shown and editable in the same UI panel:
  `Zabbix:RequestTimeoutMs` is the timeout for one JSON-RPC request, and
  `ZabbixTriggerDependencies:TriggerGetBatchSize` is the number of hostid or
  triggerid values sent in one `trigger.get` batch. Defaults are `60000` ms and
  `25`. If dry-run/apply reports a `trigger.get` timeout, lower the batch size
  or raise the timeout in `src/zabbixconfig2api/appsettings.json`, then reload
  or restart `zabbixconfig2api`;
- aggregate formula complexity limits are also enforced before publishing:
  `ZabbixTriggerDependencies:MaxSourceHostsPerAggregate` limits how many
  source-hosts can feed one suppression aggregate, and
  `ZabbixTriggerDependencies:MaxAggregateFormulaLength` limits both the
  calculated item formula and the final aggregate trigger expression. Defaults
  are `1000` hosts and `65000` characters. At 80% of a limit the dry-run shows a
  warning; above the limit dry-run/apply is blocked for that model. Typical
  fixes are narrowing source filters, splitting the template by dimension, or
  lowering transitive depth `N`;
- active triggers of dependent source hosts get dependencies on the aggregate
  trigger of the nearest cause object. Aggregate triggers of groups do not get
  Zabbix trigger dependencies on upstream groups, because that would prevent the
  intermediate group from entering Problem. Group-to-group propagation is
  expressed only inside aggregate trigger expressions up to depth `N`;
- source/leaf triggers do not receive a full matrix of direct dependencies to
  every upper-level cause. Example: `Workplaces -> City routers -> VPN hubs ->
  Core routers` is implemented as leaf-to-nearest-group dependencies plus
  inherited group state from upstream groups within depth `N`;
- direct dependencies from downstream triggers to individual source-host
  triggers are not created. This keeps VPN/failover groups and non-`all`
  aggregations correct;
- existing manual Zabbix dependencies are preserved;
- stale dependencies are removed only if they were recorded as managed by
  `zabbixconfig2api` in `ZabbixApplyState:FilePath`;
- before source triggers are loaded, `zabbixconfig2api` verifies saved
  source-host bindings with Zabbix `host.get`. Dry-run reports stale host
  bindings without changing state. Apply removes source membership records whose
  `zabbix_main_hostid` no longer exists in Zabbix, so old deleted hosts cannot
  keep polluting formulas or diagnostics. Existing hosts that simply have no
  selected triggers are not removed; they remain diagnostic warnings for the
  operator to fix in trigger selectors, templates, or monitoring setup;
- if an aggregate trigger with the expected name already exists on the technical
  aggregate host but is missing managed tags, the applier adopts it by updating
  expression, priority, and tags instead of creating a duplicate trigger.

Operators should run dry-run first. Blocking errors include dependency cycles,
missing membership for related targets, or a dependency count above
`ZabbixTriggerDependencies:MaxDependenciesPerRun`. The same screen reports
aggregate host, calculated item, trigger creation, generated formulas, upstream
cause expressions, state reason (`own`, `upstream`, `own+upstream`, `ok`),
unsupported calculated items, and trigger dependency changes. If
`unsupportedAggregateItemCount` is non-zero, open the dependency details: the UI
shows the target object, item key/id, item state, last value, clock, and Zabbix
error text from the technical host `CMDB2Monitoring suppression aggregates`.
This usually means the aggregate state selector included a trigger expression
that Zabbix cannot use inside a calculated item formula. Runtime state is not
written through `history.push`; after reconcile Zabbix recalculates aggregate
items and trigger states itself.
When a suppression membership command is applied from Kafka, `zabbixconfig2api`
automatically requests the same reconcile with debounce
`ZabbixTriggerDependencies:AutoReconcileDebounceSeconds`. The manual action
remains the operator control for dry-run, forced reconcile after relation/schema
changes, and diagnostics.
`Show suppressed problems` should not be expected to list trigger-dependency
children that never entered Problem state: Zabbix trigger dependencies block the
dependent trigger state change while the parent trigger is in Problem, then
reevaluate the dependent trigger only after the parent clears and a new metric
arrives.

For managed relations the command target is treated as the parent service and
the related target as a child service. If a referenced child service is not
present yet, the parent service is still created/updated and the relation is
reported as a warning with status `partial`; rerun the same publish action after
all referenced objects have been created to complete those deferred links.
For `Применить в Zabbix`, duplicate target commands are collapsed only inside
one current-card operation. A later publish intentionally sends the same desired
objects again so operators can replay Zabbix reconciliation after applier
restart, Kafka offset changes, or temporary Zabbix API failures.

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
  `zabbix_main_hostid` update can still appear in the raw topic, but repeated
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

Before materialization, open `Управление правилами -> Создать/обновить правила
по шаблонам и связям` and run `Проверить шаблоны`. The check loads the needed
source cards, computes `dimension.*`, shows create/update/remove counts for
generated rules and managed relations, and blocks materialization while
template/domain/target errors remain. A source class with successfully loaded
but empty cards is reported only as a warning and does not block
materialization; a missing source field/path still blocks because the template
does not match that class schema. After materialization, use
`Сохранить в папку` before reloading appliers: generated rules, templates, and
their `managed_relations` are persisted as one conversion configuration set.
That action does not execute the rules against existing CMDBuild cards and does
not create service/suppression target cards. Target cards appear after a
matching source-class webhook is processed by the rule engine.

Template deletion is also a two-step operational action. The default mode is
configured in `Администрирование -> Основные` as `Режим удаления шаблона по
умолчанию`; the default is `Удалить созданные правила и объекты`. This removes
generated rules from the configuration and creates `templateDeletionPlans`.
Choose `Отвязать правила и сохранить объекты` only when the operator wants to
stop template ownership but preserve already created CMDBuild objects. In that
mode the former generated rules become detached static-like rules and may be
cleaned up later from `Создать/обновить правила по шаблонам и связям`. The
cleanup control either removes the detached rules while keeping CMDBuild cards
or removes the rules and creates `templateDeletionPlans`. This cleanup control
lists only rules that were generated by a template and detached by the
keep-objects deletion mode; ordinary manual static rules and active generated
rules are not offered there. Plans with pending
status are not executed by folder save or applier reload. The visible
`удаление объектов CMDBuild` block in the template materialization menu enables
`Применить планы удаления в CMDBuild` only when pending plans exist; that
action deletes the matched managed cards through `cmdbaggregation2cmdbuild` and
records per-plan deleted/skipped/error counts.

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
- The base CMDBuild schema is customer-neutral. The schema preview must not
  create `PopulatedFrom<customer-class>` domains from currently loaded rules or
  templates. Such source-link domains are optional installation extensions for
  CMDBuild auditability; runtime membership is calculated from rules and Kafka
  commands.
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
- Target attributes in materialized templates follow the same rule. Service and
  suppression templates may set `is_critical`, `aggregation_type`, `threshold`,
  and `n` when those attributes exist on the selected target class.
  `is_critical` is metadata only: it does not change Zabbix trigger severity,
  service `algorithm`, or suppression aggregate formulas; when a managed Zabbix
  service is published it appears as the service tag
  `cmdb2monitoring:is_critical`.
  `aggregation_type=threshold` requires `threshold` 0..100 and empty `n`;
  `aggregation_type=n_of_m` requires integer `n >= 1` and empty `threshold`.
- `Создать на основе...` is an operator convenience, not a reuse contract. In
  static rules and templates it creates an unsaved draft, copies only editable
  fields, attempts service/suppression target-class mapping, shows copied vs
  dropped/manual fields, and records `derived_from` for audit. The operator
  must still save/apply the draft explicitly. Managed relations are not copied
  with rules or templates.
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
  Service dependency links use internal `relationType=service_depends_on`; the
  standard schema creates a full dependency-domain matrix between concrete
  managed service classes, including custom managed service classes. Explicit
  `member_of` and `aggregates_to` service domains remain for containment
  semantics. For suppression chains where a suppression aggregate must sit above
  another aggregate or resource, for example `МаршрутизаторыSupp / City04 ->
  Рабочие места / City04`, use the relation role `Подавляет`. The standard
  schema creates a full suppression-domain matrix between concrete suppression
  classes, including custom managed suppression classes. Targets of
  `SuppressionNetworkAccessZone` use internal `relationType=depends_on_network`;
  other suppression targets use `relationType=depends_on`. This means pairs
  such as `SuppressionComputeCluster -> SuppressionNetworkAccessZone` and
  `SuppressionProxyGroup -> SuppressionStoragePool` are valid without manual
  one-off domains.
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
- Relation management also has `Создать на основе...`: it creates an unsaved
  relation draft from an existing service/suppression relation, maps endpoints
  to the current layer by `derived_from`, exact id, or matching target class,
  and copies role, description, regex comparison, and generated-rule filters.
  Operators must verify direction and press `Добавить связь`.
- For the service case `Рабочее место филиал`, use relation roles consistently:
  `Сервис рабочих мест -> Ноутбуки` is `Содержит`;
  `Сервис рабочих мест -> Маршрутизаторы филиалов`,
  `Маршрутизаторы филиалов -> ВПН хаб`, and
  `ВПН хаб -> Маршрутизаторы ядра` are `Зависит от`. The resulting model is:
  the workplace service contains notebooks and depends on the network chain
  `Маршрутизаторы филиалов -> ВПН хаб -> Маршрутизаторы ядра`. If a more
  detailed diagnostic layer is required, use `Ноутбуки -> Маршрутизаторы
  филиалов` as `Зависит от`; the service impact is then explained through the
  concrete workplace/notebook layer rather than directly by the router.
  `Рабочее место филиал / City14 -> Рабочие места / City14` is `Содержит`
  because the branch service contains the workplace fleet; `Рабочие места /
  City14 -> Маршрутизаторы филиала / City14`, `Маршрутизаторы филиала / City14
  -> ВПН филиалов`, and `ВПН филиалов -> Маршрутизаторы ядра` are
  `Зависит от` because they describe technical availability causes; links from
  workplaces to AD, DNS, DHCP, VDI, applications, carrier links, or provider
  services are `Использует` when they are external functional supports rather
  than contained service parts. SLA should be attached to the business/service
  aggregate through `has_sla_policy`, not to every endpoint card.
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
