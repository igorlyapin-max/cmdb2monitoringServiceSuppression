# CMDBuild to Zabbix Service Suppression

Separate implementation for preparing a customer CMDBuild model and generating
Zabbix suppression dependencies and service tree configuration.

Initial services:

- `cmdbwebhooks2kafka`: receives CMDBuild webhooks and publishes change events.
- `cmdbaggregation2cmdbuild`: prepares CMDBuild schema and derived aggregation objects.
- `cmdbconfigbuilder`: builds desired Zabbix configuration from mapping rules and CMDB events.
- `zabbixconfig2api`: applies approved configuration changes to Zabbix.
- `monitoring-ui-api`: Node.js UI/BFF for schema setup, conversion rules, and manual apply.

Operational documentation:

- [ADMINISTRATION.md](ADMINISTRATION.md): runtime administration, debug levels,
  log routing, Kafka/ELK/syslog behavior, and secret management.
- [DEPLOYMENT.md](DEPLOYMENT.md): deployment prerequisites, external
  configuration, Kafka topics, service startup order, and validation checklist.
- [RULES_AND_MODELS_EDITOR_GUIDE.md](RULES_AND_MODELS_EDITOR_GUIDE.md):
  operator workflow for schema/model preparation, rule editing, population
  templates, synchronization, and diagnostics.

Diagnostic autotests are intentionally launched by a separate command:

```bash
./scripts/test-diagnostics.sh
```

By default it runs fast offline contracts for schema generation, conversion
rules, CMDBuild apply payloads, and the router-core population scenario. Add
`LIVE=1` to include live CMDBuild/Zabbix connectivity checks:

```bash
LIVE=1 ./scripts/test-diagnostics.sh
```

## Runtime pipeline

The .NET services now use a single pipeline contract:

`CMDBuild webhook -> cmdb.events.raw -> rule engine -> layer-specific commands -> target appliers`.

- `cmdbwebhooks2kafka` normalizes the incoming webhook into `CmdbRawEvent` and
  publishes it to `KafkaTopics:CmdbWebhookEvents`.
- `cmdbconfigbuilder` reads raw events, loads the external
  `ConversionRules:FilePath`, evaluates service/suppression rules, and
  publishes canonical `AggregationCommand` messages to
  `KafkaTopics:AggregationCommands` for CMDBuild reconciliation and to
  `KafkaTopics:ZabbixServiceApplyPlans` /
  `KafkaTopics:ZabbixSuppressionApplyPlans` for Zabbix.
- `zabbixconfig2api` reads the service and suppression Zabbix topics
  separately. Each contour has independent dry-run/status/reconcile counters
  and errors.
- `cmdbaggregation2cmdbuild` reads the same canonical command topic and owns
  only CMDBuild aggregation objects and relation reconciliation.

The readiness attribute for Zabbix-managed source objects is `zabbix_main_hostid`.
`CREATE` and `UPDATE` events must be treated as eligible for Zabbix service or
suppression reconciliation only after the CMDBuild card contains this value.
`CREATE` without `zabbix_main_hostid` is a normal intermediate state: the neighboring
monitoring application has not finished creating or binding the Zabbix host yet.
When the value is present, Zabbix application can create a managed source leaf
service and add a matching host tag to the Zabbix host so service `problem_tags`
can bind real host problems into the managed service tree.
`DELETE` must be idempotent and must remove only previously recorded managed
memberships; if the object was never applied by this service, `DELETE` is a
no-op.

The pipeline reduces feedback loops from its own CMDBuild writes without
requiring extra origin data from CMDBuild webhooks. `cmdbwebhooks2kafka` still
publishes every accepted raw webhook for auditability. `cmdbconfigbuilder`
computes a semantic fingerprint per `rule_id + source class + source card` from
the fields that the matched rule actually uses plus the current
`zabbix_main_hostid` readiness value; repeated CREATE/UPDATE events with the
same semantic input do not publish another aggregation command within the
configured deduplication window, but the transition from "no hostid" to "hostid
present" is never hidden by deduplication. `cmdbaggregation2cmdbuild` also reads an
existing managed target card before updating it and skips the CMDBuild `PUT`
when all managed values already match. This prevents relation updates,
idempotent target-card ensures, and neighboring `zabbix_main_hostid` updates from
causing repeated self-triggered apply cycles when the effective rule input did
not change.

Kafka is configured only through external settings: `Kafka`, `KafkaTopics`, and
optional `KafkaLogging`. The services do not create topics at startup. Service
credentials are selected through config `AuthMode`: `Login`, `Token`, `None`,
or `IndeedPam`. `IndeedPam` resolves `secret://...` / `aapm://...` references
from the shared `Secrets` section before options are validated.

Debug logging is controlled by `Debug:Enabled` and `Debug:Level` (`Basic` or
`Verbose`). Debug events are written through normal `ILogger` at `Information`,
so they appear in Docker stdout/stderr, direct ELK logging when `ElkLogging` is
enabled, Kafka log topics when `KafkaLogging` is enabled, and Docker syslog when
the container is started with a syslog logging driver.

The first implemented block is CMDBuild schema preview with separated service and
suppression layers, localized captions/help text, and domains configured to drop
relations when a linked card is deleted. Each layer has its own managed-object
superclass with common attributes; concrete and custom classes only define
layer-specific local attributes. Object criticality is inherited from the
managed superclass. Service aggregation settings are inherited from the service
superclass, while suppression domains carry relation-level priority, source,
and active-state flags.

The schema UI lets the operator choose separate CMDBuild class-model roots for
the service and suppression layers. Empty values are normalized by language:
`/Мониторинг` for `ru`, `/Monitoring` for `en`.
If the class root does not exist, apply creates empty prototype superclasses for
the selected path before creating managed layer superclasses. The generated root
class is shared when service and suppression use the same root path, for example
`C2M_Monitoring` for `/Monitoring`. The two models remain separated below that
root by `C2M_ServiceManagedObject` and `C2M_SuppressionManagedObject`.
CMDBuild model roots are checked by display name under the expected parent; if
multiple prototype superclasses with the same root name exist, apply stops
before creating dependent ordinary classes.
If a managed layer superclass was created earlier directly under CMDBuild
`Class` and the expected model root now exists, apply reuses that existing
superclass instead of failing, because CMDBuild does not reliably move a class
to a different parent after it has been created.
When a schema menu is opened, the UI reads the selected root and then keeps only
classes that belong to this builder model: the layer superclass
(`ServiceManagedObject` or `SuppressionManagedObject`) and its descendants.
Customer-created descendants are shown only when the inherited management
controls are enabled for the class (`managed_by_builder` and
`auto_population_enabled`). Planned classes found in that managed inheritance
tree are marked `Готовы к работе`; planned classes missing from it are marked
`Рекомендовано к созданию`. Existing managed descendants are also shown under
their parent class.
`Готовы к работе` means the class exists under the selected model root and
inherits from the expected managed superclass. A class with the same code under
another parent is a CMDBuild conflict, not a ready class, because CMDBuild does
not reparent classes during apply.
The schema view is split into `Ready classes/domains` and
`Planned classes/domains`. Domains are not shown as a detached list; each
planned or suggested domain is rendered under its source class so
the operator can review the class/domain structure together.
Each class and domain row has an apply checkbox. Planned rows are selected by
default, ready rows can be selected manually and are treated idempotently by
CMDBuild apply. The `Send selected to CMDBuild` action posts the selected
classes and domains to `cmdbaggregation2cmdbuild`; lookup dependencies required
by selected attributes are included automatically. Existing objects are skipped,
and per-object create/skip/fail results are returned to the UI.
The UI reads existing CMDBuild domains by prefix before rendering the schema, so
domains that were already created are shown as ready and are not repeatedly
listed as planned recommendations.
If a class already exists under a different superclass than the generated model
expects, apply fails that class explicitly instead of silently treating it as
ready; the existing CMDBuild class must be manually moved or recreated in the
correct branch.

## Service and suppression aggregation rules

Service and suppression schemas keep aggregation settings on objects, not on
links:

- Every service class inherits from `ServiceManagedObject`; every suppression
  class inherits from `SuppressionManagedObject`.
- Both managed superclasses own the common object attributes plus aggregation
  attributes: `aggregation_type`, `threshold`, and `n`. The common object
  attributes include `population_source_key`, which stores the source key
  copied from the customer CMDBuild card during automated population.
- `ServiceUserEndpointFleet` is the service-model aggregation class for
  populations of similar user endpoints. It should contain managed cards such
  as "all NTbook laptops", "Moscow office notebooks", "call-center thin
  clients", or "VIP endpoints", not servers, network devices, databases,
  storage pools, or platform components. Use it when individual workplace cards
  are too numerous or too low-level to appear directly under a service node.
  Typical grouping keys are location, office, floor/building/city, department,
  owner group, endpoint type, criticality, OS, or the selected source class.
  Each managed card must have stable `Code` and `name`; for example
  `Code=NTbookGroup`, `name=Все ноутбуки`, `aggregation_type=threshold`,
  `threshold=80`.
- `aggregation_type` is a CMDBuild lookup attribute using the
  `ServiceAggregationType` lookup, not a free string. Planned values are
  `all`, `any`, `threshold`, and `n_of_m`.
- `all` means every active child must be available; `any` means one active
  child is enough. Both modes ignore `threshold` and `n`.
- `threshold` is a percentage threshold from 0 to 100 used only by the
  `threshold` aggregation mode. For example, `80` means at least 80% of active
  children must be available. It is a decimal field, so CMDBuild regional
  settings may allow comma or dot as the decimal separator; generated Zabbix
  payloads must normalize both forms to dot notation.
- `n` is an absolute child count used only by the `n_of_m` aggregation mode.
- `is_active=false` keeps the CMDBuild object but excludes it from generated
  Zabbix structures. Inactive service children are not counted in
  `all`/`any`/`threshold`/`n_of_m` aggregation, and relations connected to an
  inactive object are ignored during generation.
- `is_critical=true` does not activate an object and does not create topology by
  itself. It marks stronger impact for generated Zabbix metadata and for
  builder decisions that rank root cause or suppression impact.
- Service and suppression domains describe structure only. They do not duplicate
  `aggregation_type`, `threshold`, or `n`.
- For suppression `trigger dependencies`, every managed suppression object uses
  the same approach: `zabbixconfig2api` creates one managed aggregate trigger
  for the object and one managed calculated item on the technical aggregate
  host. Zabbix calculates the current group state itself from selected
  source-host triggers. By default the selector includes triggers tagged
  `scope=availability` or `component=health`; their expanded Zabbix expressions
  are embedded into the calculated item formula, so ICMP and non-ICMP host
  checks use the same condition that creates the source host Problem.
  Downstream triggers depend on that aggregate trigger, never directly on
  individual source-host triggers. `aggregation_type=all|any|threshold|n_of_m`
  controls the trigger expression over the calculated healthy-host count.
- `ServicePlatformService.service_type` is a `ServiceType` lookup, not a free
  string. Planned values are `business`, `application`, `platform`,
  `integration`, and `infrastructure`; the field is used for grouping and
  reporting, while state calculation remains controlled by `aggregation_type`,
  `threshold`, and `n`.
- `ServicePlatformService.sla_target` is an optional decimal availability
  target for SLA reporting. Values are percentages from 0 to 100, for example
  `99`, `99.5`, `99.9`, `99.95`, or `99.99`; use `99.9` for 99.9%, not
  `0.999`. As with `threshold`, both `99.9` and `99,9` must be accepted from
  CMDBuild input and normalized to `99.9` before sending data toward Zabbix.
- There are no decimal fields in the suppression schema at this stage.
  Suppression relation `priority` is an integer.
- Lookup-driven checks are stored in the CMDBuild attribute `validationRules`
  field as JavaScript, not as separate schema objects. `aggregation_type`
  validates that `threshold`/`n` match the selected aggregation mode, and
  `service_type` validates a filled `sla_target` as a 0..100 percentage.
- Attribute validation examples are in
  `config/cmdbuild-attribute-validation-rules.sample.json`. CMDBuild executes
  this field as JavaScript function body, so examples use top-level `return`
  statements: return `true` for valid input, or return a string to block saving
  and show the validation message.

## Suppression schema rules

Suppression schema is intentionally uniform:

- Every suppression class inherits from `SuppressionManagedObject`.
- `SuppressionManagedObject` owns the common object attributes:
  `name`, `description`, `is_active`, `is_critical`, `managed_by_builder`,
  `auto_population_enabled`, `population_rule_id`, `population_source_key`,
  `last_populated_at`, and `builder_version`.
- Standard suppression classes do not add local attributes.
- Custom suppression classes also inherit the same common attributes and should
  not duplicate source-CMDB descriptive fields.
- `SuppressionProxyGroup` is the only standard exception: it adds the local
  `fallback_supported` attribute because fallback is a property of the proxy
  group, not of a relation.
- Every suppression domain uses the same relation-level attributes:
  `is_active`, `priority`, and `source`.
- Suppression domain attributes describe the relation itself. Object properties
  such as criticality stay on the inherited object attributes.
- Relation `is_active=false` keeps the CMDBuild relation but removes that edge
  from the desired Zabbix suppression/service structure on reconciliation.
- There is no `is_dynamic` flag on managed domains. Membership changes are the
  normal behavior of automated population: if a source object enters or leaves
  an aggregation, the builder creates or removes the relation and reconciles the
  desired Zabbix structure. Attribute changes on the same CMDBuild card, such as
  IP, DNS, hostname, tags, or status, arrive as object `UPDATE` events and are
  handled by recalculating that object and its relations.

## Conversion rule editing

- The UI does not seed demo conversion rules on startup. Without a loaded local
  cache or manually created rules, the service and suppression rule sets start
  empty so sample `Bind ...` rules cannot be mistaken for customer mappings.
  Legacy local caches that still contain the old built-in demo rule ids are
  ignored during rule document normalization.
- In service and suppression modification screens, `Целевой класс\экземпляр
  класса` selects either a managed class or an already existing managed card.
  Selecting a class means the UI must create a new CMDBuild card before saving
  the rule; selecting an existing card means the rule reuses that card and does
  not ask for object attributes again.
- Customer CMDBuild classes are tied to managed service/suppression classes by
  conversion rules, not by manual links in the schema view. Creating a rule is
  the fact that declares which source class population uses for the selected
  target class.
- `Класс-источник` lists customer CMDBuild classes in inheritance-path order:
  superclass branches are grouped first, then their descendants.
- Class selectors and schema lists use the same inheritance-path order. In
  `Целевой класс\экземпляр класса`, each managed class is followed immediately
  by its existing target cards as `Класс -> экземпляр`, so search/filter keeps
  the instance attached to its class.
- `Статические правила` is the manual rule editor for service and suppression
  layers. It is the place where an operator may select either a target class or
  a concrete existing target card; templates select only target classes.
- CMDBuild cache key `cmdbuild.catalogs.v3` stores prototype/superclass nodes
  for sorting, while rule source selection still exposes only non-prototype
  customer classes.
- Rules created against an existing/just-created target card store
  `target.card_id`, `create_instance=false`, and a static CMDBuild idempotency
  key. Legacy/template rules without `target.card_id` can still use
  `create_instance=true`.
- Source object selection is edited as a list of regex conditions, not as a
  single mapped attribute. Each condition chooses an available source attribute
  and either includes matching cards in the group or excludes matching cards
  from it. Include conditions are stored in `when.allRegex`; exclude conditions
  are stored in `when.noneRegex`.
- Rule modification screens show a read-only source identifier helper below
  `Атрибуты целевого объекта`: choose the recursive source attribute on the
  left, then use the generated identifier from the right in regex conditions or
  as `${source.<identifier>}` in runtime placeholders.
- Regex conditions are boolean selectors. Capture groups such as `(prod|dev)`
  or named groups such as `(?<site>msk)` may be used inside the pattern, but
  their captured text is not exported to target fields automatically. Template
  variables and target fields can use whole placeholders such as
  `${class.code}`, `${class.description}`, `${source.<attribute>}`, and
  `${vars.<name>}`. They can also transform already available template values
  with functions such as `extract(...)`, `replace(...)`, `lower(...)`,
  `upper(...)`, `trim(...)`, and `default(...)`.
- `Атрибуты целевого объекта` is shown only when the operator chooses a class,
  not an existing card. The table contains target attribute name, value, and
  help. The UI creates the CMDBuild card with these values, then saves the rule
  pointing to the created card. The created card is added to the local instance
  list so later rules can target the same object or create another one. Only
  attributes that really exist in the selected target class are editable and
  sent to CMDBuild; allowed layer attributes missing from that class are shown
  as a muted note and are not written as synthetic fields.
- In rule modification screens, target object attribute values may use the same
  template expression engine against the selected source class metadata:
  `${class.code}`, `${class.description}`, `${class.hierarchyPath}`,
  `extract(...)`, `replace(...)`, `lower(...)`, `upper(...)`, `trim(...)`, and
  `default(...)`. They cannot use `${source.<attribute>}` because no concrete
  source card is being processed while the target card is created.
- Target object attributes are restricted by the selected target class. Service
  and suppression creation expose
  `name`, `description`, `is_critical`, `aggregation_type`, `threshold`, and
  `n` when these attributes exist; `threshold` is used by
  `aggregation_type=threshold`, and `n` is the N parameter for `n_of_m` while M
  is the current active child count.
- Population template editing exposes the same user-owned target attributes for
  generated aggregate cards: service and suppression templates can set
  `is_critical`, `aggregation_type`, `threshold`, and `n` when these attributes
  exist on the selected target class. The UI only shows attributes present on
  the selected target class. Values are rendered during template
  materialization from `template.*`,
  `class.*`, `dimension.*`, and `vars.*`; `${source.*}` is rejected because no
  concrete source card exists at that stage.
- The source key is derived from the first source-selection condition, or falls
  back to CMDBuild card `_id` when no condition is configured. It is used for
  rule source field metadata and delete/update correlation, not for creating a
  separate target object per source card.

## Population Templates

- Auto-population templates are a separate configuration layer above ordinary
  binding rules. The runtime services still execute ordinary rules; the UI
  materializes templates into rules marked with `generated_from_template`.
- A template selects candidate customer source classes with a regex matched
  against class code, display name, and inheritance path. This is intended for
  repeated service/suppression structures where hundreds of source classes have
  the same pattern.
- Templates use the same include/exclude regex condition model as manual
  rules. The attribute chooser is built from the source classes matched by the
  template class regex.
- If the template class regex matches several classes, the selection-condition
  attribute chooser is built as a union of all matched class fields. The UI
  takes readable active attributes for each candidate class, expands reference
  and domain paths into leaf field identifiers, then removes duplicates by the
  normalized field identifier. Common inherited fields therefore appear once,
  not once per class.
- The UI does not currently walk the CMDBuild parent chain to add inherited
  attributes by itself. Inherited attributes are available only when the
  CMDBuild `/classes/{class}/attributes` catalog returns them for the concrete
  class. If expected inherited fields are absent, refresh the CMDBuild catalog
  and check the class schema endpoint before changing the rule.
- Selection filters stored in one template are applied to every generated rule
  for every candidate class. Prefer fields that exist for all classes selected
  by the regex; split the template into separate regex blocks when notebooks,
  thin clients, and workstations need different field sets.
- New templates use a population dimension. The UI materializes
  `candidate source class x dimension value` into ordinary static rules. For a
  city template, one regex can select `NTBook`, `ThinClient`, and `ARM`, while
  the dimension values create one generated rule per class/city pair.
- Dimension types cover bounded catalogs and generated sets: distinct source
  field values, lookup values, bool, reference/domain paths, regex capture
  from a source field, range/list generators such as `00-99`, static lists
  with `key|name|condition-regex`, and a legacy one-rule-per-class mode.
- Operators should fill population fields from top to bottom. First choose the
  template `Source class regex` and refresh the CMDBuild catalog, then choose
  the dimension type. The UI hides fields that do not participate in the
  selected type; hidden fields are not stored in the template.
- `dimension.*` values are generated by the population step. `dimension.key` is
  the stable technical key for one dimension value: lookup code, bool
  `true/false`, regex capture, distinct value, range value, or the left side of
  `key|name|condition-regex`. `dimension.value` is the comparison value,
  usually the same string as `dimension.key`. `dimension.name` is the rendered
  display name after the `Dimension name template`; inside that same template
  it means the base display name before rendering. `dimension.regexKey` is
  `dimension.key` escaped for embedding into a generated regex condition.
- The template editor shows a live `dimension.*` preview inside the
  `What is dimension.*` help block. It renders the first calculated
  `dimension.key`, `dimension.value`, `dimension.name`, `dimension.regexKey`,
  and target key. Distinct-field and regex-capture previews depend on the
  source-card cache; the UI tries to load the needed candidate and reference
  cards automatically when the preview has enough field information.
- During template application, CMDB path fields used by population dimensions
  also load intermediate reference classes and resolve the final leaf value.
  For example, `locationFloorBuildingCity` is read through a path such as
  `ARM.Location.Floor.Building.City`, not as a direct source-card property.
- Population dimension field ownership:
  - `Type`: operator-selected source of dimension values.
  - `Source attribute/path`: operator-selected final leaf source field for
    distinct, lookup, bool, and regex-capture dimensions. Reference/domain
    paths are offered by their final leaf type: lookup leaves in lookup values,
    boolean leaves in bool values, scalar leaves in distinct/regex.
  - `Value extraction regex` and `Group -> dimension.key`: used only by
    regex-capture dimensions, for example
    `(?i)^w([0-9]{2})[0-9]{2}-.*$` with group `1`, or a named group such as
    `city`. The selected capture group becomes `dimension.key`.
  - `Dimension values/range`: used only by range/list and static-list
    dimensions. Ranges use `00-99`; static rows use
    `key|name|condition-regex`, where `key` becomes `dimension.key`, `name`
    becomes the base display name, and the condition regex is optional.
  - `Key template`: usually left as `${template.id}:${dimension.key}`. Use
    `${template.id}:${class.code}:${dimension.key}` only when the target object
    must be separate for each source class and dimension value.
  - `Dimension name template`: usually `${dimension.value}` or
    `${dimension.name}`. It may include `class.*`, `dimension.*`, and `vars.*`,
    but not `source.*`.
  - `Source-card selection field` and `selection regex`: used by
    regex-capture, range/list, and static-list dimensions to build the
    generated rule condition. If the regex is empty, the UI creates an exact
    match against the dimension value.
  - `Rule limit`: an operator safety limit for cardinality explosions; increase
    it only when the expected number of generated rules is intentional.
- The unresolved reference/domain type is diagnostic only; templates that stop
  on an object link instead of a final leaf attribute must not be saved until
  recursion depth or the selected path is corrected.
- Materialized template fields can use `${template.id}`, `${template.name}`,
  `${class.code}`, `${class.description}`, `${class.hierarchyPath}`,
  `${dimension.key}`, `${dimension.value}`, `${dimension.name}`,
  `${dimension.regexKey}`, and `${vars.<name>}`. They cannot use
  `${source.<attribute>}` for target `name`, initial `description`, or
  idempotency key because no concrete source card is being processed while the
  static generated rule is created.
- `population_source_key` is an internal managed target key used for
  correlation, diagnostics, and reconcile/delete handling. In materialized
  templates it follows the dimension key template, normally
  `${template.id}:${dimension.key}`. Operators should not edit it directly.
- Source placeholders for reference, lookup, and domain attributes use the
  generated field names from the source attribute chooser. Examples:
  `${source.ownerEmail}` for reference path `Owner -> Email`,
  `${source.status}` for lookup `Status`,
  `${source.domainNetworkSegmentCode}` for domain path
  `NetworkSegment -> Code`, and `${source.domainServiceOwnerEmail}` for
  domain path `Service -> Owner -> Email`.
- Template variables and target fields support transform/extract expressions:
  `extract(value, regex, group, fallback)`, `replace(value, regex, replacement,
  flags)`, `lower(value)`, `upper(value)`, `trim(value)`, and
  `default(value, fallback)`. These functions run when the UI applies the
  template and therefore can read `template.*`, `class.*`, `dimension.*`, and
  previously rendered `vars.*`. For regex-capture dimensions, source-card
  values are read before rule generation and the captured group becomes
  `dimension.key`.
- Keep this one-line example visible for operators when explaining templates:
  `${class.code}` · `${class.description}` · `${class.hierarchyPath}` ·
  `${vars.site}` · `${dimension.key}` · `${dimension.name}` ·
  `${extract(class.code, "^C2M_(.+)$", 1)}` ·
  `${extract(class.description, "^(?<site>[A-Z]{3}) - .*$", "site", "unknown")}` ·
  `${replace(class.code, "^C2M_", "")}` ·
  `${replace(class.code, "^([A-Z]+)-([0-9]+)$", "$1_$2")}` ·
  `${lower(trim(vars.site))}`.
- Applying templates reconciles desired generated rules with the previously
  applied state. Manual rules and detached generated rules are preserved.
  Generated artifacts are matched by stable `managed_key`; their actual
  payload is compared by `artifact_fingerprint`. Unchanged artifacts are kept
  byte-for-byte, changed artifacts are updated, new artifacts are created, and
  artifacts no longer produced by the template are removed from the rule set
  and added to `templateDeletionPlans`.
- Template saves produce distinguishable immutable version snapshots in
  `templateVersions`. Template application writes `templateApplications` to the
  rule document with the applied template version, content hash, matched
  source classes, generated managed keys, and reconcile counts. The template
  version is trace metadata; reconcile decisions use stable managed keys and
  fingerprints, not the version number alone.
- Generated rules and target objects carry template origin and ownership
  metadata: `generated_from_template`, `template_generation`,
  `template_generation.managed_key`, `template_generation.artifact_fingerprint`,
  and `target.created_by_template`. This makes it possible to detach a single
  rule from a template while preserving the target object.
- Deleting a template has two UI modes: detach generated rules and keep target
  objects, or remove generated rules and add their target objects to
  `templateDeletionPlans` for manual deletion handling together with generated
  relations. Changing a template regex, target, variables, or filters is an
  in-place version change followed by reconcile: only missing, changed, or
  obsolete managed artifacts are touched.
- Relations created between templates, and relations between a template and a
  static CMDBuild class/card, must use the same ownership contract as generated
  rules: stable `managed_key` without template version, payload
  `artifact_fingerprint`, template version/content hash only as trace metadata,
  and deletion/detach through the same reconcile/deletion-plan flow.
- Template-managed relations are materialized into generated-rule `relations`.
  At runtime, `cmdbconfigbuilder` renders those relations into
  `AggregationCommand.target.relations`, and `cmdbaggregation2cmdbuild` creates
  the CMDBuild domain relation after ensuring the rule-owned target card. The
  related target object is resolved by card id first and then by generated
  lookup/Code; if it does not exist yet, the relation is skipped until the
  source object is processed again. Service-layer dependency links use
  `relationType=service_depends_on`. The service schema creates a full matrix
  of dependency domains between concrete managed service classes, including
  custom managed service classes; explicit `member_of` and `aggregates_to`
  domains remain for containment semantics. Suppression chains where one
  suppression aggregate must suppress another aggregate or generated resources,
  for example `МаршрутизаторыSupp / City04 -> Рабочие места / City04`, use the
  `Подавляет` role. The suppression schema creates a full matrix of domains
  between concrete suppression classes, including custom managed suppression
  classes. Targets of `SuppressionNetworkAccessZone` use
  `relationType=depends_on_network`; other suppression targets use
  `relationType=depends_on`. This lets `SuppressionComputeCluster ->
  SuppressionNetworkAccessZone`, `SuppressionStoragePool -> SuppressionResource`
  and similar pairs be represented without adding one-off domains.
- Template-to-template relation editing has two optional generated-rule filter
  blocks, one for the source template and one for the target template. Include
  rows are AND, exclude rows subtract matches, and the filtered candidate sets
  are still paired by source/target template variable matching. Template-to-rule
  relations use the same filter block only on the template/source side because
  the target is one concrete rule.

## Administrator instruction: regex examples

Use regular expressions only to decide which source classes and source cards
belong to a service/suppression population group.

- `(?i).*Workplace.*`: match class code, description, or hierarchy path
  containing `Workplace`, ignoring case.
- `^Server$`: match exactly `Server`.
- `^(Server|Notebook|PC)$`: match one class from a fixed list.
- `^/Monitoring/Infrastructure/.*`: match a class hierarchy branch.
- `^prod-.*`: match attribute values that start with `prod-`.
- `.*\.corp\.example$`: match DNS names ending with `.corp.example`.
- `^(10\.10\.|10\.20\.).*`: match addresses from two network prefixes.
- `^$`: match an empty value.
- `.+`: require a non-empty value.
- `^test-.*` as an exclude condition: remove test objects from a broader
  include result.
- `^(?!test-).*`: also excludes values starting with `test-`, but a separate
  exclude row is usually easier to audit.
- `^db-[0-9]{2}$`: match `db-01`, `db-02`, and other two-digit database names.
- `^(msk|spb|nsk)-.*`: match values prefixed with approved site codes.

Operational rules:

- Include rows are cumulative AND conditions: a card must match every include
  row.
- Exclude rows are subtractive: if a card matches any exclude row, it is removed
  from the result.
- Escape literal regex metacharacters. For example, use `\.` for a dot in DNS
  names and `\+` for a plus sign.
- Prefer simple include/exclude rows over complex negative lookaheads when the
  same logic can be expressed by two readable conditions.
- Regex capture groups in selection conditions are not a field-mapping
  mechanism. Marking a substring with `(…)` or `(?<name>…)` does not make it
  available as `${name}` in target object attributes.
- Use transform/extract in template variables or target templates when the
  value is known at template-application time:
  `${extract(class.code, "^C2M_Service(.+)$", 1)}`,
  `${extract(class.description, "^(?<site>[A-Z]{3}) - .*$", "site", "unknown")}`,
  `${replace(class.code, "^C2M_", "")}`,
  `${replace(class.code, "^([A-Z]+)-([0-9]+)$", "$1_$2")}`,
  `${lower(trim(vars.site))}`, and `${default(vars.owner, "monitoring")}`.
- One-line operator reminder for different attributes and extraction methods:
  `${class.code}` · `${class.description}` · `${class.hierarchyPath}` ·
  `${vars.site}` · `${source.id}` · `${source.ownerEmail}` ·
  `${source.status}` · `${source.domainNetworkSegmentCode}` ·
  `${source.domainServiceOwnerEmail}` ·
  `${extract(class.code, "^C2M_(.+)$", 1)}` ·
  `${extract(class.description, "^(?<site>[A-Z]{3}) - .*$", "site", "unknown")}` ·
  `${replace(class.code, "^C2M_", "")}` ·
  `${replace(class.code, "^([A-Z]+)-([0-9]+)$", "$1_$2")}` ·
  `${lower(trim(vars.site))}`.
- Transform functions can use regex capture groups inside their own regex
  argument, including named groups. They currently evaluate `class.*` and
  rendered `vars.*`; `${source.<attribute>}` is preserved whole for runtime
  source-card processing.

## Monitoring UI data-source sync

The top-level `Панель` menu aggregates configured microservice healthchecks
from `monitoring-ui-api` `healthChecks`. Reloadable appliers expose
`ConfigurationReload:Route`; the UI shows their application/configuration
versions and calls the route with one shared Bearer Token. Configure that token
either as the same literal value in `zabbixconfig2api`,
`cmdbaggregation2cmdbuild`, and `monitoring-ui-api`, or as the same PAM secret
reference through `BearerTokenSecret` / `reloadBearerTokenSecret`. After a
successful reload the UI refreshes the applier health data so the displayed
running configuration version is updated. The `cmdbconfigbuilder` health card
also shows the conversion rule version loaded by the microservice and compares
it with the current service/suppression rule versions in the UI. The top-level
`События` menu lists
only managed Kafka topics returned by `cmdbconfigbuilder` and opens the last
five events of the selected topic by default. Kafka topics are identified by
`KafkaTopics:ManagedIdentifier`, `KafkaTopics:ManagedPrefix`, and the explicit
topic settings; foreign customer topics are not shown.

The `Администрирование -> Основные` menu shows the Zabbix readiness attribute
name: `zabbix_main_hostid`. This is the CMDBuild card attribute that signals that the
source object already has a corresponding Zabbix host and can enter the service
or suppression model. The same menu shows the server folder used to store
conversion rules, templates, and managed relations.

The top-level `Синхронизация с источниками данных` menu is split by source:

- `CMDBuild` refreshes local class, attribute, and domain catalogs used by
  schema previews and conversion editors. The CMDBuild cache also includes
  current cards of managed service and suppression classes, grouped by class
  and stored with attribute values. Source-class cards needed by
  `Distinct source field` and `Regex capture` template dimensions are loaded
  on demand before template materialization.
- `Zabbix` checks the configured Zabbix API through `zabbixconfig2api` and
  shows connection version, endpoint, and error details.
- `Сервисный слой -> Применить в Zabbix` and
  `Каскадное подавление -> Применить в Zabbix` run the same current-card
  evaluation only for one layer and publish only to that layer's Zabbix topic.
  Each screen has its own dry-run action, publication action, Zabbix status,
  counters, errors, reconcile summary, and live progress for long operations:
  completed source classes, current class card progress, built/published
  commands, duplicate skips, remaining work, and the planned Zabbix objects and
  relations that the command set would ensure. The planned object list is
  paginated in the UI; per-object action, attributes, sources, and relations are
  hidden in expandable details. These actions do not publish to the CMDBuild
  aggregation topic.
- With `zabbixconfig2api` in auto apply mode, the layer topics are applied to
  Zabbix as managed Services. The expected UI location in Zabbix is
  `Monitoring -> Services` / service tree. Managed services are tagged with
  `cmdb2monitoring:managed=true`, `cmdb2monitoring:layer`, `cmdb2monitoring:class`,
  and `cmdb2monitoring:key`; existing CMDBuild cards also get
  `cmdb2monitoring:card_id`. If a relation points to a service that has not been
  created yet, the object is applied with a `partial` warning and the publish
  action can be rerun after the missing object appears. Zabbix-only
  current-card publishing keeps source-card identity in duplicate keys so several
  source cards that map to the same target service do not hide membership.
- Source membership is persisted by `zabbixconfig2api`
  (`ZabbixApplyState:FilePath`). A source card with `zabbix_main_hostid` produces a
  source leaf service under the managed target service. The leaf service has
  `problem_tags` for `cmdb2monitoring:source_hostid=<zabbix_main_hostid>`, and the
  applier adds the same tag to the Zabbix host while preserving existing host
  tags. This is how Zabbix Services can associate real host problems with
  managed objects such as `ВПН филиалов`. A source card that still has no
  `zabbix_main_hostid` is stored in the membership snapshot as pending
  diagnostics only: it is not counted as an active source leaf, and any previous
  active membership for that same card is removed until the readiness update
  arrives.
- Webhook payloads do not have to carry `zabbix_main_hostid`. Before building a
  command, `cmdbconfigbuilder` reads the configured readiness attribute
  (`Readiness:ZabbixHostIdAttribute`, default `zabbix_main_hostid`) from the
  current CMDBuild card and injects it into the source event. Legacy payload
  fields such as `zabbix_hostid` remain only a compatibility fallback in the rule
  engine.
- The service tree and problem-tag binding do not by themselves close or hide
  dependent problem events. Active event suppression from our side is not used:
  the suppression model must be reflected in Zabbix through service topology
  and trigger dependencies. In `Каскадное подавление -> Применить в Zabbix`,
  the `Зависимости триггеров` block first ensures managed aggregate triggers
  and calculated items for suppression objects, then builds dependencies from
  persisted membership:
  triggers of child/dependent source hosts and the child's own aggregate trigger
  depend on the aggregate trigger of the parent/cause object. Reconcile
  preserves manual Zabbix dependencies and removes only edges that were
  previously managed by this service. Runtime state is not pushed by
  cmdb2monitoring; after reconcile Zabbix reevaluates calculated items and
  aggregate triggers by its normal schedule. The aggregate calculated item is
  built from selected source trigger expressions, not from a hardcoded item key.
  After a suppression membership command is applied from Kafka, `zabbixconfig2api`
  starts the same trigger-dependency reconcile automatically with a short debounce
  (`ZabbixTriggerDependencies:AutoReconcileDebounceSeconds`). The manual button is
  still useful after schema/rule relation changes or for operator-controlled
  dry-run.
  Zabbix trigger dependencies do not create a separate "suppressed problem"
  event while the parent trigger is in Problem state; the dependent trigger is
  not switched to Problem until the dependency clears and a new metric arrives.
- `Webhooks` checks the configured `cmdbwebhooks2kafka` health endpoint, reads
  CMDBuild `etl/webhook` inventory, and shows the webhook target route plus the
  raw event Kafka topic. `Перечитать из CMDBuild` reloads the current managed
  inventory. `Опубликовать webhooks в CMDBuild` creates or updates managed
  CMDBuild webhooks for the source classes used by loaded conversion rules.
  The view also shows how many managed webhook definitions are loaded for
  `CREATE`, `UPDATE`, and `DELETE`. CMDBuild may contain webhooks or Kafka
  topics owned by other integrations, so only definitions with the configured
  `webhooks.managedIdentifier`, configured webhook code prefix, and target URL
  are treated as ours. The `Сверить правила онлайн` action calls the live
  webhooks endpoint and compares managed webhooks with source classes used by
  current conversion rules; it does not use the local cache. The check also
  compares payload fields when a managed webhook definition explicitly lists
  them. A webhook definition without a field list is treated as full-payload.
  Webhook coverage is intentionally class/event-based, not per rule: generated
  service and suppression rules may reference the same customer source class,
  but CMDBuild needs one managed webhook set for that class. Rule IDs shown in
  this view are diagnostic labels only.
- `Конфигурации конвертации` saves and loads service/suppression rule documents,
  rule templates, and managed relations through `monitoring-ui-api`, which is the
  only writer for the configured server folder. The current format writes
  separate JSON files for service rules, suppression rules, service templates,
  suppression templates, shared templates, and a manifest; relations created by
  `Создать/обновить правила по шаблонам и связям` are stored as
  `managed_relations` inside the rule/template documents. This folder can later
  be placed under Git control. Saves use manifest `version`/`etag` conflict
  checks and atomic temp-file rename writes, with the manifest written last.
  For operators, `Сохранить в папку` is the publication step for conversion
  configuration: applier services reread that shared folder on reload, and the
  same workflow will later use a Git-backed folder. CMDBuild webhook
  publication is a separate Webhooks action.
- `Управление правилами -> Создать/обновить правила по шаблонам и связям`
  contains the `Проверить шаблоны` preflight step before materializing
  templates. It loads the needed source cards, calculates `dimension.*`, shows
  generated-rule and managed-relation create/update/remove counts, target
  attributes, and blocking errors such as empty dimensions, missing
  domains/targets, or duplicate generated rule IDs.
- Each source separates `Провести синхронизацию` from `Загрузить локальный
  кэш`. Synchronization reads the real source and stores an IndexedDB browser
  cache; loading the cache restores the last stored snapshot without rereading a
  potentially large source. The UI shows the last cache update timestamp for
  each source. For conversion configurations the primary action is
  `Сохранить в папку`, with separate `Загрузить из папки` and
  `Загрузить локальный кэш` actions. After
  `Создать/обновить правила по шаблонам и связям`, folder save persists the
  generated rules, templates, and their managed links, but it does not execute
  those rules against existing CMDBuild cards. Service/suppression target cards
  are created after matching source-class webhooks are processed; successful
  folder load/save also turns on the top `Конвертация загружена` indicator. When
  service and suppression documents are assembled for runtime reading, runtime
  `rule_id` values must be globally unique. Generated template rules include
  the layer in new IDs; if older documents still contain the same `rule_id` in
  both layers, `monitoring-ui-api` exposes those runtime IDs with a `service-`
  or `suppression-` prefix instead of failing validation.

Development integration defaults:

- CMDBuild REST API URL, auth mode, login/password, token, or PAM references
  are supplied through external service config or environment variables.
- Zabbix JSON-RPC API URL, auth mode, login/password, token, or PAM references
  are supplied through external service config or environment variables.
- New service launch ports: `5180-5183`, kept separate from the existing test stand ports `5080-5083`.
