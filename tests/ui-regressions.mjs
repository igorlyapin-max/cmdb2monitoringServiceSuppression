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
const zabbixProgramPath = path.join(repoRoot, 'src/zabbixconfig2api/Program.cs');
const zabbixAggregationApplierPath = path.join(repoRoot, 'src/zabbixconfig2api/ZabbixAggregationApplier.cs');

const appText = fs.readFileSync(appPath, 'utf8');
const serverText = fs.readFileSync(serverPath, 'utf8');
const indexText = fs.readFileSync(indexPath, 'utf8');
const stylesText = fs.readFileSync(stylesPath, 'utf8');
const cmdbConfigBuilderText = fs.readFileSync(cmdbConfigBuilderPath, 'utf8');
const zabbixProgramText = fs.readFileSync(zabbixProgramPath, 'utf8');
const zabbixAggregationApplierText = fs.readFileSync(zabbixAggregationApplierPath, 'utf8');
const uiConfig = JSON.parse(fs.readFileSync(uiConfigPath, 'utf8'));
const builderConfig = JSON.parse(fs.readFileSync(builderConfigPath, 'utf8'));
const zabbixConfig = JSON.parse(fs.readFileSync(zabbixConfigPath, 'utf8'));

const api = await loadAppApi();

assertStaticUiContracts();
assertReadinessConfigContracts();
assertWebhookManagementContracts();
assertPopulationDimensionUiContracts();
assertTemplateTargetClassContracts();
assertRuleDocumentNormalizationContracts();
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
    'zabbixHostIdAttributeName'
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
  assertIncludes(indexText, 'Статические правила', 'auto-population menu must be renamed to static rules.');
  assertNotIncludes(indexText, 'Просмотр автонаполнения', 'view-only auto-population menu must not return.');
  assertIncludes(indexText, 'Фильтровать правила и классы из шаблонов', 'static rule template filter label must mention classes.');
  assertIncludes(indexText, 'Создать/обновить правила по шаблонам и связям', 'template apply action must mention links.');
  assertIncludes(indexText, 'id="runTemplateAuditButton"', 'template audit must be available inside template apply menu.');
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
  assertIncludes(indexText, 'zabbix_main_hostid', 'UI must show zabbix_main_hostid readiness attribute.');
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
  api.state.zabbixHostIdAttribute = '';
  assert(api.zabbixHostIdAttributeName() === 'zabbix_main_hostid',
    'UI zabbixHostIdAttributeName must default to zabbix_main_hostid.');
  api.state.zabbixHostIdAttribute = 'custom_hostid';
  assert(api.zabbixHostIdAttributeName() === 'custom_hostid',
    'UI zabbixHostIdAttributeName must honor configured readiness attribute.');

  assert(zabbixConfig.ZabbixTriggerDependencies?.AutoReconcileOnMembershipChange === true,
    'zabbixconfig2api must auto-reconcile suppression trigger dependencies after membership changes.');
  assert(Number.isInteger(zabbixConfig.ZabbixTriggerDependencies?.AutoReconcileDebounceSeconds),
    'suppression trigger dependency auto-reconcile debounce must be configured.');
  assertIncludes(zabbixProgramText, 'ZabbixTriggerDependencyReconcileScheduler',
    'zabbixconfig2api must register a suppression trigger dependency reconcile scheduler.');
  assertIncludes(zabbixAggregationApplierText, 'RequestSuppressionTriggerDependencyReconcile(command, layer)',
    'suppression Zabbix apply must request trigger dependency reconcile after membership changes.');
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
    { layer: 'Suppression', classCode: 'C2M_SuppressionNetworkAccessZone', cards: [{ id: '201', description: 'Existing suppression zone' }] }
  ];
  api.state.ruleExamples = { service: [], suppression: [] };

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
  const editableDescriptionCodes = api.targetObjectEditableAttributes('service', 'C2M_ServiceWorkplaceGroup')
    .map((item) => item.code)
    .filter((code) => String(code).toLowerCase() === 'description');
  assert(editableDescriptionCodes.length === 1 && editableDescriptionCodes[0] === 'Description',
    `static rule target attributes must show only one Description field, got ${JSON.stringify(editableDescriptionCodes)}.`);
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
