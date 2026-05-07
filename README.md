# CMDBuild to Zabbix Service Suppression

Separate implementation for preparing a customer CMDBuild model and generating
Zabbix suppression dependencies and service tree configuration.

Initial services:

- `cmdbwebhooks2kafka`: receives CMDBuild webhooks and publishes change events.
- `cmdbaggregation2cmdbuild`: prepares CMDBuild schema and derived aggregation objects.
- `cmdbconfigbuilder`: builds desired Zabbix configuration from mapping rules and CMDB events.
- `zabbixconfig2api`: applies approved configuration changes to Zabbix.
- `monitoring-ui-api`: Node.js UI/BFF for schema setup, conversion rules, and manual apply.

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
their parent class and can receive population source links.
The schema view is split into `Ready classes/domains` and
`Planned classes/domains`. Domains are not shown as a detached list; each
planned, suggested, or source-link domain is rendered under its source class so
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

## Service schema rules

Service schema keeps aggregation settings on objects, not on links:

- Every service class inherits from `ServiceManagedObject`.
- `ServiceManagedObject` owns the common object attributes plus service
  aggregation attributes: `aggregation_type`, `threshold`, and `n`. The common
  object attributes include `population_source_key`, which stores the source
  key copied from the customer CMDBuild card during automated population.
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
- Service domains describe structure only. They do not duplicate
  `aggregation_type`, `threshold`, or `n`.
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

## Population source links

Managed object instances are tied to customer CMDBuild objects by explicit
source-link domains:

- For every concrete managed class, the schema UI lets the operator choose an
  existing customer CMDBuild class.
- The preview generates a `populated_from` domain from the managed class to the
  selected customer class, for example
  `C2M_ServiceWorkplaceGroupPopulatedFromCustomerWorkplace`.
- Superclasses are not linked to customer classes because cards are created in
  concrete managed classes.
- Source-link domains are marked separately in the UI and use the relation
  attributes `is_active`, `source`, and `population_rule_id`.
- The source-link domain is the structural relation used by automated
  population to keep a generated object connected to the customer object that
  produced it.

## Conversion rule editing

- In service and suppression modification screens, `Целевой класс\экземпляр
  класса` selects the managed class whose card must be created by the
  conversion rule. This selector works at class/instance level, not at leaf
  attribute level.
- `Класс-источник` lists customer CMDBuild classes in inheritance-path order:
  superclass branches are grouped first, then their descendants.
- CMDBuild cache key `cmdbuild.catalogs.v3` stores prototype/superclass nodes
  for sorting, while rule source selection still exposes only non-prototype
  customer classes.
- `create_instance` is mandatory for rules created or normalized by the UI and
  is always stored as `true`; choosing a target class means the builder must
  create or idempotently find the target card by the rule key.
- Attribute mapping and user-responsibility attributes are edited in one list:
  `source` is always on the left and `target` on the right. A row with both
  values is stored as an automatic mapping; a row with only `target` is stored
  as a target attribute left for the user to fill.
- User-responsibility attributes are restricted by layer. Service rules expose
  only `description`, `is_critical`, `aggregation_type`, `threshold`, and `n`;
  `threshold` is used by `aggregation_type=threshold`, and `n` is the N
  parameter for `n_of_m` while M is the current active child count. Suppression
  rules expose only `description` and `is_critical`.
- The rule key is not edited as a separate UI field. It is derived from the
  required target mapping `population_source_key <- ${source.<attribute>}`.
  On create/update the builder writes this value into the managed target card;
  the same source value is stored as `source.key_attribute`, `when.fieldExists`,
  and `target.idempotency_key` so repeated processing finds the existing card
  instead of creating a duplicate.

## Population Templates

- Auto-population templates are a separate configuration layer above ordinary
  binding rules. The runtime services still execute ordinary rules; the UI
  materializes templates into rules marked with `generated_from_template`.
- A template selects candidate customer source classes with a regex matched
  against class code, display name, and inheritance path. This is intended for
  repeated service/suppression structures where hundreds of source classes have
  the same pattern.
- Template fields can use `${class.code}`, `${class.description}`,
  `${class.hierarchyPath}`, `${source.<attribute>}`, and `${vars.<name>}`.
  Variables are stored in the template and are rendered before target
  `name`, initial `description`, and `population_source_key` are written into
  generated rules.
- Applying templates replaces previously generated rules for the layer and
  keeps manually maintained rules. Template audit compares expected generated
  rules with existing generated rules and reports missing or stale items.

## Monitoring UI data-source sync

The top-level `Синхронизация с источниками данных` menu is split by source:

- `CMDBuild` refreshes local class, attribute, and domain catalogs used by
  schema previews and conversion editors. The CMDBuild cache also includes
  current cards of managed service and suppression classes, grouped by class
  and stored with attribute values.
- `Zabbix` checks the configured Zabbix API through `zabbixconfig2api` and
  shows connection version, endpoint, and error details.
- `Конфигурации конвертации` re-reads local service and suppression rule
  documents and refreshes the rule previews/editors against the current source
  catalogs.
- Each source separates `Провести синхронизацию` from `Загрузить локальный
  кэш`. Synchronization reads the real source and stores an IndexedDB browser
  cache; loading the cache restores the last stored snapshot without rereading a
  potentially large source. The UI shows the last cache update timestamp for
  each source.

Development integration defaults:

- CMDBuild REST API: `http://localhost:8090/cmdbuild/services/rest/v3`, `admin/admin`.
- Zabbix JSON-RPC API: `http://localhost:8081/api_jsonrpc.php`, `Admin/zabbix`.
- New service launch ports: `5180-5183`, kept separate from the existing test stand ports `5080-5083`.
