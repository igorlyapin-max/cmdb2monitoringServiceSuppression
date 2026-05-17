import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const repoRoot = path.resolve(import.meta.dirname, '..');
const appPath = path.join(repoRoot, 'src/monitoring-ui-api/public/app.js');
const serverPath = path.join(repoRoot, 'src/monitoring-ui-api/server.mjs');
const indexPath = path.join(repoRoot, 'src/monitoring-ui-api/public/index.html');
const stylesPath = path.join(repoRoot, 'src/monitoring-ui-api/public/styles.css');
const uiConfigPath = path.join(repoRoot, 'src/monitoring-ui-api/config/appsettings.json');
const builderConfigPath = path.join(repoRoot, 'src/cmdbconfigbuilder/appsettings.json');
const zabbixConfigPath = path.join(repoRoot, 'src/zabbixconfig2api/appsettings.json');
const cmdbConfigBuilderPath = path.join(repoRoot, 'src/cmdbconfigbuilder/Program.cs');
const cmdbAggregationBuilderPath = path.join(repoRoot, 'src/cmdbaggregation2cmdbuild/Program.cs');
const cmdbuildClientPath = path.join(repoRoot, 'src/shared/Integrations/CmdbuildClient.cs');
const zabbixClientPath = path.join(repoRoot, 'src/shared/Integrations/ZabbixClient.cs');
const zabbixManagedServicePath = path.join(repoRoot, 'src/shared/Integrations/ZabbixManagedService.cs');
const cmdbSchemaFactoryPath = path.join(repoRoot, 'src/shared/CmdbuildSchema/CmdbuildSchemaFactory.cs');
const zabbixProgramPath = path.join(repoRoot, 'src/zabbixconfig2api/Program.cs');
const zabbixAggregationApplierPath = path.join(repoRoot, 'src/zabbixconfig2api/ZabbixAggregationApplier.cs');
const zabbixTriggerDependencyApplierPath = path.join(repoRoot, 'src/zabbixconfig2api/ZabbixTriggerDependencyApplier.cs');

const appText = fs.readFileSync(appPath, 'utf8');
const serverText = fs.readFileSync(serverPath, 'utf8');
const indexText = fs.readFileSync(indexPath, 'utf8');
const stylesText = fs.readFileSync(stylesPath, 'utf8');
const cmdbConfigBuilderText = fs.readFileSync(cmdbConfigBuilderPath, 'utf8');
const cmdbAggregationBuilderText = fs.readFileSync(cmdbAggregationBuilderPath, 'utf8');
const cmdbuildClientText = fs.readFileSync(cmdbuildClientPath, 'utf8');
const zabbixClientText = fs.readFileSync(zabbixClientPath, 'utf8');
const zabbixManagedServiceText = fs.readFileSync(zabbixManagedServicePath, 'utf8');
const cmdbSchemaFactoryText = fs.readFileSync(cmdbSchemaFactoryPath, 'utf8');
const zabbixProgramText = fs.readFileSync(zabbixProgramPath, 'utf8');
const zabbixAggregationApplierText = fs.readFileSync(zabbixAggregationApplierPath, 'utf8');
const zabbixTriggerDependencyApplierText = fs.readFileSync(zabbixTriggerDependencyApplierPath, 'utf8');
const uiConfig = JSON.parse(fs.readFileSync(uiConfigPath, 'utf8'));
const builderConfig = JSON.parse(fs.readFileSync(builderConfigPath, 'utf8'));
const zabbixConfig = JSON.parse(fs.readFileSync(zabbixConfigPath, 'utf8'));

const api = await loadAppApi();

assertStaticUiContracts();
assertReadinessConfigContracts();
assertWebhookManagementContracts();
assertPopulationDimensionUiContracts();
assertTemplateTargetClassContracts();
assertUniversalSchemaContracts();
assertRuleDocumentNormalizationContracts();
assertStaticRuleTemplateFilterContracts();
assertTemplateMaterializationContracts();
assertTemplateRelationRegexContracts();
assertRelationGraphContracts();

console.log('UI regression checks passed.');

async function loadAppApi() {
  const startupDisabled = appText.replace(
    /await loadInitialConfig\(\);[\s\S]*?void checkHealthServices\(\{ silent: true \}\);/,
    '/* initial async startup disabled for ui-regressions */'
  );
  const exportedNames = [
    'state',
    'templatePopulationControlVisible',
    'templatePopulationSourceFieldOptionsForType',
    'templatePopulationConditionFieldOptionsForType',
    'targetClassOptions',
    'templateTargetClassOptions',
    'ruleSelectOptions',
    'deriveCopySourceOptions',
    'schemaOptionsBody',
    'automaticSchemaSourceLinks',
    'automaticSchemaSourceLinkDomainCodes',
    'serviceObjectRelationEndpointOptions',
    'serviceObjectRelationRows',
    'targetClassAttributes',
    'targetObjectEditableAttributes',
    'normalizeRuleDocument',
    'relationGraphRoleEffectDirection',
    'relationGraphRegexItemsFromRelation',
    'relationRegexComparableValue',
    'templateManagedRelationMatchesRulePair',
    'fallbackManagedRelationDomain',
    'renderRelationGraphRegexChip',
    'relationGraphRuntimeErrorDetails',
    'relationGraphEdgeGeometry',
    'relationGraphNodeDiagnosticLabel',
    'relationGraphData',
    'applyCommandCountersText',
    'zabbixHostIdAttributeName',
    'templateMaterializationPlan',
    'detachedTemplateCleanupRules',
    'zabbixScopeKeysFromRule',
    'zabbixDirtyScopeState'
  ];
  const wrapped = `(async () => { ${startupDisabled}\nreturn { ${exportedNames.join(', ')} }; })()`;
  return vm.runInNewContext(wrapped, browserLikeContext(), { filename: appPath });
}

function browserLikeContext() {
  const makeElement = () => ({
    addEventListener() {},
    appendChild() {},
    removeAttribute() {},
    setAttribute() {},
    focus() {},
    closest() { return null; },
    matches() { return false; },
    querySelector() { return makeElement(); },
    querySelectorAll() { return []; },
    classList: { add() {}, remove() {}, toggle() {} },
    dataset: {},
    style: {},
    children: [],
    options: [],
    selectedIndex: 0,
    value: '',
    checked: false,
    disabled: false,
    textContent: '',
    innerHTML: ''
  });
  const fakeElement = makeElement();
  const context = {
    console,
    document: {
      body: fakeElement,
      documentElement: fakeElement,
      addEventListener() {},
      createElement() { return makeElement(); },
      getElementById() { return makeElement(); },
      querySelector() { return makeElement(); },
      querySelectorAll() { return []; }
    },
    window: { addEventListener() {}, location: { hash: '' } },
    localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
    navigator: {},
    crypto: { randomUUID() { return 'test-uuid'; } },
    setTimeout,
    clearTimeout,
    URL,
    Blob,
    structuredClone,
    btoa: (value) => Buffer.from(value).toString('base64'),
    atob: (value) => Buffer.from(value, 'base64').toString('utf8')
  };
  context.globalThis = context;
  return context;
}

function assertStaticUiContracts() {
  assertIncludes(indexText, 'data-view="relationsGraph"', 'relations graph menu item must exist.');
  assertIncludes(indexText, 'data-view="zabbixPreflight"', 'Zabbix preflight menu item must exist.');
  assertIncludes(indexText, 'data-view="serviceObjects"', 'service layer must expose a separate service objects menu item.');
  assertIncludes(indexText, 'Объекты сервиса', 'service layer menu must use the service objects label.');
  assertIncludes(indexText, 'id="serviceObjectsView"', 'service objects view must exist.');
  assertIncludes(indexText, 'id="serviceObjectSelect"', 'service objects view must expose existing object selector.');
  assertIncludes(indexText, 'id="createServiceObjectButton"', 'service objects view must expose a creation action.');
  assertIncludes(indexText, 'id="newServiceObjectButton"', 'service objects view must expose a reset action for object editing.');
  assertIncludes(indexText, 'id="serviceObjectRelationSelect"', 'service objects view must expose existing relation selector.');
  assertIncludes(indexText, 'id="serviceObjectRelationFilterTemplateRules"',
    'service object relations must expose template-generated object filter checkbox.');
  assertIncludes(indexText, 'data-service-object-relation-filter-template-rules',
    'service object relation filter checkbox must be wired for UI events.');
  assertIncludes(indexText, 'id="createServiceObjectRelationButton"', 'service objects view must expose direct service-object relation creation.');
  assertIncludes(indexText, 'id="newServiceObjectRelationButton"', 'service objects view must expose a reset action for relation editing.');
  assertIncludes(indexText, 'id="refreshServiceObjectRelationsButton"', 'service objects view must expose relation refresh.');
  assertIncludes(indexText, 'SLA-политик', 'service objects view must mention SLA policy objects.');
  assertIncludes(indexText, 'Публикация SLA перенесена в Сервисный слой -> Применить в Zabbix',
    'SLA settings help must point operators to the service Zabbix apply view for publication.');
  assertIncludes(indexText, 'id="zabbixSlaPublicationStatus"',
    'service Zabbix apply view must expose SLA publication status.');
  assertIncludes(indexText, 'Отдельные пустые service-узлы из SLA-панели не создаются',
    'SLA help must explain that SLA publication does not create isolated service nodes.');
  const serviceZabbixApplyStart = indexText.indexOf('id="serviceZabbixApplyView"');
  const servicePublishButton = indexText.indexOf('data-zabbix-apply-publish', serviceZabbixApplyStart);
  const slaDryRunButton = indexText.indexOf('id="zabbixSlaDryRunButton"', serviceZabbixApplyStart);
  assert(serviceZabbixApplyStart >= 0 && servicePublishButton >= 0 && slaDryRunButton > servicePublishButton,
    'SLA publication controls must be placed in service Zabbix apply after publish-service action.');
  const slaSettingsStart = indexText.indexOf('id="slaSettingsView"');
  const slaSettingsEnd = indexText.indexOf('id="dataSourceSyncView"', slaSettingsStart);
  const slaSettingsMarkup = indexText.slice(slaSettingsStart, slaSettingsEnd);
  assert(!slaSettingsMarkup.includes('id="zabbixSlaDryRunButton"')
      && !slaSettingsMarkup.includes('id="zabbixSlaPublishButton"'),
    'Administration SLA view must keep settings only and not expose publication buttons.');
  assertIncludes(indexText, 'Связи сервисных объектов', 'service objects view must expose same-menu service object links.');
  assertIncludes(indexText, 'Сервис рабочих мест -> Ноутбуки', 'service objects view must document service-to-aggregate containment.');
  assertIncludes(indexText, 'Сервис рабочих мест -> Маршрутизаторы филиалов', 'service objects view must document service-to-aggregate dependency.');
  assertIncludes(indexText, 'сервисные CMDBuild-объекты',
    'relations graph help must explain that service objects are shown.');
  assertIncludes(indexText, 'id="schemaEntityBuilder"', 'shared schema entity builder must be addressable from UI code.');
  assertIncludes(appText, "view === 'serviceObjects'", 'service objects menu must activate its dedicated view.');
  assertIncludes(appText, "schemaEntityBuilder.hidden = state.activeLayer === 'Service'",
    'service object creation must be moved out of the service schema view.');
  assertIncludes(appText, 'SERVICE_OBJECT_TYPE_DEFINITIONS', 'service objects must have typed creation definitions.');
  assertIncludes(appText, 'SERVICE_OBJECT_RELATION_DEFINITIONS', 'service objects must have direct relation definitions.');
  assertIncludes(appText, 'SERVICE_AGGREGATE_TYPE_DEFINITIONS', 'service objects must expose aggregate endpoints in relation forms.');
  assertIncludes(appText, 'service_contains_workplace_group', 'service objects must support service-to-aggregate containment.');
  assertIncludes(appText, 'service_depends_on_aggregate', 'service objects must support service-to-aggregate dependency.');
  assertIncludes(appText, 'service_contains_template', 'service objects must support service-to-template containment.');
  assertIncludes(appText, 'service_depends_on_template', 'service objects must support service-to-template dependency.');
  assertIncludes(appText, "'service_storage_pool', 'service_template'",
    'ordinary service-to-aggregate relation selectors must also expose aggregate templates.');
  assertIncludes(appText, 'serviceTemplateGeneratedTargets',
    'service object template links must expand selected templates to current generated aggregate cards.');
  assertIncludes(appText, 'relationGraphServiceObjectNodes',
    'relations graph must include manual service objects such as SLA policies and calendars.');
  assertIncludes(appText, 'relationGraphServiceObjectRelationEdges',
    'relations graph must include CMDBuild service-object relations.');
  assertIncludes(appText, 'serviceObjectRelationTemplateFilterEnabled',
    'service object relations must have a dedicated template-generated object filter state.');
  assertIncludes(appText, 'state.domains.concat(state.suggestedDomains, state.cmdbDomains)',
    'service object relation lists must use live CMDBuild domains as well as schema preview domains.');
  assertIncludes(appText, 'isServiceRelationEndpointTemplateGenerated',
    'service object relation lists must be able to hide template-generated aggregate cards.');
  assertIncludes(appText, 'filterTemplateTargets: filterTemplateRules',
    'service object relation endpoint selectors must apply the template-generated object filter.');
  assertIncludes(appText, 'includeDetached: true',
    'service object relation filter must hide historical detached template-generated aggregates too.');
  assertIncludes(appText, "domainDirection: 'target_to_source'", 'service containment must handle reverse CMDBuild aggregates_to orientation.');
  assertIncludes(appText, 'ServiceFleetAggregatesToPlatformService',
    'service objects must support direct endpoint-fleet to platform-service containment domain.');
  assertIncludes(appText, 'service_network_access_zone',
    'service containment must expose network access zones as service aggregate targets.');
  assertIncludes(appText, 'fallbackServiceContainmentDomainBase',
    'service containment fallback must cover every service aggregate to platform-service domain.');
  assertIncludes(cmdbSchemaFactoryText, 'ServiceFleetAggregatesToPlatformService',
    'service schema must create direct endpoint-fleet to platform-service containment domain.');
  assertIncludes(cmdbSchemaFactoryText, 'AddUniversalServiceContainmentDomains',
    'service schema must create containment domains from every service managed aggregate to platform services.');
  assertIncludes(appText, 'standalone-domain-list',
    'schema view must render planned domains even when there are no planned classes.');
  assertIncludes(appText, 'Домены без класса-источника в текущем списке',
    'schema view must label standalone planned domains for operators.');
  assertIncludes(appText, 'Сохранить изменения', 'service object editor must switch into update mode.');
  assertIncludes(appText, 'Сохранить связь', 'service object relation editor must switch into update mode.');
  assertIncludes(appText, 'refreshServiceObjectsFromCmdb({ showMessage: serviceObjectTotalCount() === 0 })',
    'service objects view must refresh live CMDBuild cards when the menu is opened.');
  assertIncludes(appText, 'await persistCmdbSourceCache();',
    'service object refresh must update the local CMDBuild cache after live reload.');
  assertIncludes(indexText, 'Эти объекты сохраняются как карточки CMDBuild',
    'service object help must explain that objects are not saved in conversion config files.');
  assertIncludes(appText, 'deleteServiceObjectRelation', 'service object relation editing must be able to remove the old relation.');
  assertIncludes(appText, 'renderZabbixSlaPublicationPanel',
    'service Zabbix apply view must render the moved SLA publication panel.');
  assertIncludes(appText, 'zabbixSlaServiceTopologyText',
    'SLA publication view must explain whether target Zabbix services exist in the topology.');
  assertIncludes(appText, 'stateItem.result = result;',
    'SLA publication errors must preserve backend details in the UI.');
  const directGraphProgressText = api.applyCommandCountersText({
    dryRun: false,
    topics: ['zabbix-direct'],
    stage: 'zabbix_graph_publish',
    status: 'running',
    commandsBuilt: 353,
    commandsPublished: 0,
    commandsSkippedAsDuplicates: 0
  });
  assert(directGraphProgressText.includes('batch выполняется в Zabbix'),
    'direct graph publishing progress must not look like a completed zero-publish result.');
  assert(api.applyCommandCountersText({
    dryRun: false,
    topics: ['zabbix-direct'],
    stage: 'completed',
    status: 'completed',
    commandsBuilt: 353,
    commandsPublished: 353,
    commandsSkippedAsDuplicates: 0
  }).includes('применено в Zabbix 353'),
    'completed direct graph publishing progress must show applied command count.');
  assertIncludes(appText, '/api/cmdbuild/domains/${encodeURIComponent(domainCode)}/relations',
    'service object links must be sent to CMDBuild domain relations.');
  assertIncludes(serverText, 'domainRelationCreateMatch',
    'monitoring UI API must proxy service object relation creation.');
  assertIncludes(serverText, 'cardUpdateMatch',
    'monitoring UI API must proxy service object card updates.');
  assertIncludes(serverText, 'domainRelationDeleteMatch',
    'monitoring UI API must proxy service object relation deletion.');
  assertIncludes(cmdbAggregationBuilderText, '/cmdbuild/domains/{domainCode}/relations',
    'cmdbaggregation2cmdbuild must expose CMDBuild relation creation.');
  assertIncludes(cmdbAggregationBuilderText, '/cmdbuild/classes/{classCode}/cards/{cardId}',
    'cmdbaggregation2cmdbuild must expose CMDBuild card update.');
  assertIncludes(cmdbAggregationBuilderText, '/cmdbuild/domains/{domainCode}/relations/{relationId}',
    'cmdbaggregation2cmdbuild must expose CMDBuild relation deletion.');
  assertIncludes(cmdbuildClientText, 'CmdbuildCreateRelationRequest',
    'CMDBuild client must have a typed relation creation request.');
  assertIncludes(cmdbuildClientText, 'DeleteDomainRelationAsync',
    'CMDBuild client must support relation deletion for service object relation edits.');
  assertIncludes(cmdbuildClientText, 'UpdateClassCardAsync',
    'CMDBuild client must support card updates for service object edits.');
  assertIncludes(indexText, 'Статические правила', 'auto-population menu must be renamed to static rules.');
  assertNotIncludes(indexText, 'Просмотр автонаполнения', 'view-only auto-population menu must not return.');
  assertIncludes(indexText, 'Фильтровать правила и классы из шаблонов', 'static rule template filter label must mention classes.');
  assertIncludes(indexText, 'Создать/обновить правила по шаблонам и связям', 'template apply action must mention links.');
  assertIncludes(indexText, 'id="runTemplateAuditButton"', 'template audit must be available inside template apply menu.');
  assertIncludes(indexText, 'id="templateDeleteModeDefaultSelect"',
    'admin settings must expose the default template deletion mode.');
  assertIncludes(indexText, 'data-view="microserviceSettings"',
    'administration menu must expose a separate microservices settings page.');
  assertIncludes(indexText, 'id="microserviceSettingsView"',
    'microservice-owned zabbixconfig2api settings must live outside General settings.');
  assertIncludes(indexText, 'сохраняются в браузерное хранилище только кнопкой',
    'General settings must explain how local UI settings are persisted.');
  assertIncludes(indexText, 'Отвязать правила и сохранить объекты',
    'template deletion default must be visible to operators.');
  assertIncludes(indexText, 'Удалить созданные правила и объекты',
    'template deletion default must allow removing generated objects.');
  assertIncludes(indexText, 'Значение по умолчанию: <strong>Удалить созданные правила и объекты</strong>',
    'admin settings must state that deleting generated objects is the default mode.');
  assertIncludes(indexText, 'Применить планы удаления в CMDBuild',
    'template apply help must explain explicit CMDBuild deletion plans.');
  assertIncludes(appText, 'DEFAULT_TEMPLATE_DELETE_MODE',
    'UI must keep an explicit default for template deletion mode.');
  assertIncludes(appText, 'const DEFAULT_TEMPLATE_DELETE_MODE = TEMPLATE_DELETE_MODES.deleteRulesAndObjects;',
    'default template deletion mode must remove generated objects.');
  assertIncludes(appText, "const GENERAL_SETTINGS_STORAGE_KEY = 'cmdb2monitoring.serviceSuppression.generalSettings.v2';",
    'general settings storage key must reset older browser defaults after changing template deletion default.');
  assertIncludes(appText, 'удаление объектов CMDBuild',
    'template apply view must make the object deletion block visible.');
  assertIncludes(appText, 'Планов удаления пока нет.',
    'template deletion block must explain when there are no pending object deletion plans.');
  assertIncludes(appText, 'renderDetachedTemplateRulesCard',
    'template apply view must surface detached generated rules.');
  assertIncludes(appText, 'cleanupDetachedTemplateRules',
    'template apply view must be able to remove detached generated rules.');
  assertIncludes(appText, 'removeRuleReferencesToRemovedRules',
    'removing template rules must also clean managed relation references to deleted rule IDs.');
  assertIncludes(appText, 'applyTemplateDeletionPlans',
    'template apply view must be able to execute template deletion plans.');
  assertIncludes(appText, 'data-template-deletion-apply',
    'template deletion plan buttons must be wired.');
  assertIncludes(appText, 'data-template-detached-cleanup',
    'detached template cleanup buttons must be wired.');
  assertIncludes(appText, '/api/cmdbuild/classes/${encodeURIComponent(classCode)}/cards/${encodeURIComponent(cardId)}',
    'UI must delete CMDBuild cards through the BFF card endpoint.');
  assertIncludes(serverText, "cardUpdateMatch && request.method === 'DELETE'",
    'monitoring UI API must proxy CMDBuild card deletion.');
  assertIncludes(cmdbAggregationBuilderText, 'MapDelete("/cmdbuild/classes/{classCode}/cards/{cardId}"',
    'cmdbaggregation2cmdbuild must expose CMDBuild card deletion.');
  assertIncludes(cmdbuildClientText, 'DeleteClassCardAsync',
    'CMDBuild client must support card deletion for template deletion plans.');
  assertIncludes(indexText, 'id="relationHideTemplateLinks"', 'rule-rule relation filter checkbox must exist.');
  assertIncludes(indexText, 'data-link-source', 'relation editor must have an explicit source selector.');
  assertIncludes(indexText, 'data-link-target', 'relation editor must have an explicit target selector.');
  assertIncludes(indexText, 'data-link-match-field="source-filters"', 'template-template relation editor must expose source filters.');
  assertIncludes(indexText, 'data-link-match-field="target-filters"', 'template-template relation editor must expose target filters.');
  assertIncludes(indexText, 'data-link-source-match-regex', 'template-rule relation editor must expose source regex.');
  assertIncludes(indexText, 'data-link-target-match-regex', 'template relation editor must expose target regex.');
  assertIncludes(indexText, 'serviceTemplateSourceFieldCopySelect', 'template source attribute copy control must exist for service.');
  assertIncludes(indexText, 'suppressionTemplateSourceFieldCopySelect', 'template source attribute copy control must exist for suppression.');
  assertIncludes(indexText, 'Атрибуты создаваемого целевого объекта', 'template target attributes editor must exist.');
  assertIncludes(indexText, 'aggregation_type', 'template target attributes help must mention aggregation_type.');
  assertIncludes(indexText, 'cmdb2monitoring:is_critical', 'UI help must explain is_critical is only a Zabbix service tag.');
  assertIncludes(indexText, 'не влияет на расчет доступности', 'UI help must explain is_critical does not change availability calculation.');
  assertIncludes(indexText, 'serviceRuleDeriveSource', 'service static rules must support create based on existing rules.');
  assertIncludes(indexText, 'suppressionRuleDeriveSource', 'suppression static rules must support create based on existing rules.');
  assertIncludes(indexText, 'serviceTemplateDeriveSource', 'service templates must support create based on existing templates.');
  assertIncludes(indexText, 'suppressionTemplateDeriveSource', 'suppression templates must support create based on existing templates.');
  assertIncludes(indexText, 'relationDeriveSource', 'relation editor must support create based on existing relations.');
  assertIncludes(indexText, 'Создать черновик', 'copy-on-create action must be explicit draft creation.');
  assertIncludes(indexText, 'Рабочее место филиал / City14',
    'relation-management help must include the branch workplace service example.');
  assertIncludes(indexText, 'Сервис рабочих мест</code> -> <code>Ноутбуки',
    'relation-management help must include the workplace service to notebooks containment example.');
  assertIncludes(indexText, 'ВПН хаб</code> -> <code>Маршрутизаторы ядра',
    'relation-management help must include the VPN hub to core routers dependency example.');
  assertIncludes(indexText, 'Короткое правило выбора',
    'relation-management help must explain when to use contains, depends_on and uses.');
  assertIncludes(appText, 'derived_from', 'copy-on-create must persist derived_from audit metadata.');
  assertIncludes(appText, 'const sourceSelect = config.deriveSource;',
    'copy-on-create controls must read the dedicated derive source selector.');
  assertNotIncludes(appText, 'deriveCopyResult(kind, layerKey, config.source?.value',
    'copy-on-create must not read the main relation source selector.');
  assertIncludes(appText, 'selectChoiceItemMeta(field, option.value, label)',
    'wide select menus must not expose technical option values as user-visible labels.');
  assertIncludes(appText, "field?.matches('[data-derive-source]')",
    'copy-on-create dropdowns must hide encoded internal derive values.');
  assertIncludes(appText, 'looksLikeEncodedJsonValue(value)',
    'wide select menus must hide URL-encoded JSON values.');
  assertIncludes(appText, 'mapTargetClassForLayer', 'copy-on-create must map target classes between service and suppression layers.');
  assertIncludes(appText, 'SERVICE_SUPPRESSION_CLASS_SUFFIX_PAIRS', 'copy-on-create must use explicit service/suppression class pairs.');
  assertIncludes(indexText, 'zabbix_main_hostid', 'UI must show zabbix_main_hostid readiness attribute.');
  assertIncludes(indexText, 'cmdbuildAuthPanel',
    'schema UI must expose CMDBuild session credential prompt.');
  assertIncludes(indexText, 'cmdbuildPasswordInput',
    'schema UI must be able to ask for a CMDBuild password.');
  assertIncludes(stylesText, '.row-route',
    'schema domain source-target route must have dedicated styling.');
  assertIncludes(stylesText, 'line-height: 1.55',
    'schema domain route/relation rows must not visually overlap when labels wrap.');
  assertIncludes(stylesText, '"toggle apply route route"',
    'schema domain route must span the count column instead of being squeezed under long labels.');
  assertIncludes(stylesText, '"toggle apply relation relation"',
    'schema domain relation must span the count column instead of being squeezed under long labels.');
  assertIncludes(indexText, 'transitiveGroupDependencyDepthSelect',
    'admin settings must expose transitive suppression group dependency depth.');
  assertIncludes(indexText, 'ZabbixTriggerDependencies:TransitiveGroupDependencyDepth',
    'admin settings must identify zabbixconfig2api as the source of transitive depth.');
  assertIncludes(indexText, 'aggregateStateTriggerIncludeTagsInput',
    'admin settings must show AggregateStateTriggerIncludeTags read-only.');
  assertIncludes(indexText, 'aggregateStateTriggerExcludeTagsInput',
    'admin settings must show AggregateStateTriggerExcludeTags read-only.');
  assertIncludes(indexText, 'aggregateStateTriggerIncludeNameRegexInput',
    'admin settings must show AggregateStateTriggerIncludeNameRegex read-only.');
  assertIncludes(indexText, 'aggregateStateTriggerExcludeNameRegexInput',
    'admin settings must show AggregateStateTriggerExcludeNameRegex read-only.');
  assertIncludes(indexText, 'aggregateStateTriggerMinPriorityInput',
    'admin settings must show AggregateStateTriggerMinPriority read-only.');
  assertIncludes(indexText, 'zabbixRequestTimeoutMsInput',
    'admin settings must show Zabbix request timeout read-only.');
  assertIncludes(indexText, 'triggerGetBatchSizeInput',
    'admin settings must show trigger.get batch size read-only.');
  assertIncludes(indexText, 'maxSourceHostsPerAggregateInput',
    'admin settings must show max source hosts per aggregate read-only.');
  assertIncludes(indexText, 'maxAggregateFormulaLengthInput',
    'admin settings must show max aggregate formula length read-only.');
  assertIncludes(indexText, 'ZabbixTriggerDependencies:TriggerGetBatchSize',
    'admin settings must identify the trigger.get batch size config key.');
  assertIncludes(indexText, 'ZabbixTriggerDependencies:MaxSourceHostsPerAggregate',
    'admin settings must identify the max source hosts per aggregate config key.');
  assertIncludes(indexText, 'ZabbixTriggerDependencies:MaxAggregateFormulaLength',
    'admin settings must identify the max aggregate formula length config key.');
  assertIncludes(indexText, 'Zabbix:RequestTimeoutMs',
    'admin settings must identify the Zabbix request timeout config key.');
  assertIncludes(indexText, 'src/zabbixconfig2api/appsettings.json',
    'admin settings must show where the transitive depth is changed.');
  assertIncludes(indexText, 'не меняет потоковую обработку webhook/Kafka',
    'admin settings must explain that transitive depth does not change streaming conversion.');
  assertIncludes(indexText, 'Leaf/source-хосты не получают полную матрицу dependencies',
    'admin settings must explain leaf-to-nearest-group suppression sizing.');
  assertIncludes(indexText, 'Aggregate trigger группы становится PROBLEM',
    'help must explain inherited upstream PROBLEM behavior for suppression groups.');
  assertNotIncludes(indexText, 'zabbix_hostid', 'visible UI must not mention legacy zabbix_hostid.');
  const suppressionApplyView = textBetween(indexText, 'id="suppressionZabbixApplyView"', 'id="templateApplyView"');
  assert(suppressionApplyView.includes('zabbixTriggerDependenciesApplyButton'),
    'suppression Zabbix apply view must expose trigger dependency publish action.');
  assert(
    suppressionApplyView.indexOf('zabbixTriggerDependenciesApplyButton') < suppressionApplyView.indexOf('data-zabbix-apply-list'),
    'trigger dependency actions must be visible before long suppression apply details.');
  assert(!/writing-mode\s*:\s*vertical/i.test(stylesText), 'relation graph labels must not use vertical writing mode.');
  assertIncludes(stylesText, '.app-shell', 'layout shell styles must exist.');
  assertIncludes(stylesText, 'position: sticky', 'side menu must be sticky and not scroll with page content.');
  assertIncludes(appText, 'templateTargetClassOptions(layerKey)', 'template target class options must be a separate class-only path.');
  assertIncludes(appText, 'targetClassOptions(layerKey, \'\', { includeInstances: false })', 'templates must exclude target instances.');
}

function assertReadinessConfigContracts() {
  assert(uiConfig.readiness?.zabbixHostIdAttribute === 'zabbix_main_hostid',
    'monitoring UI readiness attribute must be zabbix_main_hostid.');
  assert(builderConfig.Readiness?.ZabbixHostIdAttribute === 'zabbix_main_hostid',
    'cmdbconfigbuilder readiness attribute must be zabbix_main_hostid.');
  assertIncludes(cmdbConfigBuilderText, 'SourceHostIdEnrichment.TryResolveAsync',
    'cmdbconfigbuilder must enrich source events with the configured host id attribute.');
  assertIncludes(cmdbConfigBuilderText, '"{message.ClassCode}.{attribute}"',
    'cmdbconfigbuilder must resolve the configured host id attribute from CMDBuild cards.');
  assertIncludes(cmdbConfigBuilderText, 'attributes[attribute] = value',
    'resolved host id must be injected into the source event attributes.');
  assertIncludes(appText, 'cmdbuildUiAuthHeaders',
    'UI CMDBuild requests must include session credential overrides when provided.');
  assertIncludes(appText, "headers: cmdbuildFetchHeaders({\n        'content-type': 'application/json',\n        accept: 'application/json'\n      })",
    'Zabbix service/suppression dry-run and apply must send CMDBuild session credential overrides.');
  assertIncludes(appText, 'const authFailureText = resultErrors.find(isCmdbuildAuthFailureMessage);',
    'Zabbix dry-run result errors must be checked for missing CMDBuild credentials.');
  assertIncludes(appText, 'handleCmdbuildAuthFailure(authFailureText);',
    'Zabbix dry-run must open the CMDBuild auth prompt when credentials are missing.');
  assertIncludes(appText, 'source leaf в корне',
    'Zabbix stale diagnostics must make root-level technical source leaf services visible to the operator.');
  assertIncludes(appText, 'не-root в корне',
    'Zabbix stale diagnostics must show visible non-root managed services that reached the root.');
  assertIncludes(appText, 'renderZabbixRootNonRootManagedServices',
    'UI must render root-level non-root managed services as actionable stale diagnostics.');
  assertIncludes(appText, 'orphan visible nodes',
    'Zabbix dry-run plan must show visible service nodes that would be published without parents.');
  assertIncludes(appText, 'managed key: ${escapeHtml(item?.managedKey || \'-\')} · role',
    'Zabbix object plan must expose managed key, role and visibility for topology diagnostics.');
  assertIncludes(zabbixManagedServiceText, 'public const string Role = "cmdb2monitoring:role"',
    'managed Zabbix services must carry a role tag separate from display name.');
  assertIncludes(zabbixManagedServiceText, 'public const string Visibility = "cmdb2monitoring:visibility"',
    'managed Zabbix services must carry a visibility tag separate from display name.');
  assertIncludes(zabbixManagedServiceText, 'public const string RootService = "root_service"',
    'managed Zabbix service roles must include root_service.');
  assertIncludes(zabbixManagedServiceText, 'public const string Aggregate = "aggregate"',
    'managed Zabbix service roles must include aggregate.');
  assertIncludes(zabbixManagedServiceText, 'public const string SourceLeaf = "source_leaf"',
    'managed Zabbix service roles must include source_leaf.');
  assertIncludes(zabbixManagedServiceText, 'RemoveKnownManagedPrefix',
    'role inference may strip builder prefixes but must not rewrite user-visible names.');
  assertIncludes(cmdbConfigBuilderText, 'OrphanVisibleNodeCount',
    'cmdbconfigbuilder dry-run must expose visible Zabbix service nodes without parents.');
  assertIncludes(cmdbConfigBuilderText, 'видимых managed-узлов не имеют parent',
    'cmdbconfigbuilder must report orphan visible service nodes in operator language.');
  assertIncludes(cmdbConfigBuilderText, 'incomingManagedKeys',
    'Zabbix topology orphan diagnostics must use the full incoming relation set, not the UI relation samples.');
  assertIncludes(cmdbConfigBuilderText, 'LoadServiceObjectTemplateRelationsAsync',
    'service Zabbix topology publication must read service-object-to-template relations.');
  assertIncludes(cmdbConfigBuilderText, 'AddServiceObjectTemplateRelations',
    'service-object-to-template relations must be expanded to generated aggregate services.');
  assertIncludes(cmdbConfigBuilderText, 'rule.TemplateGeneration.TemplateId, rule.GeneratedFromTemplate',
    'template-generated rules must be matched by explicit template_generation or legacy generated_from_template.');
  assertIncludes(cmdbConfigBuilderText, 'TargetLookup = target.ManagedKey',
    'service-object-to-template topology must link to generated aggregate managed keys, not only CMDBuild card ids.');
  assertIncludes(cmdbConfigBuilderText, 'PublishPendingZabbixPlansAsync',
    'current Zabbix apply must publish only after the full desired graph is built and validated.');
  assertIncludes(cmdbConfigBuilderText, 'ApplyZabbixGraphDirectAsync',
    'direct current Zabbix apply must use the graph batch endpoint instead of per-command streaming.');
  assertIncludes(cmdbConfigBuilderText, 'ResolveZabbixGraphApplyUrl',
    'direct current Zabbix apply must derive /commands/apply-graph from the configured single-command URL.');
  assertIncludes(cmdbConfigBuilderText, 'ReadFirstJsonString',
    'direct current Zabbix apply must treat graph endpoint errors array as a failed publication.');
  assertIncludes(cmdbConfigBuilderText, 'client.Timeout = Timeout.InfiniteTimeSpan',
    'direct current Zabbix graph apply must not be interrupted by the default HttpClient timeout.');
  assertIncludes(cmdbConfigBuilderText, 'Операция применения прервана по timeout внешнего вызова',
    'current Zabbix apply must distinguish backend timeouts from explicit operator cancellation.');
  assertIncludes(cmdbConfigBuilderText, 'batch выполняется целиком; дерево в Zabbix может быть промежуточным',
    'direct graph publication progress must explain that Zabbix tree can be temporarily intermediate.');
  assertIncludes(cmdbConfigBuilderText, 'PrepareZabbixGraphPublishPlans',
    'current Zabbix apply must enrich and order the desired graph before publication.');
  assertIncludes(cmdbConfigBuilderText, 'AttachServiceParentManagedKeysAsync',
    'streaming service commands must inherit saved service-object-to-template parent links.');
  assertIncludes(cmdbConfigBuilderText, 'zabbix_graph_validation_failed',
    'current Zabbix apply must block publication when graph validation finds blocking errors.');
  assertIncludes(cmdbConfigBuilderText, 'ParentManagedKeys',
    'service graph publication must carry parent managed keys for top-down Zabbix service linking.');
  assertIncludes(zabbixClientText, 'service["parents"] = ServiceReferences(parentIds);',
    'Zabbix service API payload must use parents for top-down service graph publication.');
  assertIncludes(zabbixProgramText, '/commands/apply-graph',
    'zabbixconfig2api must expose a graph batch apply endpoint.');
  assertIncludes(zabbixProgramText, 'result.Errors.Count > 0',
    'zabbixconfig2api graph endpoint must fail the request when post-verify reports errors.');
  assertIncludes(zabbixAggregationApplierText, 'ApplyGraphAsync',
    'zabbixconfig2api must apply graph batches as a first-class operation.');
  assertIncludes(zabbixAggregationApplierText, 'ApplyManagedServiceNodeAsync',
    'service graph apply must have a node phase before final relation reconciliation.');
  assertIncludes(zabbixAggregationApplierText, 'VerifyServiceGraphAsync',
    'service graph apply must verify actual Zabbix service topology after publication.');
  assertIncludes(zabbixAggregationApplierText, 'Post-verify Zabbix',
    'service graph apply must report post-publication Zabbix topology mismatches in operator-visible errors.');
  assertIncludes(zabbixClientText, 'expectedParentCount == 0',
    'graph service node apply must be able to clear stale parents for desired root services.');
  assertIncludes(zabbixProgramText, 'rootNonRootManagedServiceCount',
    'zabbixconfig2api stale report must expose root-level non-root managed services.');
  assertIncludes(zabbixProgramText, 'orphanSourceLeafCount',
    'zabbixconfig2api stale report must expose root-level technical source leaf services.');
  assertIncludes(serverText, 'cmdbuildBackendAuthHeaders(request)',
    'monitoring UI BFF must forward CMDBuild auth overrides to backend services.');
  assertIncludes(serverText, "...cmdbuildBackendAuthHeaders(request)\n        },\n        body: JSON.stringify(backendBody)",
    'monitoring UI BFF must forward CMDBuild auth overrides to current Zabbix apply/dry-run.');
  assertIncludes(serverText, 'runDetachedJsonRequest(config.backend.rulesApplyCurrentUrl, backendInit, operationId);',
    'monitoring UI BFF must run long Zabbix apply operations detached from the browser request.');
  assertIncludes(serverText, 'zabbixApplyCancelMatch',
    'monitoring UI BFF must expose cancellation for detached Zabbix apply operations.');
  assertIncludes(appText, 'detached: true',
    'Zabbix service/suppression apply must use detached mode and poll progress.');
  assertIncludes(appText, 'cancelZabbixApply',
    'Zabbix apply UI must let the operator cancel long running graph publication.');
  assertIncludes(appText, 'lastGraphCheckOk',
    'Zabbix apply UI must require a successful graph check before graph publication.');
  assertIncludes(appText, 'Сначала выполните успешную проверку графа без ошибок.',
    'Zabbix apply UI must explain why publication is disabled before a successful graph check.');
  assertIncludes(appText, 'Проверка графа',
    'Zabbix apply UI must show the current graph-check gate state to the operator.');
  assertIncludes(indexText, 'Scope из последних изменений',
    'Zabbix apply UI must show pending dirty scope from recent rule/template changes.');
  assertIncludes(indexText, 'data-zabbix-dirty-scope-use',
    'Zabbix apply UI must let operators paste dirty scope into the publication scope field.');
  assertIncludes(indexText, 'data-zabbix-scope-preview',
    'Zabbix apply UI must let operators preview scope before a long publication run.');
  assertIncludes(indexText, 'data-zabbix-apply-scope-require-match',
    'Zabbix apply UI must expose strict scope matching before publication.');
  assertIncludes(indexText, 'Не запускать, если заполненный scope не найден',
    'strict scope matching must be explained in operator language.');
  assertIncludes(appText, 'previewZabbixApplyScope',
    'Zabbix apply UI must call a lightweight scope preview endpoint.');
  assertIncludes(appText, 'ZABBIX_DIRTY_SCOPE_STORAGE_KEY',
    'dirty Zabbix scope must be persisted in a browser-local journal.');
  assertIncludes(appText, 'saveZabbixDirtyScopeJournal',
    'dirty Zabbix scope changes must be saved to the local journal.');
  assertIncludes(appText, 'loadZabbixDirtyScopeJournal();',
    'dirty Zabbix scope journal must be loaded on UI startup.');
  assertIncludes(appText, 'requireScopeMatch: scope.requireMatch',
    'Zabbix apply and scope preview must send strict scope matching to the backend.');
  assertIncludes(serverText, '/api/zabbix/apply-current/scope-preview',
    'monitoring UI BFF must proxy current apply scope preview.');
  assertIncludes(serverText, 'requireZabbixScopeMatch',
    'monitoring UI BFF must forward strict scope matching to cmdbconfigbuilder.');
  assertIncludes(cmdbConfigBuilderText, '/rules/apply-current/scope-preview',
    'cmdbconfigbuilder must expose a lightweight current apply scope preview endpoint.');
  assertIncludes(cmdbConfigBuilderText, 'RequireZabbixScopeMatch',
    'cmdbconfigbuilder must accept strict scope matching in current apply requests.');
  assertIncludes(cmdbConfigBuilderText, 'CurrentApplyScopeMatchError',
    'cmdbconfigbuilder must stop unmatched strict scope before source-card reads.');
  assertIncludes(appText, 'markZabbixDirtyScopeFromTemplateApplyResult',
    'template materialization must mark affected Zabbix publication scope.');
  assertIncludes(appText, 'markZabbixDirtyScopeForLinkRelationChange',
    'relation changes must mark affected Zabbix publication scope.');
  assertIncludes(appText, 'renderZabbixScopePrefilter',
    'Zabbix apply report must show whether scope reduced preparation before publication.');
  assertIncludes(cmdbConfigBuilderText, 'SelectScopedRulesForCurrentApply',
    'current Zabbix apply must prefilter rules/source classes when scope matches static rule metadata.');
  assertIncludes(cmdbConfigBuilderText, 'ResolveServiceObjectScopeHintsAsync',
    'service object scope must be resolved into related aggregate rule scope before source card reads.');
  assertIncludes(cmdbConfigBuilderText, 'AddServiceObjectTemplateRelations(',
    'service object scope prefilter must account for service object template links.');
  assertIncludes(appText, 'serviceObjectMatchedCount',
    'Zabbix apply report must expose service-object scope prefilter counters.');
  assertIncludes(cmdbConfigBuilderText, 'Scope сократил подготовку',
    'current Zabbix apply scope prefilter must report how much preparation was reduced.');
  assertIncludes(cmdbConfigBuilderText, 'scopePrefilter.Applied',
    'current Zabbix apply must apply the prefiltered rule set only when safe static matching succeeded.');
  assert(api.zabbixScopeKeysFromRule({
    rule_id: 'rule-city48',
    name: 'Рабочие места / City48',
    target: {
      class_code: 'C2M_ServiceWorkplaceFleet',
      idempotency_key: 'service-workplace-city48',
      attribute_mappings: { population_source_key: 'service-workplace-city48' },
      initial_user_values: { name: 'Рабочие места / City48' }
    }
  }, 'service').includes('service-workplace-city48'),
    'dirty Zabbix scope must include stable managed keys from affected rules.');
  assertIncludes(serverText, "...cmdbuildBackendAuthHeaders(request)\n        },\n        body: '{}'",
    'monitoring UI BFF must forward CMDBuild auth overrides to SLA dry-run/apply.');
  assertIncludes(serverText, 'x-cmdb2monitoring-cmdbuild-password',
    'monitoring UI BFF must support forwarding CMDBuild password overrides.');
  assertIncludes(cmdbAggregationBuilderText, 'AddHttpContextAccessor',
    'cmdbaggregation2cmdbuild must support request-scoped CMDBuild auth overrides.');
  assertIncludes(cmdbConfigBuilderText, 'AddHttpContextAccessor',
    'cmdbconfigbuilder must support request-scoped CMDBuild auth overrides.');
  assertIncludes(cmdbuildClientText, 'x-cmdb2monitoring-cmdbuild-password',
    'CmdbuildClient must read request-scoped CMDBuild password overrides.');
  assertIncludes(cmdbuildClientText, 'CurrentOptions()',
    'CmdbuildClient must merge configured and request-scoped CMDBuild options.');
  api.state.zabbixHostIdAttribute = '';
  assert(api.zabbixHostIdAttributeName() === 'zabbix_main_hostid',
    'UI zabbixHostIdAttributeName must default to zabbix_main_hostid.');
  api.state.zabbixHostIdAttribute = 'custom_hostid';
  assert(api.zabbixHostIdAttributeName() === 'custom_hostid',
    'UI zabbixHostIdAttributeName must honor configured readiness attribute.');

  assert(zabbixConfig.ZabbixTriggerDependencies?.AutoReconcileOnMembershipChange === true,
    'zabbixconfig2api must auto-reconcile suppression trigger dependencies after membership changes.');
  assertIncludes(cmdbConfigBuilderText, 'ServiceTemplatesFilePath',
    'cmdbconfigbuilder must have a configured sidecar path for service template relations.');
  assert(zabbixConfig.Apply?.CreateSuppressionServices === false,
    'zabbixconfig2api must not create Zabbix Services for suppression by default.');
  assert(Number.isInteger(zabbixConfig.ZabbixTriggerDependencies?.AutoReconcileDebounceSeconds),
    'suppression trigger dependency auto-reconcile debounce must be configured.');
  assert(!('zabbixTriggerDependencies' in uiConfig),
    'monitoring UI must not keep a separate transitive suppression group dependency depth setting.');
  assert(zabbixConfig.ZabbixTriggerDependencies?.TransitiveGroupDependencyDepth === 2,
    'zabbixconfig2api must default transitive suppression group dependency depth to 2.');
  assert(zabbixConfig.ZabbixTriggerDependencies?.TriggerGetBatchSize === 25,
    'zabbixconfig2api must default Zabbix trigger.get batch size to 25.');
  assert(zabbixConfig.ZabbixTriggerDependencies?.MaxSourceHostsPerAggregate === 1000,
    'zabbixconfig2api must default max source hosts per aggregate.');
  assert(zabbixConfig.ZabbixTriggerDependencies?.MaxAggregateFormulaLength === 65000,
    'zabbixconfig2api must default max aggregate formula length.');
  assert(zabbixConfig.Zabbix?.RequestTimeoutMs === 60000,
    'zabbixconfig2api must use a Zabbix request timeout large enough for dependency reconciliation by default.');
  assert(zabbixConfig.ZabbixTriggerDependencies?.AggregateStateTriggerMinPriority === 3,
    'suppression aggregate group state selector must ignore low-priority secondary triggers by default.');
  assert(zabbixConfig.ZabbixTriggerDependencies?.DependencyTriggerMinPriority === 0,
    'suppression dependency coverage selector must keep all active source triggers by default.');
  const aggregateStateTags = zabbixConfig.ZabbixTriggerDependencies?.AggregateStateTriggerIncludeTags ?? [];
  assert(aggregateStateTags.some((tag) => tag.Tag === 'scope' && tag.Value === 'availability'),
    'suppression aggregate state selector must include scope=availability triggers.');
  assert(!aggregateStateTags.some((tag) => tag.Tag === 'component' && tag.Value === 'health'),
    'suppression aggregate state selector must not require component=health triggers by default.');
  assert(Array.isArray(zabbixConfig.ZabbixTriggerDependencies?.DependencyTriggerIncludeTags),
    'suppression dependency selector must be independently configurable.');
  assertIncludes(appText, 'collectZabbixTriggerDependencyRunPayload()',
    'manual dependency dry-run/apply must be able to send UI overrides.');
  assertIncludes(appText, 'transitiveGroupDependencyDepth: overrideDepth',
    'manual dependency dry-run/apply must send the unsaved UI depth override when present.');
  assertNotIncludes(appText, 'config.zabbixTriggerDependencies',
    'UI must not read transitive depth from monitoring-ui-api config.');
  assertIncludes(appText, 'syncTransitiveGroupDependencyDepthFromPayload(result)',
    'UI must take the effective transitive depth from zabbixconfig2api status/result.');
  assertIncludes(appText, 'zabbixTransitiveGroupDependencyDepth(payload)',
    'UI must show the effective transitive suppression group dependency depth in dependency diagnostics.');
  assertIncludes(appText, 'zabbixAggregateStateReasonText',
    'UI must show whether suppression group state comes from own source hosts or upstream causes.');
  assertIncludes(appText, 'upstream-группы',
    'UI must render upstream cause groups in trigger dependency diagnostics.');
  assertIncludes(appText, 'unsupportedAggregateItemCount',
    'UI must show unsupported aggregate calculated item count in dependency diagnostics.');
  assertIncludes(appText, 'renderUnsupportedAggregateItems',
    'UI must render unsupported aggregate calculated item samples.');
  assertIncludes(appText, 'zabbixAggregateItemStateText',
    'UI must render calculated item state/error details for aggregate samples.');
  assertIncludes(appText, 'aggregateStateTriggerSelector',
    'UI must show the selector used for suppression group state.');
  assertIncludes(appText, 'renderAggregateStateTriggerSettings',
    'UI must render read-only aggregate state trigger selector settings.');
  assertIncludes(appText, 'renderZabbixTriggerDependencyRuntimeSettings',
    'UI must render read-only Zabbix trigger dependency runtime settings.');
  assertIncludes(appText, 'zabbixRequestTimeoutText',
    'UI must show the configured Zabbix request timeout in dependency diagnostics.');
  assertIncludes(appText, 'triggerGetBatchSize',
    'UI must show the configured trigger.get batch size in dependency diagnostics.');
  assertIncludes(appText, 'zabbixLimitText',
    'UI must show aggregate formula complexity values against configured limits.');
  assertIncludes(appText, 'renderZabbixAggregateComplexityMessages',
    'UI must render aggregate formula complexity messages per aggregate sample.');
  assertIncludes(appText, 'aggregateStateTriggerSettings',
    'UI must consume aggregate state trigger selector settings from zabbixconfig2api status.');
  assertIncludes(appText, 'dependencyTriggerSelector',
    'UI must show the selector used for source trigger dependency coverage.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'BuildUpstreamProblemExpression',
    'suppression aggregate triggers must include upstream group conditions.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'TrySelectAggregateStateTrigger',
    'suppression aggregate state trigger selection must be separate from dependency selection.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'AggregateStateTriggerIncludeTags { get; init; } = []',
    'aggregate state include tags must come from configuration, not from a code-level default list that the .NET binder appends to.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'TrySelectDependencyTrigger',
    'suppression source trigger dependency selection must be separate from aggregate state selection.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'LoadAggregateItemDiagnosticsAsync',
    'suppression dependency reconcile must load calculated item diagnostics after Zabbix recalculation.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'EvaluateAggregateComplexityLimits',
    'suppression dependency reconcile must guard aggregate formula complexity before publishing.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'AggregateComplexityWarningRatio',
    'suppression dependency reconcile must warn before aggregate complexity reaches hard limits.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'MaxSourceHostsPerAggregate',
    'suppression dependency reconcile must enforce max source-host count per aggregate.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'MaxAggregateFormulaLength',
    'suppression dependency reconcile must enforce max aggregate formula length.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'UnsupportedAggregateItems',
    'suppression dependency result must expose unsupported aggregate item diagnostics.');
  assertNotIncludes(zabbixTriggerDependencyApplierText, 'AggregateTriggerMinPriority',
    'legacy shared aggregate trigger selector must not return.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'ZabbixTriggerDependencyRunRequest',
    'zabbixconfig2api must accept explicit per-request transitive depth overrides for manual UI runs.');
  assertIncludes(zabbixTriggerDependencyApplierText, 'ApplyRunOverrides',
    'manual dependency runs must apply request overrides without changing saved zabbixconfig2api options.');
  assertNotIncludes(zabbixTriggerDependencyApplierText, 'dependentAggregate.ToTriggerInfo()',
    'suppression aggregate triggers must not be blocked by Zabbix dependencies on upstream groups.');
  assertNotIncludes(zabbixTriggerDependencyApplierText, 'AddTransitiveGroupDependencies',
    'group-to-group propagation must use aggregate expressions instead of extra trigger dependency edges.');
  assertIncludes(zabbixProgramText, 'ZabbixTriggerDependencyReconcileScheduler',
    'zabbixconfig2api must register a suppression trigger dependency reconcile scheduler.');
  assertIncludes(zabbixProgramText, 'aggregateStateTriggerSettings',
    'zabbixconfig2api status must expose aggregate state trigger selector settings for read-only UI.');
  assertIncludes(zabbixProgramText, 'triggerGetBatchSize',
    'zabbixconfig2api status must expose trigger.get batch size for read-only UI.');
  assertIncludes(zabbixProgramText, 'zabbixRequestTimeoutMs',
    'zabbixconfig2api status must expose Zabbix request timeout for read-only UI.');
  assertIncludes(zabbixProgramText, 'maxSourceHostsPerAggregate',
    'zabbixconfig2api status must expose max source hosts per aggregate for read-only UI.');
  assertIncludes(zabbixProgramText, 'maxAggregateFormulaLength',
    'zabbixconfig2api status must expose max aggregate formula length for read-only UI.');
  assertIncludes(zabbixProgramText, 'HasAggregateStateSelector',
    'zabbixconfig2api must validate that aggregate state selector is explicit.');
  assertIncludes(zabbixProgramText, 'HasValidTriggerSelectorRegex',
    'zabbixconfig2api must validate trigger selector regex settings on startup.');
  assertIncludes(zabbixAggregationApplierText, 'RequestSuppressionTriggerDependencyReconcile(command, layer)',
    'suppression Zabbix apply must request trigger dependency reconcile after membership changes.');
  assertIncludes(zabbixAggregationApplierText, 'ShouldCreateManagedServices(layer, options)',
    'suppression Zabbix apply must gate Zabbix Services creation behind configuration.');
  assertIncludes(indexText, 'Zabbix Services по умолчанию не создаются',
    'suppression Zabbix apply UI must explain that suppression does not create Zabbix Services by default.');
  assertIncludes(appText, 'suppression membership',
    'suppression Zabbix apply UI must render membership-oriented status.');
  assertIncludes(zabbixProgramText, 'PendingSources',
    'zabbixconfig2api must retain unready source cards as pending membership diagnostics.');
  assertIncludes(appText, 'pendingSourceCount',
    'UI must display source cards waiting for zabbix_main_hostid.');
}

function assertWebhookManagementContracts() {
  const eventsMatch = serverText.match(/const WEBHOOK_EVENTS = \[([\s\S]*?)\];/);
  assert(eventsMatch, 'server must declare managed webhook events.');
  const eventsBlock = eventsMatch[1];
  for (const { eventType, suffix, cmdbuildEvent } of [
    { eventType: 'CREATE', suffix: 'create', cmdbuildEvent: 'card_create_after' },
    { eventType: 'UPDATE', suffix: 'update', cmdbuildEvent: 'card_update_after' },
    { eventType: 'DELETE', suffix: 'delete', cmdbuildEvent: 'card_delete_after' }
  ]) {
    assertIncludes(eventsBlock, `eventType: '${eventType}'`, `managed webhooks must include ${eventType}.`);
    assertIncludes(eventsBlock, `suffix: '${suffix}'`, `managed webhook ${eventType} must use stable suffix ${suffix}.`);
    assertIncludes(eventsBlock, `cmdbuildEvent: '${cmdbuildEvent}'`, `managed webhook ${eventType} must target ${cmdbuildEvent}.`);
  }

  assertIncludes(serverText, 'for (const sourceClass of sourceClasses)', 'webhook publish must iterate source classes.');
  assertIncludes(serverText, 'for (const event of WEBHOOK_EVENTS)', 'webhook publish must create every required event per class.');
  assertIncludes(serverText, 'code: managedWebhookCode(sourceClass.code, event.suffix)', 'webhook code must be based on class and event.');
  assertIncludes(serverText, 'target: sourceClass.code', 'CMDBuild webhook target must be the source class code.');

  const payloadStart = serverText.indexOf('function buildCmdbuildWebhookPayload');
  const payloadEnd = serverText.indexOf('function webhookTargetHeaders', payloadStart);
  assert(payloadStart >= 0 && payloadEnd > payloadStart, 'server must expose webhook payload builder.');
  const payloadBlock = serverText.slice(payloadStart, payloadEnd);
  assert(!/rule[_-]?id|ruleId/i.test(payloadBlock), 'managed webhook payload must not depend on rule_id.');

  const codeStart = serverText.indexOf('function managedWebhookCode');
  const codeEnd = serverText.indexOf('function webhookCodePrefix', codeStart);
  assert(codeStart >= 0 && codeEnd > codeStart, 'server must expose managed webhook code builder.');
  const codeBlock = serverText.slice(codeStart, codeEnd);
  assert(!/rule/i.test(codeBlock), 'managed webhook code must not be rule-based.');
}

function assertPopulationDimensionUiContracts() {
  assert(!api.templatePopulationControlVisible('legacy', 'source'), 'legacy population must hide source controls.');
  assert(api.templatePopulationControlVisible('source_field', 'source'), 'distinct source field must show source selector.');
  assert(api.templatePopulationControlVisible('source_lookup', 'key'), 'lookup population must show dimension key template.');
  assert(!api.templatePopulationControlVisible('source_lookup', 'regex'), 'lookup population must hide regex controls.');
  assert(api.templatePopulationControlVisible('regex_capture', 'regex'), 'regex capture must show regex input.');
  assert(api.templatePopulationControlVisible('regex_capture', 'capture-group'), 'regex capture must show capture group input.');
  assert(api.templatePopulationControlVisible('range', 'values'), 'range population must show static values input.');
  assert(!api.templatePopulationControlVisible('range', 'source'), 'range population must not require a source field.');
  assert(api.templatePopulationControlVisible('cmdb_reference', 'source'), 'unresolved reference mode must show diagnostic source selector.');
  assert(!api.templatePopulationControlVisible('cmdb_reference', 'key'), 'unresolved reference mode must not show dynamic key templates.');

  const options = [
    fieldOption('criticality', 'lookup', { lookupType: 'Criticality' }),
    fieldOption('enabled', 'boolean'),
    fieldOption('ipAddressDescription', 'string', { resolve: { mode: 'cmdbPath', leafType: 'string' } }),
    fieldOption('roomReference', 'unresolved_reference'),
    fieldOption('roomDomain', 'unresolved_domain')
  ];
  assertValues(
    api.templatePopulationSourceFieldOptionsForType(options, 'source_lookup'),
    ['criticality'],
    'lookup dimension must show only lookup leaf fields.');
  assertValues(
    api.templatePopulationSourceFieldOptionsForType(options, 'source_bool'),
    ['enabled'],
    'boolean dimension must show only boolean leaf fields.');
  assertValues(
    api.templatePopulationSourceFieldOptionsForType(options, 'source_field'),
    ['ipAddressDescription'],
    'distinct source field must show resolved non-enumerated leaf fields.');
  assertValues(
    api.templatePopulationSourceFieldOptionsForType(options, 'regex_capture'),
    ['ipAddressDescription'],
    'regex capture must show resolved non-enumerated leaf fields.');
  assertValues(
    api.templatePopulationSourceFieldOptionsForType(options, 'cmdb_reference'),
    ['roomReference', 'roomDomain'],
    'unresolved reference/domain modes must show only unresolved object links.');
  assertValues(
    api.templatePopulationSourceFieldOptionsForType(options, 'range'),
    [],
    'range dimension must not show source field options.');
  assertValues(
    api.templatePopulationConditionFieldOptionsForType(options, 'source_lookup'),
    ['criticality'],
    'lookup condition field must be typed by lookup leaf fields.');
}

function assertTemplateTargetClassContracts() {
  api.state.classes = [
    { code: 'C2M_Monitoring', layer: '', origin: 'model_root_superclass', isSuperclass: true, attributes: [] },
    { code: 'C2M_ServiceManagedObject', layer: 'Service', isSuperclass: true, parentClassCode: 'C2M_Monitoring', attributes: [
      { code: 'is_critical', type: 'boolean' },
      { code: 'aggregation_type', type: 'lookup', lookupTypeCode: 'ServiceAggregationType' },
      { code: 'threshold', type: 'decimal' },
      { code: 'n', type: 'integer' }
    ] },
    { code: 'C2M_ServiceWorkplaceGroup', layer: 'Service', parentClassCode: 'C2M_ServiceManagedObject', attributes: [] },
    { code: 'C2M_SuppressionManagedObject', layer: 'Suppression', isSuperclass: true, parentClassCode: 'C2M_Monitoring', attributes: [
      { code: 'is_critical', type: 'boolean' },
      { code: 'aggregation_type', type: 'lookup', lookupTypeCode: 'ServiceAggregationType' },
      { code: 'threshold', type: 'decimal' },
      { code: 'n', type: 'integer' }
    ] },
    { code: 'C2M_SuppressionNetworkAccessZone', layer: 'Suppression', parentClassCode: 'C2M_SuppressionManagedObject', attributes: [] }
  ];
  api.state.cmdbClassInstances = [
    { layer: 'Service', classCode: 'C2M_ServiceWorkplaceGroup', cards: [{ id: '101', description: 'Existing workplace group', attributes: [{ code: 'wrong', value: 'value' }] }] },
    { layer: 'Service', classCode: 'C2M_ServicePlatformService', cards: [{ id: '501', description: 'Сервис рабочих мест', attributes: [{ code: 'Code', value: 'workplace-service' }, { code: 'name', value: 'Сервис рабочих мест' }] }] },
    { layer: 'Service', classCode: 'C2M_ServiceSlaPolicy', cards: [{ id: '502', description: 'SLA 99.9', attributes: [{ code: 'Code', value: 'sla-999' }, { code: 'name', value: 'SLA 99.9' }] }] },
    { layer: 'Service', classCode: 'C2M_ServiceSlaCalendar', cards: [{ id: '503', description: '24x7', attributes: [{ code: 'Code', value: 'calendar-24x7' }, { code: 'name', value: '24x7' }] }] },
    {
      layer: 'Service',
      classCode: 'C2M_ServiceNetworkAccessZone',
      cards: [{
        id: '301',
        description: 'City04 network zone',
        attributes: [{ code: 'population_source_key', value: 'network-zone:City04' }]
      }]
    },
    { layer: 'Suppression', classCode: 'C2M_SuppressionNetworkAccessZone', cards: [{ id: '201', description: 'Existing suppression zone' }] }
  ];
  api.state.domains = [{
    code: 'C2M_ServicePlatformServiceHasSlaPolicy',
    relationType: 'has_sla_policy',
    sourceClassCode: 'C2M_ServicePlatformService',
    targetClassCode: 'C2M_ServiceSlaPolicy'
  }, {
    code: 'C2M_ServiceSlaPolicyHasSlaCalendar',
    relationType: 'has_sla_calendar',
    sourceClassCode: 'C2M_ServiceSlaPolicy',
    targetClassCode: 'C2M_ServiceSlaCalendar'
  }, {
    code: 'C2M_ServiceNetworkAccessZoneAggregatesToPlatformService',
    relationType: 'aggregates_to',
    sourceClassCode: 'C2M_ServiceNetworkAccessZone',
    targetClassCode: 'C2M_ServicePlatformService'
  }];
  api.state.suggestedDomains = [];
  api.state.serviceObjectEditor.relations = [{
    domainCode: 'C2M_ServicePlatformServiceHasSlaPolicy',
    relationId: 'r-sla',
    sourceType: 'C2M_ServicePlatformService',
    sourceId: '501',
    destinationType: 'C2M_ServiceSlaPolicy',
    destinationId: '502'
  }, {
    domainCode: 'C2M_ServiceSlaPolicyHasSlaCalendar',
    relationId: 'r-calendar',
    sourceType: 'C2M_ServiceSlaPolicy',
    sourceId: '502',
    destinationType: 'C2M_ServiceSlaCalendar',
    destinationId: '503'
  }, {
    domainCode: 'C2M_ServiceNetworkAccessZoneAggregatesToPlatformService',
    relationId: 'r-aggregate',
    sourceType: 'C2M_ServiceNetworkAccessZone',
    sourceId: '301',
    destinationType: 'C2M_ServicePlatformService',
    destinationId: '501'
  }];
  api.state.ruleExamples = { service: [], suppression: [] };
  api.state.templateDocuments.service = {
    layer: 'service',
    templates: [{
      template_id: 'network-zone-by-city',
      name: 'Рабочие места (Сервис)',
      enabled: true,
      target: { class_code: 'C2M_ServiceNetworkAccessZone' }
    }]
  };

  const templateOptions = api.templateTargetClassOptions('service');
  assert(templateOptions.some((item) => item.value === 'C2M_ServiceWorkplaceGroup'),
    'template target selector must include service target classes.');
  assert(!templateOptions.some((item) => String(item.value).startsWith('instance:')),
    'template target selector must not include concrete CMDBuild instances.');
  assert(api.targetClassOptions('service').some((item) => String(item.value).startsWith('instance:')),
    'static rule target selector must keep concrete CMDBuild instances.');
  api.state.ruleExamples.service = [{
    rule_id: 'generated-workplace-city04',
    generated_from_template: 'workplaces-by-city',
    template_generation: { status: 'managed' },
    target: {
      class_code: 'C2M_ServiceWorkplaceGroup',
      card_id: '101',
      card_description: 'Existing workplace group'
    }
  }, {
    rule_id: 'detached-network-zone-city04',
    name: 'Рабочие места / City04',
    template_generation: { status: 'detached', template_id: 'network-zone-by-city' },
    target: {
      class_code: 'C2M_ServiceNetworkAccessZone',
      idempotency_key: 'network-zone:City04',
      attribute_mappings: { population_source_key: 'network-zone:City04' }
    }
  }, {
    rule_id: 'historical-network-zone-city05',
    generated_from_template: 'historical-workplaces',
    template_generation: { status: 'detached', template_id: 'historical-workplaces', template_name: 'Исторические рабочие места' },
    target: {
      class_code: 'C2M_ServiceNetworkAccessZone',
      idempotency_key: 'network-zone:City05',
      attribute_mappings: { population_source_key: 'network-zone:City05' }
    }
  }];
  const filteredTargetOptions = api.targetClassOptions('service', '', { filterTemplateTargets: true });
  assert(filteredTargetOptions.some((item) => item.value === 'C2M_ServiceWorkplaceGroup'),
    'static rule template filter must keep the target class available for manual rules.');
  assert(!filteredTargetOptions.some((item) => item.value === 'instance:C2M_ServiceWorkplaceGroup:101'),
    'static rule template filter must hide target instances generated from templates.');

  const serviceAttrs = api.targetClassAttributes('C2M_ServiceWorkplaceGroup').map((item) => item.code);
  assert(serviceAttrs.includes('Code') && serviceAttrs.includes('Description'),
    'targetClassAttributes must expose CMDBuild system identity fields even when class attributes omit them.');
  assert(serviceAttrs.includes('aggregation_type') && serviceAttrs.includes('threshold') && serviceAttrs.includes('n'),
    'service template target attributes must come from the class hierarchy and include aggregation fields.');
  assert(!serviceAttrs.includes('wrong'),
    'targetClassAttributes must not prefer stale instance attributes when planned class attributes exist.');
  const suppressionAttrs = api.targetClassAttributes('C2M_SuppressionNetworkAccessZone').map((item) => item.code);
  assert(suppressionAttrs.includes('Code') && suppressionAttrs.includes('Description'),
    'suppression target classes must expose CMDBuild system identity fields for manual rule creation.');
  assert(suppressionAttrs.includes('aggregation_type') && suppressionAttrs.includes('is_critical') && suppressionAttrs.includes('threshold') && suppressionAttrs.includes('n'),
    'suppression template target attributes must include aggregation_type, is_critical, threshold and n.');
  const serviceFallbackDomain = api.fallbackManagedRelationDomain(
    'service',
    'C2M_ServiceComputeCluster',
    'C2M_ServiceNetworkAccessZone',
    'service_depends_on');
  assert(serviceFallbackDomain?.code === 'C2M_ServiceComputeClusterDependsOnServiceNetworkAccessZone',
    'service fallback domains must cover ComputeCluster -> NetworkAccessZone dependency relations.');
  const suppressionFallbackDomain = api.fallbackManagedRelationDomain(
    'suppression',
    'C2M_SuppressionComputeCluster',
    'C2M_SuppressionNetworkAccessZone',
    'depends_on_network');
  assert(suppressionFallbackDomain?.code === 'C2M_SuppressionComputeClusterSuppressesSuppressionNetworkAccessZone',
    'suppression fallback domains must cover ComputeCluster -> NetworkAccessZone suppress relations.');
  const serviceContainmentFallbackDomain = api.fallbackManagedRelationDomain(
    'service',
    'C2M_ServiceNetworkAccessZone',
    'C2M_ServicePlatformService',
    'aggregates_to');
  assert(serviceContainmentFallbackDomain?.code === 'C2M_ServiceNetworkAccessZoneAggregatesToPlatformService',
    'service fallback domains must cover NetworkAccessZone -> PlatformService containment relations.');
  const filteredServiceRelationTargets = api.serviceObjectRelationEndpointOptions(['service_network_access_zone'], { filterTemplateTargets: true });
  assert(!filteredServiceRelationTargets.some((item) => !item.disabled && item.label.includes('City04 network zone')),
    'service object relation filter must hide detached template-generated aggregate cards as selectable options.');
  assert(filteredServiceRelationTargets.some((item) => item.disabled && item.label.includes('скрыто фильтром шаблонов')),
    'service object relation filter must explain when aggregate cards are hidden by the template filter.');
  const unfilteredServiceRelationTargets = api.serviceObjectRelationEndpointOptions(['service_network_access_zone'], { filterTemplateTargets: false });
  assert(unfilteredServiceRelationTargets.some((item) => item.label.includes('City04 network zone')),
    'service object relation filter must allow detached template-generated aggregate cards when disabled.');
  assert(unfilteredServiceRelationTargets.some((item) => item.label.includes('Рабочие места / City04')),
    'service object relation aggregate labels must include the rule/template name that created the card.');
  const serviceTemplateTargets = api.serviceObjectRelationEndpointOptions(['service_template']);
  assert(serviceTemplateTargets.some((item) => item.label.includes('Рабочие места (Сервис)') && item.label.includes('текущих агрегатов 1')),
    'service object relation template selector must list service aggregate templates with current generated aggregate count.');
  assert(serviceTemplateTargets.some((item) => item.label.includes('Шаблон из текущих правил') && item.label.includes('Исторические рабочие места')),
    'service object relation template selector must list historical generated-rule templates that still own aggregate cards.');
  api.state.templateDocuments.service.templates.push({
    template_id: 'empty-network-zone',
    name: 'Пустые сетевые зоны',
    enabled: true,
    target: { class_code: 'C2M_ServiceNetworkAccessZone' }
  });
  api.state.templateDocuments.service.serviceObjectTemplateRelations = [{
    relation_id: 'service-template-link-empty-network-zone',
    relation_kind: 'service_depends_on_template',
    relation_type: 'service_depends_on',
    domain_direction: 'source_to_target',
    source_type: 'service',
    source_class_code: 'C2M_ServicePlatformService',
    source_card_id: '501',
    target_type: 'service_template',
    target_template_id: 'empty-network-zone',
    target_class_code: 'C2M_ServiceNetworkAccessZone'
  }];
  api.state.serviceObjectEditor.filterTemplateRules = true;
  const serviceRelationRows = api.serviceObjectRelationRows();
  assert(serviceRelationRows.length === 4,
    'existing service object relation list must show created and pending template relations even when endpoint selectors hide template-generated aggregate cards.');
  assert(serviceRelationRows.some((row) => row.sourceLabel.includes('Сервис рабочих мест') && row.targetLabel.includes('SLA 99.9')),
    'existing service object relation list must include service-to-SLA links.');
  assert(serviceRelationRows.some((row) => row.sourceLabel.includes('Сервис рабочих мест') && row.targetLabel.includes('City04 network zone')),
    'existing service object relation list must include service links to template-generated aggregates.');
  assert(serviceRelationRows.some((row) =>
    row.pendingTemplate === true
    && row.sourceLabel.includes('Сервис рабочих мест')
    && row.targetLabel.includes('Пустые сетевые зоны')
    && row.targetLabel.includes('текущих агрегатов нет')),
    'existing service object relation list must include saved service-to-template links even before aggregates exist.');
  const serviceGraph = api.relationGraphData('service');
  assert(serviceGraph.nodes.some((node) => node.nodeType === 'service_object' && node.label.includes('SLA 99.9')),
    'relations graph must include SLA policy service objects.');
  assert(serviceGraph.nodes.some((node) => node.nodeType === 'service_object' && node.label.includes('24x7')),
    'relations graph must include SLA calendar service objects.');
  assert(serviceGraph.edges.some((edge) => edge.sourceLabel.includes('Сервис рабочих мест') && edge.targetLabel.includes('SLA 99.9')),
    'relations graph must include service-to-SLA CMDBuild relations.');
  assert(serviceGraph.edges.some((edge) => edge.sourceLabel.includes('Сервис рабочих мест') && edge.targetKind === 'template'),
    'relations graph must collapse service-to-template-generated aggregate relations to the template node.');
  assert(serviceGraph.edges.some((edge) =>
    edge.sourceLabel.includes('Сервис рабочих мест')
    && edge.targetKind === 'template'
    && edge.targetLabel.includes('Пустые сетевые зоны')
    && edge.expectedRelations.length === 0),
    'relations graph must show pending service-to-template links before generated aggregates exist.');
  const editableDescriptionCodes = api.targetObjectEditableAttributes('service', 'C2M_ServiceWorkplaceGroup')
    .map((item) => item.code)
    .filter((code) => String(code).toLowerCase() === 'description');
  assert(editableDescriptionCodes.length === 1 && editableDescriptionCodes[0] === 'Description',
    `static rule target attributes must show only one Description field, got ${JSON.stringify(editableDescriptionCodes)}.`);
  const suppressionInstanceAttrs = api.targetObjectEditableAttributes('suppression', 'C2M_SuppressionNetworkAccessZone', { includeIdentity: false })
    .map((item) => item.code);
  assert(!suppressionInstanceAttrs.includes('Code') && !suppressionInstanceAttrs.includes('Description') && !suppressionInstanceAttrs.includes('name'),
    'static suppression rules for existing target instances must not expose identity fields.');
  assert(suppressionInstanceAttrs.includes('aggregation_type') && suppressionInstanceAttrs.includes('threshold') && suppressionInstanceAttrs.includes('n'),
    'static suppression rules for existing target instances must expose aggregation fields.');
}

function assertUniversalSchemaContracts() {
  const previousActiveLayer = api.state.activeLayer;
  const previousRuleDocument = api.state.ruleDocuments.service;
  const previousTemplateDocument = api.state.templateDocuments.service;
  api.state.activeLayer = 'Service';
  api.state.ruleDocuments.service = {
    rules: [
      templateRule('service-vpn-hub', 'VPN_HUB', 'C2M_ServiceNetworkAccessZone', 'Code')
    ]
  };
  api.state.templateDocuments.service = {
    templates: [
      {
        template_id: 'network-by-source-class',
        enabled: true,
        source_class_regex: '(?i)^VPN_HUB$',
        target: { class_code: 'C2M_ServiceNetworkAccessZone' }
      }
    ]
  };

  const options = api.schemaOptionsBody();
  assert(Array.isArray(options.sourceLinks) && options.sourceLinks.length === 0,
    'universal schema options must not include customer-specific source links from rules/templates.');
  assert(api.automaticSchemaSourceLinks().length === 0,
    'automatic schema source links must stay disabled for the universal schema UI.');
  assert(api.automaticSchemaSourceLinkDomainCodes().length === 0,
    'universal schema apply selection must not auto-select PopulatedFrom<customer-class> domains.');

  api.state.activeLayer = previousActiveLayer;
  api.state.ruleDocuments.service = previousRuleDocument;
  api.state.templateDocuments.service = previousTemplateDocument;
}

function assertRuleDocumentNormalizationContracts() {
  const normalized = api.normalizeRuleDocument({
    version: 'test',
    rules: [
      templateRule('rule', 'ARM', 'C2M_ServiceWorkplaceGroup', 'City04'),
      templateRule('rule', 'TKGK', 'C2M_ServiceWorkplaceGroup', 'City04'),
      templateRule('', 'NTbook', 'C2M_ServiceWorkplaceGroup', 'City04')
    ]
  }, 'service');
  const ids = normalized.rules.map((rule) => rule.rule_id);
  assert(new Set(ids).size === ids.length, 'normalizeRuleDocument must make rule_id values unique.');
  assert(ids.every((id) => id && id !== 'rule'), 'generic rule_id placeholders must be replaced deterministically.');
}

function assertStaticRuleTemplateFilterContracts() {
  const manualRule = {
    rule_id: 'manual-workplace',
    name: 'Ручное правило рабочих мест',
    layer: 'service',
    source: { class_code: 'ARM' },
    target: { class_code: 'C2M_ServiceWorkplaceGroup' }
  };
  const generatedRule = {
    rule_id: 'generated-workplace-city04',
    name: 'Рабочие места / City04',
    layer: 'service',
    generated_from_template: 'workplaces-by-city',
    template_generation: { status: 'managed' },
    source: { class_code: 'ARM' },
    target: { class_code: 'C2M_ServiceWorkplaceGroup' }
  };
  const detachedRule = {
    rule_id: 'detached-workplace-city05',
    name: 'Рабочие места / City05',
    layer: 'service',
    detached_from_template: 'workplaces-by-city',
    template_generation: { status: 'detached', template_id: 'workplaces-by-city' },
    source: { class_code: 'ARM' },
    target: { class_code: 'C2M_ServiceWorkplaceGroup' }
  };
  const cleanupDetachedRule = {
    ...detachedRule,
    rule_id: 'detached-cleanup-city06',
    name: 'Рабочие места / City06',
    detach_reason: 'template_deleted_keep_objects',
    template_generation: { status: 'detached', template_id: 'workplaces-by-city', detach_reason: 'template_deleted_keep_objects' },
    target: { class_code: 'C2M_ServiceWorkplaceGroup', preserve_on_template_delete: true }
  };
  api.state.ruleDocuments.service = api.normalizeRuleDocument({
    version: '1',
    layer: 'service',
    rules: [manualRule, generatedRule, detachedRule, cleanupDetachedRule]
  }, 'service');
  api.state.ruleDocuments.suppression = api.normalizeRuleDocument({
    version: '1',
    layer: 'suppression',
    rules: [{
      ...generatedRule,
      rule_id: 'generated-suppression-city04',
      name: 'Подавление / City04',
      layer: 'suppression',
      target: { class_code: 'C2M_SuppressionNetworkAccessZone' }
    }]
  }, 'suppression');

  const filteredRuleOptions = api.ruleSelectOptions(api.state.ruleDocuments.service.rules, { filterTemplateRules: true });
  assert(filteredRuleOptions.some((item) => item.label.includes('Ручное правило рабочих мест')),
    'static rule filter must keep manual rules in the rule selector.');
  assert(!filteredRuleOptions.some((item) => item.label.includes('Рабочие места / City04')),
    'static rule filter must hide generated template rules in the rule selector.');
  assert(!filteredRuleOptions.some((item) => item.label.includes('Рабочие места / City05')),
    'static rule filter must hide detached historical template rules in the rule selector.');
  assert(!filteredRuleOptions.some((item) => item.label.includes('Рабочие места / City06')),
    'static rule filter must hide cleanup-eligible detached template rules in the rule selector.');

  api.state.ruleEditorFilterTemplateRules.service = true;
  const filteredDeriveOptions = api.deriveCopySourceOptions('rule', 'service');
  assert(filteredDeriveOptions.some((item) => item.label.includes('Ручное правило рабочих мест')),
    'static rule filter must keep manual rules in create-draft source selector.');
  assert(!filteredDeriveOptions.some((item) => item.label.includes('Рабочие места / City04')),
    'static rule filter must hide generated service rules in create-draft source selector.');
  assert(!filteredDeriveOptions.some((item) => item.label.includes('Рабочие места / City05')),
    'static rule filter must hide detached service rules in create-draft source selector.');
  assert(!filteredDeriveOptions.some((item) => item.label.includes('Рабочие места / City06')),
    'static rule filter must hide cleanup-eligible detached service rules in create-draft source selector.');
  assert(!filteredDeriveOptions.some((item) => item.label.includes('Подавление / City04')),
    'static rule filter must hide generated cross-layer rules in create-draft source selector.');

  api.state.ruleEditorFilterTemplateRules.service = false;
  const unfilteredDeriveOptions = api.deriveCopySourceOptions('rule', 'service');
  assert(unfilteredDeriveOptions.some((item) => item.label.includes('Рабочие места / City04')),
    'turning off the static rule filter must expose generated service rules for create-draft.');
  assert(unfilteredDeriveOptions.some((item) => item.label.includes('Рабочие места / City05')),
    'turning off the static rule filter must expose detached service rules for create-draft.');
  assert(unfilteredDeriveOptions.some((item) => item.label.includes('Рабочие места / City06')),
    'turning off the static rule filter must expose cleanup-eligible detached service rules for create-draft.');
  assert(unfilteredDeriveOptions.some((item) => item.label.includes('Подавление / City04')),
    'turning off the static rule filter must expose generated cross-layer rules for create-draft.');
  const cleanupRules = api.detachedTemplateCleanupRules('service');
  assert(cleanupRules.length === 1 && cleanupRules[0].rule_id === 'detached-cleanup-city06',
    'detached template cleanup must propose only rules detached by the keep-objects template deletion mode.');
  api.state.ruleEditorFilterTemplateRules.service = true;
}

function assertTemplateMaterializationContracts() {
  api.state.prefix = 'C2M_';
  api.state.classes = [];
  api.state.domains = [];
  api.state.suggestedDomains = [];
  api.state.cmdbClasses = [
    { code: 'ARM', description: 'АРМ' },
    { code: 'NTbook', description: 'Ноутбук' },
    { code: 'routerG', description: 'Маршрутизатор' }
  ];
  api.state.cmdbClassSchemas = api.state.cmdbClasses.map((item) => ({
    code: item.code,
    attributes: [{ code: 'locationFloorBuildingCity', type: 'string', description: 'City' }]
  }));
  api.state.cmdbClassInstances = [
    { layer: 'Source', classCode: 'ARM', cards: [] },
    { layer: 'Source', classCode: 'NTbook', cards: [{ Code: 'ntbook-1', locationFloorBuildingCity: 'City01' }] },
    { layer: 'Source', classCode: 'routerG', cards: [{ Code: 'router-1', locationFloorBuildingCity: 'City01' }] }
  ];
  api.state.ruleDocuments.suppression = { version: '1', layer: 'suppression', rules: [] };
  api.state.templateDocuments.suppression = {
    version: '1',
    layer: 'suppression',
    templates: [
      materializationTemplate('workplaces', 'Рабочие места', '(?i)^(АРМ|Ноутбук)$', 'C2M_SuppressionResource'),
      {
        ...materializationTemplate('routers', 'МаршрутизаторыSupp', '(?i)^Маршрутизатор$', 'C2M_SuppressionNetworkAccessZone'),
        managed_relations: [{
          kind: 'template',
          relation_role: 'suppresses',
          target_template_id: 'workplaces',
          attributes: {
            match: {
              mode: 'exact',
              source_variable: 'dimension_key',
              target_variable: 'dimension_key'
            }
          }
        }]
      }
    ]
  };

  const plan = api.templateMaterializationPlan('suppression', { safe: true });
  assert(plan.errors.length === 0,
    `empty source class in a broad template regex must not block materialization when other candidates produce rules: ${plan.errors.join('; ')}`);
  assert(plan.warnings.some((item) => item.includes('ARM') && item.includes('нет карточек')),
    'empty source class must be reported as a warning.');
  assert(plan.generatedRules.some((rule) => rule.generated_from_template === 'workplaces' && ruleSource(rule) === 'NTbook'),
    'template target rules must still be generated for source classes with dimension values.');
  const routerRule = plan.generatedRules.find((rule) => rule.generated_from_template === 'routers');
  assert(routerRule && (routerRule.relations ?? []).length === 1,
    'template-template relation must materialize after skipping an empty unrelated source class.');

  api.state.templateDocuments.suppression = {
    version: '1',
    layer: 'suppression',
    templates: [
      materializationTemplate('empty-arm-only', 'Пустой ARM', '(?i)^АРМ$', 'C2M_SuppressionResource')
    ]
  };
  const emptyOnlyPlan = api.templateMaterializationPlan('suppression', { safe: true });
  assert(emptyOnlyPlan.errors.length === 0,
    `a template matching only empty source classes must warn but not block: ${emptyOnlyPlan.errors.join('; ')}`);
  assert(emptyOnlyPlan.warnings.some((item) => item.includes('ARM') && item.includes('нет карточек')),
    'a template matching only empty source classes must keep the no-card message as a warning.');
  assert(emptyOnlyPlan.generatedRules.length === 0,
    'a template matching only empty source classes must not generate rules.');
}

function materializationTemplate(templateId, name, sourceRegex, targetClass) {
  return {
    template_id: templateId,
    name,
    layer: 'suppression',
    enabled: true,
    source_class_regex: sourceRegex,
    population_dimension: {
      enabled: true,
      type: 'source_field',
      source_field: 'locationFloorBuildingCity',
      key_template: '${template.id}:${dimension.key}',
      name_template: '${dimension.value}',
      max_rules: 1000
    },
    target: {
      class_code: targetClass,
      name_template: '${dimension.name}',
      description_template: name,
      population_source_key_template: '${template.id}:${dimension.key}',
      initial_user_values: {}
    },
    variables: []
  };
}

function ruleSource(rule) {
  return rule.source?.class_code ?? rule.sourceClass ?? '';
}

function assertTemplateRelationRegexContracts() {
  const sourceRule = generatedRule('source-city04', 'МаршрутизаторыSupp / City04', 'City04', 'routerG');
  const targetRule = generatedRule('target-city04', 'Маршрутизаторы ядра / City04', 'City04', 'routerCore');
  const otherTargetRule = generatedRule('target-city05', 'Маршрутизаторы ядра / City05', 'City05', 'routerCore');
  const relation = {
    kind: 'template',
    relation_role: 'uses',
    attributes: {
      match: {
        source_variable: 'dimension_key',
        source_pattern: '(?i)^City([0-9]+)$',
        target_variable: 'dimension_key',
        target_pattern: '(?i)^City([0-9]+)$',
        source_filters: [{ variable: 'source_class', regex: '(?i)^routerG$' }],
        target_filters: [{ variable: 'source_class', regex: '(?i)^routerCore$' }]
      }
    }
  };
  assert(api.templateManagedRelationMatchesRulePair(sourceRule, targetRule, relation),
    'template-template relation regex must match equal captured template variables.');
  assert(!api.templateManagedRelationMatchesRulePair(sourceRule, otherTargetRule, relation),
    'template-template relation regex must reject different captured template variables.');

  const comparable = api.relationRegexComparableValue('City33', '(?i)^City([0-9]+)$');
  assert(comparable.matched && comparable.value === '33',
    'relation regex comparison must return the first captured group.');
  const regexItems = api.relationGraphRegexItemsFromRelation(relation);
  assert(regexItems.some((item) => item.includes('source') || item.includes('dimension')),
    'relation graph regex layer must include source-side regex items.');
  assert(regexItems.some((item) => item.includes('цель')),
    'relation graph regex layer must include target-side regex items.');
}

function assertRelationGraphContracts() {
  assert(api.relationGraphRoleEffectDirection('uses') === 'target_to_source',
    'uses relation must reverse to target->source in effect view.');
  assert(api.relationGraphRoleEffectDirection('depends_on') === 'target_to_source',
    'depends_on relation must reverse to target->source in effect view.');
  assert(api.relationGraphRoleEffectDirection('contains') === 'source_to_target',
    'contains relation must remain source->target in effect view.');

  const chip = api.renderRelationGraphRegexChip([
    'класс-источник: (?i)^(Ноутбук|АРМ|Тонкий клиент)$',
    'dimension_key: (?i)^City([0-9]+)$'
  ], 'Регулярные выражения узла');
  assertIncludes(chip, 'relation-graph-tooltip', 'relation graph regex chip must expose full tooltip.');
  assertIncludes(chip, '<code>', 'relation graph regex tooltip must render full regex code entries.');

  const detail = api.relationGraphRuntimeErrorDetails([{
    edge: {
      id: 'edge-1',
      sourceKind: 'template',
      targetKind: 'manual_rule',
      sourceTemplate: { name: 'МаршрутизаторыSupp', template_id: 'supp-routerg' },
      targetRule: { name: 'Маршрутизаторы ядра', rule_id: 'router-core' },
      sourceId: 'template:supp-routerg',
      targetId: 'rule:router-core'
    },
    error: {
      message: 'не найден domain для связи C2M_SuppressionNetworkAccessZone -> C2M_SuppressionNetworkAccessZone',
      sourceRule: { name: 'МаршрутизаторыSupp / City33', rule_id: 'supp-routerg-city33' },
      targetRule: { name: 'Маршрутизаторы ядра', rule_id: 'router-core' }
    }
  }]);
  assertIncludes(detail, 'МаршрутизаторыSupp', 'runtime graph errors must include user-visible source names.');
  assertIncludes(detail, 'Маршрутизаторы ядра', 'runtime graph errors must include user-visible target names.');
  assertNotIncludes(detail, 'template:supp -> rule:rule', 'runtime graph errors must not fall back to raw placeholder ids.');

  const geometry = api.relationGraphEdgeGeometry({
    source: { x: 0, y: 120, width: 300, height: 108 },
    target: { x: 840, y: 120, width: 300, height: 108 }
  }, 0);
  assert((geometry.d.match(/\bL\b/g) ?? []).length >= 4,
    'relation graph edges must be routed as polylines so long arrows do not visually cross nodes.');
  const downtimeLabel = 'Календарь окон обслуживания (C2M_ServiceSlaDowntime #1541178)';
  assert(api.relationGraphNodeDiagnosticLabel({ label: downtimeLabel, objectLabel: downtimeLabel }) === downtimeLabel,
    'relation graph diagnostics must not repeat identical node labels in parentheses.');
  assert(api.relationGraphNodeDiagnosticLabel({ label: 'Календарь окон обслуживания', objectLabel: downtimeLabel }) === downtimeLabel,
    'relation graph diagnostics must prefer the fuller object label instead of duplicating a shorter label.');

  const previousRuleDocument = api.state.ruleDocuments.service;
  const previousTemplateDocument = api.state.templateDocuments.service;
  api.state.templateDocuments.service = { layer: 'service', templates: [] };
  api.state.ruleDocuments.service = api.normalizeRuleDocument({
    version: '1',
    layer: 'service',
    rules: [{
      rule_id: 'manual-service-rule',
      name: 'Ручное правило',
      source: { class_code: 'ManualSource' },
      target: { class_code: 'C2M_ServiceWorkplaceGroup', idempotency_key: 'manual' }
    }, {
      rule_id: 'detached-template-city07',
      name: 'Рабочие места / City07',
      detached_from_template: 'workplaces-by-city',
      template_generation: { status: 'detached', template_id: 'workplaces-by-city' },
      source: { class_code: 'NTbook' },
      target: { class_code: 'C2M_ServiceWorkplaceGroup', idempotency_key: 'workplaces-by-city:City07' }
    }]
  }, 'service');
  const graph = api.relationGraphData('service');
  assert(graph.nodes.some((node) => node.label === 'Ручное правило'),
    'relation graph must keep real manual rules.');
  assert(!graph.nodes.some((node) => node.label === 'Рабочие места / City07'),
    'relation graph must not show detached historical template rules as manual rules.');
  api.state.ruleDocuments.service = previousRuleDocument;
  api.state.templateDocuments.service = previousTemplateDocument;
}

function fieldOption(value, leafKind, extra = {}) {
  return {
    value,
    label: value,
    fieldRule: {
      leafKind,
      leafType: leafKind,
      type: leafKind,
      ...extra
    }
  };
}

function templateRule(ruleId, sourceClass, targetClass, dimension) {
  return {
    rule_id: ruleId,
    name: `${sourceClass} ${dimension}`,
    layer: 'service',
    source: { class_code: sourceClass, key_attribute: 'locationFloorBuildingCity' },
    when: { fieldExists: 'locationFloorBuildingCity' },
    target: {
      class_code: targetClass,
      idempotency_key: `${targetClass}:${dimension}`,
      attribute_mappings: { population_source_key: `${targetClass}:${dimension}` }
    }
  };
}

function generatedRule(ruleId, name, dimensionKey, sourceClass) {
  return {
    rule_id: ruleId,
    name,
    source: { class_code: sourceClass },
    target: { class_code: 'C2M_SuppressionNetworkAccessZone', idempotency_key: ruleId },
    template_generation: {
      dimension_key: dimensionKey,
      dimension_value: dimensionKey,
      dimension_name: dimensionKey,
      variables: { source_class: sourceClass, dimension_key: dimensionKey }
    }
  };
}

function assertValues(items, expected, message) {
  const actual = items.map((item) => item.value);
  assert(actual.length === expected.length && actual.every((value, index) => value === expected[index]),
    `${message} Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}.`);
}

function assertIncludes(text, expected, message) {
  assert(String(text).includes(expected), `${message} Missing '${expected}'.`);
}

function assertNotIncludes(text, expected, message) {
  assert(!String(text).includes(expected), `${message} Unexpected '${expected}'.`);
}

function textBetween(text, startNeedle, endNeedle) {
  const start = text.indexOf(startNeedle);
  if (start < 0) {
    return '';
  }
  const end = text.indexOf(endNeedle, start);
  return end < 0 ? text.slice(start) : text.slice(start, end);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
