const DATA_CACHE_DB = 'cmdb2monitoring-service-suppression';
const DATA_CACHE_STORE = 'dataSourceCache';
const DATA_CACHE_VERSION = 1;
const CACHE_KEYS = {
  cmdbuild: 'cmdbuild.catalogs.v3',
  zabbix: 'zabbix.check',
  webhooks: 'webhooks.check',
  conversionConfig: 'conversion.config'
};
const GENERAL_SETTINGS_STORAGE_KEY = 'cmdb2monitoring.serviceSuppression.generalSettings.v1';
const POPULATION_SOURCE_KEY_ATTRIBUTE = 'population_source_key';
const TARGET_CARD_IDENTITY_ATTRIBUTES = [
  'Code',
  'Description',
  'name'
];
const SERVICE_USER_RESPONSIBILITY_ATTRIBUTES = [
  'is_critical',
  'aggregation_type',
  'threshold',
  'n'
];
const SUPPRESSION_USER_RESPONSIBILITY_ATTRIBUTES = [
  'is_critical',
  'aggregation_type',
  'threshold',
  'n'
];
const TARGET_CARD_SYSTEM_ATTRIBUTES = [
  {
    code: 'Code',
    type: 'string',
    displayName: 'Code',
    description: 'Системный код карточки CMDBuild',
    help: 'Стабильный Code создаваемого целевого объекта. Используется для поиска уже созданной карточки и безопасного повторного применения правила.'
  },
  {
    code: 'Description',
    type: 'string',
    displayName: 'Description',
    description: 'Системное описание карточки CMDBuild',
    help: 'Отображаемое Description создаваемого целевого объекта CMDBuild.'
  }
];
const TEMPLATE_DELETE_MODES = {
  detachRulesKeepObjects: 'detach_rules_keep_objects',
  deleteRulesAndObjects: 'delete_rules_and_objects'
};
const LINK_RELATION_VIEW_CONFIG = {
  serviceTemplateTemplateRelations: { layer: 'service', kind: 'template_template' },
  serviceTemplateRuleRelations: { layer: 'service', kind: 'template_rule' },
  serviceRuleRuleRelations: { layer: 'service', kind: 'rule_rule' },
  suppressionTemplateTemplateRelations: { layer: 'suppression', kind: 'template_template' },
  suppressionTemplateRuleRelations: { layer: 'suppression', kind: 'template_rule' },
  suppressionRuleRuleRelations: { layer: 'suppression', kind: 'rule_rule' }
};
const DEFAULT_TEMPLATE_POPULATION_SOURCE_KEY = '${source.id}';
const TEMPLATE_DIMENSION_DEFAULT_MAX_RULES = 1000;
const TEMPLATE_DIMENSION_MAX_RULES = 10000;
const TEMPLATE_DIMENSION_PREVIEW_LIMIT = 5;
const TEMPLATE_POPULATION_DIMENSION_TYPES = new Set([
  'legacy',
  'source_field',
  'source_lookup',
  'source_bool',
  'cmdb_reference',
  'cmdb_domain',
  'regex_capture',
  'range',
  'static_list'
]);
const DEFAULT_TEMPLATE_POPULATION_DIMENSION = {
  enabled: true,
  type: 'source_field',
  source_field: '',
  values: '',
  regex: '',
  capture_group: '1',
  key_template: '${template.id}:${dimension.key}',
  name_template: '${dimension.value}',
  condition_field: '',
  condition_pattern_template: '',
  max_rules: TEMPLATE_DIMENSION_DEFAULT_MAX_RULES
};
const TEMPLATE_APPLICATION_HISTORY_LIMIT = 20;
const WIDE_CHOICE_MENU_MIN_WIDTH = 520;
const WIDE_CHOICE_MENU_MAX_WIDTH = 760;
const WIDE_CHOICE_MENU_MAX_ITEMS = 120;
const LEGACY_SEEDED_RULE_IDS = new Set([
  'service-workplace-group-by-branch',
  'suppression-network-zone-by-subnet'
]);
const REQUIRED_WEBHOOK_EVENTS = ['CREATE', 'UPDATE', 'DELETE'];

const wideChoiceMenuState = {
  field: null,
  items: [],
  highlightedIndex: -1,
  selecting: false
};

const state = {
  prefix: 'C2M_',
  language: 'ru',
  classes: [],
  domains: [],
  suggestedDomains: [],
  lookups: [],
  modelRoots: [],
  serviceModelRoot: '',
  suppressionModelRoot: '',
  rootClassesByLayer: {
    Service: [],
    Suppression: []
  },
  rootClassErrors: {
    Service: '',
    Suppression: ''
  },
  customEntities: [],
  cmdbClasses: [],
  cmdbClassSchemas: [],
  cmdbDomains: [],
  cmdbSourceDomains: [],
  cmdbClassInstances: [],
  cmdbClassError: '',
  cmdbClassSchemaError: '',
  cmdbDomainError: '',
  cmdbClassInstanceError: '',
  maxTraversalDepth: 2,
  transitiveGroupDependencyDepth: 2,
  ruleExamples: {
    service: [],
    suppression: []
  },
  ruleDocuments: {
    service: null,
    suppression: null
  },
  templateDocuments: {
    service: null,
    suppression: null,
    shared: null
  },
  ruleEditorSuggestions: {
    service: null,
    suppression: null
  },
  ruleEditorSelectionFilters: {
    service: [],
    suppression: []
  },
  ruleEditorFilterTemplateRules: {
    service: true,
    suppression: true
  },
  rulesPreviewSelection: {
    service: null,
    suppression: null
  },
  rulesPreviewSearch: {
    service: { source: '', rules: '', target: '' },
    suppression: { source: '', rules: '', target: '' }
  },
  templateEditorSelectionFilters: {
    service: [],
    suppression: []
  },
  templateEditorTargetValues: {
    service: {},
    suppression: {}
  },
  templatePopulationPreviewLoads: new Set(),
  templateEditorSelected: {
    service: '',
    suppression: ''
  },
  templateEditorStatus: {
    service: { message: '', type: '' },
    suppression: { message: '', type: '' }
  },
  templateApplyMessage: '',
  templateApplyError: '',
  templateApplyLastResult: null,
  templateAudit: {
    checking: false,
    checkedAt: '',
    message: '',
    error: '',
    fingerprint: '',
    result: null
  },
  linkRelationContext: { layer: 'service', kind: 'template_template' },
  linkRelationStatus: { message: '', type: '' },
  linkRelationHideTemplateLinks: true,
  relationGraph: {
    layer: 'service',
    direction: 'configured',
    showRegex: false,
    showDiagnostics: true,
    showOnline: false,
    loadingOnline: false,
    onlineError: '',
    onlineMessage: '',
    onlineCheckedAt: '',
    onlineInstances: [],
    onlineRelations: [],
    filter: ''
  },
  zabbixPreflight: {
    layer: 'all',
    direction: 'effect'
  },
  zabbixApply: {
    service: {
      applying: false,
      loadingStatus: false,
      message: '',
      error: '',
      result: null,
      status: null,
      progress: null,
      planPage: 1
    },
    suppression: {
      applying: false,
      loadingStatus: false,
      message: '',
      error: '',
      result: null,
      status: null,
      progress: null,
      planPage: 1
    }
  },
  zabbixTriggerDependencies: {
    applying: false,
    loadingStatus: false,
    message: '',
    error: '',
    result: null,
    status: null
  },
  ruleEditorTargetValues: {
    service: {},
    suppression: {}
  },
  ruleEditorStatus: {
    service: { message: '', type: '' },
    suppression: { message: '', type: '' }
  },
  applySelection: {
    classes: new Set(),
    domains: new Set()
  },
  applySelectionTouched: {
    classes: new Set(),
    domains: new Set()
  },
  applying: false,
  applyMessage: '',
  applyError: '',
  syncingSources: false,
  loadingSourcesCache: false,
  syncMessage: '',
  syncError: '',
  cmdbCacheUpdatedAt: '',
  checkingZabbix: false,
  loadingZabbixCache: false,
  zabbixCheck: null,
  zabbixCheckError: '',
  zabbixCacheUpdatedAt: '',
  checkingWebhooks: false,
  checkingWebhookRuleCoverage: false,
  loadingWebhooksCache: false,
  publishingWebhooks: false,
  webhooksCheck: null,
  webhooksConfig: {},
  webhooksCheckError: '',
  webhooksPublishMessage: '',
  webhooksPublishError: '',
  webhookRuleCoverage: null,
  webhookRuleCoverageError: '',
  webhooksCacheUpdatedAt: '',
  zabbixHostIdAttribute: 'zabbix_main_hostid',
  checkingHealth: false,
  healthServices: [],
  healthCheckedAt: '',
  healthCheckError: '',
  reloadingAppliers: new Set(),
  applierReloadMessage: '',
  applierReloadError: '',
  kafkaConfig: {},
  loadingKafkaTopics: false,
  loadingKafkaEvents: false,
  kafkaTopics: [],
  kafkaTopicsEnabled: null,
  kafkaTopicsIdentifier: '',
  kafkaTopicsPrefix: '',
  kafkaTopicsCheckedAt: '',
  kafkaTopicsError: '',
  kafkaSelectedTopic: '',
  kafkaEvents: [],
  kafkaEventsCheckedAt: '',
  kafkaEventsError: '',
  kafkaEventLimit: 5,
  conversionConfigStorage: {},
  conversionConfigStorageVersion: 0,
  conversionConfigStorageEtag: '',
  syncingConversionConfigs: false,
  loadingStoredConversionConfigs: false,
  loadingConversionConfigCache: false,
  syncConversionConfigMessage: '',
  syncConversionConfigError: '',
  conversionConfigStorageUpdatedAt: '',
  conversionConfigCacheUpdatedAt: '',
  generalSettingsMessage: '',
  generalSettingsError: '',
  openClassRows: new Set(),
  openDomainRows: new Set(),
  activeLayer: 'Service',
  loading: false,
  error: ''
};

document.querySelectorAll('.nav-item').forEach((button) => {
  button.addEventListener('click', () => {
    void activateView(button.dataset.view, button);
  });
});

document.querySelectorAll('[data-nav-toggle]').forEach((button) => {
  button.addEventListener('click', () => {
    const group = button.closest('.nav-group, .nav-subgroup');
    const isOpen = !group?.classList.contains('open');
    group?.classList.toggle('open', isOpen);
    button.setAttribute('aria-expanded', String(isOpen));
  });
});

document.addEventListener('mousedown', (event) => {
  const menu = event.target.closest('.wide-choice-menu');
  if (menu) {
    return;
  }

  const field = choiceFieldFromTarget(event.target);
  if (!field) {
    hideWideChoiceMenu();
    return;
  }

  if (field.matches('select') && event.button === 0) {
    event.preventDefault();
    field.focus({ preventScroll: true });
    toggleWideChoiceMenu(field);
  }
});

document.addEventListener('focusin', (event) => {
  const field = choiceFieldFromTarget(event.target);
  if (field && !field.matches('select')) {
    showWideChoiceMenu(field);
  }
});

document.addEventListener('input', (event) => {
  if (wideChoiceMenuState.selecting) {
    return;
  }

  const field = choiceFieldFromTarget(event.target);
  if (field) {
    showWideChoiceMenu(field);
  }
});

document.addEventListener('keydown', (event) => {
  handleWideChoiceKeydown(event);
});

document.addEventListener('scroll', () => {
  positionWideChoiceMenu();
}, true);

window.addEventListener('resize', () => {
  positionWideChoiceMenu();
});

document.querySelector('#buildPreviewButton').addEventListener('click', async () => {
  const nextPrefix = document.querySelector('#prefixInput').value;
  if (nextPrefix !== state.prefix) {
    state.cmdbClassInstances = [];
    state.cmdbCacheUpdatedAt = '';
    state.conversionConfigCacheUpdatedAt = '';
    state.conversionConfigStorageUpdatedAt = '';
    state.conversionConfigStorageVersion = 0;
    state.conversionConfigStorageEtag = '';
    resetApplySelection();
  }

  state.prefix = nextPrefix;
  state.language = document.querySelector('#languageSelect').value;
  syncModelRootsFromInputs();
  await loadPreview();
});

document.querySelector('#sendSelectedButton').addEventListener('click', async () => {
  await applySelectedSchema();
});

document.querySelector('#languageSelect').addEventListener('change', async (event) => {
  const previousDefault = defaultModelRoot(state.language);
  syncModelRootsFromInputs();
  state.language = event.target.value;
  applyLanguageDefaultModelRoots(previousDefault);
  applyModelRootInputs();
  await loadPreview();
});

document.querySelector('#maxTraversalDepthSelect').addEventListener('change', (event) => {
  state.maxTraversalDepth = clampNumber(Number(event.target.value), 2, 2, 5);
  event.target.value = String(state.maxTraversalDepth);
  state.generalSettingsMessage = '';
  state.generalSettingsError = '';
  renderRuleEditors();
  renderGeneralSettingsView();
  renderConversionConfigSyncView();
});

document.querySelector('#saveGeneralSettingsButton').addEventListener('click', () => {
  saveGeneralSettings();
});

document.querySelector('#loadGeneralSettingsButton').addEventListener('click', () => {
  loadGeneralSettings();
});

document.querySelector('#syncSourcesButton').addEventListener('click', async () => {
  await syncDataSources();
});

document.querySelector('#loadCachedSourcesButton').addEventListener('click', async () => {
  await loadCmdbSourceCache();
});

document.querySelector('#syncZabbixButton').addEventListener('click', async () => {
  await checkZabbixSource();
});

document.querySelector('#loadCachedZabbixButton').addEventListener('click', async () => {
  await loadZabbixSourceCache();
});

document.querySelector('#syncWebhooksButton').addEventListener('click', async () => {
  await checkWebhooksSource();
});

document.querySelector('#checkWebhookRulesButton').addEventListener('click', async () => {
  await checkWebhooksAgainstConversionRules();
});

document.querySelector('#publishWebhooksButton').addEventListener('click', async () => {
  await publishWebhooksToCmdbuild();
});

document.querySelector('#loadCachedWebhooksButton').addEventListener('click', async () => {
  await loadWebhooksSourceCache();
});

document.querySelector('#refreshDashboardHealthButton').addEventListener('click', async () => {
  await checkHealthServices();
});

document.querySelector('#dashboardHealthList').addEventListener('click', async (event) => {
  const button = event.target.closest('[data-applier-reload]');
  if (!button) {
    return;
  }

  await reloadApplierConfiguration(button.dataset.applierReload);
});

document.querySelector('#refreshKafkaTopicsButton').addEventListener('click', async () => {
  await loadKafkaTopics({ refreshEvents: true });
});

document.querySelector('#refreshKafkaEventsButton').addEventListener('click', async () => {
  await loadKafkaEvents();
});

document.querySelector('#kafkaTopicSelect').addEventListener('change', async (event) => {
  state.kafkaSelectedTopic = event.target.value;
  await loadKafkaEvents();
});

document.querySelector('#kafkaEventLimitInput').addEventListener('change', async (event) => {
  state.kafkaEventLimit = clampNumber(Number(event.target.value), 5, 1, 100);
  event.target.value = String(state.kafkaEventLimit);
  await loadKafkaEvents();
});

document.querySelector('#eventsView').addEventListener('click', async (event) => {
  const topicButton = event.target.closest('[data-kafka-topic]');
  if (!topicButton) {
    return;
  }

  state.kafkaSelectedTopic = topicButton.dataset.kafkaTopic;
  await loadKafkaEvents();
});

document.querySelector('#syncConversionConfigButton').addEventListener('click', () => {
  void saveConversionConfigsToFolder();
});

document.querySelector('#loadStoredConversionConfigButton').addEventListener('click', async () => {
  await loadStoredConversionConfigs();
});

document.querySelector('#loadCachedConversionConfigButton').addEventListener('click', async () => {
  await loadConversionConfigCache();
});

document.querySelector('#addEntityButton').addEventListener('click', async () => {
  if (addCustomEntity()) {
    await loadPreview();
  }
});

document.querySelector('#customEntityList').addEventListener('click', async (event) => {
  const button = event.target.closest('[data-remove-entity]');
  if (!button) {
    return;
  }

  const index = Number(button.dataset.removeEntity);
  if (Number.isInteger(index)) {
    state.customEntities.splice(index, 1);
    await loadPreview();
  }
});

document.querySelector('#schemaView').addEventListener('click', async (event) => {
  if (event.target.closest('[data-apply-select]')) {
    event.stopPropagation();
  }
});

document.querySelector('#schemaView').addEventListener('change', (event) => {
  const checkbox = event.target.closest('[data-apply-select]');
  if (!checkbox) {
    return;
  }

  updateApplySelection(checkbox.dataset.applyKind, checkbox.dataset.applyCode, checkbox.checked);
  render();
});

document.querySelector('#schemaView').addEventListener('toggle', (event) => {
  if (event.target.matches('details[data-class-code]')) {
    updateOpenSet(state.openClassRows, event.target.dataset.classCode, event.target.open);
    return;
  }

  if (event.target.matches('details[data-domain-code]')) {
    updateOpenSet(state.openDomainRows, event.target.dataset.domainCode, event.target.open);
  }
}, true);

document.querySelectorAll('[data-rules-preview-layer]').forEach((panel) => {
  panel.addEventListener('click', (event) => {
    handleRulesPreviewClick(panel.dataset.rulesPreviewLayer, event);
  });
  panel.addEventListener('input', (event) => {
    handleRulesPreviewInput(panel.dataset.rulesPreviewLayer, event.target);
  });
});

document.querySelectorAll('[data-rule-editor-layer]').forEach((panel) => {
  panel.addEventListener('click', (event) => {
    handleRuleEditorClick(panel.dataset.ruleEditorLayer, event);
  });
  panel.addEventListener('change', (event) => {
    handleRuleEditorChange(panel.dataset.ruleEditorLayer, event.target);
  });
  panel.addEventListener('input', (event) => {
    if (event.target.matches('[data-rule-source-class]')) {
      renderRuleSourceFieldOptions(panel.dataset.ruleEditorLayer);
      renderRuleSelectionFilterList(panel.dataset.ruleEditorLayer);
      return;
    }

    if (event.target.matches('[data-selection-filter-field], [data-selection-filter-regex]')) {
      ensureSelectionFilterDraftRow(panel.dataset.ruleEditorLayer, 'rule');
      state.ruleEditorSelectionFilters[panel.dataset.ruleEditorLayer] = selectionFilterRowsFromDom(
        ruleEditorConfig(panel.dataset.ruleEditorLayer).selectionFilterList);
      return;
    }

    if (event.target.matches('[data-rule-target-value]')) {
      const config = ruleEditorConfig(panel.dataset.ruleEditorLayer);
      state.ruleEditorTargetValues[panel.dataset.ruleEditorLayer] = ruleTargetValuesFromDom(config.attributeList);
    }
  });
});

document.querySelectorAll('[data-rule-apply]').forEach((button) => {
  button.addEventListener('click', () => {
    const panel = button.closest('[data-rule-editor-layer]');
    if (panel) {
      void applyRuleEditorChange(panel.dataset.ruleEditorLayer);
    }
  });
});

document.querySelectorAll('[data-template-editor-layer]').forEach((panel) => {
  panel.addEventListener('click', (event) => {
    if (event.target.matches('[data-template-copy-source-field], [data-template-copy-source-expression]')) {
      void copyTemplateSourceField(panel.dataset.templateEditorLayer, event.target);
    }
  });
  panel.addEventListener('change', (event) => {
    handleTemplateEditorChange(panel.dataset.templateEditorLayer, event.target);
  });
  panel.addEventListener('input', (event) => {
    const layerKey = panel.dataset.templateEditorLayer;
    if (event.target.matches('[data-template-source-regex]')) {
      renderTemplateSourceFieldOptions(layerKey);
      renderTemplateSourceFieldHelper(layerKey);
      renderTemplatePopulationDimensionOptions(layerKey);
      renderTemplateSelectionFilterList(layerKey);
      return;
    }

    if (event.target.matches('[data-template-population-key-template]')) {
      const config = templateEditorConfig(layerKey);
      if (config.sourceKey) {
        config.sourceKey.value = event.target.value.trim() || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template;
      }
    }

    if (event.target.matches([
      '[data-template-population-source-field]',
      '[data-template-population-values]',
      '[data-template-population-regex]',
      '[data-template-population-capture-group]',
      '[data-template-population-key-template]',
      '[data-template-population-name-template]',
      '[data-template-population-condition-field]',
      '[data-template-population-condition-pattern]',
      '[data-template-population-max-rules]',
      '[data-template-variable-name]',
      '[data-template-variable-value]'
    ].join(', '))) {
      renderTemplatePopulationDimensionPreview(layerKey);
    } else {
      const config = templateEditorConfig(layerKey);
      if (event.target === config.id || event.target === config.name) {
        renderTemplatePopulationDimensionPreview(layerKey);
      }
    }

    if (event.target.matches('[data-selection-filter-field], [data-selection-filter-regex]')) {
      ensureSelectionFilterDraftRow(layerKey, 'template');
      state.templateEditorSelectionFilters[layerKey] = selectionFilterRowsFromDom(
        templateEditorConfig(layerKey).selectionFilterList);
      return;
    }

    if (event.target.matches('[data-template-variable-name], [data-template-variable-value]')) {
      ensureTemplateVariableDraftRow(layerKey);
    }

    if (event.target.matches('[data-template-target-value]')) {
      state.templateEditorTargetValues[layerKey] = templateTargetValuesFromDom(
        templateEditorConfig(layerKey).targetAttributeList);
    }
  });
});

document.querySelectorAll('[data-template-save]').forEach((button) => {
  button.addEventListener('click', () => {
    const panel = button.closest('[data-template-editor-layer]');
    if (panel) {
      saveTemplateEditorChange(panel.dataset.templateEditorLayer);
    }
  });
});

document.querySelectorAll('[data-template-delete]').forEach((button) => {
  button.addEventListener('click', () => {
    const panel = button.closest('[data-template-editor-layer]');
    if (panel) {
      deleteTemplateEditorSelection(panel.dataset.templateEditorLayer);
    }
  });
});

document.querySelector('#applyTemplatesButton')?.addEventListener('click', () => {
  void applyTemplatesToRuleDocuments();
});

document.querySelector('#runTemplateAuditButton')?.addEventListener('click', () => {
  void runTemplateAudit();
});

document.querySelectorAll('[data-zabbix-apply-layer]').forEach((panel) => {
  const layerKey = panel.dataset.zabbixApplyLayer;
  panel.querySelector('[data-zabbix-apply-refresh]')?.addEventListener('click', () => {
    void loadZabbixApplyStatus(layerKey);
  });
  panel.querySelector('[data-zabbix-apply-dry-run]')?.addEventListener('click', () => {
    void applyZabbixLayer(layerKey, { dryRun: true });
  });
  panel.querySelector('[data-zabbix-apply-publish]')?.addEventListener('click', () => {
    void applyZabbixLayer(layerKey, { dryRun: false });
  });
  panel.addEventListener('click', (event) => {
    const button = event.target.closest?.('[data-zabbix-plan-page]');
    if (!button || !panel.contains(button)) {
      return;
    }

    const page = Number(button.dataset.zabbixPlanPage);
    if (!Number.isInteger(page) || page < 1) {
      return;
    }

    zabbixApplyState(layerKey).planPage = page;
    renderZabbixApplyView(layerKey);
  });
});

document.querySelector('#zabbixTriggerDependenciesRefreshButton')?.addEventListener('click', () => {
  void loadZabbixTriggerDependenciesStatus();
});

document.querySelector('#zabbixTriggerDependenciesDryRunButton')?.addEventListener('click', () => {
  void runZabbixTriggerDependencies({ dryRun: true });
});

document.querySelector('#zabbixTriggerDependenciesApplyButton')?.addEventListener('click', () => {
  void runZabbixTriggerDependencies({ dryRun: false });
});

document.querySelector('#relationAddButton')?.addEventListener('click', () => {
  applyLinkRelationEditorChange();
});

document.querySelector('#relationSourceSelect')?.addEventListener('change', () => {
  const context = state.linkRelationContext ?? { layer: 'service', kind: 'template_template' };
  ensureLinkRelationMixedPairSelection(linkRelationEditorConfig(), linkRelationKindConfig(context.kind, context.layer));
  renderLinkRelationVariableControls();
});

document.querySelector('#relationTargetSelect')?.addEventListener('change', () => {
  const context = state.linkRelationContext ?? { layer: 'service', kind: 'template_template' };
  ensureLinkRelationMixedPairSelection(linkRelationEditorConfig(), linkRelationKindConfig(context.kind, context.layer));
  renderLinkRelationVariableControls();
});

document.querySelector('#relationHideTemplateLinks')?.addEventListener('change', (event) => {
  state.linkRelationHideTemplateLinks = event.target.checked;
  renderLinkRelationEditor();
});

document.querySelector('#relationGraphLayerSelect')?.addEventListener('change', (event) => {
  state.relationGraph.layer = event.target.value === 'suppression' ? 'suppression' : 'service';
  renderRelationsGraphView();
});

document.querySelector('#relationGraphDirectionSelect')?.addEventListener('change', (event) => {
  state.relationGraph.direction = event.target.value === 'effect' ? 'effect' : 'configured';
  renderRelationsGraphView();
});

document.querySelector('#relationGraphShowRegex')?.addEventListener('change', (event) => {
  state.relationGraph.showRegex = event.target.checked;
  renderRelationsGraphView();
});

document.querySelector('#relationGraphShowDiagnostics')?.addEventListener('change', (event) => {
  state.relationGraph.showDiagnostics = event.target.checked;
  renderRelationsGraphView();
});

document.querySelector('#relationGraphShowOnline')?.addEventListener('change', (event) => {
  state.relationGraph.showOnline = event.target.checked;
  if (state.relationGraph.showOnline && !state.relationGraph.onlineCheckedAt && !state.relationGraph.loadingOnline) {
    void refreshRelationGraphOnlineLayer();
    return;
  }

  renderRelationsGraphView();
});

document.querySelector('#relationGraphRefreshOnlineButton')?.addEventListener('click', () => {
  state.relationGraph.showOnline = true;
  void refreshRelationGraphOnlineLayer();
});

document.querySelector('#relationGraphFilterInput')?.addEventListener('input', (event) => {
  state.relationGraph.filter = event.target.value;
  renderRelationsGraphView();
});

document.querySelector('#zabbixPreflightLayerSelect')?.addEventListener('change', (event) => {
  state.zabbixPreflight.layer = ['all', 'service', 'suppression'].includes(event.target.value)
    ? event.target.value
    : 'all';
  renderZabbixPreflightView();
});

document.querySelector('#zabbixPreflightDirectionSelect')?.addEventListener('change', (event) => {
  state.zabbixPreflight.direction = event.target.value === 'configured' ? 'configured' : 'effect';
  renderZabbixPreflightView();
});

document.querySelector('#zabbixPreflightRefreshButton')?.addEventListener('click', () => {
  renderZabbixPreflightView();
});

document.querySelector('#relationManagementView')?.addEventListener('input', (event) => {
  handleLinkRelationFilterInput(event.target);
});

document.querySelector('#relationManagementView')?.addEventListener('change', (event) => {
  handleLinkRelationFilterInput(event.target);
});

document.addEventListener('click', (event) => {
  const button = event.target.closest('[data-detach-template-rule]');
  if (!button) {
    return;
  }

  detachGeneratedRuleFromTemplate(button.dataset.layer, button.dataset.ruleId);
});

document.addEventListener('click', (event) => {
  const button = event.target.closest('[data-link-delete]');
  if (!button) {
    return;
  }

  deleteLinkRelation(button.dataset.layer, button.dataset.kind, button.dataset.sourceId, button.dataset.managedKey, button.dataset.sourceType);
});

await loadInitialConfig();
loadGeneralSettings({ silent: true });
await loadCmdbSourceCache({ silent: true });
await loadZabbixSourceCache({ silent: true });
await loadWebhooksSourceCache({ silent: true });
await loadPreview({ refreshCmdbDomains: false });
await loadConversionConfigCache({ silent: true });
render();
void checkHealthServices({ silent: true });

async function activateView(view, activeButton = null) {
  document.querySelectorAll('.nav-item').forEach((button) => {
    button.classList.toggle('active', activeButton ? button === activeButton : button.dataset.view === view);
  });

  document.querySelectorAll('.view').forEach((section) => {
    section.classList.add('hidden');
  });

  if (view === 'serviceSchema' || view === 'suppressionSchema') {
    state.activeLayer = view === 'serviceSchema' ? 'Service' : 'Suppression';
    document.querySelector('#schemaView').classList.remove('hidden');
    await loadPreview();
    return;
  }

  const linkContext = linkRelationViewContext(view);
  if (linkContext) {
    state.linkRelationContext = linkContext;
    state.linkRelationStatus = { message: '', type: '' };
    document.querySelector('#relationManagementView')?.classList.remove('hidden');
    renderLinkRelationEditor();
    return;
  }

  document.querySelector(`#${view}View`).classList.remove('hidden');
  if (view === 'serviceTemplates') {
    renderTemplateEditor('service');
  } else if (view === 'suppressionTemplates') {
    renderTemplateEditor('suppression');
  } else if (view === 'templateApply') {
    renderTemplateApplyView();
  } else if (view === 'relationsGraph') {
    renderRelationsGraphView();
  } else if (view === 'zabbixPreflight') {
    renderZabbixPreflightView();
  } else if (view === 'generalSettings') {
    renderGeneralSettingsView();
    if (!state.zabbixTriggerDependencies.status && !state.zabbixTriggerDependencies.loadingStatus) {
      void loadZabbixTriggerDependenciesStatus({ renderDependenciesView: false });
    }
  } else if (view === 'serviceZabbixApply') {
    renderZabbixApplyView('service');
    if (!state.zabbixApply.service.status && !state.zabbixApply.service.loadingStatus) {
      void loadZabbixApplyStatus('service');
    }
  } else if (view === 'suppressionZabbixApply') {
    renderZabbixApplyView('suppression');
    if (!state.zabbixApply.suppression.status && !state.zabbixApply.suppression.loadingStatus) {
      void loadZabbixApplyStatus('suppression');
    }
    renderZabbixTriggerDependenciesView();
    if (!state.zabbixTriggerDependencies.status && !state.zabbixTriggerDependencies.loadingStatus) {
      void loadZabbixTriggerDependenciesStatus();
    }
  } else if (view === 'dashboard' && state.healthServices.length === 0 && !state.checkingHealth) {
    void checkHealthServices({ silent: true });
  } else if (view === 'events' && state.kafkaTopics.length === 0 && !state.loadingKafkaTopics) {
    void loadKafkaTopics({ refreshEvents: true });
  }
}

async function loadInitialConfig() {
  try {
    const response = await fetch('/api/config');
    if (!response.ok) {
      throw new Error(`запрос конфигурации не выполнен: ${response.status}`);
    }

    const config = await response.json();
    state.prefix = config.cmdbuildSchema?.defaultPrefix ?? state.prefix;
    state.language = String(config.cmdbuildSchema?.defaultLanguage ?? state.language).toLowerCase();
    state.webhooksConfig = config.webhooks ?? {};
    state.kafkaConfig = config.kafka ?? {};
    state.conversionConfigStorage = config.conversionConfig ?? {};
    state.zabbixHostIdAttribute = config.readiness?.zabbixHostIdAttribute ?? state.zabbixHostIdAttribute;
    state.kafkaEventLimit = clampNumber(Number(state.kafkaConfig.defaultEventLimit), 5, 1, 100);
    document.querySelector('#kafkaEventLimitInput').value = String(state.kafkaEventLimit);
    renderGeneralSettingsView();
    applyLanguageDefaultModelRoots();
    document.querySelector('#prefixInput').value = state.prefix;
    document.querySelector('#languageSelect').value = state.language;
    applyModelRootInputs();
  } catch (error) {
    state.error = error.message;
    render();
  }
}

async function loadPreview(options = {}) {
  const preserveClassCodes = options.preserveClassCodes ?? [];
  syncModelRootsFromInputs();
  const loaders = [loadModelRootClasses()];
  if (options.refreshCmdbDomains === true) {
    loaders.push(loadCmdbDomains());
  }
  await Promise.all(loaders);
  state.loading = true;
  state.error = '';
  state.applyError = '';
  if (options.renderLoading !== false) {
    render();
  }

  try {
    const response = await fetch('/api/schema/preview', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json'
      },
      body: JSON.stringify(schemaOptionsBody())
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `предпросмотр схемы не выполнен: ${response.status}`);
    }

    const preview = await response.json();
    state.classes = preview.classes ?? [];
    state.domains = preview.domains ?? [];
    state.suggestedDomains = preview.suggestedDomains ?? [];
    state.lookups = preview.lookups ?? [];
    state.modelRoots = preview.modelRoots ?? [];
    applyPreviewModelRoots();
    applyModelRootInputs();
  } catch (error) {
    state.classes = [];
    state.domains = [];
    state.suggestedDomains = [];
    state.lookups = [];
    state.modelRoots = [];
    state.error = error.message;
  } finally {
    preserveClassCodes
      .filter((code) => code)
      .forEach((code) => state.openClassRows.add(code));
    state.loading = false;
    render();
  }
}

function schemaOptionsBody() {
  return {
    prefix: state.prefix,
    language: normalizeLanguage(state.language),
    serviceModelRoot: state.serviceModelRoot,
    suppressionModelRoot: state.suppressionModelRoot,
    existingModelClasses: existingModelClassOptions(),
    customEntities: state.customEntities,
    sourceLinks: automaticSchemaSourceLinks()
  };
}

async function applySelectedSchema() {
  rememberOpenRows();
  syncModelRootsFromInputs();
  const selection = selectedApplyObjects();
  if (selection.classes.length === 0 && selection.domains.length === 0) {
    state.applyMessage = '';
    state.applyError = 'Выберите хотя бы один класс или домен.';
    render();
    return;
  }

  state.applying = true;
  state.applyMessage = 'Отправка выбранных объектов схемы в CMDBuild...';
  state.applyError = '';
  render();

  try {
    const response = await fetch('/api/schema/apply', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json'
      },
      body: JSON.stringify({
        options: schemaOptionsBody(),
        selection
      })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      const detail = payload.detail || payload.error || `применение схемы не выполнено: ${response.status}`;
      throw new Error(detail);
    }

    state.applyMessage = applyResultMessage(payload);
    state.applyError = '';
    await loadPreview({ refreshCmdbDomains: true });
    state.applyMessage = applyResultMessage(payload);
  } catch (error) {
    state.applyMessage = '';
    state.applyError = error.message;
  } finally {
    state.applying = false;
    render();
  }
}

async function checkHealthServices(options = {}) {
  const silent = options.silent === true;
  state.checkingHealth = true;
  state.healthCheckError = '';
  if (!silent) {
    renderDashboardView();
  }

  try {
    const response = await fetch('/api/health/services', {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || `проверка состояния не выполнена: ${response.status}`);
    }

    state.healthServices = Array.isArray(payload.services) ? payload.services : [];
    state.healthCheckedAt = payload.checkedAt ?? new Date().toISOString();
    state.healthCheckError = '';
  } catch (error) {
    state.healthServices = [];
    state.healthCheckError = error.message;
  } finally {
    state.checkingHealth = false;
    renderDashboardView();
  }
}

async function reloadApplierConfiguration(applierId) {
  const normalizedId = String(applierId ?? '');
  if (!normalizedId || state.reloadingAppliers.has(normalizedId)) {
    return;
  }

  state.reloadingAppliers.add(normalizedId);
  state.applierReloadMessage = '';
  state.applierReloadError = '';
  renderDashboardView();

  try {
    const response = await fetch(`/api/appliers/${encodeURIComponent(normalizedId)}/configuration/reload`, {
      method: 'POST',
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok || payload.success === false) {
      throw new Error(payload.detail || payload.error || `перечитывание конфигурации не выполнено: ${response.status}`);
    }

    upsertReloadedApplierHealthService(normalizedId, payload);
    const appVersion = payload.version ?? payload.payload?.version ?? payload.payload?.Version;
    const configVersion = payload.configurationVersion ?? payload.payload?.configurationVersion ?? payload.payload?.ConfigurationVersion;
    const versionText = [
      appVersion ? `версия приложения ${appVersion}` : '',
      configVersion ? `конфигурация v${configVersion}` : ''
    ].filter(Boolean).join(', ');
    state.applierReloadMessage = `Конфигурация ${payload.name || normalizedId} перечитана${versionText ? `, ${versionText}` : ''}.`;
    state.applierReloadError = '';
    renderDashboardView();
    await checkHealthServices({ silent: true });
  } catch (error) {
    state.applierReloadMessage = '';
    state.applierReloadError = error.message;
  } finally {
    state.reloadingAppliers.delete(normalizedId);
    renderDashboardView();
  }
}

function upsertReloadedApplierHealthService(applierId, reloadResult) {
  const payload = reloadResult.payload ?? {};
  const existingIndex = state.healthServices.findIndex((service) => service.id === applierId);
  const existing = existingIndex >= 0 ? state.healthServices[existingIndex] : {};
  const next = {
    ...existing,
    id: applierId,
    name: reloadResult.name ?? existing.name ?? applierId,
    canReloadConfiguration: existing.canReloadConfiguration ?? true,
    status: 'ok',
    httpStatus: 200,
    payload: {
      ...(existing.payload ?? {}),
      ...payload
    },
    error: ''
  };

  if (existingIndex >= 0) {
    state.healthServices.splice(existingIndex, 1, next);
  } else {
    state.healthServices.push(next);
  }
  state.healthCheckedAt = reloadResult.configurationReloadedAt || new Date().toISOString();
}

async function loadKafkaTopics(options = {}) {
  state.loadingKafkaTopics = true;
  state.kafkaTopicsError = '';
  renderEventsView();

  try {
    const response = await fetch('/api/kafka/topics', {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || `запрос Kafka-топиков не выполнен: ${response.status}`);
    }

    state.kafkaTopics = Array.isArray(payload.topics) ? payload.topics : [];
    state.kafkaTopicsEnabled = payload.enabled ?? null;
    state.kafkaTopicsIdentifier = payload.managedIdentifier ?? state.kafkaConfig.managedIdentifier ?? '';
    state.kafkaTopicsPrefix = payload.managedPrefix ?? state.kafkaConfig.managedTopicPrefix ?? '';
    state.kafkaTopicsCheckedAt = payload.checkedAtUtc ?? payload.checkedAt ?? new Date().toISOString();
    state.kafkaTopicsError = payload.error ?? '';
    if (!state.kafkaSelectedTopic || !state.kafkaTopics.some((item) => item.name === state.kafkaSelectedTopic)) {
      state.kafkaSelectedTopic = state.kafkaTopics[0]?.name ?? '';
    }

    if (options.refreshEvents === true && state.kafkaSelectedTopic) {
      await loadKafkaEvents({ preserveTopicRender: true });
    }
  } catch (error) {
    state.kafkaTopics = [];
    state.kafkaTopicsError = error.message;
  } finally {
    state.loadingKafkaTopics = false;
    renderEventsView();
  }
}

async function loadKafkaEvents(options = {}) {
  if (!state.kafkaSelectedTopic) {
    state.kafkaEvents = [];
    state.kafkaEventsError = state.kafkaTopics.length === 0 ? 'Нет доступных управляемых Kafka-топиков.' : '';
    renderEventsView();
    return;
  }

  state.loadingKafkaEvents = true;
  state.kafkaEventsError = '';
  if (options.preserveTopicRender !== true) {
    renderEventsView();
  }

  try {
    const response = await fetch(`/api/kafka/topics/${encodeURIComponent(state.kafkaSelectedTopic)}/events?limit=${encodeURIComponent(state.kafkaEventLimit)}`, {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || `запрос событий Kafka не выполнен: ${response.status}`);
    }

    state.kafkaEvents = Array.isArray(payload.events) ? payload.events : [];
    state.kafkaEventsCheckedAt = payload.checkedAtUtc ?? payload.checkedAt ?? new Date().toISOString();
    state.kafkaEventsError = payload.error ?? '';
  } catch (error) {
    state.kafkaEvents = [];
    state.kafkaEventsError = error.message;
  } finally {
    state.loadingKafkaEvents = false;
    renderEventsView();
  }
}

async function syncDataSources() {
  syncModelRootsFromInputs();
  state.syncingSources = true;
  state.syncMessage = 'Синхронизация CMDBuild с источником...';
  state.syncError = '';
  renderDataSourceSyncView();

  try {
    await Promise.all([
      loadCmdbClasses(),
      loadCmdbClassSchemas(),
      loadCmdbDomains(),
      loadCmdbClassInstances()
    ]);
    const catalogError = cmdbCatalogError();
    if (catalogError) {
      throw new Error(catalogError);
    }
    const cacheRecord = await writeDataCache(cmdbSourceCacheKey(), {
      prefix: state.prefix,
      classes: state.cmdbClasses,
      classSchemas: state.cmdbClassSchemas,
      domains: state.cmdbDomains,
      sourceDomains: state.cmdbSourceDomains,
      classInstances: state.cmdbClassInstances
    });
    state.cmdbCacheUpdatedAt = cacheRecord.updatedAt;
    await loadPreview({ renderLoading: false });
    state.syncMessage = `CMDBuild синхронизирован: классов ${state.cmdbClasses.length}, атрибутов ${cmdbAttributeCount()}, доменов ${state.cmdbSourceDomains.length}, экземпляров ${cmdbInstanceCount()}.`;
    state.syncError = '';
  } catch (error) {
    state.syncMessage = '';
    state.syncError = error.message;
  } finally {
    state.syncingSources = false;
    render();
  }
}

async function loadCmdbSourceCache(options = {}) {
  const silent = options.silent === true;
  if (!silent) {
    state.loadingSourcesCache = true;
    state.syncMessage = 'Загрузка локального кэша CMDBuild...';
    state.syncError = '';
    renderDataSourceSyncView();
  }

  try {
    const cacheRecord = await readDataCache(cmdbSourceCacheKey());
    if (!cacheRecord) {
      throw new Error('Локальный кэш CMDBuild для текущего префикса не найден.');
    }

    applyCmdbSourceCache(cacheRecord);
    if (!silent) {
      state.syncMessage = `Локальный кэш CMDBuild загружен: классов ${state.cmdbClasses.length}, атрибутов ${cmdbAttributeCount()}, доменов ${state.cmdbSourceDomains.length}, экземпляров ${cmdbInstanceCount()}.`;
      state.syncError = '';
    }
  } catch (error) {
    if (!silent) {
      state.syncMessage = '';
      state.syncError = error.message;
    }
  } finally {
    if (!silent) {
      state.loadingSourcesCache = false;
      render();
    }
  }
}

async function checkZabbixSource() {
  state.checkingZabbix = true;
  state.zabbixCheckError = '';
  renderZabbixSyncView();

  try {
    const response = await fetch('/api/zabbix/check', {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || `проверка Zabbix не выполнена: ${response.status}`);
    }

    state.zabbixCheck = payload;
    state.zabbixCheckError = '';
    const cacheRecord = await writeDataCache(CACHE_KEYS.zabbix, payload);
    state.zabbixCacheUpdatedAt = cacheRecord.updatedAt;
  } catch (error) {
    state.zabbixCheck = null;
    state.zabbixCheckError = error.message;
  } finally {
    state.checkingZabbix = false;
    render();
  }
}

async function loadZabbixSourceCache(options = {}) {
  const silent = options.silent === true;
  if (!silent) {
    state.loadingZabbixCache = true;
    state.zabbixCheckError = '';
    renderZabbixSyncView();
  }

  try {
    const cacheRecord = await readDataCache(CACHE_KEYS.zabbix);
    if (!cacheRecord) {
      throw new Error('Локальный кэш Zabbix не найден.');
    }

    state.zabbixCheck = cacheRecord.payload ?? null;
    state.zabbixCacheUpdatedAt = cacheRecord.updatedAt ?? '';
    if (!silent) {
      state.zabbixCheckError = '';
    }
  } catch (error) {
    if (!silent) {
      state.zabbixCheck = null;
      state.zabbixCheckError = error.message;
    }
  } finally {
    if (!silent) {
      state.loadingZabbixCache = false;
      render();
    }
  }
}

async function checkWebhooksSource() {
  state.checkingWebhooks = true;
  state.webhooksCheckError = '';
  state.webhooksPublishError = '';
  state.webhookRuleCoverageError = '';
  renderWebhooksSyncView();

  try {
    const response = await fetch('/api/webhooks/check', {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || `проверка webhooks не выполнена: ${response.status}`);
    }

    const check = buildOnlineWebhookCheck(payload);
    state.webhooksCheck = check;
    state.webhooksCheckError = '';
    const cacheRecord = await writeDataCache(CACHE_KEYS.webhooks, {
      check,
      config: state.webhooksConfig
    });
    state.webhooksCacheUpdatedAt = cacheRecord.updatedAt;
  } catch (error) {
    state.webhooksCheck = null;
    state.webhooksCheckError = error.message;
  } finally {
    state.checkingWebhooks = false;
    render();
  }
}

async function publishWebhooksToCmdbuild() {
  state.publishingWebhooks = true;
  state.webhooksPublishMessage = 'Публикация управляемых webhooks в CMDBuild...';
  state.webhooksPublishError = '';
  renderWebhooksSyncView();

  try {
    const sourceClasses = conversionRuleSourceClasses();
    const response = await fetch('/api/webhooks/publish', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json'
      },
      body: JSON.stringify({
        sourceClasses,
        ruleDocuments: cloneJson(state.ruleDocuments),
        templateDocuments: cloneJson(state.templateDocuments)
      })
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok || result.success === false) {
      const errorText = result.errors?.length
        ? result.errors.map((item) => `${item.code}: ${item.error}`).slice(0, 5).join('; ')
        : '';
      throw new Error(errorText || result.message || result.error || `публикация webhooks не выполнена: ${response.status}`);
    }

    const created = Number(result.created ?? 0) || 0;
    const updated = Number(result.updated ?? 0) || 0;
    const managed = Number(result.cmdbuild?.managed ?? result.events?.length ?? 0) || 0;
    state.webhooksPublishMessage = `Опубликовано в CMDBuild: создано ${created}, обновлено ${updated}; управляемых webhooks в inventory: ${managed}.`;
    state.webhooksPublishError = '';
    if (Array.isArray(result.events)) {
      const check = buildOnlineWebhookCheck({
        status: 'ok',
        success: true,
        endpoint: state.webhooksConfig?.targetUrl ?? '',
        route: state.webhooksConfig?.route ?? '',
        rawTopic: state.webhooksConfig?.rawTopic ?? '',
        identifier: state.webhooksConfig?.managedIdentifier ?? '',
        events: result.events
      });
      state.webhooksCheck = check;
      state.webhookRuleCoverage = calculateWebhookRuleCoverage(check);
      await writeWebhooksCacheSnapshot(check);
    }
    await checkWebhooksAgainstConversionRules({ triggeredByDeploy: true });
  } catch (error) {
    state.webhooksPublishMessage = '';
    state.webhooksPublishError = error.message;
  } finally {
    state.publishingWebhooks = false;
    render();
  }
}

async function checkWebhooksAgainstConversionRules(options = {}) {
  state.checkingWebhookRuleCoverage = true;
  state.webhookRuleCoverageError = '';
  if (!options.triggeredByDeploy) {
    renderWebhooksSyncView();
  }

  try {
    const response = await fetch('/api/webhooks/check', {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || `проверка webhooks не выполнена: ${response.status}`);
    }

    const check = buildOnlineWebhookCheck(payload);
    state.webhooksCheck = check;
    state.webhooksCheckError = '';
    state.webhookRuleCoverage = calculateWebhookRuleCoverage(check);
    await writeWebhooksCacheSnapshot(check);
  } catch (error) {
    state.webhookRuleCoverage = null;
    state.webhookRuleCoverageError = error.message;
  } finally {
    state.checkingWebhookRuleCoverage = false;
    if (options.triggeredByDeploy) {
      renderWebhooksSyncView();
    } else {
      render();
    }
  }
}

function buildOnlineWebhookCheck(payload) {
  const config = state.webhooksConfig ?? {};
  const onlineEvents = onlineWebhookEvents(payload);
  const effectiveConfig = onlineEvents.length > 0
    ? { ...config, events: onlineEvents }
    : config;
  return {
    ...payload,
    endpoint: payload.endpoint ?? payload.Endpoint ?? config.targetUrl ?? '',
    route: payload.route ?? payload.Route ?? config.route ?? '',
    rawTopic: payload.rawTopic ?? payload.raw_topic ?? payload.RawTopic ?? config.rawTopic ?? '',
    identifier: payload.identifier ?? payload.Identifier ?? config.managedIdentifier ?? '',
    eventCounts: countManagedWebhookEvents(effectiveConfig),
    events: effectiveConfig.events ?? []
  };
}

async function writeWebhooksCacheSnapshot(check) {
  try {
    const cacheRecord = await writeDataCache(CACHE_KEYS.webhooks, {
      check,
      config: state.webhooksConfig
    });
    state.webhooksCacheUpdatedAt = cacheRecord.updatedAt;
  } catch {
    // Browser cache is optional; the online check result remains valid.
  }
}

async function loadWebhooksSourceCache(options = {}) {
  const silent = options.silent === true;
  if (!silent) {
    state.loadingWebhooksCache = true;
    state.webhooksCheckError = '';
    renderWebhooksSyncView();
  }

  try {
    const cacheRecord = await readDataCache(CACHE_KEYS.webhooks);
    if (!cacheRecord) {
      throw new Error('Локальный кэш Webhooks не найден.');
    }

    const payload = cacheRecord.payload ?? {};
    state.webhooksCheck = payload.check ?? null;
    state.webhooksConfig = {
      ...(payload.config ?? {}),
      ...state.webhooksConfig
    };
    state.webhooksCacheUpdatedAt = cacheRecord.updatedAt ?? '';
    if (!silent) {
      state.webhooksCheckError = '';
    }
  } catch (error) {
    if (!silent) {
      state.webhooksCheck = null;
      state.webhooksCheckError = error.message;
    }
  } finally {
    if (!silent) {
      state.loadingWebhooksCache = false;
      render();
    }
  }
}

async function saveConversionConfigsToFolder() {
  state.syncingConversionConfigs = true;
  state.syncConversionConfigMessage = 'Сохранение правил, шаблонов и связей конвертации в папку...';
  state.syncConversionConfigError = '';
  renderConversionConfigSyncView();

  try {
    const payload = currentConversionConfigPayload();
    const response = await fetch('/api/conversion-config/deploy', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json'
      },
      body: JSON.stringify(payload)
    });
    const result = await response.json();
    if (!response.ok || result.success === false) {
      throw new Error(result.message || result.error || `запись конфигураций конвертации не выполнена: ${response.status}`);
    }

    renderRulesPreviews();
    renderRuleEditors();
    state.conversionConfigStorage = result.storage ?? state.conversionConfigStorage;
    state.conversionConfigStorageUpdatedAt = result.savedAt ?? '';
    state.conversionConfigStorageVersion = Number(result.version ?? state.conversionConfigStorageVersion) || 0;
    state.conversionConfigStorageEtag = String(result.etag ?? state.conversionConfigStorageEtag ?? '');
    const cacheMessage = await writeConversionConfigCacheSnapshot(payload);
    const runtime = result.runtimeRules
      ? ` Runtime: ${result.runtimeRules.configuredFile}, ${Number(result.runtimeRules.ruleCount) || 0} правил.`
      : '';
    state.syncConversionConfigMessage = `${conversionConfigStatsMessage('Сохранено и опубликовано')} Папка: ${conversionConfigFolderLabel()}.${runtime}${cacheMessage}`;
    state.syncConversionConfigError = '';
  } catch (error) {
    state.syncConversionConfigMessage = '';
    state.syncConversionConfigError = error.message;
  } finally {
    state.syncingConversionConfigs = false;
    render();
  }
}

async function loadStoredConversionConfigs() {
  state.loadingStoredConversionConfigs = true;
  state.syncConversionConfigMessage = 'Загрузка правил, шаблонов и связей конвертации из папки...';
  state.syncConversionConfigError = '';
  renderConversionConfigSyncView();

  try {
    const response = await fetch('/api/conversion-config/storage', {
      headers: {
        accept: 'application/json'
      }
    });
    const payload = await response.json();
    if (!response.ok || payload.success === false) {
      throw new Error(payload.error ?? `чтение конфигураций конвертации не выполнено: ${response.status}`);
    }
    if (!payload.exists) {
      throw new Error(`В папке ${payload.storage?.resolvedStorageFolder ?? payload.storage?.storageFolder ?? ''} нет сохраненных конфигураций конвертации.`);
    }

    applyConversionConfigPayload(payload);
    const cacheMessage = await writeConversionConfigCacheSnapshot(currentConversionConfigPayload());
    state.conversionConfigStorageUpdatedAt = payload.savedAt ?? '';
    state.conversionConfigStorageVersion = Number(payload.version ?? 0) || 0;
    state.conversionConfigStorageEtag = String(payload.etag ?? '');
    state.conversionConfigStorage = payload.storage ?? state.conversionConfigStorage;
    state.syncConversionConfigMessage = `${conversionConfigStatsMessage('Загружено из папки')}${cacheMessage}`;
    state.syncConversionConfigError = '';
  } catch (error) {
    state.syncConversionConfigMessage = '';
    state.syncConversionConfigError = error.message;
  } finally {
    state.loadingStoredConversionConfigs = false;
    render();
  }
}

function currentConversionConfigPayload() {
  const serviceOk = syncRulesFromDocument('service');
  const suppressionOk = syncRulesFromDocument('suppression');
  if (!serviceOk || !suppressionOk) {
    throw new Error('Одна или несколько конфигураций конвертации некорректны.');
  }
  state.templateDocuments.service = normalizeTemplateDocument(state.templateDocuments.service, 'service');
  state.templateDocuments.suppression = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression');
  state.templateDocuments.shared = normalizeTemplateDocument(state.templateDocuments.shared, 'shared');
  state.ruleDocuments.service = normalizeRuleDocument(state.ruleDocuments.service, 'service');
  state.ruleDocuments.suppression = normalizeRuleDocument(state.ruleDocuments.suppression, 'suppression');

  return {
    prefix: state.prefix,
    baseVersion: state.conversionConfigStorageVersion || undefined,
    baseEtag: state.conversionConfigStorageEtag || undefined,
    ruleDocuments: cloneJson(state.ruleDocuments),
    templateDocuments: cloneJson(state.templateDocuments)
  };
}

function applyConversionConfigPayload(payload) {
  const documents = payload.ruleDocuments ?? {};
  if (Object.hasOwn(documents, 'service')) {
    state.ruleDocuments.service = documents.service ?? defaultRuleDocument('service');
  }
  if (Object.hasOwn(documents, 'suppression')) {
    state.ruleDocuments.suppression = documents.suppression ?? defaultRuleDocument('suppression');
  }
  const templateDocuments = payload.templateDocuments ?? {};
  if (Object.hasOwn(templateDocuments, 'service')) {
    state.templateDocuments.service = normalizeTemplateDocument(templateDocuments.service, 'service');
  }
  if (Object.hasOwn(templateDocuments, 'suppression')) {
    state.templateDocuments.suppression = normalizeTemplateDocument(templateDocuments.suppression, 'suppression');
  }
  if (Object.hasOwn(templateDocuments, 'shared')) {
    state.templateDocuments.shared = normalizeTemplateDocument(templateDocuments.shared, 'shared');
  }
  syncRulesFromDocument('service');
  syncRulesFromDocument('suppression');
  renderRulesPreviews();
  renderRuleEditors();
}

function conversionConfigStatsMessage(prefix) {
  const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates.length;
  const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates.length;
  const sharedTemplates = normalizeTemplateDocument(state.templateDocuments.shared, 'shared').templates.length;
  const relationCounts = conversionConfigManagedRelationCounts();
  return `${prefix}: ${state.ruleExamples.service.length} правил сервиса, ${state.ruleExamples.suppression.length} правил подавления, ${serviceTemplates + suppressionTemplates + sharedTemplates} шаблонов, ${relationCounts.total} управляемых связей.`;
}

function conversionConfigManagedRelationCounts() {
  const countRuleRelations = (rules) => (Array.isArray(rules) ? rules : [])
    .reduce((sum, rule) => sum + (Array.isArray(rule?.managed_relations) ? rule.managed_relations.length : 0), 0);
  const countTemplateRelations = (document, layerKey) => normalizeTemplateDocument(document, layerKey).templates
    .reduce((sum, template) => sum + (Array.isArray(template.managed_relations) ? template.managed_relations.length : 0), 0);
  const service = countRuleRelations(state.ruleExamples.service)
    + countTemplateRelations(state.templateDocuments.service, 'service');
  const suppression = countRuleRelations(state.ruleExamples.suppression)
    + countTemplateRelations(state.templateDocuments.suppression, 'suppression');
  return {
    service,
    suppression,
    total: service + suppression
  };
}

async function writeConversionConfigCacheSnapshot(payload) {
  try {
    const cacheRecord = await writeDataCache(conversionConfigCacheKey(), payload);
    state.conversionConfigCacheUpdatedAt = cacheRecord.updatedAt;
    return ' Локальный кэш обновлен.';
  } catch (error) {
    return ` Локальный кэш не обновлен: ${error.message}.`;
  }
}

function conversionConfigFolderLabel() {
  return state.conversionConfigStorage.resolvedStorageFolder
    ?? state.conversionConfigStorage.storageFolder
    ?? state.conversionConfigStorage.folder
    ?? '';
}

function currentGeneralSettingsPayload() {
  return {
    version: 1,
    maxTraversalDepth: state.maxTraversalDepth
  };
}

function applyGeneralSettingsPayload(payload, options = {}) {
  state.maxTraversalDepth = clampNumber(Number(payload?.maxTraversalDepth), 2, 2, 5);
  const maxDepth = document.querySelector('#maxTraversalDepthSelect');
  if (maxDepth) {
    maxDepth.value = String(state.maxTraversalDepth);
  }

  if (options.render !== false) {
    renderRuleEditors();
    renderConversionConfigSyncView();
    renderZabbixTriggerDependenciesView();
  }
}

function saveGeneralSettings() {
  try {
    localStorage.setItem(GENERAL_SETTINGS_STORAGE_KEY, JSON.stringify({
      savedAt: new Date().toISOString(),
      settings: currentGeneralSettingsPayload()
    }));
    state.generalSettingsMessage = 'Настройки сохранены в локальном браузерном хранилище.';
    state.generalSettingsError = '';
  } catch (error) {
    state.generalSettingsMessage = '';
    state.generalSettingsError = `Настройки не сохранены: ${error.message}`;
  }

  renderGeneralSettingsView();
}

function loadGeneralSettings(options = {}) {
  const silent = options.silent === true;
  try {
    const raw = localStorage.getItem(GENERAL_SETTINGS_STORAGE_KEY);
    if (!raw) {
      throw new Error('сохраненные настройки не найдены');
    }

    const payload = JSON.parse(raw);
    applyGeneralSettingsPayload(payload.settings ?? {}, { render: !silent });
    if (!silent) {
      const savedAt = payload.savedAt ? ` (${formatCacheTimestamp(payload.savedAt)})` : '';
      state.generalSettingsMessage = `Настройки загружены${savedAt}.`;
      state.generalSettingsError = '';
    }
  } catch (error) {
    if (!silent) {
      state.generalSettingsMessage = '';
      state.generalSettingsError = `Настройки не загружены: ${error.message}`;
    }
  }

  if (!silent) {
    renderGeneralSettingsView();
  }
}

async function loadConversionConfigCache(options = {}) {
  const silent = options.silent === true;
  if (!silent) {
    state.loadingConversionConfigCache = true;
    state.syncConversionConfigMessage = 'Загрузка локального кэша конфигураций конвертации...';
    state.syncConversionConfigError = '';
    renderConversionConfigSyncView();
  }

  try {
    const cacheRecord = await readDataCache(conversionConfigCacheKey());
    if (!cacheRecord) {
      throw new Error('Локальный кэш конфигураций конвертации для текущего префикса не найден.');
    }

    applyConversionConfigPayload(cacheRecord.payload ?? {});
    state.conversionConfigCacheUpdatedAt = cacheRecord.updatedAt ?? '';
    if (!silent) {
      state.syncConversionConfigMessage = conversionConfigStatsMessage('Локальный кэш конфигураций конвертации загружен');
      state.syncConversionConfigError = '';
    }
  } catch (error) {
    if (!silent) {
      state.syncConversionConfigMessage = '';
      state.syncConversionConfigError = error.message;
    }
  } finally {
    if (!silent) {
      state.loadingConversionConfigCache = false;
      render();
    }
  }
}

function render() {
  const classes = byActiveLayer(state.classes);
  const domains = byActiveLayer(state.domains);
  const suggestedDomains = byActiveLayer(state.suggestedDomains);
  const classByCode = new Map(classes.map((item) => [item.code, item]));
  const existingDomainCodes = new Set(state.cmdbDomains.map((item) => item.code));
  const allDomains = domains
    .map((item) => withDomainStatus(item, classByCode, existingDomainCodes, false))
    .concat(suggestedDomains.map((item) => withDomainStatus(item, classByCode, existingDomainCodes, true)));
  const readyClasses = classes.filter(isReadySchemaItem);
  const plannedClasses = classes.filter((item) => !isReadySchemaItem(item));
  const readyDomains = allDomains.filter(isReadySchemaItem);
  const plannedDomains = allDomains.filter((item) => !isReadySchemaItem(item));
  const readySchemaPanelTitle = document.querySelector('#readySchemaPanelTitle');
  const plannedSchemaPanelTitle = document.querySelector('#plannedSchemaPanelTitle');
  document.querySelector('#schemaTitle').textContent = state.activeLayer === 'Service'
    ? 'Сервисная схема'
    : 'Схема подавления';
  document.querySelector('#schemaLead').textContent = state.activeLayer === 'Service'
    ? 'Классы и домены для сервисного дерева Zabbix.'
    : 'Классы и домены для подавления зависимостей триггеров Zabbix.';
  document.querySelector('#entityCodeInput').placeholder = state.activeLayer === 'Service'
    ? 'ApplicationCluster'
    : 'FirewallGroup';
  document.querySelector('#entityDisplayInput').placeholder = state.activeLayer === 'Service'
    ? 'Кластер приложений'
    : 'Группа межсетевых экранов';
  document.querySelector('#addEntityButton').textContent = state.activeLayer === 'Service'
    ? 'Добавить сервисную сущность'
    : 'Добавить сущность подавления';
  document.querySelector('#serviceModelRootInput').placeholder = defaultModelRoot(state.language);
  document.querySelector('#suppressionModelRootInput').placeholder = defaultModelRoot(state.language);
  renderGeneralSettingsView();
  const selected = selectedApplyObjects();
  const sendButton = document.querySelector('#sendSelectedButton');
  sendButton.disabled = state.loading || state.applying;
  sendButton.textContent = state.applying
    ? 'Отправка...'
    : `Отправить выбранное в CMDBuild (${selected.classes.length + selected.domains.length})`;
  readySchemaPanelTitle.textContent = `Готовые классы/домены (${readyClasses.length}/${readyDomains.length})`;
  plannedSchemaPanelTitle.textContent = `Планируемые классы/домены (${plannedClasses.length}/${plannedDomains.length})`;

  const status = document.querySelector('#schemaStatus');
  const rootError = state.rootClassErrors[state.activeLayer] ?? '';
  const catalogError = state.cmdbDomainError;
  status.textContent = state.loading
    ? 'Загрузка предпросмотра схемы...'
    : (state.applyError || state.error || rootError || catalogError || state.applyMessage);
  status.classList.toggle('error', Boolean(state.applyError || state.error || rootError || catalogError));

  renderCmdbClassOptions();
  renderClassTree(document.querySelector('#readyClassList'), readyClasses, domainsBySource(readyDomains));
  renderClassTree(document.querySelector('#plannedClassList'), plannedClasses, domainsBySource(plannedDomains));

  renderCustomEntityList();
  renderRulesPreviews();
  renderRuleEditors();
  renderTopSourceStatus();
  renderDashboardView();
  renderEventsView();
  renderDataSourceSyncView();
  renderZabbixSyncView();
  renderWebhooksSyncView();
  renderConversionConfigSyncView();
  renderTemplateApplyView();
  renderTemplateAuditView();
  renderLinkRelationEditor();
  renderRelationsGraphView();
}

function renderGeneralSettingsView() {
  const maxDepth = document.querySelector('#maxTraversalDepthSelect');
  const transitiveDepth = document.querySelector('#transitiveGroupDependencyDepthSelect');
  const zabbixAttribute = document.querySelector('#zabbixHostIdAttributeInput');
  const conversionFolder = document.querySelector('#conversionConfigFolderInput');
  const status = document.querySelector('#generalSettingsStatus');
  const transitiveStatus = document.querySelector('#transitiveGroupDependencyDepthStatus');
  const aggregateStateInputs = {
    includeTags: document.querySelector('#aggregateStateTriggerIncludeTagsInput'),
    excludeTags: document.querySelector('#aggregateStateTriggerExcludeTagsInput'),
    includeNameRegex: document.querySelector('#aggregateStateTriggerIncludeNameRegexInput'),
    excludeNameRegex: document.querySelector('#aggregateStateTriggerExcludeNameRegexInput'),
    minPriority: document.querySelector('#aggregateStateTriggerMinPriorityInput')
  };
  const zabbixDependencyRuntimeInputs = {
    requestTimeoutMs: document.querySelector('#zabbixRequestTimeoutMsInput'),
    triggerGetBatchSize: document.querySelector('#triggerGetBatchSizeInput'),
    maxSourceHostsPerAggregate: document.querySelector('#maxSourceHostsPerAggregateInput'),
    maxAggregateFormulaLength: document.querySelector('#maxAggregateFormulaLengthInput'),
    maxDependenciesPerRun: document.querySelector('#maxDependenciesPerRunInput'),
    sampleLimit: document.querySelector('#triggerDependencySampleLimitInput')
  };
  if (!maxDepth || !transitiveDepth || !zabbixAttribute || !conversionFolder || !status) {
    return;
  }

  const dependencyState = state.zabbixTriggerDependencies;
  const payload = dependencyState.status ?? dependencyState.result;
  maxDepth.value = String(state.maxTraversalDepth);
  transitiveDepth.value = String(state.transitiveGroupDependencyDepth);
  transitiveDepth.disabled = true;
  zabbixAttribute.value = state.zabbixHostIdAttribute;
  conversionFolder.value = conversionConfigFolderLabel();
  renderAggregateStateTriggerSettings(aggregateStateInputs, payload);
  renderZabbixTriggerDependencyRuntimeSettings(zabbixDependencyRuntimeInputs, payload);
  if (transitiveStatus) {
    const hasServiceValue = Number.isInteger(Number(payload?.transitiveGroupDependencyDepth));
    transitiveStatus.classList.toggle('error', Boolean(dependencyState.error) && !hasServiceValue);
    if (dependencyState.loadingStatus) {
      transitiveStatus.textContent = 'Загружаю эффективное значение N из zabbixconfig2api...';
    } else if (hasServiceValue) {
      transitiveStatus.textContent = `Эффективное значение N=${state.transitiveGroupDependencyDepth} берется из zabbixconfig2api. Ручной dry-run/apply и автоматический reconcile используют одно и то же значение.`;
    } else if (dependencyState.error) {
      transitiveStatus.textContent = `Не удалось получить N из zabbixconfig2api: ${dependencyState.error}`;
    } else {
      transitiveStatus.textContent = 'Значение N будет загружено из zabbixconfig2api; локальная настройка UI не используется.';
    }
  }
  status.textContent = state.generalSettingsError || state.generalSettingsMessage;
  status.classList.toggle('error', Boolean(state.generalSettingsError));
}

function renderZabbixTriggerDependencyRuntimeSettings(inputs, payload) {
  const emptyText = payload ? '-' : 'ожидание статуса zabbixconfig2api';
  const values = {
    requestTimeoutMs: payload?.zabbixRequestTimeoutMs ?? emptyText,
    triggerGetBatchSize: payload?.triggerGetBatchSize ?? emptyText,
    maxSourceHostsPerAggregate: payload?.maxSourceHostsPerAggregate ?? emptyText,
    maxAggregateFormulaLength: payload?.maxAggregateFormulaLength ?? emptyText,
    maxDependenciesPerRun: payload?.maxDependenciesPerRun ?? emptyText,
    sampleLimit: payload?.sampleLimit ?? emptyText
  };

  for (const [key, input] of Object.entries(inputs)) {
    if (input) {
      input.value = values[key] === undefined || values[key] === null || values[key] === ''
        ? '-'
        : String(values[key]);
    }
  }
}

function renderAggregateStateTriggerSettings(inputs, payload) {
  const settings = payload?.aggregateStateTriggerSettings
    ?? aggregateStateTriggerSettingsFromSummary(payload?.aggregateStateTriggerSelector ?? payload?.aggregateStateTriggerSelectorSummary)
    ?? {};
  const emptyText = payload ? '-' : 'ожидание статуса zabbixconfig2api';
  const values = {
    includeTags: payload ? zabbixTriggerTagSelectorsText(settings.includeTags) : emptyText,
    excludeTags: payload ? zabbixTriggerTagSelectorsText(settings.excludeTags) : emptyText,
    includeNameRegex: payload ? emptyAsDash(settings.includeNameRegex) : emptyText,
    excludeNameRegex: payload ? emptyAsDash(settings.excludeNameRegex) : emptyText,
    minPriority: settings.minPriority === undefined || settings.minPriority === null || settings.minPriority === ''
      ? emptyText
      : String(settings.minPriority)
  };

  for (const [key, input] of Object.entries(inputs)) {
    if (input) {
      input.value = values[key] ?? '-';
    }
  }
}

function aggregateStateTriggerSettingsFromSummary(summary) {
  if (!summary) {
    return null;
  }

  const fields = Object.fromEntries(String(summary)
    .split(';')
    .map((part) => part.trim())
    .map((part) => {
      const separator = part.indexOf(':');
      return separator < 0 ? null : [part.slice(0, separator).trim(), part.slice(separator + 1).trim()];
    })
    .filter(Boolean));
  return {
    includeTags: zabbixTriggerTagSelectorsFromText(fields['include tags']),
    excludeTags: zabbixTriggerTagSelectorsFromText(fields['exclude tags']),
    includeNameRegex: fields['include name regex'] ?? '',
    excludeNameRegex: fields['exclude name regex'] ?? '',
    minPriority: fields['min priority'] ?? ''
  };
}

function zabbixTriggerTagSelectorsFromText(value) {
  if (!value || value === '-' || value.toLowerCase() === 'нет') {
    return [];
  }

  return String(value)
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const separator = part.indexOf('=');
      return separator < 0
        ? { tag: part, value: '' }
        : { tag: part.slice(0, separator).trim(), value: part.slice(separator + 1).trim() };
    });
}

function zabbixTriggerTagSelectorsText(items) {
  if (!Array.isArray(items) || items.length === 0) {
    return 'нет';
  }

  return items
    .map((item) => {
      const tag = item?.tag ?? item?.Tag ?? '';
      const value = item?.value ?? item?.Value ?? '';
      return value ? `${tag}=${value}` : tag;
    })
    .filter(Boolean)
    .join(', ') || 'нет';
}

function emptyAsDash(value) {
  return value === undefined || value === null || String(value).trim() === ''
    ? '-'
    : String(value);
}

function applyResultMessage(payload) {
  return `Применение CMDBuild завершено: создано ${payload.created ?? 0}, обновлено ${payload.updated ?? 0}, пропущено ${payload.skipped ?? 0}, ошибок ${payload.failed ?? 0}.`;
}

function isReadySchemaItem(item) {
  return item?.schemaStatus === 'ready_to_work';
}

function withDomainStatus(domain, classByCode, existingDomainCodes, suggested) {
  const source = classByCode.get(domain.sourceClassCode);
  const ready = existingDomainCodes.has(domain.code);
  return {
    ...domain,
    suggested,
    sourceClassReady: isReadySchemaItem(source),
    schemaStatus: ready ? 'ready_to_work' : 'recommended_to_create',
    schemaStatusLabel: ready ? 'Готовый домен' : 'Планируемый домен'
  };
}

function domainsBySource(domains) {
  const result = new Map();
  for (const domain of domains) {
    const items = result.get(domain.sourceClassCode) ?? [];
    items.push(domain);
    result.set(domain.sourceClassCode, items);
  }

  for (const items of result.values()) {
    items.sort((left, right) => left.code.localeCompare(right.code));
  }

  return result;
}

function renderApplyCheckbox(kind, code, defaultSelected) {
  const checked = isApplySelected(kind, code, defaultSelected);
  return `
    <label class="apply-checkbox" title="Отправить в CMDBuild">
      <input
        type="checkbox"
        data-apply-select
        data-apply-kind="${escapeHtml(kind)}"
        data-apply-code="${escapeHtml(code)}"
        ${checked ? 'checked' : ''}
        ${state.applying ? 'disabled' : ''}>
    </label>
  `;
}

function selectedApplyObjects() {
  const classes = byActiveLayer(state.classes);
  const classByCode = new Map(classes.map((item) => [item.code, item]));
  const existingDomainCodes = new Set(state.cmdbDomains.map((item) => item.code));
  const domains = byActiveLayer(state.domains)
    .map((item) => withDomainStatus(item, classByCode, existingDomainCodes, false))
    .concat(byActiveLayer(state.suggestedDomains).map((item) => withDomainStatus(item, classByCode, existingDomainCodes, true)));
  const selectedDomains = new Set(domains
    .filter((item) => isApplySelected('domain', item.code, item.schemaStatus !== 'ready_to_work'))
    .map((item) => item.code));
  for (const domainCode of automaticSchemaSourceLinkDomainCodes()) {
    if (!existingDomainCodes.has(domainCode)) {
      selectedDomains.add(domainCode);
    }
  }

  return {
    classes: classes
      .filter((item) => isApplySelected('class', item.code, item.schemaStatus !== 'ready_to_work'))
      .map((item) => item.code),
    domains: [...selectedDomains],
    includeDependencies: true
  };
}

function isApplySelected(kind, code, defaultSelected) {
  const group = applySelectionGroup(kind);
  if (!group) {
    return false;
  }

  if (!state.applySelectionTouched[group].has(code)) {
    return defaultSelected;
  }

  return state.applySelection[group].has(code);
}

function updateApplySelection(kind, code, selected) {
  const group = applySelectionGroup(kind);
  if (!group || !code) {
    return;
  }

  state.applySelectionTouched[group].add(code);
  if (selected) {
    state.applySelection[group].add(code);
    return;
  }

  state.applySelection[group].delete(code);
}

function resetApplySelection() {
  state.applySelection.classes.clear();
  state.applySelection.domains.clear();
  state.applySelectionTouched.classes.clear();
  state.applySelectionTouched.domains.clear();
}

function applySelectionGroup(kind) {
  if (kind === 'class') {
    return 'classes';
  }

  if (kind === 'domain') {
    return 'domains';
  }

  return '';
}

function syncModelRootsFromInputs() {
  state.serviceModelRoot = document.querySelector('#serviceModelRootInput').value.trim();
  state.suppressionModelRoot = document.querySelector('#suppressionModelRootInput').value.trim();
}

function applyLanguageDefaultModelRoots(previousDefault = '') {
  const nextDefault = defaultModelRoot(state.language);
  if (!state.serviceModelRoot || state.serviceModelRoot === previousDefault) {
    state.serviceModelRoot = nextDefault;
  }

  if (!state.suppressionModelRoot || state.suppressionModelRoot === previousDefault) {
    state.suppressionModelRoot = nextDefault;
  }
}

function applyPreviewModelRoots() {
  const serviceRoot = state.modelRoots.find((item) => item.layer === 'Service')?.rootPath;
  const suppressionRoot = state.modelRoots.find((item) => item.layer === 'Suppression')?.rootPath;
  if (serviceRoot) {
    state.serviceModelRoot = serviceRoot;
  }

  if (suppressionRoot) {
    state.suppressionModelRoot = suppressionRoot;
  }
}

function applyModelRootInputs() {
  document.querySelector('#serviceModelRootInput').value = state.serviceModelRoot;
  document.querySelector('#suppressionModelRootInput').value = state.suppressionModelRoot;
}

function defaultModelRoot(language) {
  return normalizeLanguage(language) === 'En'
    ? '/Monitoring'
    : '/Мониторинг';
}

async function loadModelRootClasses() {
  const serviceRoot = normalizeRootPath(state.serviceModelRoot || defaultModelRoot(state.language));
  const suppressionRoot = normalizeRootPath(state.suppressionModelRoot || defaultModelRoot(state.language));
  const results = await Promise.all([
    loadModelRootClassesForLayer('Service', serviceRoot),
    loadModelRootClassesForLayer('Suppression', suppressionRoot)
  ]);

  for (const result of results) {
    state.rootClassesByLayer[result.layer] = result.classes;
    state.rootClassErrors[result.layer] = result.error;
  }
}

async function loadModelRootClassesForLayer(layer, rootPath) {
  try {
    const url = new URL('/api/cmdbuild/classes', window.location.origin);
    url.searchParams.set('rootPath', rootPath);
    url.searchParams.set('prefix', state.prefix);
    url.searchParams.set('layer', layer);
    url.searchParams.set('managedOnly', 'true');
    const response = await fetch(url, {
      headers: {
        accept: 'application/json'
      }
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `запрос корневых классов CMDBuild не выполнен: ${response.status}`);
    }

    const catalog = await response.json();
    const classes = catalog.classes ?? [];
    const error = catalog.rootFound === false
      ? `Корень CMDBuild ${rootPath} не найден.`
      : '';
    return { layer, classes, error };
  } catch (error) {
    return { layer, classes: [], error: `Корень CMDBuild ${rootPath} недоступен: ${error.message}` };
  }
}

function existingModelClassOptions() {
  return ['Service', 'Suppression'].flatMap((layer) => {
    const rootPath = layer === 'Service'
      ? state.serviceModelRoot
      : state.suppressionModelRoot;
    const classes = (state.rootClassesByLayer[layer] ?? [])
      .filter((item) => item.managedByBuilder === true && item.autoPopulationEnabled === true)
      .map((item) => ({
        code: item.code,
        layer,
        displayName: item.description || item.name || item.code,
        modelRoot: rootPath,
        parentClassCode: item.parent || '',
        managedByBuilder: true,
        autoPopulationEnabled: true
      }));

    return sortSchemaClassesByInheritance(classes, classes);
  });
}

function automaticSchemaSourceLinks() {
  const links = new Map();
  const addLink = (managedClassCode, customerClassCode, layerKey = '') => {
    const managed = String(managedClassCode ?? '').trim();
    const customer = String(customerClassCode ?? '').trim();
    const layer = String(layerKey ?? '').trim().toLowerCase();
    if (!managed || !customer) {
      return;
    }

    if (layer && schemaLayerForRuleLayer(layer) !== state.activeLayer) {
      return;
    }

    links.set(`${canonicalToken(managed)}:${canonicalToken(customer)}`, {
      managedClassCode: managed,
      customerClassCode: customer
    });
  };

  for (const layerKey of ['service', 'suppression']) {
    for (const rule of normalizedRuleDocumentForLayer(layerKey).rules ?? []) {
      addLink(ruleTargetClassCode(rule), ruleSourceClassCode(rule), layerKey);
    }

    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    for (const template of document.templates.filter((item) => item.enabled !== false)) {
      const targetClassCode = String(template.target?.class_code ?? '').trim();
      if (!targetClassCode) {
        continue;
      }

      try {
        for (const candidate of templateCandidateClasses(template)) {
          addLink(targetClassCode, candidate.code, layerKey);
        }
      } catch {
        // Invalid source regex is handled by the template editor; schema preview should remain available.
      }
    }
  }

  return [...links.values()]
    .sort((left, right) =>
      left.managedClassCode.localeCompare(right.managedClassCode, undefined, { sensitivity: 'base' })
      || left.customerClassCode.localeCompare(right.customerClassCode, undefined, { sensitivity: 'base' }));
}

function normalizedRuleDocumentForLayer(layerKey) {
  const parsed = parseRuleDocument(layerKey);
  return parsed.ok ? parsed.document : defaultRuleDocument(layerKey);
}

function automaticSchemaSourceLinkDomainCodes() {
  return automaticSchemaSourceLinks()
    .map((link) => sourceLinkDomainCode(link.managedClassCode, link.customerClassCode))
    .filter(Boolean);
}

function sourceLinkDomainCode(managedClassCode, customerClassCode) {
  const managedPart = removeManagedPrefix(managedClassCode);
  const customerPart = normalizeSourceLinkDomainPart(customerClassCode);
  if (!managedPart || !customerPart) {
    return '';
  }

  return `${state.prefix}${managedPart}PopulatedFrom${customerPart}`;
}

function removeManagedPrefix(classCode) {
  const code = String(classCode ?? '').trim();
  const prefix = String(state.prefix ?? '').trim();
  return prefix && code.startsWith(prefix)
    ? code.slice(prefix.length)
    : code;
}

function normalizeSourceLinkDomainPart(classCode) {
  return String(classCode ?? '')
    .trim()
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
}

function schemaLayerForRuleLayer(layerKey) {
  return String(layerKey ?? '').toLowerCase() === 'suppression' ? 'Suppression' : 'Service';
}

function normalizeRootPath(rootPath) {
  const normalized = String(rootPath ?? '').trim();
  if (!normalized) {
    return '';
  }

  return normalized.startsWith('/') ? normalized : `/${normalized}`;
}

function renderClassTree(container, classes, domainsByClass) {
  if (classes.length === 0 && !state.loading) {
    container.innerHTML = '<div class="empty-state">Нет элементов для показа.</div>';
    return;
  }

  const classOrder = schemaClassOrderMapFrom(byActiveLayer(state.classes));
  const childrenByParent = new Map();
  for (const item of classes) {
    if (!item.parentClassCode) {
      continue;
    }

    const children = childrenByParent.get(item.parentClassCode) ?? [];
    children.push(item);
    childrenByParent.set(item.parentClassCode, children);
  }

  const classCodes = new Set(classes.map((item) => item.code));
  const roots = classes.filter((item) =>
    !item.parentClassCode || !classCodes.has(item.parentClassCode))
    .sort((left, right) => compareSchemaClassesByHierarchy(left, right, classOrder));
  container.innerHTML = roots.map((item) =>
    renderClassRow(item, childrenByParent, domainsByClass, classOrder)).join('');
}

function renderClassRow(item, childrenByParent = new Map(), domainsByClass = new Map(), classOrder = new Map()) {
  const isOpen = item.isSuperclass || state.openClassRows.has(item.code);
  const children = (childrenByParent.get(item.code) ?? [])
    .sort((left, right) => compareSchemaClassesByHierarchy(left, right, classOrder));
  const classDomains = domainsByClass.get(item.code) ?? [];
  const defaultSelected = item.schemaStatus !== 'ready_to_work';
  return `
    <details class="row class-row ${item.isSuperclass ? 'superclass-row' : 'child-class-row'}" data-class-code="${escapeHtml(item.code)}" ${isOpen ? 'open' : ''}>
      <summary>
        ${renderApplyCheckbox('class', item.code, defaultSelected)}
        <span class="row-labels class-labels">
          <span class="badge ${layerClass(item.layer)}">${item.layer}</span>
          <span class="structure-mark">${item.isSuperclass ? 'суперкласс' : 'класс'}</span>
          ${item.schemaStatusLabel ? `<span class="schema-status-mark ${escapeHtml(item.schemaStatus)}">${escapeHtml(item.schemaStatusLabel)}</span>` : ''}
          ${item.origin === 'existing_managed_descendant' ? '<span class="source-link-mark">управляемый потомок</span>' : ''}
          ${item.managedByBuilder ? '<span class="source-link-mark">управляется builder</span>' : ''}
          ${item.autoPopulationEnabled ? '<span class="source-link-mark">автонаполнение включено</span>' : ''}
        </span>
        <span class="row-title">${escapeHtml(item.code)}</span>
        <span class="row-meta">${escapeHtml(item.displayName ?? '')}</span>
        <span class="row-count">${classAttributeLabel(item, classDomains.length)}</span>
      </summary>
      <p class="help-text">${escapeHtml(item.help ?? '')}</p>
      ${item.parentClassCode ? `<p class="help-text">наследует ${escapeHtml(item.parentClassCode)}</p>` : ''}
      ${item.modelRoot ? `<p class="help-text">корень модели ${escapeHtml(item.modelRoot)}</p>` : ''}
      ${renderClassDomains(classDomains)}
      ${renderAttributeTable(item.attributes ?? [], item.isSuperclass ? 'Атрибуты суперкласса' : 'Локальные атрибуты класса')}
      ${children.length > 0 ? `<div class="child-list">${children.map((child) => renderClassRow(child, childrenByParent, domainsByClass, classOrder)).join('')}</div>` : ''}
    </details>
  `;
}

function renderClassDomains(domains) {
  if (domains.length === 0) {
    return '';
  }

  return `
    <div class="class-domain-list">
      <h3>Домены (${domains.length})</h3>
      <div class="nested-domain-list">
        ${domains.map((domain) => renderDomainRow(domain, domain.suggested)).join('')}
      </div>
    </div>
  `;
}

function renderAttributeTable(attributes, label) {
  if (attributes.length === 0) {
    return '<div class="empty-state">Атрибуты не запланированы.</div>';
  }

  return `
    <div class="attribute-grid" role="table" aria-label="${escapeHtml(label)}">
      <div class="attribute-head" role="row">
        <span role="columnheader">Код</span>
        <span role="columnheader">Имя</span>
        <span role="columnheader">Тип</span>
        <span role="columnheader">Обяз.</span>
        <span role="columnheader">Помощь</span>
      </div>
      ${attributes.map((attribute) => `
        <div class="attribute-row" role="row">
          <span role="cell">${escapeHtml(attribute.code)}</span>
          <span role="cell">${escapeHtml(attribute.displayName)}</span>
          <span role="cell">${escapeHtml(formatAttributeType(attribute))}</span>
          <span role="cell">${attribute.required ? 'да' : 'нет'}</span>
          <span role="cell">${escapeHtml(attribute.help)}</span>
        </div>
      `).join('')}
    </div>
  `;
}

function formatAttributeType(attribute) {
  const type = attribute.lookupTypeCode
    ? `${attribute.type}: ${attribute.lookupTypeCode}`
    : attribute.type;

  return attribute.validationRules
    ? `${type} · JS-проверка`
    : type;
}

function renderDomainRow(item, suggested = false) {
  const defaultSelected = item.schemaStatus !== 'ready_to_work';
  return `
    <details class="row domain-row" data-domain-code="${escapeHtml(item.code)}" ${state.openDomainRows.has(item.code) ? 'open' : ''}>
      <summary>
        ${renderApplyCheckbox('domain', item.code, defaultSelected)}
        <span class="row-labels">
          <span class="badge ${layerClass(item.layer)}">${item.layer}</span>
          ${suggested ? '<span class="suggestion-mark">предложено</span>' : ''}
          ${item.isSourceLink ? '<span class="source-link-mark">связь источника</span>' : ''}
          ${item.schemaStatusLabel ? `<span class="schema-status-mark ${escapeHtml(item.schemaStatus)}">${escapeHtml(item.schemaStatusLabel)}</span>` : ''}
        </span>
        <span class="row-title">${escapeHtml(item.code)}</span>
        <span class="row-meta row-route">${escapeHtml(item.sourceClassCode)} -> ${escapeHtml(item.targetClassCode)}</span>
        <span class="row-meta row-relation">${escapeHtml(item.relationType)} · удалять связь при удалении карточки: ${item.deleteRelationOnCardDelete}</span>
        <span class="row-count">${item.attributes?.length ?? 0} атр.</span>
      </summary>
      <p class="help-text">${escapeHtml(item.help ?? '')}</p>
      ${suggested ? `<p class="help-text">${escapeHtml(item.reason)}</p>` : ''}
      ${renderAttributeTable(item.attributes ?? [], 'Атрибуты домена')}
    </details>
  `;
}

function classAttributeLabel(item, domainCount = 0) {
  const count = item.attributes?.length ?? 0;
  const attributeLabel = item.isSuperclass ? `${count} общих` : `${count} локальных`;
  return `${attributeLabel} · ${domainCount} дом.`;
}

async function loadCmdbClasses() {
  try {
    const url = new URL('/api/cmdbuild/classes', window.location.origin);
    url.searchParams.set('includePrototypes', 'true');
    const response = await fetch(url, {
      headers: {
        accept: 'application/json'
      }
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `запрос классов CMDBuild не выполнен: ${response.status}`);
    }

    const catalog = await response.json();
    state.cmdbClasses = catalog.classes ?? [];
    state.cmdbClassError = '';
  } catch (error) {
    state.cmdbClasses = [];
    state.cmdbClassError = `Каталог классов CMDBuild недоступен: ${error.message}`;
  }
}

async function loadCmdbClassSchemas() {
  try {
    const response = await fetch('/api/cmdbuild/classes/schema', {
      headers: {
        accept: 'application/json'
      }
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `запрос схемы классов CMDBuild не выполнен: ${response.status}`);
    }

    const catalog = await response.json();
    state.cmdbClassSchemas = catalog.classes ?? [];
    state.cmdbClassSchemaError = '';
  } catch (error) {
    state.cmdbClassSchemas = [];
    state.cmdbClassSchemaError = `Схема классов CMDBuild недоступна: ${error.message}`;
  }
}

async function loadCmdbDomains() {
  try {
    const [managedCatalog, sourceCatalog] = await Promise.all([
      fetchCmdbDomains(state.prefix),
      fetchCmdbDomains('')
    ]);
    state.cmdbDomains = managedCatalog.domains ?? [];
    state.cmdbSourceDomains = sourceCatalog.domains ?? state.cmdbDomains;
    state.cmdbDomainError = '';
  } catch (error) {
    state.cmdbDomains = [];
    state.cmdbSourceDomains = [];
    state.cmdbDomainError = `Каталог доменов CMDBuild недоступен: ${error.message}`;
  }
}

async function loadCmdbClassInstances() {
  try {
    const url = new URL('/api/cmdbuild/classes/instances', window.location.origin);
    url.searchParams.set('prefix', state.prefix);
    url.searchParams.set('serviceModelRoot', state.serviceModelRoot || defaultModelRoot(state.language));
    url.searchParams.set('suppressionModelRoot', state.suppressionModelRoot || defaultModelRoot(state.language));
    const response = await fetch(url, {
      headers: {
        accept: 'application/json'
      }
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `запрос экземпляров классов CMDBuild не выполнен: ${response.status}`);
    }

    const catalog = await response.json();
    replaceCmdbClassInstances(catalog.classes ?? [], { preserveLoadedSourceCards: true });
    state.cmdbClassInstanceError = '';
  } catch (error) {
    state.cmdbClassInstances = [];
    state.cmdbClassInstanceError = `Экземпляры классов CMDBuild недоступны: ${error.message}`;
  }
}

async function loadSourceClassCards(classCode) {
  const url = new URL(`/api/cmdbuild/classes/${encodeURIComponent(classCode)}/cards`, window.location.origin);
  url.searchParams.set('layer', 'Source');
  const response = await fetch(url, {
    headers: {
      accept: 'application/json'
    }
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${classCode}: ${text || `запрос карточек источника CMDBuild не выполнен: ${response.status}`}`);
  }

  const classItem = await response.json();
  rememberSourceClassCards(classItem);
  return classItem;
}

function rememberSourceClassCards(classItem) {
  const classCode = classItem?.classCode ?? classItem?.ClassCode ?? classItem?.code ?? '';
  if (!classCode) {
    return;
  }

  const normalized = {
    layer: classItem.layer ?? classItem.Layer ?? 'Source',
    classCode,
    className: classItem.className ?? classItem.ClassName ?? classCode,
    classDescription: classItem.classDescription ?? classItem.ClassDescription ?? classCode,
    attributes: classItem.attributes ?? classItem.Attributes ?? [],
    cards: classItem.cards ?? classItem.Cards ?? []
  };
  state.cmdbClassInstances = state.cmdbClassInstances.filter((item) =>
    !(String(item.layer).toLowerCase() === 'source'
      && canonicalToken(item.classCode ?? item.code ?? item.class_code ?? item.name) === canonicalToken(classCode)));
  state.cmdbClassInstances.push(normalized);
}

function replaceCmdbClassInstances(classItems, options = {}) {
  const incoming = Array.isArray(classItems) ? classItems : [];
  if (options.preserveLoadedSourceCards !== true) {
    state.cmdbClassInstances = incoming;
    return;
  }

  const incomingSourceKeys = new Set(incoming
    .filter((item) => String(item.layer).toLowerCase() === 'source')
    .map((item) => canonicalToken(item.classCode ?? item.code ?? item.class_code ?? item.name))
    .filter(Boolean));
  const preservedSourceItems = state.cmdbClassInstances.filter((item) => {
    const key = canonicalToken(item.classCode ?? item.code ?? item.class_code ?? item.name);
    return String(item.layer).toLowerCase() === 'source'
      && key
      && !incomingSourceKeys.has(key)
      && Array.isArray(item.cards)
      && item.cards.length > 0;
  });
  state.cmdbClassInstances = incoming.concat(preservedSourceItems);
}

async function fetchCmdbDomains(prefix) {
  const url = new URL('/api/cmdbuild/domains', window.location.origin);
  if (prefix) {
    url.searchParams.set('prefix', prefix);
  }
  const response = await fetch(url, {
    headers: {
      accept: 'application/json'
    }
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `запрос доменов CMDBuild не выполнен: ${response.status}`);
  }

  return await response.json();
}

function renderCmdbClassOptions() {
  const datalist = document.querySelector('#cmdbClassOptions');
  datalist.innerHTML = availableSourceClasses().map((item) => `
    <option value="${escapeHtml(item.code)}" label="${escapeHtml(item.hierarchyLabel)}">${escapeHtml(item.hierarchyLabel)}</option>
  `).join('');
}

function availableSourceClasses() {
  const aggregationCodes = aggregationClassCodes();
  const parentByCode = new Map(state.cmdbClasses.map((item) => [item.code, item.parent || '']));
  const candidates = state.cmdbClasses
    .filter((item) => item.prototype !== true && !isAggregationClassCode(item.code, parentByCode, aggregationCodes));

  return sortClassesByInheritance(candidates, state.cmdbClasses);
}

function aggregationClassCodes() {
  const codes = new Set();
  for (const item of state.classes) {
    if (item.code) {
      codes.add(item.code);
    }
  }

  return codes;
}

function isAggregationClassCode(code, parentByCode, aggregationCodes, seen = new Set()) {
  if (!code || seen.has(code)) {
    return false;
  }

  if (aggregationCodes.has(code)) {
    return true;
  }

  seen.add(code);
  const parent = parentByCode.get(code) || '';
  return aggregationCodes.has(parent)
    || isAggregationClassCode(parent, parentByCode, aggregationCodes, seen);
}

function sortClassesByInheritance(classes, hierarchyClasses = classes) {
  const selectableCodes = new Set(classes.map((item) => item.code));
  const hierarchyItems = [...hierarchyClasses];
  for (const item of classes) {
    if (item?.code && !hierarchyItems.some((existing) => existing.code === item.code)) {
      hierarchyItems.push(item);
    }
  }

  const byCode = new Map(hierarchyItems.map((item) => [item.code, item]));
  const childrenByParent = new Map();
  const roots = [];

  for (const item of hierarchyItems) {
    const parent = item.parent || '';
    if (parent && byCode.has(parent)) {
      const children = childrenByParent.get(parent) ?? [];
      children.push(item);
      childrenByParent.set(parent, children);
    } else {
      roots.push(item);
    }
  }

  const result = [];
  const visit = (item, parentLabels) => {
    const label = classDisplayName(item);
    const path = parentLabels.concat(label);
    if (selectableCodes.has(item.code)) {
      result.push({
        ...item,
        hierarchyLabel: `${path.join(' / ')} (${item.code})`,
        hierarchyPath: path.join(' / '),
        hierarchyDepth: path.length - 1
      });
    }

    const children = childrenByParent.get(item.code) ?? [];
    children
      .sort(compareClassCatalogItems)
      .forEach((child) => visit(child, path));
  };

  roots
    .sort(compareClassCatalogItems)
    .forEach((item) => visit(item, []));

  return result;
}

function compareClassCatalogItems(left, right) {
  return classDisplayName(left).localeCompare(classDisplayName(right), undefined, {
    sensitivity: 'base'
  }) || left.code.localeCompare(right.code, undefined, { sensitivity: 'base' });
}

function classDisplayName(item) {
  return String(item.description || item.name || item.code || '').trim();
}

function renderRulesPreviews() {
  renderLayerRulesPreview('service', 'Service', {
    sourceStatus: '#serviceSourceSchemaStatus',
    sourceList: '#serviceSourceSchemaList',
    rulesList: '#serviceRulesPreviewList',
    targetList: '#serviceTargetSchemaList'
  });
  renderLayerRulesPreview('suppression', 'Suppression', {
    sourceStatus: '#suppressionSourceSchemaStatus',
    sourceList: '#suppressionSourceSchemaList',
    rulesList: '#suppressionRulesPreviewList',
    targetList: '#suppressionTargetSchemaList'
  });
}

function renderTopSourceStatus() {
  setSourceLamp('#cmdbuildLoadedLamp', {
    loaded: Boolean(state.cmdbCacheUpdatedAt) && !cmdbCatalogError(),
    loading: state.syncingSources || state.loadingSourcesCache,
    error: Boolean(state.syncError || cmdbCatalogError()),
    loadedText: 'CMDBuild загружен',
    loadingText: 'CMDBuild загружается',
    errorText: 'CMDBuild ошибка',
    emptyText: 'CMDBuild не загружен',
    updatedAt: state.cmdbCacheUpdatedAt
  });
  setSourceLamp('#zabbixLoadedLamp', {
    loaded: Boolean(state.zabbixCacheUpdatedAt && state.zabbixCheck),
    loading: state.checkingZabbix || state.loadingZabbixCache,
    error: Boolean(state.zabbixCheckError),
    loadedText: 'Zabbix загружен',
    loadingText: 'Zabbix загружается',
    errorText: 'Zabbix ошибка',
    emptyText: 'Zabbix не загружен',
    updatedAt: state.zabbixCacheUpdatedAt
  });
  setSourceLamp('#webhooksLoadedLamp', {
    loaded: Boolean(state.webhooksCacheUpdatedAt && state.webhooksCheck),
    loading: state.checkingWebhooks || state.checkingWebhookRuleCoverage || state.loadingWebhooksCache,
    error: Boolean(state.webhooksCheckError || state.webhookRuleCoverageError || state.webhookRuleCoverage?.missing?.length > 0),
    loadedText: 'Webhooks загружены',
    loadingText: 'Webhooks загружаются',
    errorText: 'Webhooks ошибка',
    emptyText: 'Webhooks не загружены',
    updatedAt: state.webhooksCacheUpdatedAt
  });
  setSourceLamp('#conversionLoadedLamp', {
    loaded: Boolean(state.conversionConfigStorageUpdatedAt || state.conversionConfigCacheUpdatedAt),
    loading: state.syncingConversionConfigs || state.loadingStoredConversionConfigs || state.loadingConversionConfigCache,
    error: Boolean(state.syncConversionConfigError),
    loadedText: 'Конвертация загружена',
    loadingText: 'Конвертация загружается',
    errorText: 'Конвертация ошибка',
    emptyText: 'Конвертация не загружена',
    updatedAt: state.conversionConfigStorageUpdatedAt || state.conversionConfigCacheUpdatedAt
  });
}

function setSourceLamp(selector, options) {
  const lamp = document.querySelector(selector);
  const label = lamp?.querySelector('.lamp-label');
  if (!lamp || !label) {
    return;
  }

  const stateClass = options.loading
    ? 'loading'
    : (options.error ? 'error' : (options.loaded ? 'loaded' : ''));
  const text = options.loading
    ? options.loadingText
    : (options.error ? options.errorText : (options.loaded ? options.loadedText : options.emptyText));
  lamp.classList.toggle('loaded', stateClass === 'loaded');
  lamp.classList.toggle('loading', stateClass === 'loading');
  lamp.classList.toggle('error', stateClass === 'error');
  label.textContent = text;
  lamp.title = options.updatedAt
    ? `${text}; последнее обновление: ${formatCacheTimestamp(options.updatedAt)}`
    : text;
}

function renderDashboardView() {
  const total = document.querySelector('#dashboardServiceCount');
  const healthy = document.querySelector('#dashboardHealthyCount');
  const failed = document.querySelector('#dashboardFailedCount');
  const checkedAt = document.querySelector('#dashboardHealthCheckedAt');
  const status = document.querySelector('#dashboardHealthStatus');
  const list = document.querySelector('#dashboardHealthList');
  const button = document.querySelector('#refreshDashboardHealthButton');
  if (!total || !healthy || !failed || !checkedAt || !status || !list || !button) {
    return;
  }

  const services = state.healthServices;
  const okCount = services.filter((service) => service.status === 'ok').length;
  const failedCount = services.filter((service) => service.status !== 'ok').length;
  total.textContent = String(services.length);
  healthy.textContent = String(okCount);
  failed.textContent = String(failedCount);
  checkedAt.textContent = formatCacheTimestamp(state.healthCheckedAt);
  button.disabled = state.checkingHealth;
  button.textContent = state.checkingHealth ? 'Проверка...' : 'Обновить состояние';
  status.textContent = state.applierReloadError
    || state.healthCheckError
    || state.applierReloadMessage
    || (state.checkingHealth
      ? 'Проверка состояния микросервисов...'
      : (services.length > 0 ? `Состояние: доступно ${okCount}, ошибок ${failedCount}.` : 'Проверка состояния еще не выполнялась.'));
  status.classList.toggle('error', Boolean(state.applierReloadError || state.healthCheckError || failedCount > 0));

  list.innerHTML = services.length > 0
    ? services.map(renderHealthServiceCard).join('')
    : '<div class="empty-state">Нет данных проверки состояния.</div>';
}

function renderHealthServiceCard(service) {
  const ok = service.status === 'ok';
  const payloadService = service.payload?.service ?? service.payload?.Service ?? '';
  const appVersion = service.payload?.version ?? service.payload?.Version ?? '';
  const configurationVersion = service.payload?.configurationVersion ?? service.payload?.ConfigurationVersion ?? '';
  const configurationReloadedAt = service.payload?.configurationReloadedAt ?? service.payload?.ConfigurationReloadedAt ?? '';
  const conversionRules = readServiceConversionRulesStatus(service.payload);
  const uiRules = uiConversionRulesVersionInfo();
  const showUiRules = Boolean(conversionRules || service.canReloadConfiguration);
  const comparison = conversionRules ? compareConversionRulesVersions(uiRules, conversionRules) : null;
  const reloading = state.reloadingAppliers.has(service.id);
  const details = [
    service.httpStatus ? `HTTP ${service.httpStatus}` : '',
    Number.isFinite(Number(service.latencyMs)) ? `${service.latencyMs} ms` : '',
    payloadService ? `сервис ${payloadService}` : ''
  ].filter(Boolean).join(' · ');
  const versionDetails = [
    appVersion ? { label: 'Версия', value: appVersion } : null,
    configurationVersion ? { label: 'Конфигурация', value: `v${configurationVersion}` } : null,
    configurationReloadedAt ? { label: 'Перечитана', value: formatCacheTimestamp(configurationReloadedAt) } : null,
    showUiRules ? { label: 'Правила UI', value: formatUiConversionRulesVersion(uiRules) } : null,
    conversionRules ? {
      label: 'Правила микросервиса',
      value: formatServiceConversionRulesVersion(conversionRules),
      className: conversionRules.error ? 'error' : ''
    } : null,
    comparison ? {
      label: 'Сравнение правил',
      value: comparison.text,
      className: comparison.matches ? 'match' : 'warning'
    } : null
  ].filter(Boolean);
  const versionDetailsHtml = versionDetails.length > 0
    ? `<dl class="health-version-list">${versionDetails.map((item) => `
        <div class="${escapeHtml(item.className || '')}">
          <dt>${escapeHtml(item.label)}</dt>
          <dd>${escapeHtml(item.value)}</dd>
        </div>
      `).join('')}</dl>`
    : '';
  const reloadButton = service.canReloadConfiguration
    ? `<button class="secondary-button health-reload-button" type="button" data-applier-reload="${escapeHtml(service.id)}" ${reloading ? 'disabled' : ''}>${reloading ? 'Перечитывание...' : 'Перечитать конфигурацию'}</button>`
    : '';
  return `
    <article class="health-card ${ok ? 'ok' : 'error'}">
      <div>
        <strong>${escapeHtml(service.name || service.id || service.url)}</strong>
        <span>${escapeHtml(service.url || '')}</span>
      </div>
      <div class="health-card-actions">
        <span class="health-status">${ok ? 'ОК' : 'ошибка'}</span>
        ${reloadButton}
      </div>
      ${versionDetailsHtml}
      <p class="health-meta">${escapeHtml(details || '-')}</p>
      ${ok ? '' : `<p class="health-error">${escapeHtml(service.error || 'проверка состояния не выполнена')}</p>`}
    </article>
  `;
}

function readServiceConversionRulesStatus(payload) {
  const status = payload?.conversionRules ?? payload?.ConversionRules ?? null;
  if (!status || typeof status !== 'object') {
    return null;
  }

  const error = String(status.error ?? status.Error ?? '').trim();
  return {
    version: String(status.version ?? status.Version ?? '').trim(),
    ruleCount: Number(status.ruleCount ?? status.RuleCount ?? 0),
    serviceRuleCount: Number(status.serviceRuleCount ?? status.ServiceRuleCount ?? 0),
    suppressionRuleCount: Number(status.suppressionRuleCount ?? status.SuppressionRuleCount ?? 0),
    filePath: String(status.filePath ?? status.FilePath ?? ''),
    loadedAtUtc: status.loadedAtUtc ?? status.LoadedAtUtc ?? '',
    fileLastModifiedAtUtc: status.fileLastModifiedAtUtc ?? status.FileLastModifiedAtUtc ?? '',
    isValid: status.isValid ?? status.IsValid ?? !error,
    error
  };
}

function uiConversionRulesVersionInfo() {
  const service = parseRuleDocument('service');
  const suppression = parseRuleDocument('suppression');
  if (!service.ok || !suppression.ok) {
    return {
      ok: false,
      serviceVersion: '',
      suppressionVersion: '',
      serviceRuleCount: state.ruleExamples.service.length,
      suppressionRuleCount: state.ruleExamples.suppression.length,
      totalRuleCount: state.ruleExamples.service.length + state.ruleExamples.suppression.length
    };
  }

  const serviceVersion = String(service.document.version ?? '1').trim() || '1';
  const suppressionVersion = String(suppression.document.version ?? '1').trim() || '1';
  const serviceRuleCount = service.document.rules.length;
  const suppressionRuleCount = suppression.document.rules.length;
  return {
    ok: true,
    serviceVersion,
    suppressionVersion,
    serviceRuleCount,
    suppressionRuleCount,
    totalRuleCount: serviceRuleCount + suppressionRuleCount
  };
}

function formatUiConversionRulesVersion(info) {
  if (!info.ok) {
    return 'ошибка локальных правил';
  }

  return [
    `сервис v${info.serviceVersion} (${info.serviceRuleCount})`,
    `подавление v${info.suppressionVersion} (${info.suppressionRuleCount})`,
    `runtime v${expectedRuntimeConversionRulesVersion(info)} (${info.totalRuleCount})`
  ].join(' · ');
}

function formatServiceConversionRulesVersion(status) {
  if (status.error) {
    return status.error;
  }

  const version = status.version ? `runtime v${status.version}` : 'runtime-версия -';
  const counts = [
    `${Number.isFinite(status.ruleCount) ? status.ruleCount : 0} правил`,
    `${Number.isFinite(status.serviceRuleCount) ? status.serviceRuleCount : 0} сервис`,
    `${Number.isFinite(status.suppressionRuleCount) ? status.suppressionRuleCount : 0} подавление`
  ].join(', ');
  const updatedAt = status.loadedAtUtc || status.fileLastModifiedAtUtc
    ? ` · ${formatCacheTimestamp(status.loadedAtUtc || status.fileLastModifiedAtUtc)}`
    : '';
  return `${version} (${counts})${updatedAt}`;
}

function expectedRuntimeConversionRulesVersion(info) {
  if (!info?.ok) {
    return '';
  }

  const versions = [info.serviceVersion, info.suppressionVersion]
    .map(normalizeConversionRulesVersion)
    .filter(Boolean);
  const uniqueVersions = [...new Set(versions)];
  return uniqueVersions.length === 1
    ? uniqueVersions[0]
    : (uniqueVersions.join('+') || '1');
}

function normalizeConversionRulesVersion(value) {
  return String(value ?? '').trim().replace(/^v/i, '');
}

function splitRuntimeConversionRulesVersion(value) {
  return normalizeConversionRulesVersion(value)
    .split(/[+/]/)
    .map(normalizeConversionRulesVersion)
    .filter(Boolean);
}

function conversionRulesVersionsMatch(uiRules, serviceRules) {
  const expectedVersion = expectedRuntimeConversionRulesVersion(uiRules);
  const serviceVersion = normalizeConversionRulesVersion(serviceRules.version);
  if (!expectedVersion || !serviceVersion) {
    return false;
  }

  if (serviceVersion === expectedVersion) {
    return true;
  }

  const expectedParts = splitRuntimeConversionRulesVersion(expectedVersion);
  const serviceParts = splitRuntimeConversionRulesVersion(serviceVersion);
  return expectedParts.length > 0
    && expectedParts.length === serviceParts.length
    && expectedParts.every((version, index) => version === serviceParts[index]);
}

function compareConversionRulesVersions(uiRules, serviceRules) {
  if (serviceRules.error || !serviceRules.isValid) {
    return {
      matches: false,
      text: serviceRules.error || 'правила микросервиса невалидны'
    };
  }

  if (!uiRules.ok) {
    return {
      matches: false,
      text: 'локальные правила UI невалидны'
    };
  }

  const expectedVersion = expectedRuntimeConversionRulesVersion(uiRules);
  const serviceVersion = normalizeConversionRulesVersion(serviceRules.version);
  const versionMatches = conversionRulesVersionsMatch(uiRules, serviceRules);
  const countMatches = Number(serviceRules.ruleCount) === uiRules.totalRuleCount;
  if (versionMatches && countMatches) {
    return {
      matches: true,
      text: `совпадает: runtime v${expectedVersion}, ${uiRules.totalRuleCount} правил`
    };
  }

  const parts = [];
  if (!versionMatches) {
    parts.push(`UI runtime v${expectedVersion || '-'} (сервис v${uiRules.serviceVersion}, подавление v${uiRules.suppressionVersion}) vs микросервис runtime v${serviceVersion || '-'}`);
  }
  if (!countMatches) {
    parts.push(`UI ${uiRules.totalRuleCount} правил vs микросервис ${Number(serviceRules.ruleCount) || 0}`);
  }
  return {
    matches: false,
    text: parts.join('; ')
  };
}

function renderEventsView() {
  const identifier = document.querySelector('#kafkaManagedIdentifier');
  const prefix = document.querySelector('#kafkaManagedPrefix');
  const topicCount = document.querySelector('#kafkaTopicCount');
  const checkedAt = document.querySelector('#kafkaTopicsCheckedAt');
  const status = document.querySelector('#kafkaEventsStatus');
  const select = document.querySelector('#kafkaTopicSelect');
  const limitInput = document.querySelector('#kafkaEventLimitInput');
  const topicsButton = document.querySelector('#refreshKafkaTopicsButton');
  const eventsButton = document.querySelector('#refreshKafkaEventsButton');
  const topicList = document.querySelector('#kafkaTopicList');
  const eventList = document.querySelector('#kafkaEventList');
  if (!identifier
    || !prefix
    || !topicCount
    || !checkedAt
    || !status
    || !select
    || !limitInput
    || !topicsButton
    || !eventsButton
    || !topicList
    || !eventList) {
    return;
  }

  identifier.textContent = state.kafkaTopicsIdentifier || state.kafkaConfig.managedIdentifier || '-';
  prefix.textContent = state.kafkaTopicsPrefix || state.kafkaConfig.managedTopicPrefix || '-';
  topicCount.textContent = String(state.kafkaTopics.length);
  checkedAt.textContent = formatCacheTimestamp(state.kafkaTopicsCheckedAt);
  limitInput.value = String(state.kafkaEventLimit);
  topicsButton.disabled = state.loadingKafkaTopics || state.loadingKafkaEvents;
  topicsButton.textContent = state.loadingKafkaTopics ? 'Загрузка...' : 'Обновить топики';
  eventsButton.disabled = state.loadingKafkaTopics || state.loadingKafkaEvents || !state.kafkaSelectedTopic;
  eventsButton.textContent = state.loadingKafkaEvents ? 'Загрузка...' : 'Обновить события';

  select.innerHTML = state.kafkaTopics.length > 0
    ? state.kafkaTopics.map((topic) => {
      const selected = topic.name === state.kafkaSelectedTopic ? 'selected' : '';
      return `<option value="${escapeHtml(topic.name)}" ${selected}>${escapeHtml(kafkaTopicLabel(topic))}</option>`;
    }).join('')
    : '<option value="">Нет управляемых топиков</option>';
  select.disabled = state.loadingKafkaTopics || state.kafkaTopics.length === 0;

  const currentTopic = state.kafkaTopics.find((topic) => topic.name === state.kafkaSelectedTopic);
  const summary = currentTopic
    ? `Загружено событий: ${state.kafkaEvents.length} из ${currentTopic.name}.`
    : 'Выберите Kafka-топик.';
  status.textContent = state.kafkaTopicsError
    || state.kafkaEventsError
    || (state.loadingKafkaTopics
      ? 'Загрузка списка управляемых Kafka-топиков...'
      : (state.loadingKafkaEvents ? 'Загрузка последних событий...' : summary));
  status.classList.toggle('error', Boolean(state.kafkaTopicsError || state.kafkaEventsError));

  topicList.innerHTML = state.kafkaTopics.length > 0
    ? state.kafkaTopics.map(renderKafkaTopicCard).join('')
    : '<div class="empty-state">Управляемые Kafka-топики не загружены.</div>';
  eventList.innerHTML = state.kafkaEvents.length > 0
    ? state.kafkaEvents.map(renderKafkaEventCard).join('')
    : '<div class="empty-state">События не загружены.</div>';
}

function renderKafkaTopicCard(topic) {
  const selected = topic.name === state.kafkaSelectedTopic;
  const existsClass = topic.exists === true ? 'ok' : (topic.exists === false ? 'error' : '');
  const existsText = topic.exists === true ? 'есть' : (topic.exists === false ? 'нет' : 'не проверен');
  const partitions = topic.partitionCount == null ? '-' : `${topic.partitionCount} разделов`;
  return `
    <button class="topic-card ${selected ? 'selected' : ''}" type="button" data-kafka-topic="${escapeHtml(topic.name)}">
      <strong>${escapeHtml(topic.name)}</strong>
      <span>${escapeHtml(topic.description || topic.role || '')}</span>
      <span class="topic-meta ${existsClass}">${escapeHtml(existsText)} · ${escapeHtml(partitions)}</span>
      ${topic.error ? `<span class="topic-error">${escapeHtml(topic.error)}</span>` : ''}
    </button>
  `;
}

function renderKafkaEventCard(event) {
  const timestamp = formatCacheTimestamp(event.timestampUtc ?? event.timestamp ?? '');
  const value = typeof event.json === 'object' && event.json !== null
    ? JSON.stringify(event.json, null, 2)
    : String(event.value ?? '');
  return `
    <article class="event-card">
      <div class="event-meta">
        <strong>${escapeHtml(timestamp)}</strong>
        <span>раздел ${escapeHtml(event.partition)} · offset ${escapeHtml(event.offset)} · ключ ${escapeHtml(event.key || '-')}</span>
      </div>
      <pre class="event-value">${escapeHtml(value || '-')}</pre>
    </article>
  `;
}

function kafkaTopicLabel(topic) {
  return `${topic.name}${topic.exists === false ? ' (отсутствует)' : ''}`;
}

function renderDataSourceSyncView() {
  renderTopSourceStatus();
  const classCount = document.querySelector('#syncClassCount');
  const attributeCount = document.querySelector('#syncAttributeCount');
  const domainCount = document.querySelector('#syncDomainCount');
  const serviceInstanceCount = document.querySelector('#syncServiceInstanceCount');
  const suppressionInstanceCount = document.querySelector('#syncSuppressionInstanceCount');
  const updatedAt = document.querySelector('#syncLastUpdatedAt');
  const status = document.querySelector('#syncSourcesStatus');
  const button = document.querySelector('#syncSourcesButton');
  const cacheButton = document.querySelector('#loadCachedSourcesButton');
  if (!classCount
    || !attributeCount
    || !domainCount
    || !serviceInstanceCount
    || !suppressionInstanceCount
    || !updatedAt
    || !status
    || !button
    || !cacheButton) {
    return;
  }

  classCount.textContent = String(state.cmdbClasses.length);
  attributeCount.textContent = String(cmdbAttributeCount());
  domainCount.textContent = String(state.cmdbSourceDomains.length);
  serviceInstanceCount.textContent = String(cmdbInstanceCount('service'));
  suppressionInstanceCount.textContent = String(cmdbInstanceCount('suppression'));
  updatedAt.textContent = formatCacheTimestamp(state.cmdbCacheUpdatedAt);
  button.disabled = state.syncingSources || state.loadingSourcesCache;
  button.textContent = state.syncingSources
    ? 'Синхронизация...'
    : 'Провести синхронизацию';
  cacheButton.disabled = state.syncingSources || state.loadingSourcesCache;
  cacheButton.textContent = state.loadingSourcesCache
    ? 'Загрузка...'
    : 'Загрузить локальный кэш';
  status.textContent = state.syncError
    || state.cmdbClassError
    || state.cmdbClassSchemaError
    || state.cmdbDomainError
    || state.cmdbClassInstanceError
    || state.syncMessage;
  status.classList.toggle('error', Boolean(state.syncError || state.cmdbClassError || state.cmdbClassSchemaError || state.cmdbDomainError || state.cmdbClassInstanceError));
}

function renderZabbixSyncView() {
  renderTopSourceStatus();
  const apiState = document.querySelector('#zabbixSyncApiState');
  const version = document.querySelector('#zabbixSyncVersion');
  const endpoint = document.querySelector('#zabbixSyncEndpoint');
  const updatedAt = document.querySelector('#zabbixSyncLastUpdatedAt');
  const status = document.querySelector('#syncZabbixStatus');
  const button = document.querySelector('#syncZabbixButton');
  const cacheButton = document.querySelector('#loadCachedZabbixButton');
  if (!apiState || !version || !endpoint || !updatedAt || !status || !button || !cacheButton) {
    return;
  }

  const check = state.zabbixCheck;
  const success = Boolean(check?.success ?? check?.Success);
  apiState.textContent = state.checkingZabbix
    ? 'Проверка...'
    : (success ? 'Доступен' : (state.zabbixCheckError ? 'Ошибка' : 'Не проверен'));
  version.textContent = String(check?.version ?? check?.Version ?? '-');
  endpoint.textContent = String(check?.endpoint ?? check?.Endpoint ?? '-');
  updatedAt.textContent = formatCacheTimestamp(state.zabbixCacheUpdatedAt);
  button.disabled = state.checkingZabbix || state.loadingZabbixCache;
  button.textContent = state.checkingZabbix
    ? 'Синхронизация...'
    : 'Провести синхронизацию';
  cacheButton.disabled = state.checkingZabbix || state.loadingZabbixCache;
  cacheButton.textContent = state.loadingZabbixCache
    ? 'Загрузка...'
    : 'Загрузить локальный кэш';
  status.textContent = state.zabbixCheckError
    || check?.summary
    || check?.Summary
    || check?.error
    || check?.Error
    || '';
  status.classList.toggle('error', Boolean(state.zabbixCheckError || check?.error || check?.Error));
}

function renderWebhooksSyncView() {
  renderTopSourceStatus();
  const apiState = document.querySelector('#webhooksSyncApiState');
  const endpoint = document.querySelector('#webhooksSyncEndpoint');
  const route = document.querySelector('#webhooksSyncRoute');
  const rawTopic = document.querySelector('#webhooksSyncRawTopic');
  const identifier = document.querySelector('#webhooksSyncIdentifier');
  const createCount = document.querySelector('#webhooksSyncCreateCount');
  const updateCount = document.querySelector('#webhooksSyncUpdateCount');
  const deleteCount = document.querySelector('#webhooksSyncDeleteCount');
  const ruleClassCount = document.querySelector('#webhooksRuleClassCount');
  const missingClassCount = document.querySelector('#webhooksMissingClassCount');
  const updatedAt = document.querySelector('#webhooksSyncLastUpdatedAt');
  const status = document.querySelector('#syncWebhooksStatus');
  const coverageStatus = document.querySelector('#webhookRuleCoverageStatus');
  const coverageList = document.querySelector('#webhookRuleCoverageList');
  const button = document.querySelector('#syncWebhooksButton');
  const publishButton = document.querySelector('#publishWebhooksButton');
  const ruleCheckButton = document.querySelector('#checkWebhookRulesButton');
  const cacheButton = document.querySelector('#loadCachedWebhooksButton');
  if (!apiState
    || !endpoint
    || !route
    || !rawTopic
    || !identifier
    || !createCount
    || !updateCount
    || !deleteCount
    || !ruleClassCount
    || !missingClassCount
    || !updatedAt
    || !status
    || !coverageStatus
    || !coverageList
    || !button
    || !publishButton
    || !ruleCheckButton
    || !cacheButton) {
    return;
  }

  const check = state.webhooksCheck;
  const success = Boolean((check?.status ?? check?.Status) === 'ok' || check?.success || check?.Success);
  const config = state.webhooksConfig ?? {};
  const counts = check?.eventCounts ?? countManagedWebhookEvents(config);
  const managedIdentifier = String(check?.identifier ?? config.managedIdentifier ?? '');
  apiState.textContent = state.checkingWebhooks
    ? 'Проверка...'
    : (success ? 'Доступен' : (state.webhooksCheckError ? 'Ошибка' : 'Не проверен'));
  endpoint.textContent = String(check?.endpoint ?? config.targetUrl ?? '-');
  route.textContent = String(check?.route ?? config.route ?? '-');
  rawTopic.textContent = String(check?.rawTopic ?? config.rawTopic ?? '-');
  identifier.textContent = managedIdentifier || '-';
  createCount.textContent = String(counts.CREATE ?? 0);
  updateCount.textContent = String(counts.UPDATE ?? 0);
  deleteCount.textContent = String(counts.DELETE ?? 0);
  const coverage = state.webhookRuleCoverage;
  ruleClassCount.textContent = coverage ? String(coverage.sourceClassCount) : '-';
  missingClassCount.textContent = coverage ? String(coverage.missing.length) : '-';
  updatedAt.textContent = formatCacheTimestamp(state.webhooksCacheUpdatedAt);
  const busy = state.checkingWebhooks || state.checkingWebhookRuleCoverage || state.loadingWebhooksCache || state.publishingWebhooks;
  button.disabled = busy;
  button.textContent = state.checkingWebhooks
    ? 'Перечитывание...'
    : 'Перечитать из CMDBuild';
  publishButton.disabled = busy;
  publishButton.textContent = state.publishingWebhooks
    ? 'Публикация...'
    : 'Опубликовать webhooks в CMDBuild';
  ruleCheckButton.disabled = busy;
  ruleCheckButton.textContent = state.checkingWebhookRuleCoverage
    ? 'Проверка...'
    : 'Сверить правила онлайн';
  cacheButton.disabled = busy;
  cacheButton.textContent = state.loadingWebhooksCache
    ? 'Загрузка...'
    : 'Загрузить локальный кэш';
  status.textContent = state.webhooksPublishError
    || state.webhooksCheckError
    || state.webhooksPublishMessage
    || check?.summary
    || check?.Summary
    || (success && managedIdentifier
      ? `Загружены наши webhooks по идентификатору ${managedIdentifier}: CREATE ${counts.CREATE ?? 0}, UPDATE ${counts.UPDATE ?? 0}, DELETE ${counts.DELETE ?? 0}. Чужие webhooks CMDBuild не учитываются.`
      : '')
    || check?.service
    || check?.Service
    || '';
  status.classList.toggle('error', Boolean(state.webhooksPublishError || state.webhooksCheckError || check?.error || check?.Error));
  coverageStatus.textContent = webhookRuleCoverageSummary();
  coverageStatus.classList.toggle('error', Boolean(state.webhookRuleCoverageError || coverage?.missing?.length > 0));
  coverageList.innerHTML = renderWebhookRuleCoverageList(coverage);
}

function webhookRuleCoverageSummary() {
  if (state.checkingWebhookRuleCoverage) {
    return 'Онлайн-проверка webhooks против текущих правил конвертации...';
  }
  if (state.webhookRuleCoverageError) {
    return state.webhookRuleCoverageError;
  }

  const coverage = state.webhookRuleCoverage;
  if (!coverage) {
    return 'Онлайн-проверка не выполнялась. Кнопка проверяет текущий адрес webhooks, а не локальный кэш.';
  }
  if (coverage.sourceClassCount === 0) {
    return `Онлайн-проверка ${formatCacheTimestamp(coverage.checkedAt)}: в текущих правилах нет классов-источников для проверки.`;
  }
  if (coverage.missing.length === 0) {
    const globalText = coverage.hasGlobalWebhooks
      ? ` Общие webhooks покрывают события: ${coverage.globalEvents.join(', ')}.`
      : '';
    return `Онлайн-проверка ${formatCacheTimestamp(coverage.checkedAt)}: все ${coverage.sourceClassCount} классов-источников правил покрыты управляемыми webhooks.${globalText}`;
  }

  const missingText = webhookMissingClassSummary(coverage);
  return `Онлайн-проверка ${formatCacheTimestamp(coverage.checkedAt)}: ${coverage.missing.length} из ${coverage.sourceClassCount} классов-источников правил не имеют полного набора управляемых webhooks или payload-полей.${missingText} Нажмите "Опубликовать webhooks в CMDBuild" или обновите управляемый реестр.`;
}

function webhookMissingClassSummary(coverage) {
  const names = (coverage?.missing ?? [])
    .map((item) => item.displayName || item.code)
    .filter(Boolean);
  if (names.length === 0) {
    return '';
  }

  const visible = names.slice(0, 6);
  const rest = names.length - visible.length;
  return ` Не покрыты: ${visible.join(', ')}${rest > 0 ? ` и еще ${rest}` : ''}.`;
}

function renderWebhookRuleCoverageList(coverage) {
  if (state.webhookRuleCoverageError) {
    return `<div class="empty-state error-state">${escapeHtml(state.webhookRuleCoverageError)}</div>`;
  }
  if (!coverage || coverage.missing.length === 0) {
    return '';
  }

  return `
    <div class="webhook-coverage-list">
      ${coverage.missing.map((item) => `
        <div class="webhook-coverage-item">
          <strong>${escapeHtml(item.displayName || item.code)} (${escapeHtml(item.code)})</strong>
          <span>класс-источник проверяется один раз · слои ${escapeHtml(item.layers.join(', '))} · правил для диагностики ${item.ruleCount ?? item.ruleIds.length} · нет событий ${escapeHtml(item.missingEvents.join(', ') || '-')} · поля ${escapeHtml(webhookMissingFieldText(item))}</span>
        </div>
      `).join('')}
    </div>
  `;
}

function webhookMissingFieldText(item) {
  const parts = Object.entries(item.missingFieldsByEvent ?? {})
    .filter(([, fields]) => Array.isArray(fields) && fields.length > 0)
    .map(([eventType, fields]) => `${eventType}: ${fields.join(', ')}`);
  return parts.join(' | ') || '-';
}

function countManagedWebhookEvents(config) {
  const identifier = String(config?.managedIdentifier ?? '').trim();
  const counts = { CREATE: 0, UPDATE: 0, DELETE: 0 };
  for (const event of Array.isArray(config?.events) ? config.events : []) {
    const eventIdentifier = String(event?.identifier ?? event?.Identifier ?? '').trim();
    if (identifier && eventIdentifier !== identifier) {
      continue;
    }

    const eventType = String(event?.eventType ?? event?.type ?? event?.EventType ?? event?.Type ?? '').toUpperCase();
    if (Object.hasOwn(counts, eventType)) {
      counts[eventType] += 1;
    }
  }

  return counts;
}

function calculateWebhookRuleCoverage(check) {
  const sourceClasses = conversionRuleSourceClasses();
  const coverage = managedWebhookCoverage(check);
  const missing = [];

  for (const sourceClass of sourceClasses) {
    const classToken = canonicalToken(sourceClass.code);
    const classEvents = coverage.byClass.get(classToken) ?? new Set();
    const missingEvents = REQUIRED_WEBHOOK_EVENTS.filter((eventType) =>
      !coverage.globalEvents.has(eventType) && !classEvents.has(eventType));
    const missingFieldsByEvent = {};
    for (const eventType of REQUIRED_WEBHOOK_EVENTS.filter((item) => !missingEvents.includes(item))) {
      const missingFields = sourceClass.requiredFields.filter((field) =>
        !webhookFieldCovered(coverage, classToken, eventType, field));
      if (missingFields.length > 0) {
        missingFieldsByEvent[eventType] = missingFields;
      }
    }

    if (missingEvents.length > 0 || Object.keys(missingFieldsByEvent).length > 0) {
      missing.push({
        ...sourceClass,
        missingEvents,
        missingFieldsByEvent
      });
    }
  }

  return {
    checkedAt: new Date().toISOString(),
    online: true,
    sourceClassCount: sourceClasses.length,
    missing,
    globalEvents: [...coverage.globalEvents],
    classSpecificWebhookCount: coverage.classSpecificWebhookCount,
    hasGlobalWebhooks: coverage.globalEvents.size > 0
  };
}

function conversionRuleSourceClasses() {
  syncRulesFromDocument('service');
  syncRulesFromDocument('suppression');
  const byCode = new Map();
  for (const layerKey of ['service', 'suppression']) {
    for (const rule of state.ruleExamples[layerKey] ?? []) {
      if (rule?.enabled === false) {
        continue;
      }

      const classCode = ruleSourceClassCode(rule);
      if (!classCode) {
        continue;
      }

      const key = canonicalToken(classCode);
      const current = byCode.get(key) ?? {
        code: classCode,
        displayName: sourceClassDisplayName(classCode),
        layers: new Set(),
        ruleIds: new Set(),
        ruleCount: 0,
        requiredFields: new Set(),
        payloadFields: new Set()
      };
      current.layers.add(layerKey === 'service' ? 'service' : 'suppression');
      current.ruleCount += 1;
      if (rule.rule_id || rule.name) {
        current.ruleIds.add(rule.rule_id ?? rule.name ?? '');
      }
      for (const field of sourceAttributeCodesForRulePreview(rule)) {
        current.requiredFields.add(field);
      }
      for (const field of sourceDirectPayloadFieldsForRule(rule)) {
        current.payloadFields.add(field);
      }
      byCode.set(key, current);
    }
  }

  const sourceOrder = sourceClassOrderMap();
  return [...byCode.values()]
    .map((item) => ({
      ...item,
      layers: [...item.layers],
      ruleIds: [...item.ruleIds].filter(Boolean),
      requiredFields: [...item.requiredFields].filter(Boolean).sort((left, right) =>
        left.localeCompare(right, undefined, { sensitivity: 'base' })),
      payloadFields: [...item.payloadFields].filter(Boolean).sort((left, right) =>
        left.localeCompare(right, undefined, { sensitivity: 'base' }))
    }))
    .sort((left, right) => compareSourceClassCodesByHierarchy(left.code, right.code, sourceOrder));
}

function sourceDirectPayloadFieldsForRule(rule) {
  const sourceClass = ruleSourceClassCode(rule);
  const directAttributes = sourceDirectAttributes(sourceClass).map(attributeCode).filter(Boolean);
  const result = new Set();
  for (const field of sourceFieldsForRulePreview(rule)) {
    for (const attribute of directAttributes) {
      if (sourceFieldCanRepresentAttribute(field, attribute)) {
        result.add(attribute);
      }
    }
  }
  return [...result].filter(Boolean);
}

function managedWebhookCoverage(check) {
  const identifier = String(check?.identifier ?? state.webhooksConfig?.managedIdentifier ?? '').trim();
  const config = {
    ...state.webhooksConfig,
    events: onlineWebhookEvents(check)
  };
  if (config.events.length === 0) {
    config.events = Array.isArray(state.webhooksConfig?.events) ? state.webhooksConfig.events : [];
  }

  const globalEvents = new Set();
  const byClass = new Map();
  const globalFieldScopes = new Map();
  const fieldScopesByClass = new Map();
  let classSpecificWebhookCount = 0;
  for (const event of config.events) {
    const eventIdentifier = String(event?.identifier ?? event?.Identifier ?? '').trim();
    if (identifier && eventIdentifier && eventIdentifier !== identifier) {
      continue;
    }

    const eventType = String(event?.eventType ?? event?.type ?? event?.EventType ?? event?.Type ?? '').toUpperCase();
    if (!REQUIRED_WEBHOOK_EVENTS.includes(eventType)) {
      continue;
    }

    const classCodes = webhookEventClassCodes(event);
    const fieldScope = webhookEventFieldScope(event);
    if (classCodes.length === 0) {
      globalEvents.add(eventType);
      mergeWebhookFieldScope(globalFieldScopes, eventType, fieldScope);
      continue;
    }

    classSpecificWebhookCount += classCodes.length;
    for (const classCode of classCodes) {
      const key = canonicalToken(classCode);
      const eventSet = byClass.get(key) ?? new Set();
      eventSet.add(eventType);
      byClass.set(key, eventSet);
      const scopes = fieldScopesByClass.get(key) ?? new Map();
      mergeWebhookFieldScope(scopes, eventType, fieldScope);
      fieldScopesByClass.set(key, scopes);
    }
  }

  return { globalEvents, byClass, globalFieldScopes, fieldScopesByClass, classSpecificWebhookCount };
}

function mergeWebhookFieldScope(scopeMap, eventType, nextScope) {
  const current = scopeMap.get(eventType) ?? { wildcard: false, fields: new Set() };
  current.wildcard ||= nextScope.wildcard;
  for (const field of nextScope.fields) {
    current.fields.add(field);
  }
  scopeMap.set(eventType, current);
}

function webhookFieldCovered(coverage, classToken, eventType, field) {
  const fieldToken = canonicalToken(field);
  const scopes = [
    coverage.globalFieldScopes?.get(eventType),
    coverage.fieldScopesByClass?.get(classToken)?.get(eventType)
  ].filter(Boolean);
  if (scopes.some((scope) => scope.wildcard)) {
    return true;
  }

  return scopes.some((scope) =>
    [...scope.fields].some((candidate) => canonicalToken(candidate) === fieldToken));
}

function onlineWebhookEvents(payload) {
  const candidates = [
    payload?.events,
    payload?.Events,
    payload?.webhooks,
    payload?.Webhooks,
    payload?.config?.events,
    payload?.Config?.Events
  ];
  for (const candidate of candidates) {
    if (Array.isArray(candidate)) {
      return candidate;
    }
  }
  return [];
}

function webhookEventClassCodes(event) {
  const result = [];
  const directValues = [
    event?.classCode,
    event?.class_code,
    event?.ClassCode,
    event?.class,
    event?.Class,
    event?.className,
    event?.ClassName,
    event?.sourceClass,
    event?.source_class,
    event?.sourceClassCode,
    event?.source_class_code,
    event?.cmdbClass,
    event?.cmdbClassCode,
    event?.filter?.class_code,
    event?.filter?.classCode
  ];
  for (const value of directValues) {
    if (String(value ?? '').trim()) {
      result.push(String(value).trim());
    }
  }

  const arrayValues = [
    event?.classCodes,
    event?.class_codes,
    event?.classes,
    event?.sourceClasses,
    event?.source_class_codes,
    event?.filter?.classes
  ];
  for (const values of arrayValues) {
    if (!Array.isArray(values)) {
      continue;
    }

    for (const value of values) {
      const classCode = String(value?.code ?? value?.classCode ?? value ?? '').trim();
      if (classCode) {
        result.push(classCode);
      }
    }
  }

  return [...new Set(result)];
}

function webhookEventFieldScope(event) {
  const fieldArrays = [
    event?.fields,
    event?.fieldCodes,
    event?.field_codes,
    event?.attributes,
    event?.attributeCodes,
    event?.attribute_codes,
    event?.payloadFields,
    event?.payload_fields,
    event?.filter?.fields,
    event?.filter?.attributes
  ];
  const explicitArrays = fieldArrays.filter((value) => Array.isArray(value));
  if (explicitArrays.length === 0) {
    return { wildcard: true, fields: new Set() };
  }

  const fields = new Set();
  for (const values of explicitArrays) {
    for (const value of values) {
      const field = String(value?.code ?? value?.name ?? value?.field ?? value ?? '').trim();
      if (field) {
        fields.add(field);
      }
    }
  }

  return { wildcard: false, fields };
}

function renderConversionConfigSyncView() {
  renderTopSourceStatus();
  const serviceRuleCount = document.querySelector('#conversionServiceRuleCount');
  const suppressionRuleCount = document.querySelector('#conversionSuppressionRuleCount');
  const serviceTemplateCount = document.querySelector('#conversionServiceTemplateCount');
  const suppressionTemplateCount = document.querySelector('#conversionSuppressionTemplateCount');
  const sharedTemplateCount = document.querySelector('#conversionSharedTemplateCount');
  const serviceRelationCount = document.querySelector('#conversionServiceRelationCount');
  const suppressionRelationCount = document.querySelector('#conversionSuppressionRelationCount');
  const traversalDepth = document.querySelector('#conversionTraversalDepth');
  const folder = document.querySelector('#conversionConfigFolder');
  const storageUpdatedAt = document.querySelector('#conversionConfigStorageUpdatedAt');
  const updatedAt = document.querySelector('#conversionConfigLastUpdatedAt');
  const status = document.querySelector('#syncConversionConfigStatus');
  const button = document.querySelector('#syncConversionConfigButton');
  const storedButton = document.querySelector('#loadStoredConversionConfigButton');
  const cacheButton = document.querySelector('#loadCachedConversionConfigButton');
  if (!serviceRuleCount || !suppressionRuleCount || !serviceTemplateCount || !suppressionTemplateCount || !sharedTemplateCount || !serviceRelationCount || !suppressionRelationCount || !traversalDepth || !folder || !storageUpdatedAt || !updatedAt || !status || !button || !storedButton || !cacheButton) {
    return;
  }

  const relationCounts = conversionConfigManagedRelationCounts();
  serviceRuleCount.textContent = String(state.ruleExamples.service.length);
  suppressionRuleCount.textContent = String(state.ruleExamples.suppression.length);
  serviceTemplateCount.textContent = String(normalizeTemplateDocument(state.templateDocuments.service, 'service').templates.length);
  suppressionTemplateCount.textContent = String(normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates.length);
  sharedTemplateCount.textContent = String(normalizeTemplateDocument(state.templateDocuments.shared, 'shared').templates.length);
  serviceRelationCount.textContent = String(relationCounts.service);
  suppressionRelationCount.textContent = String(relationCounts.suppression);
  traversalDepth.textContent = String(state.maxTraversalDepth);
  folder.textContent = conversionConfigFolderLabel() || '-';
  storageUpdatedAt.textContent = [
    state.conversionConfigStorageVersion ? `v${state.conversionConfigStorageVersion}` : '',
    formatCacheTimestamp(state.conversionConfigStorageUpdatedAt)
  ].filter(Boolean).join(' · ') || 'нет';
  updatedAt.textContent = formatCacheTimestamp(state.conversionConfigCacheUpdatedAt);
  const busy = state.syncingConversionConfigs || state.loadingStoredConversionConfigs || state.loadingConversionConfigCache;
  button.disabled = busy;
  button.textContent = state.syncingConversionConfigs
    ? 'Сохранение...'
    : 'Сохранить в папку';
  storedButton.disabled = busy;
  storedButton.textContent = state.loadingStoredConversionConfigs
    ? 'Загрузка...'
    : 'Загрузить из папки';
  cacheButton.disabled = busy;
  cacheButton.textContent = state.loadingConversionConfigCache
    ? 'Загрузка...'
    : 'Загрузить локальный кэш';
  status.textContent = state.syncConversionConfigError || state.syncConversionConfigMessage;
  status.classList.toggle('error', Boolean(state.syncConversionConfigError));
}

function cmdbCatalogError() {
  return state.cmdbClassError
    || state.cmdbClassSchemaError
    || state.cmdbDomainError
    || state.cmdbClassInstanceError;
}

function cmdbSourceCacheKey() {
  return `${CACHE_KEYS.cmdbuild}:${state.prefix || 'empty'}`;
}

function conversionConfigCacheKey() {
  return `${CACHE_KEYS.conversionConfig}:${state.prefix || 'empty'}`;
}

function applyCmdbSourceCache(cacheRecord) {
  const payload = cacheRecord.payload ?? {};
  state.cmdbClasses = Array.isArray(payload.classes) ? payload.classes : [];
  state.cmdbClassSchemas = Array.isArray(payload.classSchemas) ? payload.classSchemas : [];
  state.cmdbDomains = Array.isArray(payload.domains) ? payload.domains : [];
  state.cmdbSourceDomains = Array.isArray(payload.sourceDomains) ? payload.sourceDomains : state.cmdbDomains;
  replaceCmdbClassInstances(payload.classInstances, { preserveLoadedSourceCards: true });
  state.cmdbCacheUpdatedAt = cacheRecord.updatedAt ?? '';
  state.cmdbClassError = '';
  state.cmdbClassSchemaError = '';
  state.cmdbDomainError = '';
  state.cmdbClassInstanceError = '';
}

function formatCacheTimestamp(value) {
  if (!value) {
    return 'нет';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return 'нет';
  }

  return new Intl.DateTimeFormat('ru-RU', {
    dateStyle: 'short',
    timeStyle: 'medium'
  }).format(date);
}

function cmdbInstanceCount(layer = '') {
  const normalizedLayer = String(layer).toLowerCase();
  return state.cmdbClassInstances
    .filter((item) => !normalizedLayer || String(item.layer).toLowerCase() === normalizedLayer)
    .reduce((sum, item) => sum + (item.cards?.length ?? 0), 0);
}

function rememberCreatedTargetCard(layerKey, classCode, card) {
  const layer = layerKey === 'service' ? 'Service' : 'Suppression';
  let classItem = state.cmdbClassInstances.find((item) =>
    canonicalToken(item.classCode) === canonicalToken(classCode));
  if (!classItem) {
    const schemaClass = state.classes.find((item) => canonicalToken(item.code) === canonicalToken(classCode));
    classItem = {
      layer,
      classCode,
      className: schemaClass?.displayName || classCode,
      classDescription: schemaClass?.displayName || classCode,
      attributes: targetClassAttributes(classCode),
      cards: []
    };
    state.cmdbClassInstances.push(classItem);
  }

  if ((classItem.cards ?? []).some((item) => String(item.id) === String(card.id))) {
    return;
  }

  classItem.cards ??= [];
  classItem.cards.push({
    layer,
    classCode,
    id: card.id,
    description: card.description,
    attributes: targetClassAttributes(classCode).map((attribute) => ({
      code: attributeCode(attribute),
      name: attribute.name || attribute.displayName || attributeCode(attribute),
      description: attribute.description || attribute.displayName || attributeCode(attribute),
      type: attribute.type || '',
      valueKind: typeof card.values?.[attributeCode(attribute)],
      value: card.values?.[attributeCode(attribute)] == null
        ? null
        : String(card.values[attributeCode(attribute)])
    }))
  });
}

function cmdbAttributeCount() {
  return state.cmdbClassSchemas.reduce((sum, item) => sum + (item.attributes?.length ?? 0), 0);
}

function renderLayerRulesPreview(layerKey, layer, selectors) {
  const sourceStatus = document.querySelector(selectors.sourceStatus);
  const sourceList = document.querySelector(selectors.sourceList);
  const rulesList = document.querySelector(selectors.rulesList);
  const targetList = document.querySelector(selectors.targetList);
  if (!sourceStatus || !sourceList || !rulesList || !targetList) {
    return;
  }

  const sourceClasses = availableSourceClassSchemas(layerKey);
  sourceStatus.textContent = state.cmdbClassSchemaError || `Доступно классов-источников: ${sourceClasses.length}.`;
  sourceStatus.classList.toggle('error', Boolean(state.cmdbClassSchemaError));
  const highlight = rulesPreviewHighlight(layerKey);
  const search = rulesPreviewSearchValues(layerKey);
  const previewPanel = sourceList.closest('[data-rules-preview-layer]');
  previewPanel?.classList.toggle('rules-preview-has-selection', highlight.active);
  sourceList.innerHTML = renderSourceSchemaCards(layerKey, sourceClasses, highlight, search.source);
  rulesList.innerHTML = renderRuleGroups(layerKey, state.ruleExamples[layerKey] ?? [], highlight, search.rules);
  targetList.innerHTML = renderTargetSchemaCards(layerKey, layer, highlight, search.target);
}

function handleRulesPreviewClick(layerKey, event) {
  const clearButton = event.target.closest('[data-clear-rules-preview-selection]');
  if (clearButton) {
    state.rulesPreviewSelection[layerKey] = null;
    renderRulesPreviews();
    return;
  }

  if (event.target.closest('[data-detach-template-rule]')) {
    return;
  }

  const node = event.target.closest('[data-preview-node]');
  if (!node || !event.currentTarget.contains(node)) {
    return;
  }

  state.rulesPreviewSelection[layerKey] = {
    kind: node.dataset.previewNode || '',
    sourceClass: node.dataset.sourceClass || '',
    sourceAttribute: node.dataset.sourceAttribute || '',
    ruleId: node.dataset.ruleId || '',
    targetClass: node.dataset.targetClass || '',
    targetCardId: node.dataset.targetCardId || ''
  };
  renderRulesPreviews();
}

function handleRulesPreviewInput(layerKey, target) {
  if (!target.matches('[data-rules-preview-search]')) {
    return;
  }

  const column = target.dataset.rulesPreviewSearch;
  if (!['source', 'rules', 'target'].includes(column)) {
    return;
  }

  const search = rulesPreviewSearchValues(layerKey);
  search[column] = target.value;
  state.rulesPreviewSearch[layerKey] = search;
  renderRulesPreviews();
}

function rulesPreviewSearchValues(layerKey) {
  const current = state.rulesPreviewSearch[layerKey] ?? {};
  return {
    source: current.source ?? '',
    rules: current.rules ?? '',
    target: current.target ?? ''
  };
}

function rulesPreviewHighlight(layerKey) {
  const selection = state.rulesPreviewSelection[layerKey];
  const empty = {
    active: Boolean(selection),
    selected: selection ?? null,
    sourceClasses: new Set(),
    sourceAttributes: new Set(),
    rules: new Set(),
    targetClasses: new Set(),
    targetInstances: new Set()
  };
  if (!selection) {
    return empty;
  }

  const sourceClass = selection.sourceClass;
  const sourceAttribute = selection.sourceAttribute;
  const targetClass = selection.targetClass;
  const targetCardId = selection.targetCardId;
  const rules = state.ruleExamples[layerKey] ?? [];
  const matchedRules = rules.filter((rule) =>
    ruleMatchesPreviewSelection(layerKey, rule, selection, sourceClass, sourceAttribute, targetClass, targetCardId));

  if (sourceClass) {
    empty.sourceClasses.add(canonicalToken(sourceClass));
  }

  if (sourceClass && sourceAttribute) {
    empty.sourceAttributes.add(previewSourceAttributeKey(sourceClass, sourceAttribute));
  }

  if (targetClass) {
    empty.targetClasses.add(canonicalToken(targetClass));
  }

  if (targetClass && targetCardId) {
    empty.targetInstances.add(previewTargetInstanceKey(targetClass, targetCardId));
  }

  for (const rule of matchedRules) {
    const ruleSourceClass = ruleSourceClassCode(rule);
    const ruleTargetClass = ruleTargetClassCode(rule);
    const cardId = String(rule.target?.card_id ?? '').trim();
    empty.rules.add(rulePreviewId(rule));
    empty.sourceClasses.add(canonicalToken(ruleSourceClass));
    empty.targetClasses.add(canonicalToken(ruleTargetClass));
    if (cardId) {
      empty.targetInstances.add(previewTargetInstanceKey(ruleTargetClass, cardId));
    }

    for (const attributeCodeValue of sourceAttributeCodesForRulePreview(rule)) {
      empty.sourceAttributes.add(previewSourceAttributeKey(ruleSourceClass, attributeCodeValue));
    }
  }

  return empty;
}

function ruleMatchesPreviewSelection(layerKey, rule, selection, sourceClass, sourceAttribute, targetClass, targetCardId) {
  if (selection.kind === 'rule') {
    return rulePreviewId(rule) === selection.ruleId;
  }

  if (selection.kind === 'source-class') {
    return canonicalToken(ruleSourceClassCode(rule)) === canonicalToken(sourceClass);
  }

  if (selection.kind === 'source-attribute') {
    return canonicalToken(ruleSourceClassCode(rule)) === canonicalToken(sourceClass)
      && sourceAttributeCodesForRulePreview(rule).some((field) => canonicalToken(field) === canonicalToken(sourceAttribute));
  }

  if (selection.kind === 'target-class') {
    return targetClassMatchesSelection(layerKey, ruleTargetClassCode(rule), targetClass);
  }

  if (selection.kind === 'target-instance') {
    return canonicalToken(ruleTargetClassCode(rule)) === canonicalToken(targetClass)
      && String(rule.target?.card_id ?? '') === String(targetCardId ?? '');
  }

  return false;
}

function targetClassMatchesSelection(layerKey, ruleTargetClass, selectedTargetClass) {
  if (canonicalToken(ruleTargetClass) === canonicalToken(selectedTargetClass)) {
    return true;
  }

  if (!ruleTargetClass || !selectedTargetClass) {
    return false;
  }

  const layer = layerKey === 'service' ? 'Service' : 'Suppression';
  const classes = schemaClassesForLayer(layer);
  const byCode = new Map(classes.map((item) => [canonicalToken(item.code), item]));
  let current = byCode.get(canonicalToken(ruleTargetClass));
  const seen = new Set();
  while (current && !seen.has(canonicalToken(current.code))) {
    seen.add(canonicalToken(current.code));
    const parent = schemaParentClassCode(current);
    if (canonicalToken(parent) === canonicalToken(selectedTargetClass)) {
      return true;
    }

    current = byCode.get(canonicalToken(parent));
  }

  return false;
}

function previewNodeClass(kind, highlight, values = {}) {
  const stateHighlight = highlight ?? {
    active: false,
    selected: null,
    sourceClasses: new Set(),
    sourceAttributes: new Set(),
    rules: new Set(),
    targetClasses: new Set(),
    targetInstances: new Set()
  };
  const classes = ['preview-selectable'];
  const selected = previewNodeSelected(kind, stateHighlight.selected, values);
  const linked = previewNodeLinked(kind, stateHighlight, values);
  if (selected) {
    classes.push('preview-selected');
  } else if (linked) {
    classes.push('preview-linked');
  }

  return classes.join(' ');
}

function previewNodeSelected(kind, selection, values) {
  if (!selection || selection.kind !== kind) {
    return false;
  }

  if (kind === 'rule') {
    return selection.ruleId === values.ruleId;
  }

  if (kind === 'source-class') {
    return canonicalToken(selection.sourceClass) === canonicalToken(values.sourceClass);
  }

  if (kind === 'source-attribute') {
    return canonicalToken(selection.sourceClass) === canonicalToken(values.sourceClass)
      && canonicalToken(selection.sourceAttribute) === canonicalToken(values.sourceAttribute);
  }

  if (kind === 'target-class') {
    return canonicalToken(selection.targetClass) === canonicalToken(values.targetClass);
  }

  if (kind === 'target-instance') {
    return canonicalToken(selection.targetClass) === canonicalToken(values.targetClass)
      && String(selection.targetCardId ?? '') === String(values.targetCardId ?? '');
  }

  return false;
}

function previewNodeLinked(kind, highlight, values) {
  if (!highlight.active) {
    return false;
  }

  if (kind === 'rule') {
    return highlight.rules.has(values.ruleId);
  }

  if (kind === 'source-class') {
    return highlight.sourceClasses.has(canonicalToken(values.sourceClass));
  }

  if (kind === 'source-attribute') {
    return highlight.sourceAttributes.has(previewSourceAttributeKey(values.sourceClass, values.sourceAttribute));
  }

  if (kind === 'target-class') {
    return highlight.targetClasses.has(canonicalToken(values.targetClass));
  }

  if (kind === 'target-instance') {
    return highlight.targetInstances.has(previewTargetInstanceKey(values.targetClass, values.targetCardId));
  }

  return false;
}

function previewSourceAttributeKey(sourceClass, sourceAttribute) {
  return `${canonicalToken(sourceClass)}\u0000${canonicalToken(sourceAttribute)}`;
}

function previewTargetInstanceKey(targetClass, cardId) {
  return `${canonicalToken(targetClass)}\u0000${String(cardId ?? '')}`;
}

function rulePreviewId(rule) {
  return String(rule?.rule_id || `${ruleSourceClassCode(rule)}:${ruleTargetClassCode(rule)}:${rule?.name || ''}`).trim();
}

function normalizePreviewSearchQuery(value) {
  return String(value ?? '').trim().toLowerCase();
}

function previewTextMatches(values, query) {
  if (!query) {
    return true;
  }

  return values.some((value) => String(value ?? '').toLowerCase().includes(query));
}

function previewAttributeMatches(attribute, query) {
  return previewTextMatches([
    attributeCode(attribute),
    attribute.name,
    attribute.displayName,
    attribute.description,
    attribute.type,
    attribute.lookupTypeCode,
    attribute.lookupType
  ], query);
}

function availableSourceClassSchemas(layerKey) {
  const schemasByCode = new Map(state.cmdbClassSchemas.map((item) => [item.code, item]));
  const classes = availableSourceClasses().map((item) => {
    const schema = schemasByCode.get(item.code) ?? item;
    return {
      ...schema,
      hierarchyLabel: item.hierarchyLabel,
      attributes: schema.attributes ?? []
    };
  });
  const byCode = new Map(classes.map((item) => [canonicalToken(item.code), item]));
  for (const rule of state.ruleExamples[layerKey] ?? []) {
    const code = ruleSourceClassCode(rule);
    const token = canonicalToken(code);
    if (!token || byCode.has(token)) {
      continue;
    }

    const schema = schemasByCode.get(code);
    const fallback = {
      ...(schema ?? {}),
      code,
      name: schema?.name || code,
      description: schema?.description || code,
      hierarchyLabel: schema?.hierarchyLabel || `${code} (используется правилом)`,
      hierarchyPath: schema?.hierarchyPath || code,
      attributes: schema?.attributes?.length
        ? schema.attributes
        : sourceFieldsForRulePreview(rule).map((field) => ({
          code: field,
          name: field,
          description: field,
          type: 'поле правила'
        }))
    };
    classes.push(fallback);
    byCode.set(token, fallback);
  }

  return sortClassesByInheritance(classes, state.cmdbClasses);
}

function renderSourceSchemaCards(layerKey, classes, highlight, searchQuery = '') {
  if (classes.length === 0) {
    return '<div class="empty-state">Нет классов-источников для показа.</div>';
  }

  const query = normalizePreviewSearchQuery(searchQuery);
  const cards = classes.map((item) => {
    const model = sourceClassPreviewModel(item, highlight, query);
    if (!model.visible) {
      return '';
    }

    return `
    <details
      class="preview-card ${previewNodeClass('source-class', highlight, { sourceClass: item.code })}"
      data-preview-node="source-class"
      data-layer="${escapeHtml(layerKey)}"
      data-source-class="${escapeHtml(item.code)}"
      ${highlight.active || query ? 'open' : ''}>
      <summary>
        <strong>${escapeHtml(item.description || item.name || item.code)}</strong>
        <span>${escapeHtml(item.hierarchyLabel || item.code)}</span>
      </summary>
      ${item.parent ? `<p class="preview-meta">наследует ${escapeHtml(item.parent)}</p>` : ''}
      ${renderPreviewAttributes(model.attributes, {
        kind: 'source',
        layerKey,
        sourceClass: item.code,
        highlight
      })}
    </details>
  `;
  }).filter(Boolean);

  return cards.join('') || '<div class="empty-state">Нет классов-источников по текущему выделению или поиску.</div>';
}

function sourceClassPreviewModel(item, highlight, query) {
  const attributes = item.attributes ?? [];
  const classToken = canonicalToken(item.code);
  const linkedClass = !highlight.active || highlight.sourceClasses.has(classToken);
  const linkedAttributes = attributes.filter((attribute) =>
    highlight.sourceAttributes.has(previewSourceAttributeKey(item.code, attributeCode(attribute))));
  let visibleAttributes = highlight.active ? linkedAttributes : attributes;
  const classMatches = previewTextMatches([
    item.code,
    item.name,
    item.description,
    item.hierarchyLabel,
    item.hierarchyPath,
    item.parent
  ], query);
  const matchingAttributes = visibleAttributes.filter((attribute) => previewAttributeMatches(attribute, query));
  if (query) {
    visibleAttributes = classMatches ? visibleAttributes : matchingAttributes;
  }

  return {
    visible: (linkedClass || linkedAttributes.length > 0)
      && (!query || classMatches || matchingAttributes.length > 0),
    attributes: visibleAttributes
  };
}

function renderPreviewAttributes(attributes, options = {}) {
  if (attributes.length === 0) {
    return '<div class="empty-state">Атрибуты не загружены.</div>';
  }

  return `
    <div class="preview-attribute-list">
      ${attributes.map((attribute) => renderPreviewAttributeRow(attribute, options)).join('')}
    </div>
  `;
}

function renderPreviewAttributeRow(attribute, options = {}) {
  const code = attributeCode(attribute);
  const sourceNode = options.kind === 'source';
  const className = sourceNode
    ? `preview-attribute-row ${previewNodeClass('source-attribute', options.highlight, {
      sourceClass: options.sourceClass,
      sourceAttribute: code
    })}`
    : 'preview-attribute-row';
  const dataAttributes = sourceNode
    ? `
        data-preview-node="source-attribute"
        data-layer="${escapeHtml(options.layerKey || '')}"
        data-source-class="${escapeHtml(options.sourceClass || '')}"
        data-source-attribute="${escapeHtml(code)}"`
    : '';
  return `
    <div class="${className}"${dataAttributes}>
      <strong>${escapeHtml(attribute.description || attribute.displayName || attribute.name || code)}</strong>
      <span>${escapeHtml(code)} · ${escapeHtml(formatCatalogAttributeType(attribute))}${attribute.required ? ' · обязателен' : ''}</span>
    </div>
  `;
}

function formatCatalogAttributeType(attribute) {
  return attribute.lookupTypeCode
    ? `${attribute.type}: ${attribute.lookupTypeCode}`
    : (attribute.type || 'неизвестно');
}

function renderRuleGroups(layerKey, rules, highlight, searchQuery = '') {
  if (rules.length === 0) {
    return '<div class="empty-state">Нет правил конвертации для показа.</div>';
  }

  const query = normalizePreviewSearchQuery(searchQuery);
  const visibleRules = rules.filter((rule) =>
    (!highlight.active || highlight.rules.has(rulePreviewId(rule)))
    && ruleMatchesPreviewSearch(rule, query));
  if (visibleRules.length === 0) {
    return '<div class="empty-state">Нет правил по текущему выделению или поиску.</div>';
  }

  const descriptions = classDescriptionsByCode();
  const groups = new Map();
  for (const rule of visibleRules) {
    const sourceCode = ruleSourceClassCode(rule);
    const groupKey = canonicalToken(sourceCode) || 'unknown';
    const group = groups.get(groupKey) ?? {
      sourceCode,
      description: (descriptions.get(sourceCode) ?? sourceCode) || 'Неизвестный класс-источник',
      items: []
    };
    group.items.push(rule);
    groups.set(groupKey, group);
  }

  const sourceOrder = sourceClassOrderMap();
  return [...groups.values()]
    .sort((left, right) =>
      compareSourceClassCodesByHierarchy(left.sourceCode, right.sourceCode, sourceOrder)
      || left.description.localeCompare(right.description, undefined, { sensitivity: 'base' }))
    .map(({ description, items }) => `
      <section class="preview-card rule-group">
        <h3>${escapeHtml(description)}</h3>
        ${items.map((rule) => renderRuleSummary(layerKey, rule, descriptions, highlight)).join('')}
      </section>
    `).join('');
}

function renderRuleSummary(layerKey, rule, descriptions, highlight) {
  const sourceCode = ruleSourceClassCode(rule);
  const targetCode = ruleTargetClassCode(rule);
  const targetCardId = String(rule.target?.card_id ?? '').trim();
  const ruleId = rulePreviewId(rule);
  const filterText = ruleFilterDescriptions(rule).join('; ');
  const mappingCount = Object.keys(rule.target?.attribute_mappings ?? {}).length;
  const initialValueCount = Object.keys(rule.target?.initial_user_values ?? {}).length;
  const targetLabel = targetCardId
    ? `целевая карточка #${targetCardId}`
    : 'создать целевой экземпляр';
  const ruleKind = rule.generated_from_template
    ? `сгенерировано из ${rule.generated_from_template}`
    : 'правило привязки';
  const templateActions = rule.generated_from_template
    ? `
      <div class="rule-summary-actions">
        <button
          class="secondary-button compact-button"
          type="button"
          data-detach-template-rule
          data-layer="${escapeHtml(rule.layer || '')}"
          data-rule-id="${escapeHtml(rule.rule_id || '')}">
          Отвязать от шаблона
        </button>
      </div>
    `
    : '';
  return `
    <div
      class="rule-summary ${previewNodeClass('rule', highlight, { ruleId })}"
      data-preview-node="rule"
      data-layer="${escapeHtml(layerKey)}"
      data-rule-id="${escapeHtml(ruleId)}"
      data-source-class="${escapeHtml(sourceCode)}"
      data-target-class="${escapeHtml(targetCode)}"
      data-target-card-id="${escapeHtml(targetCardId)}">
      <span class="structure-mark">${escapeHtml(ruleKind)}</span>
      <strong>${escapeHtml(sourceCode)} -> ${escapeHtml(targetCode)}</strong>
      <span>${escapeHtml(descriptions.get(targetCode) ?? targetCode)}</span>
      <span>фильтр: ${escapeHtml(filterText || 'нет')}</span>
      <span>приоритет ${escapeHtml(rule.priority ?? 100)} · ${escapeHtml(targetLabel)} · ключ ${escapeHtml(rule.source?.key_attribute ?? rule.when?.fieldExists ?? '')} · идемпотентность ${escapeHtml(rule.target?.idempotency_key ?? '')} · маппинги ${mappingCount} · атрибуты цели ${initialValueCount}</span>
      ${templateActions}
    </div>
  `;
}

function ruleMatchesPreviewSearch(rule, query) {
  if (!query) {
    return true;
  }

  return previewTextMatches([
    rule.rule_id,
    rule.name,
    rule.layer,
    rule.generated_from_template,
    ruleTemplateId(rule),
    ruleSourceClassCode(rule),
    ruleTargetClassCode(rule),
    rule.target?.card_id,
    rule.target?.card_description,
    rule.target?.idempotency_key,
    rule.source?.key_attribute,
    rule.when?.fieldExists,
    ruleFilterDescriptions(rule).join(' '),
    sourceFieldsForRulePreview(rule).join(' ')
  ], query);
}

function ruleSourceClassCode(rule) {
  return String(rule?.source?.class_code || classCodeFromWhen(rule?.when) || '').trim();
}

function ruleTargetClassCode(rule) {
  return String(rule?.target?.class_code || '').trim();
}

function targetSelectionValueFromRule(rule) {
  const classCode = ruleTargetClassCode(rule);
  const cardId = String(rule?.target?.card_id ?? '').trim();
  return cardId ? targetInstanceOptionValue(classCode, cardId) : classCode;
}

function targetInstanceOptionValue(classCode, cardId) {
  return `instance:${encodeURIComponent(classCode)}:${encodeURIComponent(cardId)}`;
}

function parseTargetSelection(value) {
  const text = String(value ?? '').trim();
  if (!text.startsWith('instance:')) {
    return {
      kind: text ? 'class' : '',
      classCode: text,
      cardId: ''
    };
  }

  const [, encodedClassCode = '', encodedCardId = ''] = text.split(':');
  return {
    kind: 'instance',
    classCode: safeDecodeURIComponent(encodedClassCode),
    cardId: safeDecodeURIComponent(encodedCardId)
  };
}

function safeDecodeURIComponent(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function targetInstanceBySelection(selection, layerKey = '') {
  const classItem = state.cmdbClassInstances.find((item) =>
    canonicalToken(item.classCode) === canonicalToken(selection.classCode));
  const card = classItem?.cards?.find((item) => String(item.id) === String(selection.cardId)) ?? null;
  if (card) {
    return { ...card, classCode: classItem.classCode };
  }

  return fallbackTargetInstanceFromRules(selection.classCode, selection.cardId, layerKey);
}

function fallbackTargetInstanceFromRules(classCode, cardId, layerKey = '') {
  const classToken = canonicalToken(classCode);
  const cardText = String(cardId ?? '').trim();
  if (!classToken || !cardText) {
    return null;
  }

  const layerKeys = layerKey ? [layerKey] : ['service', 'suppression'];
  for (const key of layerKeys) {
    for (const rule of state.ruleExamples[key] ?? []) {
      const targetClass = ruleTargetClassCode(rule);
      const targetCardId = String(rule?.target?.card_id ?? '').trim();
      if (canonicalToken(targetClass) !== classToken || targetCardId !== cardText) {
        continue;
      }

      return ruleTargetInstanceCard(rule, targetClass || classCode, targetCardId);
    }
  }

  return null;
}

function ruleTargetInstanceCard(rule, classCode, cardId) {
  const initialValues = rule?.target?.initial_user_values ?? {};
  const attributeMappings = rule?.target?.attribute_mappings ?? {};
  const description = rule?.target?.card_description
    || initialValues.Description
    || initialValues.description
    || initialValues.Name
    || initialValues.name
    || attributeMappings.Description
    || attributeMappings.description
    || attributeMappings.name
    || `${classCode} #${cardId}`;

  return {
    id: cardId,
    classCode,
    description,
    attributes: []
  };
}

function targetCardDisplayLabel(card, classCode = '') {
  if (!card) {
    return classCode || 'объект';
  }

  const name = cardAttributeValue(card, 'name');
  const label = card.description || name || card.id;
  return `${label} (${card.classCode || classCode} #${card.id})`;
}

function cardAttributeValue(card, attributeCodeValue) {
  return (card?.attributes ?? []).find((attribute) =>
    canonicalToken(attribute.code) === canonicalToken(attributeCodeValue))?.value
    ?? (card?.attributes ?? []).find((attribute) =>
      canonicalToken(attribute.code) === canonicalToken(attributeCodeValue))?.Value
    ?? '';
}

function ruleFilterDescriptions(rule) {
  const sourceFilters = (rule?.source?.filters ?? [])
    .map((filter) => `${filter.attribute} ${filter.operator} ${filter.value}`);
  const includeFilters = regexMatchersWithoutSystem(rule, ['allRegex', 'anyRegex'])
    .map((matcher) => `${matcher.field} matches ${matcher.pattern}`);
  const excludeFilters = regexMatchersWithoutSystem(rule, ['noneRegex'])
    .map((matcher) => `${matcher.field} not matches ${matcher.pattern}`);
  return sourceFilters.concat(includeFilters, excludeFilters);
}

function classCodeFromWhen(when) {
  const matcher = [
    ...(when?.allRegex ?? []),
    ...(when?.anyRegex ?? [])
  ].find((item) => canonicalToken(item.field) === 'classname');

  return matcher ? regexLiteralValue(matcher.pattern) : '';
}

function renderRuleEditors() {
  renderRuleEditor('service');
  renderRuleEditor('suppression');
}

function renderRuleEditor(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.status) {
    return;
  }

  const parsed = parseRuleDocument(layerKey);
  const rules = parsed.ok ? parsed.document.rules : [];
  const action = config.action.value || 'add';
  const selectedRule = config.select.value;
  const filterTemplateRules = state.ruleEditorFilterTemplateRules[layerKey] !== false;
  if (config.filterTemplateRules) {
    config.filterTemplateRules.checked = filterTemplateRules;
  }

  const selectOptions = ruleSelectOptions(rules, { filterTemplateRules });
  setSelectOptions(config.select, selectOptions, selectedRule);
  config.select.disabled = action === 'add' || !selectOptions.some((option) => !option.disabled && option.value !== '');
  config.selectField.classList.toggle('hidden', action === 'add');

  const selectedTarget = config.targetClass.value;
  const suggestedTarget = action === 'add'
    ? ruleTargetClassCode(state.ruleEditorSuggestions[layerKey])
    : '';
  setSelectOptions(config.targetClass, targetClassOptions(layerKey, suggestedTarget, {
    filterTemplateTargets: filterTemplateRules
  }), selectedTarget);

  const editDisabled = action === 'delete';
  [
    config.name,
    config.sourceClass,
    config.priority,
    config.targetClass
  ].forEach((element) => {
    element.disabled = editDisabled;
  });

  config.applyButton.textContent = action === 'delete' ? 'Удалить правило' : 'Применить';
  renderRuleSourceFieldOptions(layerKey);
  renderRuleSelectionFilterList(layerKey);
  renderRuleTargetFieldOptions(layerKey);
  renderRuleAttributeList(layerKey);
  config.selectionFilterList?.querySelectorAll('input, select').forEach((element) => {
    element.disabled = editDisabled;
  });
  config.attributeList.querySelectorAll('input, select, textarea').forEach((element) => {
    element.disabled = editDisabled;
  });
  renderRuleEditorStatus(layerKey, parsed);
}

function handleRuleEditorClick(layerKey, event) {
  void layerKey;
  void event;
}

function handleRuleEditorChange(layerKey, target) {
  if (!layerKey) {
    return;
  }

  const config = ruleEditorConfig(layerKey);

  if (target.matches('[data-rule-action]')) {
    if (target.value === 'add') {
      resetRuleEditorForCreate(layerKey);
      renderRuleEditor(layerKey);
      return;
    }

    renderRuleEditor(layerKey);
    if (target.value !== 'add') {
      selectFirstRuleIfNeeded(layerKey);
      loadSelectedRuleIntoEditor(layerKey);
    }
    return;
  }

  if (target.matches('[data-rule-filter-template-rules]')) {
    state.ruleEditorFilterTemplateRules[layerKey] = target.checked;
    renderRuleEditor(layerKey);
    if ((config.action.value || 'add') !== 'add' && config.select.value) {
      loadSelectedRuleIntoEditor(layerKey);
    }
    return;
  }

  if (target.matches('[data-rule-select]')) {
    loadSelectedRuleIntoEditor(layerKey);
    return;
  }

  if (target.matches('[data-rule-source-class]')) {
    renderRuleSourceFieldOptions(layerKey);
    renderRuleSelectionFilterList(layerKey);
    return;
  }

  if (target.matches('[data-selection-filter-mode], [data-selection-filter-field]')) {
    ensureSelectionFilterDraftRow(layerKey, 'rule');
    state.ruleEditorSelectionFilters[layerKey] = selectionFilterRowsFromDom(config.selectionFilterList);
    return;
  }

  if (target.matches('[data-rule-target-class]')) {
    renderRuleTargetFieldOptions(layerKey);
    renderRuleAttributeList(layerKey);
  }
}

async function applyRuleEditorChange(layerKey) {
  const config = ruleEditorConfig(layerKey);
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    setRuleEditorStatus(layerKey, parsed.error, 'error');
    return;
  }

  const document = parsed.document;
  const action = config.action.value || 'add';
  const selectedIndex = Number(config.select.value);

  try {
    if (action === 'delete') {
      if (!Number.isInteger(selectedIndex) || !document.rules[selectedIndex]) {
        throw new Error('Выберите правило для удаления.');
      }

      const [deleted] = document.rules.splice(selectedIndex, 1);
      writeRuleDocument(layerKey, document);
      setRuleEditorStatus(layerKey, `Удалено правило ${deleted.name || deleted.rule_id || selectedIndex}.`);
      renderRuleEditor(layerKey);
      return;
    }

    const values = readRuleEditorValues(layerKey);
    const existingRule = action === 'modify' ? document.rules[selectedIndex] : null;
    if (action === 'modify' && !existingRule) {
      throw new Error('Выберите правило для изменения.');
    }

    const targetCard = await ensureRuleTargetCard(layerKey, values, existingRule);
    const rule = buildBindingRule(layerKey, { ...values, targetCard }, existingRule);
    rule.rule_id = uniqueRuleIdForDocument(document, rule.rule_id, action === 'modify' ? selectedIndex : -1);
    config.targetClass.value = targetInstanceOptionValue(values.targetClass, targetCard.id);
    state.ruleEditorTargetValues[layerKey] = { ...(targetCard.initialValues ?? {}) };
    ensureRuleDocumentSource(document, values.sourceClass, sourceFieldsForRule(values));
    if (action === 'modify') {
      document.rules[selectedIndex] = rule;
    } else {
      document.rules.push(rule);
      config.name.value = '';
    }

    writeRuleDocument(layerKey, document);
    setRuleEditorStatus(layerKey, `${action === 'modify' ? 'Изменено' : 'Создано'} правило ${rule.name}.`);
    renderRuleEditor(layerKey);
  } catch (error) {
    setRuleEditorStatus(layerKey, error.message, 'error');
  }
}

function uniqueRuleIdForDocument(document, desiredRuleId, currentIndex = -1) {
  const base = normalizeRuleId(desiredRuleId);
  const existing = new Set((document.rules ?? [])
    .map((rule, index) => index === currentIndex ? '' : String(rule?.rule_id ?? '').trim())
    .filter(Boolean));
  if (!existing.has(base)) {
    return base;
  }

  for (let suffix = 2; suffix < 10000; suffix += 1) {
    const candidate = `${base}-${suffix}`;
    if (!existing.has(candidate)) {
      return candidate;
    }
  }

  throw new Error(`Не удалось сформировать уникальный rule_id для ${base}.`);
}

function readRuleEditorValues(layerKey) {
  const config = ruleEditorConfig(layerKey);
  const sourceClass = config.sourceClass.value.trim();
  const targetSelection = parseTargetSelection(config.targetClass.value.trim());
  const targetClass = targetSelection.classCode;
  const priority = Number(config.priority.value || 100);
  const name = config.name.value.trim();

  if (!sourceClass) {
    throw new Error('Класс-источник обязателен.');
  }

  const parentByCode = new Map(state.cmdbClasses.map((item) => [item.code, item.parent || '']));
  if (isAggregationClassCode(sourceClass, parentByCode, aggregationClassCodes())) {
    throw new Error(`Класс ${sourceClass} входит в модель агрегации мониторинга и не может быть источником.`);
  }

  if (!targetClass) {
    throw new Error('Целевой класс\\экземпляр класса обязателен.');
  }

  if (!Number.isFinite(priority) || priority < 1) {
    throw new Error('Приоритет должен быть положительным целым числом; 1 самый высокий.');
  }

  const selectionFilters = readSelectionFilterRows(config.selectionFilterList, {
    layerKey,
    kind: 'rule',
    sourceClass
  });
  const keyField = sourceKeyFieldFromSelection(selectionFilters);
  const targetValues = targetSelection.kind
    ? readRuleTargetObjectValues(layerKey, sourceClass)
    : {};
  const selectedCard = targetSelection.kind === 'instance'
    ? targetInstanceBySelection(targetSelection, layerKey)
    : null;
  if (targetSelection.kind === 'instance' && !selectedCard) {
    throw new Error('Выбранный экземпляр целевого класса не найден в локальном кэше CMDBuild.');
  }

  return {
    sourceClass,
    keyField,
    selectionFilters,
    targetClass,
    targetSelection,
    targetValues,
    selectedCard,
    priority,
    name
  };
}

function sourceKeyFieldFromSelection(selectionFilters) {
  return selectionFilters.find((filter) => filter.field)?.field || '_id';
}

async function ensureRuleTargetCard(layerKey, values, existingRule = null) {
  if (values.targetSelection.kind === 'instance') {
    return {
      id: values.selectedCard.id,
      description: targetCardDisplayLabel(values.selectedCard, values.targetClass),
      initialValues: values.targetValues ?? {}
    };
  }

  const identity = bindingRuleIdentity(layerKey, values, existingRule);
  const payloadValues = ruleTargetCardPayload(values.targetValues, identity.ruleId);
  const response = await fetch(`/api/cmdbuild/classes/${encodeURIComponent(values.targetClass)}/cards`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      accept: 'application/json'
    },
    body: JSON.stringify({ values: payloadValues })
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.detail || payload.error || `Не удалось создать целевой объект CMDBuild: ${response.status}`);
  }

  const cardId = String(payload.id ?? payload.Id ?? '').trim();
  if (!cardId) {
    throw new Error('CMDBuild создал объект, но не вернул его _id; правило не может безопасно ссылаться на экземпляр.');
  }

  const description = String(payload.description ?? payload.Description ?? payloadValues.Description ?? payloadValues.description ?? payloadValues.name ?? cardId);
  rememberCreatedTargetCard(layerKey, values.targetClass, {
    id: cardId,
    description,
    values: payloadValues
  });
  return {
    id: cardId,
    description,
    initialValues: values.targetValues
  };
}

function ruleTargetCardPayload(targetValues, ruleId) {
  const code = String(targetValues.Code ?? targetValues.code ?? ruleId ?? '').trim();
  const name = String(targetValues.name ?? code).trim();
  const description = String(targetValues.Description ?? targetValues.description ?? name).trim();
  return {
    ...targetValues,
    Code: code,
    Description: description,
    name,
    is_active: true,
    managed_by_builder: true,
    auto_population_enabled: true,
    population_rule_id: ruleId
  };
}

function bindingRuleIdentity(layerKey, values, existingRule = null) {
  const fallbackName = [
    layerKey,
    values.sourceClass,
    values.targetClass,
    values.keyField
  ].join('-');
  const name = values.name || existingRule?.name || existingRule?.rule_id || normalizeRuleId(fallbackName);
  const existingRuleId = String(existingRule?.rule_id ?? '').trim();
  const nameRuleId = normalizeRuleId(name);
  const fallbackRuleId = normalizeRuleId(fallbackName);
  return {
    name,
    ruleId: existingRuleId && !isGenericRuleId(existingRuleId)
      ? existingRuleId
      : (isGenericRuleId(nameRuleId) ? fallbackRuleId : nameRuleId)
  };
}

function isGenericRuleId(ruleId) {
  return normalizeRuleId(ruleId) === 'rule';
}

function buildBindingRule(layerKey, values, existingRule = null) {
  const { name, ruleId } = bindingRuleIdentity(layerKey, values, existingRule);
  const allRegex = [
    {
      field: 'className',
      pattern: `(?i)^${escapeRegex(values.sourceClass)}$`
    }
  ].concat(selectionFiltersToRegexMatchers(values.selectionFilters, 'include'));
  const noneRegex = selectionFiltersToRegexMatchers(values.selectionFilters, 'exclude');

  return {
    rule_id: ruleId,
    name,
    layer: layerKey,
    priority: values.priority,
    managed_relations: Array.isArray(existingRule?.managed_relations)
      ? cloneJson(existingRule.managed_relations)
      : [],
    source: {
      class_code: values.sourceClass,
      key_attribute: values.keyField
    },
    when: {
      allRegex,
      ...(noneRegex.length > 0 ? { noneRegex } : {}),
      fieldExists: values.keyField
    },
    target: {
      class_code: values.targetClass,
      create_instance: false,
      card_id: values.targetCard.id,
      card_description: values.targetCard.description,
      idempotency_key: `cmdbuild:${values.targetClass}:${values.targetCard.id}`,
      attribute_mappings: {},
      initial_user_values: values.targetCard.initialValues ?? {},
      user_responsibility_attributes: []
    }
  };
}

function ensureRuleDocumentSource(document, sourceClass, fields) {
  document.source ??= {};
  document.source.entityClasses = Array.isArray(document.source.entityClasses)
    ? document.source.entityClasses
    : [];
  const existingClassIndex = document.source.entityClasses.findIndex((item) =>
    canonicalToken(item) === canonicalToken(sourceClass));
  if (existingClassIndex >= 0) {
    document.source.entityClasses[existingClassIndex] = sourceClass;
  } else {
    document.source.entityClasses.push(sourceClass);
  }
  document.source.entityClasses.sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'base' }));

  document.source.fields = document.source.fields && typeof document.source.fields === 'object' && !Array.isArray(document.source.fields)
    ? document.source.fields
    : {};
  for (const field of fields) {
    if (!field || document.source.fields[field]) {
      continue;
    }

    document.source.fields[field] = sourceFieldDefinition(sourceClass, field);
  }
}

function sourceFieldsForRule(values) {
  const fields = new Set([values.keyField]);
  for (const filter of values.selectionFilters ?? []) {
    fields.add(filter.field);
  }

  return [...fields].filter(Boolean);
}

function sourceFieldsFromMappings(mappings) {
  const fields = new Set();
  const visit = (value) => {
    if (typeof value === 'string') {
      for (const match of value.matchAll(/\$\{source\.([A-Za-z_][A-Za-z0-9_]*)\}/g)) {
        fields.add(match[1]);
      }
      return;
    }

    if (Array.isArray(value)) {
      value.forEach(visit);
      return;
    }

    if (value && typeof value === 'object') {
      Object.values(value).forEach(visit);
    }
  };

  visit(mappings);
  return [...fields];
}

function sourceFieldsForRulePreview(rule) {
  const fields = new Set([
    rule?.source?.key_attribute,
    rule?.when?.fieldExists
  ]);

  for (const filter of rule?.source?.filters ?? []) {
    fields.add(filter.attribute);
  }

  for (const matcher of [
    ...(rule?.when?.allRegex ?? []),
    ...(rule?.when?.anyRegex ?? []),
    ...(rule?.when?.noneRegex ?? [])
  ]) {
    if (!isSystemRuleMatcherField(matcher?.field)) {
      fields.add(matcher.field);
    }
  }

  for (const field of sourceFieldsFromMappings(rule?.target?.attribute_mappings ?? {})) {
    fields.add(field);
  }

  for (const field of sourceFieldsFromMappings(rule?.target?.initial_user_values ?? {})) {
    fields.add(field);
  }

  for (const field of sourceFieldsFromMappings(rule?.target?.idempotency_key ?? '')) {
    fields.add(field);
  }

  return [...fields].filter(Boolean);
}

function sourceAttributeCodesForRulePreview(rule) {
  const sourceClass = ruleSourceClassCode(rule);
  const directAttributes = sourceDirectAttributes(sourceClass).map(attributeCode).filter(Boolean);
  const result = new Set();
  for (const field of sourceFieldsForRulePreview(rule)) {
    result.add(field);
    for (const attribute of directAttributes) {
      if (sourceFieldCanRepresentAttribute(field, attribute)) {
        result.add(attribute);
      }
    }
  }

  return [...result].filter(Boolean);
}

function sourceFieldCanRepresentAttribute(field, attribute) {
  const fieldToken = canonicalToken(field);
  const attributeToken = canonicalToken(attribute);
  if (!fieldToken || !attributeToken) {
    return false;
  }

  return fieldToken === attributeToken
    || fieldToken.startsWith(canonicalToken(camelPathSegment(attribute, true)));
}

function isSystemRuleMatcherField(field) {
  return ['classname', 'eventtype'].includes(canonicalToken(field));
}

function sourceFieldDefinition(sourceClass, field) {
  const option = sourceFieldOptionsForClass(sourceClass)
    .find((item) => canonicalToken(item.value) === canonicalToken(field));
  if (option?.fieldRule) {
    return cloneJson(option.fieldRule);
  }

  return sourceFieldRuleForDirectAttribute(sourceClass, { code: field, name: field, type: 'string' }, field);
}

function loadSelectedRuleIntoEditor(layerKey) {
  const config = ruleEditorConfig(layerKey);
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    setRuleEditorStatus(layerKey, parsed.error, 'error');
    return;
  }

  const index = Number(config.select.value);
  const rule = Number.isInteger(index) ? parsed.document.rules[index] : null;
  if (!rule) {
    return;
  }

  config.name.value = rule.name || rule.rule_id || '';
  config.sourceClass.value = ruleSourceClassCode(rule);
  config.priority.value = String(rule.priority ?? 100);
  config.targetClass.value = targetSelectionValueFromRule(rule);
  state.ruleEditorSelectionFilters[layerKey] = selectionFiltersFromRule(rule);
  state.ruleEditorTargetValues[layerKey] = ruleTargetInitialValuesForEditor(layerKey, rule);
  renderRuleSourceFieldOptions(layerKey);
  renderRuleSelectionFilterList(layerKey);
  renderRuleTargetFieldOptions(layerKey);
  renderRuleAttributeList(layerKey);
}

function ruleTargetInitialValuesForEditor(layerKey, rule) {
  const initialValues = {};
  const selection = parseTargetSelection(targetSelectionValueFromRule(rule));
  if (selection.kind === 'instance') {
    const card = targetInstanceBySelection(selection, layerKey);
    for (const attribute of targetObjectEditableAttributes(layerKey, selection.classCode, { includeIdentity: false })) {
      const code = attributeCode(attribute);
      const value = cardAttributeValue(card, code);
      if (hasEditableTargetAttributeValue(value)) {
        initialValues[code] = value;
      }
    }
  }

  return {
    ...initialValues,
    ...(rule?.target?.initial_user_values ?? {})
  };
}

function hasEditableTargetAttributeValue(value) {
  return value !== undefined
    && value !== null
    && String(value).trim() !== '';
}

function resetRuleEditorForCreate(layerKey) {
  const config = ruleEditorConfig(layerKey);
  const suggestion = state.ruleEditorSuggestions[layerKey];
  config.action.value = 'add';
  config.select.value = '';
  config.name.value = '';
  config.sourceClass.value = '';
  config.priority.value = '';
  config.targetClass.value = '';
  state.ruleEditorSelectionFilters[layerKey] = selectionFiltersFromRule(suggestion);
  state.ruleEditorTargetValues[layerKey] = {};
  applyRuleEditorSuggestions(layerKey, suggestion);
  renderRuleSelectionFilterList(layerKey);
  renderRuleAttributeList(layerKey);
}

function applyRuleEditorSuggestions(layerKey, rule) {
  const config = ruleEditorConfig(layerKey);
  config.name.placeholder = rule?.name || 'Название правила';
  config.sourceClass.placeholder = ruleSourceClassCode(rule) || 'Класс заказчика';
  config.priority.placeholder = String(rule?.priority ?? 100);
}

function sourceAttributeFromDirectExpression(expression) {
  const match = String(expression ?? '').match(/^\$\{source\.([A-Za-z_][A-Za-z0-9_]*)\}$/);
  return match?.[1] || '';
}

function keyFieldFromMappings(mappings) {
  const [, expression] = Object.entries(mappings ?? {}).find(([targetAttribute]) =>
    canonicalToken(targetAttribute) === canonicalToken(POPULATION_SOURCE_KEY_ATTRIBUTE)) ?? [];
  return sourceAttributeFromDirectExpression(expression) || firstSourceAttributeFromTemplate(expression);
}

function selectionFiltersFromRule(rule) {
  const legacySourceFilters = (rule?.source?.filters ?? []).map((filter) => ({
    mode: filter.operator === 'not_equals' ? 'exclude' : 'include',
    field: filter.attribute || '',
    regex: filter.operator === 'equals' || filter.operator === 'not_equals'
      ? `(?i)^${escapeRegex(String(filter.value ?? ''))}$`
      : String(filter.value ?? '')
  }));
  const includeFilters = regexMatchersWithoutSystem(rule, ['allRegex', 'anyRegex'])
    .map((matcher) => ({ mode: 'include', field: matcher.field || '', regex: matcher.pattern || '' }));
  const excludeFilters = regexMatchersWithoutSystem(rule, ['noneRegex'])
    .map((matcher) => ({ mode: 'exclude', field: matcher.field || '', regex: matcher.pattern || '' }));
  return normalizeSelectionFilterRows([...legacySourceFilters, ...includeFilters, ...excludeFilters]);
}

function selectionFiltersFromTemplate(template) {
  const filter = template?.filter && typeof template.filter === 'object' && !Array.isArray(template.filter)
    ? template.filter
    : {};
  if (Array.isArray(filter.include) || Array.isArray(filter.exclude)) {
    return normalizeSelectionFilterRows([
      ...(filter.include ?? []).map((item) => ({ ...item, mode: 'include' })),
      ...(filter.exclude ?? []).map((item) => ({ ...item, mode: 'exclude' }))
    ]);
  }

  return normalizeSelectionFilterRows([{
    mode: 'include',
    field: filter.field || '',
    regex: filter.regex || ''
  }]);
}

function selectionFiltersToTemplateFilter(filters) {
  return {
    include: selectionFiltersToRegexMatchers(filters, 'include').map(regexMatcherToTemplateFilter),
    exclude: selectionFiltersToRegexMatchers(filters, 'exclude').map(regexMatcherToTemplateFilter)
  };
}

function regexMatcherToTemplateFilter(matcher) {
  return {
    field: matcher.field,
    regex: matcher.pattern
  };
}

function regexMatchersWithoutSystem(rule, keys) {
  const systemFields = new Set(['classname', 'eventtype']);
  return keys.flatMap((key) => Array.isArray(rule?.when?.[key]) ? rule.when[key] : [])
    .filter((matcher) => matcher?.field && matcher?.pattern && !systemFields.has(canonicalToken(matcher.field)));
}

function normalizeSelectionFilterRows(rows) {
  const seen = new Set();
  return (Array.isArray(rows) ? rows : []).map((row) => ({
    mode: row?.mode === 'exclude' ? 'exclude' : 'include',
    field: String(row?.field ?? row?.attribute ?? '').trim(),
    regex: String(row?.regex ?? row?.pattern ?? '').trim()
  })).filter((row) => {
    if (!row.field && !row.regex) {
      return false;
    }

    const key = `${row.mode}\u0000${canonicalToken(row.field)}\u0000${row.regex}`;
    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

function selectionFiltersToRegexMatchers(filters, mode) {
  return (filters ?? [])
    .filter((filter) => filter.mode === mode && filter.field && filter.regex)
    .map((filter) => ({ field: filter.field, pattern: filter.regex }));
}

function selectFirstRuleIfNeeded(layerKey) {
  const config = ruleEditorConfig(layerKey);
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok || config.select.value) {
    return;
  }

  const options = ruleSelectOptions(parsed.document.rules, {
    filterTemplateRules: state.ruleEditorFilterTemplateRules[layerKey] !== false
  });
  const firstRule = options.find((option) => !option.disabled && option.value !== '');
  if (firstRule) {
    config.select.value = firstRule.value;
  }
}

function renderRuleSourceFieldOptions(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.fieldOptions) {
    return;
  }

  const options = sourceFieldOptionsForClass(config.sourceClass.value.trim());
  config.fieldOptions.innerHTML = options.map((option) => {
    const label = [option.label, option.meta].filter(Boolean).join(' · ');
    return `<option value="${escapeHtml(option.value)}" label="${escapeHtml(label)}"></option>`;
  }).join('');
}

function renderRuleSelectionFilterList(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.selectionFilterList) {
    return;
  }

  config.selectionFilterList.innerHTML = selectionFilterListTemplate(
    state.ruleEditorSelectionFilters[layerKey],
    sourceFieldOptionsForClass(config.sourceClass.value.trim()));
}

function renderTemplateSelectionFilterList(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.selectionFilterList) {
    return;
  }

  config.selectionFilterList.innerHTML = selectionFilterListTemplate(
    state.templateEditorSelectionFilters[layerKey],
    templateSourceFieldOptions(layerKey));
}

function selectionFilterListTemplate(rows, fieldOptions) {
  const normalizedRows = normalizeSelectionFilterRows(rows);
  return `
    <div class="selection-filter-header" role="row">
      <span role="columnheader">режим</span>
      <span role="columnheader">атрибут</span>
      <span role="columnheader">regex</span>
    </div>
    ${normalizedRows.concat([{ mode: 'include', field: '', regex: '' }])
      .map((row) => selectionFilterRowTemplate(row, fieldOptions))
      .join('')}
  `;
}

function renderTemplateSourceFieldOptions(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.fieldOptions) {
    return;
  }

  const options = templateSourceFieldOptions(layerKey);
  config.fieldOptions.innerHTML = options.map((option) => {
    const label = [option.label, option.meta].filter(Boolean).join(' · ');
    return `<option value="${escapeHtml(option.value)}" label="${escapeHtml(label)}"></option>`;
  }).join('');
}

function renderTemplateSourceFieldHelper(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.sourceFieldCopySelect) {
    return;
  }

  const options = templateSourceFieldOptions(layerKey);
  const selectedValue = config.sourceFieldCopySelect.value;
  setSelectOptions(
    config.sourceFieldCopySelect,
    [
      { value: '', label: options.length === 0 ? 'Нет доступных атрибутов' : 'Выберите атрибут' },
      ...options.map((option) => ({
        value: option.value,
        label: [option.label, option.meta].filter(Boolean).join(' · ') || option.value
      }))
    ],
    selectedValue);
  updateTemplateSourceFieldCopyFields(layerKey);
}

function updateTemplateSourceFieldCopyFields(layerKey) {
  const config = templateEditorConfig(layerKey);
  const value = config.sourceFieldCopySelect?.value || '';
  if (config.sourceFieldCopyValue) {
    config.sourceFieldCopyValue.value = value;
  }

  if (config.sourceFieldCopyExpression) {
    config.sourceFieldCopyExpression.value = value ? `\${source.${value}}` : '';
  }
}

function renderTemplatePopulationDimensionOptions(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.populationSourceField || !config.populationConditionField) {
    return;
  }

  const options = templateSourceFieldOptions(layerKey);
  const dimensionType = normalizeTemplatePopulationDimensionType(config.populationType?.value || DEFAULT_TEMPLATE_POPULATION_DIMENSION.type);
  const sourceOptions = templatePopulationSourceFieldOptionsForType(options, dimensionType);
  const conditionOptions = templatePopulationConditionFieldOptionsForType(options, dimensionType);
  const sourceSelected = templatePopulationSelectValue(config.populationSourceField);
  const conditionSelected = templatePopulationSelectValue(config.populationConditionField);
  renderTemplatePopulationDimensionVisibility(layerKey, dimensionType);
  renderTemplatePopulationFieldDatalist(config.populationSourceFieldOptions, sourceOptions);
  renderTemplatePopulationFieldDatalist(config.populationConditionFieldOptions, conditionOptions);
  setTemplatePopulationFieldOptions(
    config.populationSourceField,
    templatePopulationFieldSelectOptions(sourceOptions, sourceSelected, templatePopulationSourcePlaceholder(dimensionType)),
    sourceSelected,
    dimensionType);
  setTemplatePopulationFieldOptions(
    config.populationConditionField,
    templatePopulationFieldSelectOptions(conditionOptions, conditionSelected, 'Как поле измерения источника'),
    conditionSelected,
    dimensionType);
  rememberTemplatePopulationSelectValue(config.populationSourceField);
  rememberTemplatePopulationSelectValue(config.populationConditionField);
  if (config.sourceKey && config.populationType) {
    config.sourceKey.value = config.populationType.value === 'legacy'
      ? DEFAULT_TEMPLATE_POPULATION_SOURCE_KEY
      : (config.populationKeyTemplate?.value.trim() || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template);
  }
  renderTemplatePopulationDimensionPreview(layerKey);
}

function renderTemplatePopulationDimensionVisibility(layerKey, dimensionType) {
  const panel = document.querySelector(`[data-template-editor-layer="${layerKey}"]`);
  if (!panel) {
    return;
  }

  panel.querySelectorAll('[data-template-population-control]').forEach((item) => {
    const visible = templatePopulationControlVisible(dimensionType, item.dataset.templatePopulationControl);
    item.classList.toggle('hidden', !visible);
  });
}

function templatePopulationControlVisible(dimensionType, control) {
  const common = new Set(['key', 'name', 'max']);
  if (dimensionType === 'legacy') {
    return false;
  }
  if (dimensionType === 'cmdb_reference' || dimensionType === 'cmdb_domain') {
    return control === 'source';
  }
  if (['source_field', 'source_lookup', 'source_bool'].includes(dimensionType)) {
    return common.has(control) || control === 'source';
  }
  if (dimensionType === 'regex_capture') {
    return common.has(control)
      || ['source', 'regex', 'capture-group', 'condition-field', 'condition-pattern'].includes(control);
  }
  if (dimensionType === 'range' || dimensionType === 'static_list') {
    return common.has(control)
      || ['values', 'condition-field', 'condition-pattern'].includes(control);
  }

  return common.has(control) || control === 'source';
}

function renderTemplatePopulationFieldDatalist(datalist, options) {
  if (!datalist) {
    return;
  }

  datalist.innerHTML = (options ?? []).map((option) => {
    const label = [option.label, option.meta].filter(Boolean).join(' · ');
    return `<option value="${escapeHtml(option.value)}" label="${escapeHtml(label)}"></option>`;
  }).join('');
}

function setTemplatePopulationFieldOptions(field, options, selectedValue = '', dimensionType = '') {
  if (!field) {
    return;
  }

  if (field.matches('select')) {
    setSelectOptions(field, options, selectedValue);
    return;
  }

  if (selectedValue) {
    field.value = selectedValue;
    field.dataset.selectedValue = selectedValue;
  }

  const hasChoices = options.some((option) => option.value);
  const isConditionField = field.matches('[data-template-population-condition-field]');
  field.placeholder = hasChoices
    ? 'Выберите из списка или введите id поля'
    : (isConditionField ? 'Введите id поля для отбора карточек-источников' : templatePopulationSourcePlaceholder(dimensionType));
  field.title = selectedValue || field.placeholder;
}

function templatePopulationSourcePlaceholder(dimensionType) {
  if (['range', 'static_list'].includes(dimensionType)) {
    return 'Не требуется для этого типа; заполните поле условия';
  }
  if (dimensionType === 'cmdb_reference' || dimensionType === 'cmdb_domain') {
    return 'Только диагностика неразрешенных ссылок; сохранение будет запрещено';
  }

  return 'Введите id поля вручную или синхронизируйте CMDBuild';
}

function templatePopulationSourceFieldOptionsForType(options, dimensionType) {
  const normalized = uniqueSourceFieldOptions(options ?? []);
  const nonEnumerated = normalized.filter((option) =>
    !['lookup', 'boolean', 'unresolved_reference', 'unresolved_domain'].includes(sourceFieldLeafKind(option)));
  if (dimensionType === 'source_lookup') {
    return normalized.filter(isLookupTemplateFieldOption);
  }
  if (dimensionType === 'source_bool') {
    return normalized.filter(isBooleanTemplateFieldOption);
  }
  if (dimensionType === 'cmdb_reference' || dimensionType === 'cmdb_domain') {
    return normalized.filter(isUnresolvedObjectLinkTemplateFieldOption);
  }
  if (dimensionType === 'regex_capture') {
    return nonEnumerated;
  }
  if (['range', 'static_list'].includes(dimensionType)) {
    return [];
  }

  return nonEnumerated;
}

function templatePopulationConditionFieldOptionsForType(options, dimensionType) {
  const normalized = uniqueSourceFieldOptions(options ?? []);
  const resolved = normalized.filter((option) => !isUnresolvedObjectLinkTemplateFieldOption(option));
  if (dimensionType === 'source_lookup') {
    return resolved.filter(isLookupTemplateFieldOption);
  }
  if (dimensionType === 'source_bool') {
    return resolved.filter(isBooleanTemplateFieldOption);
  }

  return resolved;
}

function isLookupTemplateFieldOption(option) {
  return sourceFieldLeafKind(option) === 'lookup';
}

function isBooleanTemplateFieldOption(option) {
  return sourceFieldLeafKind(option) === 'boolean';
}

function isResolvedPathTemplateFieldOption(option) {
  return option?.fieldRule?.resolve?.mode === 'cmdbPath';
}

function isUnresolvedObjectLinkTemplateFieldOption(option) {
  return ['unresolved_reference', 'unresolved_domain'].includes(sourceFieldLeafKind(option));
}

function sourceFieldLeafKind(option) {
  const rule = option?.fieldRule ?? {};
  return normalizedRuleFieldKind({
    leafKind: rule.leafKind || rule.resolve?.leafKind || rule.resolve?.leafType || '',
    lookupTypeCode: rule.leafLookupType || rule.lookupType || rule.resolve?.lookupType || '',
    lookupType: rule.leafLookupType || rule.lookupType || rule.resolve?.lookupType || '',
    type: rule.leafType || rule.type || ''
  });
}

function templatePopulationSelectValue(select) {
  if (select?.matches?.('input, textarea')) {
    return String(select.value ?? '').trim();
  }

  return String(select?.value || select?.dataset.selectedValue || '').trim();
}

function rememberTemplatePopulationSelectValue(select) {
  if (!select) {
    return;
  }

  const value = String(select.value ?? '').trim();
  if (value) {
    select.dataset.selectedValue = value;
  } else {
    delete select.dataset.selectedValue;
  }
}

function templatePopulationFieldSelectOptions(options, selectedValue, placeholder) {
  const normalizedOptions = uniqueSourceFieldOptions(options ?? []);
  const hasSelected = normalizedOptions.some((option) =>
    canonicalToken(option.value) === canonicalToken(selectedValue));
  return [
    { value: '', label: placeholder },
    ...(selectedValue && !hasSelected
      ? [{ value: selectedValue, label: `${selectedValue} (не найден)` }]
      : []),
    ...normalizedOptions.map((option) => ({
      value: option.value,
      label: [option.label, option.meta].filter(Boolean).join(' · ') || option.value
    }))
  ];
}

function setTemplatePopulationDimensionEditorValues(layerKey, dimension) {
  const config = templateEditorConfig(layerKey);
  const normalized = normalizeTemplatePopulationDimension(dimension, { missingMode: 'legacy' });
  if (config.populationType) {
    config.populationType.value = normalized.type;
  }
  if (config.populationSourceField) {
    config.populationSourceField.value = normalized.source_field;
    config.populationSourceField.dataset.selectedValue = normalized.source_field;
  }
  if (config.populationValues) {
    config.populationValues.value = normalized.values;
  }
  if (config.populationRegex) {
    config.populationRegex.value = normalized.regex;
  }
  if (config.populationCaptureGroup) {
    config.populationCaptureGroup.value = normalized.capture_group;
  }
  if (config.populationKeyTemplate) {
    config.populationKeyTemplate.value = normalized.key_template;
  }
  if (config.populationNameTemplate) {
    config.populationNameTemplate.value = normalized.name_template;
  }
  if (config.populationConditionField) {
    config.populationConditionField.value = normalized.condition_field;
    config.populationConditionField.dataset.selectedValue = normalized.condition_field;
  }
  if (config.populationConditionPattern) {
    config.populationConditionPattern.value = normalized.condition_pattern_template;
  }
  if (config.populationMaxRules) {
    config.populationMaxRules.value = String(normalized.max_rules || TEMPLATE_DIMENSION_DEFAULT_MAX_RULES);
  }
}

function readTemplatePopulationDimension(layerKey) {
  const config = templateEditorConfig(layerKey);
  const type = normalizeTemplatePopulationDimensionType(config.populationType?.value || 'legacy');
  if (type === 'legacy') {
    return normalizeTemplatePopulationDimension({ enabled: false, type: 'legacy' });
  }

  const dimension = normalizeTemplatePopulationDimension({
    enabled: true,
    type,
    source_field: templatePopulationControlVisible(type, 'source') ? templatePopulationSelectValue(config.populationSourceField) : '',
    values: templatePopulationControlVisible(type, 'values') ? config.populationValues?.value.trim() || '' : '',
    regex: templatePopulationControlVisible(type, 'regex') ? config.populationRegex?.value.trim() || '' : '',
    capture_group: templatePopulationControlVisible(type, 'capture-group') ? config.populationCaptureGroup?.value.trim() || '1' : '1',
    key_template: config.populationKeyTemplate?.value.trim() || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template,
    name_template: config.populationNameTemplate?.value.trim() || DEFAULT_TEMPLATE_POPULATION_DIMENSION.name_template,
    condition_field: templatePopulationControlVisible(type, 'condition-field') ? templatePopulationSelectValue(config.populationConditionField) : '',
    condition_pattern_template: templatePopulationControlVisible(type, 'condition-pattern') ? config.populationConditionPattern?.value.trim() || '' : '',
    max_rules: config.populationMaxRules?.value || TEMPLATE_DIMENSION_DEFAULT_MAX_RULES
  });

  const fieldBasedTypes = new Set(['source_field', 'source_lookup', 'source_bool', 'cmdb_reference', 'cmdb_domain', 'regex_capture']);
  if (fieldBasedTypes.has(type) && !dimension.source_field) {
    throw new Error('В измерении population выберите Атрибут/путь источника.');
  }

  if (['static_list', 'range'].includes(type) && !dimension.values) {
    throw new Error('В измерении population заполните статические значения / диапазон.');
  }

  if (['range', 'static_list'].includes(type) && !dimension.condition_field) {
    throw new Error('Для диапазона/статического списка заполните поле отбора карточек-источников.');
  }

  validateTemplatePopulationDimensionLeafFields(layerKey, dimension);

  if (type === 'regex_capture') {
    if (!dimension.regex) {
      throw new Error('Для regex-вырезки заполните регулярное выражение вырезки.');
    }
    assertValidRegexPattern(dimension.regex);
  }

  if (dimension.condition_pattern_template) {
    assertValidRegexPattern(renderTemplateString(dimension.condition_pattern_template, {
      template: { id: 'template', name: 'template', layer: layerKey },
      class: { code: 'SourceClass', name: 'SourceClass', description: 'SourceClass', hierarchyPath: '/SourceClass' },
      dimension: templateDimensionContext({
        key: '01',
        value: '01',
        name: '01',
        condition_pattern: ''
      }),
      vars: {}
    }));
  }

  return dimension;
}

function readTemplatePopulationDimensionDraft(layerKey) {
  const config = templateEditorConfig(layerKey);
  const type = normalizeTemplatePopulationDimensionType(config.populationType?.value || 'legacy');
  if (type === 'legacy') {
    return normalizeTemplatePopulationDimension({ enabled: false, type: 'legacy' });
  }

  return normalizeTemplatePopulationDimension({
    enabled: true,
    type,
    source_field: templatePopulationControlVisible(type, 'source') ? templatePopulationSelectValue(config.populationSourceField) : '',
    values: templatePopulationControlVisible(type, 'values') ? config.populationValues?.value.trim() || '' : '',
    regex: templatePopulationControlVisible(type, 'regex') ? config.populationRegex?.value.trim() || '' : '',
    capture_group: templatePopulationControlVisible(type, 'capture-group') ? config.populationCaptureGroup?.value.trim() || '1' : '1',
    key_template: config.populationKeyTemplate?.value.trim() || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template,
    name_template: config.populationNameTemplate?.value.trim() || DEFAULT_TEMPLATE_POPULATION_DIMENSION.name_template,
    condition_field: templatePopulationControlVisible(type, 'condition-field') ? templatePopulationSelectValue(config.populationConditionField) : '',
    condition_pattern_template: templatePopulationControlVisible(type, 'condition-pattern') ? config.populationConditionPattern?.value.trim() || '' : '',
    max_rules: config.populationMaxRules?.value || TEMPLATE_DIMENSION_DEFAULT_MAX_RULES
  });
}

function renderTemplatePopulationDimensionPreview(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.populationPreview) {
    return;
  }

  const dimension = readTemplatePopulationDimensionDraft(layerKey);
  const missingClassCodes = templatePopulationPreviewRequiredClassCodes(layerKey, dimension);
  if (missingClassCodes.length > 0) {
    void queueTemplatePopulationPreviewCardLoad(layerKey, dimension, missingClassCodes);
    config.populationPreview.innerHTML = `
      <span class="template-dimension-preview-title">Текущий предпросмотр dimension.*</span>
      <p class="template-dimension-preview-empty">Догружаю карточки для предпросмотра: ${escapeHtml(missingClassCodes.join(', '))}.</p>
    `;
    return;
  }

  const preview = templatePopulationDimensionPreview(layerKey, dimension);
  if (preview.rows.length === 0) {
    config.populationPreview.innerHTML = `
      <span class="template-dimension-preview-title">Текущий предпросмотр dimension.*</span>
      <p class="template-dimension-preview-empty">${escapeHtml(preview.message)}</p>
    `;
    return;
  }

  config.populationPreview.innerHTML = `
    <span class="template-dimension-preview-title">Текущий предпросмотр dimension.*</span>
    <p class="template-dimension-preview-meta">${escapeHtml(preview.meta)}</p>
    ${preview.warning ? `<p class="template-dimension-preview-empty">${escapeHtml(preview.warning)}</p>` : ''}
    <table>
      <thead>
        <tr>
          <th>dimension.key</th>
          <th>dimension.value</th>
          <th>dimension.name</th>
          <th>dimension.regexKey</th>
          <th>ключ цели</th>
        </tr>
      </thead>
      <tbody>
        ${preview.rows.map((row) => `
          <tr>
            <td><code>${escapeHtml(row.key)}</code></td>
            <td><code>${escapeHtml(row.value)}</code></td>
            <td><code>${escapeHtml(row.name)}</code></td>
            <td><code>${escapeHtml(row.regexKey)}</code></td>
            <td><code>${escapeHtml(row.targetKey)}</code></td>
          </tr>
        `).join('')}
      </tbody>
    </table>
  `;
}

function templatePopulationDimensionPreview(layerKey, dimension) {
  if (!isTemplateDimensionMaterialized(dimension)) {
    return { rows: [], message: 'Режим совместимости не создает dimension.*: будет одно сгенерированное правило на класс-источник.' };
  }

  const type = dimension.type;
  if (type === 'cmdb_reference' || type === 'cmdb_domain') {
    return { rows: [], message: 'Диагностический тип неразрешенных reference/domain не создает сохраняемый dimension.*. Выберите конечный leaf-атрибут.' };
  }

  if (['source_field', 'source_lookup', 'source_bool', 'regex_capture'].includes(type) && !dimension.source_field) {
    return { rows: [], message: 'Выберите Атрибут/путь источника, чтобы увидеть dimension.*.' };
  }

  if (type === 'regex_capture') {
    if (!dimension.regex) {
      return { rows: [], message: 'Заполните regex вырезки значения, чтобы capture-группа стала dimension.key.' };
    }
    try {
      assertValidRegexPattern(dimension.regex);
    } catch (error) {
      return { rows: [], message: `Regex вырезки значения некорректен: ${error.message}` };
    }
  }

  if (['range', 'static_list'].includes(type) && !dimension.values) {
    return { rows: [], message: 'Заполните значения измерения / диапазон, чтобы увидеть dimension.key/value/name.' };
  }

  const template = templatePopulationPreviewDraftTemplate(layerKey, dimension);
  const candidates = templatePopulationPreviewCandidates(template);
  if (candidates.error) {
    return { rows: [], message: candidates.error };
  }

  const candidateList = candidates.items.length > 0
    ? candidates.items
    : (templatePopulationPreviewCanUseDummyCandidate(dimension) ? [templatePopulationPreviewDummyCandidate()] : []);
  if (candidateList.length === 0) {
    return { rows: [], message: 'Не найдены классы-источники по регулярному выражению класса-источника. Предпросмотр для этого типа появится после выбора класса.' };
  }

  const errors = [];
  for (const candidate of candidateList) {
    try {
      const values = templateDimensionValues(template, candidate, dimension);
      if (values.length === 0) {
        continue;
      }

      const rows = values.slice(0, TEMPLATE_DIMENSION_PREVIEW_LIMIT)
        .map((value) => templatePopulationDimensionPreviewRow(template, candidate, dimension, value));
      const extraCount = Math.max(values.length - rows.length, 0);
      return {
        rows,
        meta: [
          `класс-источник: ${candidate.code || 'предпросмотр'}`,
          `значений: ${values.length}`,
          extraCount > 0 ? `показано первых ${rows.length}` : 'показаны все',
          candidates.items.length > 1 ? `классов-кандидатов: ${candidates.items.length}` : ''
        ].filter(Boolean).join(' · '),
        warning: templatePopulationDimensionPreviewWarning(dimension)
      };
    } catch (error) {
      errors.push(`${candidate.code || 'preview'}: ${error.message}`);
    }
  }

  return {
    rows: [],
    message: errors.length > 0
      ? `Не удалось вычислить dimension.*: ${errors.slice(0, 2).join('; ')}`
      : 'По выбранным полям пока нет значений. Для уникальных значений/regex предпросмотра нужны загруженные карточки класса-источника.'
  };
}

function templatePopulationPreviewDraftTemplate(layerKey, dimension) {
  const config = templateEditorConfig(layerKey);
  return {
    template_id: normalizeRuleId(config.id?.value.trim() || config.name?.value.trim() || `${layerKey}-template-preview`),
    name: config.name?.value.trim() || config.id?.value.trim() || `${layerKey} template preview`,
    layer: layerKey,
    source_class_regex: config.sourceRegex?.value.trim() || '',
    population_dimension: dimension,
    variables: templatePopulationPreviewDraftVariables(layerKey)
  };
}

function templatePopulationPreviewDraftVariables(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.variableList) {
    return [];
  }

  return [...config.variableList.querySelectorAll('[data-template-variable-row]')]
    .map(templateVariableDomRowValues)
    .filter((variable) => variable.name && variable.value);
}

function templatePopulationPreviewCandidates(template) {
  try {
    return { items: templateCandidateClasses(template), error: '' };
  } catch (error) {
    return { items: [], error: `Регулярное выражение класса-источника некорректно: ${error.message}` };
  }
}

function templatePopulationPreviewCanUseDummyCandidate(dimension) {
  return ['source_bool', 'range', 'static_list'].includes(dimension.type);
}

function templatePopulationPreviewDummyCandidate() {
  return {
    code: 'PreviewSourceClass',
    name: 'PreviewSourceClass',
    description: 'Класс-источник для предпросмотра',
    hierarchyPath: '/PreviewSourceClass'
  };
}

function templatePopulationDimensionPreviewRow(template, candidate, dimension, value) {
  const context = templateContext(template, candidate, value);
  const dimensionContext = templateDimensionContext(value);
  return {
    key: dimensionContext.key,
    value: dimensionContext.value,
    name: dimensionContext.name,
    regexKey: dimensionContext.regexKey,
    targetKey: renderTemplateString(dimension.key_template || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template, context)
  };
}

function templatePopulationDimensionPreviewWarning(dimension) {
  if (['range', 'static_list'].includes(dimension.type) && !dimension.condition_field) {
    return 'Предпросмотр dimension.* уже рассчитан, но для сохранения диапазона/статического списка нужно заполнить поле отбора карточек-источников.';
  }

  return '';
}

async function queueTemplatePopulationPreviewCardLoad(layerKey, dimension, classCodes = templatePopulationPreviewRequiredClassCodes(layerKey, dimension)) {
  if (classCodes.length === 0) {
    return;
  }

  const key = `${layerKey}:${classCodes.slice().sort((left, right) => left.localeCompare(right)).join(',')}`;
  if (state.templatePopulationPreviewLoads.has(key)) {
    return;
  }

  state.templatePopulationPreviewLoads.add(key);
  try {
    const failures = [];
    for (const classCode of classCodes) {
      try {
        await loadSourceClassCards(classCode);
      } catch (error) {
        failures.push(error.message);
      }
    }
    if (failures.length > 0) {
      const visibleFailures = failures.slice(0, 4).join('; ');
      const hiddenFailureCount = Math.max(0, failures.length - 4);
      throw new Error(hiddenFailureCount > 0
        ? `${visibleFailures}; еще ${hiddenFailureCount}`
        : visibleFailures);
    }
    renderTemplatePopulationDimensionPreview(layerKey);
  } catch (error) {
    const config = templateEditorConfig(layerKey);
    if (config.populationPreview) {
      config.populationPreview.innerHTML = `
        <span class="template-dimension-preview-title">Текущий предпросмотр dimension.*</span>
        <p class="template-dimension-preview-empty">Не удалось догрузить карточки для предпросмотра (${escapeHtml(classCodes.join(', '))}): ${escapeHtml(error.message)}</p>
      `;
    }
  } finally {
    state.templatePopulationPreviewLoads.delete(key);
  }
}

function templatePopulationPreviewRequiredClassCodes(layerKey, dimension) {
  if (!templatePopulationDimensionUsesSourceCards(dimension)) {
    return [];
  }

  if (!dimension.source_field || (dimension.type === 'regex_capture' && !dimension.regex)) {
    return [];
  }

  const template = templatePopulationPreviewDraftTemplate(layerKey, dimension);
  const candidates = templatePopulationPreviewCandidates(template);
  if (candidates.error || candidates.items.length === 0) {
    return [];
  }

  const classCodes = new Set();
  for (const candidate of candidates.items) {
    if (!sourceClassCardsAvailable(candidate.code)) {
      classCodes.add(candidate.code);
    }

    for (const dependencyClass of templatePopulationDimensionDependencyClasses(candidate.code, dimension)) {
      if (!sourceClassCardsAvailable(dependencyClass)) {
        classCodes.add(dependencyClass);
      }
    }
  }

  return [...classCodes];
}

function templatePopulationDimensionUsesSourceCards(dimension) {
  return ['source_field', 'source_lookup', 'regex_capture'].includes(dimension.type)
    || Boolean(String(dimension.condition_field ?? '').trim());
}

function validateTemplatePopulationDimensionLeafFields(layerKey, dimension) {
  const options = templateSourceFieldOptions(layerKey);
  const selectedFields = [
    { role: 'Атрибут/путь источника', field: dimension.source_field },
    { role: 'Поле условия', field: dimension.condition_field }
  ].filter((item) => item.field);

  if (dimension.type === 'cmdb_reference' || dimension.type === 'cmdb_domain') {
    throw new Error('Тип "Неразрешенные reference/domain-ссылки" является диагностическим: увеличьте глубину рекурсии или выберите конечный leaf-атрибут типа distinct/lookup/bool/regex.');
  }

  for (const item of selectedFields) {
    const option = options.find((candidate) =>
      canonicalToken(candidate.value) === canonicalToken(item.field));
    if (!option || !isUnresolvedObjectLinkTemplateFieldOption(option)) {
      continue;
    }

    throw new Error(`${item.role} ${item.field} указывает на неразрешенную ссылку (${option.fieldRule?.unresolvedReason || 'leaf-атрибут не получен'}). Увеличьте глубину рекурсии domains/reference/lookups или выберите конечный leaf-атрибут.`);
  }
}

function defaultTemplatePopulationDimension() {
  return cloneJson(DEFAULT_TEMPLATE_POPULATION_DIMENSION);
}

async function copyTemplateSourceField(layerKey, target) {
  const config = templateEditorConfig(layerKey);
  const value = config.sourceFieldCopySelect?.value || '';
  if (!value) {
    setTemplateEditorStatus(layerKey, 'Выберите атрибут источника для копирования.', 'error');
    return;
  }

  const text = target.matches('[data-template-copy-source-expression]')
    ? `\${source.${value}}`
    : value;
  try {
    await copyText(text);
    setTemplateEditorStatus(layerKey, `Скопировано: ${text}`);
  } catch (error) {
    setTemplateEditorStatus(layerKey, `Не удалось скопировать: ${error.message}`, 'error');
  }
}

function templateSourceFieldOptions(layerKey) {
  const config = templateEditorConfig(layerKey);
  const sourceRegex = config.sourceRegex.value.trim();
  const template = {
    source_class_regex: sourceRegex
  };
  let candidates = [];
  try {
    candidates = templateCandidateClasses(template);
  } catch {
    candidates = [];
  }

  const sourceClasses = sourceRegex ? candidates : availableSourceClasses();
  const options = sourceClasses.flatMap((item) => sourceFieldOptionsForClass(item.code));
  return uniqueSourceFieldOptions(options)
    .sort((left, right) => left.label.localeCompare(right.label, undefined, { sensitivity: 'base' }));
}

async function copyText(text) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }

  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.setAttribute('readonly', 'readonly');
  textarea.style.position = 'fixed';
  textarea.style.left = '-9999px';
  document.body.append(textarea);
  textarea.select();
  const copied = document.execCommand('copy');
  textarea.remove();
  if (!copied) {
    throw new Error('clipboard API недоступен');
  }
}

function selectionFilterRowTemplate(row, fieldOptions) {
  const mode = row.mode === 'exclude' ? 'exclude' : 'include';
  return `
    <div class="selection-filter-row" data-selection-filter-row>
      <select data-selection-filter-mode aria-label="режим">
        <option value="include" ${mode === 'include' ? 'selected' : ''}>Включить</option>
        <option value="exclude" ${mode === 'exclude' ? 'selected' : ''}>Исключить</option>
      </select>
      <select data-selection-filter-field aria-label="атрибут">
        ${selectionFilterFieldOptionsTemplate(fieldOptions, row.field || '')}
      </select>
      <input data-selection-filter-regex value="${escapeHtml(row.regex || '')}" placeholder="(?i)^active$" aria-label="регулярное выражение" autocomplete="off">
    </div>
  `;
}

function selectionFilterFieldOptionsTemplate(fieldOptions, selectedField) {
  const options = uniqueSourceFieldOptions(fieldOptions ?? []);
  const hasSelected = options.some((option) => canonicalToken(option.value) === canonicalToken(selectedField));
  const selectedFallback = selectedField && !hasSelected
    ? `<option value="${escapeHtml(selectedField)}" selected>${escapeHtml(selectedField)} (не найден)</option>`
    : '';
  const placeholderLabel = options.length === 0
    ? 'Сначала выберите класс-источник'
    : 'Выберите атрибут';
  return `
    <option value="">${placeholderLabel}</option>
    ${selectedFallback}
    ${options.map((option) => {
      const label = [option.label, option.meta].filter(Boolean).join(' · ') || option.value;
      const selected = canonicalToken(option.value) === canonicalToken(selectedField) ? 'selected' : '';
      return `<option value="${escapeHtml(option.value)}" title="${escapeHtml(label)}" ${selected}>${escapeHtml(label)}</option>`;
    }).join('')}
  `;
}

function ensureSelectionFilterDraftRow(layerKey, kind) {
  const config = kind === 'template' ? templateEditorConfig(layerKey) : ruleEditorConfig(layerKey);
  const list = config.selectionFilterList;
  if (!list) {
    return;
  }

  const rows = [...list.querySelectorAll('[data-selection-filter-row]')];
  const lastRow = rows.at(-1);
  if (!lastRow || selectionFilterDomRowValues(lastRow).field || selectionFilterDomRowValues(lastRow).regex) {
    const fieldOptions = kind === 'template'
      ? templateSourceFieldOptions(layerKey)
      : sourceFieldOptionsForClass(config.sourceClass.value.trim());
    list.insertAdjacentHTML('beforeend', selectionFilterRowTemplate({ mode: 'include', field: '', regex: '' }, fieldOptions));
  }
}

function readSelectionFilterRows(list, options) {
  const rows = [];
  const seen = new Set();
  for (const values of selectionFilterRowsFromDom(list)) {
    if (!values.field && !values.regex) {
      continue;
    }

    if (!values.field || !values.regex) {
      throw new Error('В условии выборки заполните атрибут и regex.');
    }

    if (options.kind === 'rule' && !ruleSourceFieldInfo(options.layerKey, values.field)) {
      throw new Error(`Атрибут выборки ${values.field} не найден в выбранном классе-источнике.`);
    }

    if (options.kind === 'template' && !templateSourceFieldOptions(options.layerKey).some((option) =>
      canonicalToken(option.value) === canonicalToken(values.field))) {
      throw new Error(`Атрибут выборки ${values.field} не найден среди доступных атрибутов классов шаблона.`);
    }

    assertValidRegexPattern(values.regex);
    const key = `${values.mode}\u0000${canonicalToken(values.field)}\u0000${values.regex}`;
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    rows.push(values);
  }

  return rows;
}

function selectionFilterRowsFromDom(list) {
  return [...(list?.querySelectorAll('[data-selection-filter-row]') ?? [])]
    .map(selectionFilterDomRowValues);
}

function selectionFilterDomRowValues(row) {
  return {
    mode: row.querySelector('[data-selection-filter-mode]')?.value === 'exclude' ? 'exclude' : 'include',
    field: row.querySelector('[data-selection-filter-field]')?.value.trim() ?? '',
    regex: row.querySelector('[data-selection-filter-regex]')?.value.trim() ?? ''
  };
}

function assertValidRegexPattern(pattern) {
  regexFromUiPattern(pattern);
}

function renderRuleTargetFieldOptions(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.targetFieldOptions) {
    return;
  }

  const attributes = targetClassAttributes(parseTargetSelection(config.targetClass.value.trim()).classCode);
  config.targetFieldOptions.innerHTML = attributes.map((attribute) => {
    const code = attribute.code || attribute.name;
    const label = [attribute.displayName || attribute.description, formatRuleAttributeCatalogType(attribute)]
      .filter(Boolean)
      .join(' · ');
    return `<option value="${escapeHtml(code)}" label="${escapeHtml(label)}"></option>`;
  }).join('');
}

function renderRuleAttributeList(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.attributeList) {
    return;
  }

  const selection = parseTargetSelection(config.targetClass.value.trim());
  if (!selection.classCode) {
    config.attributeList.innerHTML = '<div class="empty-state">Выберите целевой класс или существующий экземпляр.</div>';
    return;
  }

  const isInstanceTarget = selection.kind === 'instance';
  if (isInstanceTarget) {
    const card = targetInstanceBySelection(selection, layerKey);
    const rows = ruleTargetObjectAttributeRows(layerKey, selection.classCode, { includeIdentity: false });
    const missingCodes = missingTargetObjectEditableAttributeCodes(layerKey, selection.classCode, { includeIdentity: false });
    if (rows.length === 0) {
      config.attributeList.innerHTML = `
        <div class="empty-state">Выбран существующий объект ${escapeHtml(targetCardDisplayLabel(card, selection.classCode))}; в выбранном классе нет пользовательских атрибутов модели для сохранения в правиле.</div>
        ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
      `;
      return;
    }

    config.attributeList.innerHTML = `
      <div class="rule-attribute-note">Выбран существующий объект ${escapeHtml(targetCardDisplayLabel(card, selection.classCode))}; Code, name и Description не меняются. Атрибуты ниже сохраняются в правиле и используются runtime-командами агрегации.</div>
      <div class="rule-attribute-header" role="row">
        <span role="columnheader">атрибут</span>
        <span role="columnheader">значение</span>
        <span role="columnheader">помощь</span>
      </div>
      ${rows.map((row) => ruleAttributeRowTemplate(row)).join('')}
      ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
    `;
    return;
  }

  const rows = ruleTargetObjectAttributeRows(layerKey, selection.classCode);
  const missingCodes = missingTargetObjectEditableAttributeCodes(layerKey, selection.classCode);
  if (rows.length === 0) {
    config.attributeList.innerHTML = `
      <div class="empty-state">В выбранном классе нет атрибутов, разрешенных для заполнения пользователем. Некорректные поля не будут подставлены автоматически.</div>
      ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
    `;
    return;
  }

  config.attributeList.innerHTML = `
    <div class="rule-attribute-header" role="row">
      <span role="columnheader">атрибут</span>
      <span role="columnheader">значение</span>
      <span role="columnheader">помощь</span>
    </div>
    ${rows.map((row) => ruleAttributeRowTemplate(row)).join('')}
    ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
  `;
}

function ruleTargetObjectAttributeRows(layerKey, classCode, options = {}) {
  const values = state.ruleEditorTargetValues[layerKey] ?? {};
  return targetObjectEditableAttributes(layerKey, classCode, options).map((attribute) => {
    const code = attributeCode(attribute);
    return {
      attribute,
      code,
      value: Object.hasOwn(values, code) ? values[code] : defaultRuleTargetObjectValue(layerKey, code)
    };
  });
}

function targetObjectEditableAttributes(layerKey, classCode, options = {}) {
  const allowedCodes = targetObjectEditableAttributeCodes(layerKey, options);
  const attributes = targetClassAttributes(classCode);
  const attributeByExactCode = new Map(attributes
    .map((attribute) => [attributeCode(attribute), attribute]));
  const attributeByToken = new Map(attributes
    .map((attribute) => [canonicalToken(attributeCode(attribute)), attribute]));
  return allowedCodes
    .map((code) => attributeByExactCode.get(code) ?? attributeByToken.get(canonicalToken(code)))
    .filter(Boolean);
}

function missingTargetObjectEditableAttributeCodes(layerKey, classCode, options = {}) {
  const attributes = targetClassAttributes(classCode);
  const exactCodes = new Set(attributes.map((attribute) => attributeCode(attribute)));
  const attributeTokens = new Set(attributes.map((attribute) => canonicalToken(attributeCode(attribute))));
  return targetObjectEditableAttributeCodes(layerKey, options)
    .filter((code) => !exactCodes.has(code) && !attributeTokens.has(canonicalToken(code)));
}

function targetMissingAttributeNoteTemplate(codes) {
  return `
    <div class="rule-attribute-missing">
      Не показаны и не будут отправлены в CMDBuild, потому что отсутствуют в выбранном классе:
      ${codes.map((code) => `<code>${escapeHtml(code)}</code>`).join(' · ')}
    </div>
  `;
}

function targetObjectEditableAttributeCodes(layerKey, options = {}) {
  const normalizedLayer = String(layerKey ?? '').toLowerCase();
  const identityAttributes = options.includeIdentity === false
    ? []
    : TARGET_CARD_IDENTITY_ATTRIBUTES;
  if (normalizedLayer === 'service') {
    return [...identityAttributes, ...SERVICE_USER_RESPONSIBILITY_ATTRIBUTES];
  }

  if (normalizedLayer === 'suppression') {
    return [...identityAttributes, ...SUPPRESSION_USER_RESPONSIBILITY_ATTRIBUTES];
  }

  return [...identityAttributes];
}

function defaultRuleTargetObjectValue(layerKey, code) {
  const token = canonicalToken(code);
  if (token === 'iscritical') {
    return 'false';
  }

  if (['service', 'suppression'].includes(String(layerKey).toLowerCase()) && token === 'aggregationtype') {
    return 'all';
  }

  return '';
}

function ruleAttributeRowTemplate(row) {
  const label = row.attribute.displayName || row.attribute.description || row.code;
  return `
    <div class="rule-attribute-row" data-rule-attribute-row>
      <div class="rule-attribute-name">
        <strong>${escapeHtml(label)}</strong>
        <span>${escapeHtml(row.code)}</span>
      </div>
      ${ruleTargetValueControlTemplate(row.attribute, row.value)}
      <div class="rule-attribute-help" data-rule-attribute-help>
        ${escapeHtml(row.attribute.help || row.attribute.description || '')}
      </div>
    </div>
  `;
}

function ruleTargetValueControlTemplate(attribute, value, options = {}) {
  const code = attributeCode(attribute);
  const kind = normalizedRuleFieldKind(attribute);
  const valueDataAttribute = options.valueDataAttribute || 'data-rule-target-value';
  const controlAttributes = `${valueDataAttribute} data-target-attribute="${escapeHtml(code)}"`;
  if (kind === 'boolean') {
    const normalizedValue = String(value ?? '').toLowerCase();
    return `
      <label class="select-field rule-attribute-cell">
        <select ${controlAttributes}>
          <option value="false" ${normalizedValue === 'false' || normalizedValue === '' ? 'selected' : ''}>Нет</option>
          <option value="true" ${normalizedValue === 'true' ? 'selected' : ''}>Да</option>
        </select>
      </label>
    `;
  }

  if (kind === 'lookup') {
    const options = lookupValueOptionsForAttribute(attribute);
    const selectedValue = String(value ?? '');
    const selectedFallback = selectedValue && !options.some((option) => option.value === selectedValue)
      ? `<option value="${escapeHtml(selectedValue)}" selected>${escapeHtml(selectedValue)}</option>`
      : '';
    return `
      <label class="select-field rule-attribute-cell">
        <select ${controlAttributes}>
          <option value="">Не выбрано</option>
          ${selectedFallback}
          ${options.map((option) => `
            <option value="${escapeHtml(option.value)}" ${option.value === selectedValue ? 'selected' : ''}>${escapeHtml(option.label)}</option>
          `).join('')}
        </select>
      </label>
    `;
  }

  if (kind === 'text') {
    return `
      <label class="text-field rule-attribute-cell">
        <textarea ${controlAttributes} rows="2">${escapeHtml(value ?? '')}</textarea>
      </label>
    `;
  }

  const inputType = kind === 'integer' ? 'number' : 'text';
  const step = kind === 'integer' ? ' step="1"' : '';
  return `
    <label class="text-field rule-attribute-cell">
      <input ${controlAttributes} type="${inputType}"${step} value="${escapeHtml(value ?? '')}" autocomplete="off">
    </label>
  `;
}

function lookupValueOptionsForAttribute(attribute) {
  const lookupType = lookupTypeCode(attribute) || attribute?.lookupType || '';
  if (!lookupType) {
    return [];
  }

  const lookup = state.lookups.find((item) =>
    canonicalToken(item.code || item.name) === canonicalToken(lookupType));
  return (lookup?.values ?? []).map((value) => {
    const code = value.code || value.name || value.value || '';
    const label = value.displayName || value.description || code;
    return {
      value: code,
      label: code && label && code !== label ? `${label} (${code})` : (label || code)
    };
  }).filter((item) => item.value);
}

function ruleTargetValuesFromDom(attributeList) {
  const values = {};
  attributeList?.querySelectorAll('[data-rule-target-value]').forEach((field) => {
    const code = field.dataset.targetAttribute;
    if (code) {
      values[code] = field.value;
    }
  });
  return values;
}

function readRuleTargetObjectValues(layerKey, sourceClass) {
  const config = ruleEditorConfig(layerKey);
  const selection = parseTargetSelection(config.targetClass.value.trim());
  const values = {};
  const includeIdentity = selection.kind === 'class';
  const attributes = targetObjectEditableAttributes(layerKey, selection.classCode, { includeIdentity });
  const attributeByExactCode = new Map(attributes.map((attribute) => [attributeCode(attribute), attribute]));
  const attributeByToken = new Map(attributes.map((attribute) => [canonicalToken(attributeCode(attribute)), attribute]));
  const templateContext = ruleTargetObjectTemplateContext(sourceClass);

  for (const field of config.attributeList.querySelectorAll('[data-rule-target-value]')) {
    const code = field.dataset.targetAttribute;
    if (!code) {
      continue;
    }

    const attribute = attributeByExactCode.get(code) ?? attributeByToken.get(canonicalToken(code));
    if (!attribute) {
      continue;
    }

    const rawValue = String(field.value ?? '').trim();
    if (!rawValue && !isRequiredRuleTargetValue(code)) {
      continue;
    }

    const renderedValue = renderRuleTargetObjectValue(rawValue, templateContext, code);
    values[code] = coerceRuleTargetObjectValue(attribute, renderedValue);
  }

  validateRuleTargetObjectValues(layerKey, values, attributes, selection.classCode, { requireIdentity: includeIdentity });
  state.ruleEditorTargetValues[layerKey] = { ...values };
  return values;
}

function ruleTargetObjectTemplateContext(sourceClass) {
  const source = state.cmdbClasses.find((item) =>
    canonicalToken(item.code) === canonicalToken(sourceClass));
  return {
    class: {
      code: source?.code || sourceClass || '',
      name: source?.name || source?.code || sourceClass || '',
      description: source ? classDisplayName(source) : (sourceClass || ''),
      hierarchyPath: source?.hierarchyPath || ''
    },
    vars: {}
  };
}

function renderRuleTargetObjectValue(rawValue, context, attributeCodeValue) {
  const text = String(rawValue ?? '');
  if (!text.includes('${')) {
    return text;
  }

  const rendered = renderTemplateString(text, context);
  if (/\$\{source\.[A-Za-z_][A-Za-z0-9_]*\}/.test(rendered)) {
    throw new Error(`Атрибут ${attributeCodeValue} создаваемого целевого объекта не может использовать ${'${source.*}'}; конкретная source-карточка еще не обрабатывается.`);
  }

  return rendered;
}

function isRequiredRuleTargetValue(code) {
  return ['code', 'name'].includes(canonicalToken(code));
}

function coerceRuleTargetObjectValue(attribute, rawValue) {
  const kind = normalizedRuleFieldKind(attribute);
  if (kind === 'boolean') {
    return String(rawValue).toLowerCase() === 'true';
  }

  if (kind === 'integer') {
    const number = Number(rawValue);
    if (!Number.isInteger(number)) {
      throw new Error(`Атрибут ${attributeCode(attribute)} должен быть целым числом.`);
    }
    return number;
  }

  if (['decimal', 'double', 'float', 'number'].includes(kind)) {
    const normalized = String(rawValue).replace(',', '.');
    const number = Number(normalized);
    if (!Number.isFinite(number)) {
      throw new Error(`Атрибут ${attributeCode(attribute)} должен быть числом.`);
    }
    return number;
  }

  return rawValue;
}

function validateRuleTargetObjectValues(layerKey, values, attributes = [], classCode = '', options = {}) {
  const availableTokens = new Set(attributes.map((attribute) => canonicalToken(attributeCode(attribute))));
  if (options.requireIdentity !== false) {
    if (!availableTokens.has('name')) {
      throw new Error(`В целевом классе ${classCode || ''} нет атрибута name; создание экземпляра из этого правила невозможно.`);
    }

    if (!availableTokens.has('code')) {
      throw new Error(`В целевом классе ${classCode || ''} нет атрибута Code; создание экземпляра из этого правила невозможно.`);
    }

    if (!String(values.Code ?? values.code ?? '').trim()) {
      throw new Error('Для создаваемого целевого объекта заполните Code.');
    }

    if (!String(values.name ?? '').trim()) {
      throw new Error('Для создаваемого целевого объекта заполните name.');
    }
  }

  validateLayerAggregationTargetValues(layerKey, values, attributes, classCode);
}

function validateLayerAggregationTargetValues(layerKey, values, attributes = [], classCode = '', options = {}) {
  if (!['service', 'suppression'].includes(String(layerKey).toLowerCase())) {
    return;
  }

  const availableTokens = new Set(attributes.map((attribute) => canonicalToken(attributeCode(attribute))));
  if (!availableTokens.has('aggregationtype')) {
    return;
  }

  const allowTemplateExpressions = options.allowTemplateExpressions === true;
  const hasExpression = (value) => /\$\{[^}]+\}/.test(String(value ?? ''));

  const aggregationType = String(values.aggregation_type ?? 'all').trim();
  const hasThreshold = availableTokens.has('threshold') && values.threshold !== undefined && values.threshold !== '';
  const hasN = availableTokens.has('n') && values.n !== undefined && values.n !== '';
  if (allowTemplateExpressions && [aggregationType, values.threshold, values.n].some(hasExpression)) {
    return;
  }
  const thresholdNumber = hasThreshold
    ? Number(String(values.threshold).replace(',', '.'))
    : Number.NaN;
  const nNumber = hasN ? Number(values.n) : Number.NaN;

  if ((aggregationType === 'all' || aggregationType === 'any') && (hasThreshold || hasN)) {
    throw new Error('Для aggregation_type all/any поля threshold и n должны быть пустыми.');
  }

  if (aggregationType === 'threshold' && !availableTokens.has('threshold')) {
    throw new Error('Для aggregation_type threshold в целевом классе должен быть атрибут threshold.');
  }

  if (aggregationType === 'threshold' && (!hasThreshold || !Number.isFinite(thresholdNumber) || thresholdNumber < 0 || thresholdNumber > 100 || hasN)) {
    throw new Error('Для aggregation_type threshold заполните threshold от 0 до 100 и оставьте n пустым.');
  }

  if (aggregationType === 'n_of_m' && !availableTokens.has('n')) {
    throw new Error('Для aggregation_type n_of_m в целевом классе должен быть атрибут n.');
  }

  if (aggregationType === 'n_of_m' && (!hasN || !Number.isInteger(nNumber) || nNumber < 1 || hasThreshold)) {
    throw new Error('Для aggregation_type n_of_m заполните n >= 1 и оставьте threshold пустым.');
  }
}

function normalizedRuleFieldKind(attributeOrField) {
  if (!attributeOrField) {
    return 'unknown';
  }

  if (attributeOrField.leafKind) {
    return String(attributeOrField.leafKind).toLowerCase();
  }

  if (lookupTypeCode(attributeOrField) || attributeOrField.lookupType || attributeOrField.resolve?.lookupType) {
    return 'lookup';
  }

  const type = String(attributeOrField.type ?? '').toLowerCase();
  if (type.includes('lookup')) {
    return 'lookup';
  }
  if (['boolean', 'bool'].includes(type)) {
    return 'boolean';
  }
  if (['integer', 'int', 'long'].includes(type)) {
    return 'integer';
  }
  if (['decimal', 'double', 'float', 'number'].includes(type)) {
    return type === 'float' ? 'double' : type;
  }
  if (type) {
    return type;
  }

  return 'unknown';
}

function ruleSourceFieldInfo(layerKey, sourceAttributeValue) {
  const sourceField = sourceFieldLookupValue(sourceAttributeValue);
  if (!sourceField) {
    return null;
  }

  const config = ruleEditorConfig(layerKey);
  return sourceFieldOptionsForClass(config.sourceClass.value.trim()).find((option) =>
    canonicalToken(option.value) === canonicalToken(sourceField)) ?? null;
}

function sourceFieldLookupValue(sourceAttributeValue) {
  const value = String(sourceAttributeValue ?? '').trim();
  if (!value) {
    return '';
  }

  if (value.startsWith('${')) {
    return sourceAttributeFromDirectExpression(value);
  }

  return value;
}

function formatRuleAttributeCatalogType(attribute, options = {}) {
  const includeValidation = options.includeValidation !== false;
  const baseType = formatRuleFieldType(attribute);
  const lookupValues = lookupValueLabelsForAttribute(attribute);
  const valuesText = lookupValues.length > 0
    ? ` · значения: ${lookupValues.join(', ')}`
    : '';
  const validationText = includeValidation && attribute?.validationRules
    ? ' · JS-проверка'
    : '';
  return `${baseType}${valuesText}${validationText}`;
}

function formatRuleSourceFieldType(option) {
  const fieldRule = option?.fieldRule ?? {};
  const type = formatRuleFieldType(fieldRule);
  const lookupValues = lookupValueLabelsForAttribute(fieldRule);
  const valuesText = lookupValues.length > 0
    ? ` · значения: ${lookupValues.join(', ')}`
    : '';
  return fieldRule.resolve?.mode && fieldRule.resolve.mode !== 'none'
    ? `${type}${valuesText} · ${fieldRule.resolve.mode}`
    : `${type}${valuesText}`;
}

function formatRuleFieldType(attributeOrField) {
  const kind = normalizedRuleFieldKind(attributeOrField);
  if (kind === 'boolean') {
    return normalizeLanguage(state.language) === 'en' ? 'Yes/No' : 'Да/Нет';
  }

  const lookupType = lookupTypeCode(attributeOrField)
    || attributeOrField?.lookupType
    || attributeOrField?.resolve?.lookupType
    || '';
  if (lookupType || kind === 'lookup') {
    return `${attributeOrField?.type || 'lookup'}: ${lookupType || 'lookup'}`;
  }

  return attributeOrField?.type || 'неизвестно';
}

function lookupValueLabelsForAttribute(attributeOrField) {
  const lookupType = lookupTypeCode(attributeOrField)
    || attributeOrField?.lookupType
    || attributeOrField?.resolve?.lookupType
    || '';
  if (!lookupType) {
    return [];
  }

  const lookup = state.lookups.find((item) =>
    canonicalToken(item.code || item.name) === canonicalToken(lookupType));
  return (lookup?.values ?? []).map((value) => {
    const code = value.code || value.name || value.value || '';
    const label = value.displayName || value.description || code;
    return code && label && code !== label
      ? `${label} (${code})`
      : (label || code);
  }).filter(Boolean);
}

function allowedUserResponsibilityAttributes(layerKey) {
  const normalizedLayer = String(layerKey ?? '').toLowerCase();
  if (normalizedLayer === 'service') {
    return [...SERVICE_USER_RESPONSIBILITY_ATTRIBUTES];
  }

  if (normalizedLayer === 'suppression') {
    return [...SUPPRESSION_USER_RESPONSIBILITY_ATTRIBUTES];
  }

  return [];
}

function renderTemplateTargetAttributeList(layerKey) {
  const config = templateEditorConfig(layerKey);
  const list = config.targetAttributeList;
  if (!list) {
    return;
  }

  const targetSelection = parseTargetSelection(config.targetClass.value);
  if (!targetSelection.classCode) {
    list.innerHTML = '<div class="empty-state">Выберите целевой класс, чтобы задать атрибуты создаваемого объекта.</div>';
    return;
  }

  const rows = templateTargetObjectAttributeRows(layerKey, targetSelection.classCode);
  const missingCodes = missingTemplateTargetObjectAttributeCodes(layerKey, targetSelection.classCode);
  if (rows.length === 0) {
    list.innerHTML = `
      <div class="empty-state">В выбранном классе нет атрибутов, разрешенных для заполнения шаблоном.</div>
      ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
    `;
    return;
  }

  list.innerHTML = `
    <div class="rule-attribute-header" role="row">
      <span role="columnheader">атрибут</span>
      <span role="columnheader">значение</span>
      <span role="columnheader">помощь</span>
    </div>
    ${rows.map((row) => templateTargetAttributeRowTemplate(row)).join('')}
    ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
  `;
}

function templateTargetObjectAttributeRows(layerKey, classCode) {
  const values = state.templateEditorTargetValues[layerKey] ?? {};
  return templateTargetObjectEditableAttributes(layerKey, classCode).map((attribute) => {
    const code = attributeCode(attribute);
    return {
      attribute,
      code,
      value: Object.hasOwn(values, code) ? values[code] : defaultRuleTargetObjectValue(layerKey, code)
    };
  });
}

function templateTargetObjectEditableAttributes(layerKey, classCode) {
  const allowedCodes = allowedUserResponsibilityAttributes(layerKey);
  const attributes = targetClassAttributes(classCode);
  const attributeByExactCode = new Map(attributes
    .map((attribute) => [attributeCode(attribute), attribute]));
  const attributeByToken = new Map(attributes
    .map((attribute) => [canonicalToken(attributeCode(attribute)), attribute]));
  return allowedCodes
    .map((code) => attributeByExactCode.get(code) ?? attributeByToken.get(canonicalToken(code)))
    .filter(Boolean);
}

function missingTemplateTargetObjectAttributeCodes(layerKey, classCode) {
  const attributes = targetClassAttributes(classCode);
  const exactCodes = new Set(attributes.map((attribute) => attributeCode(attribute)));
  const attributeTokens = new Set(attributes.map((attribute) => canonicalToken(attributeCode(attribute))));
  return allowedUserResponsibilityAttributes(layerKey)
    .filter((code) => !exactCodes.has(code) && !attributeTokens.has(canonicalToken(code)));
}

function templateTargetAttributeRowTemplate(row) {
  const label = row.attribute.displayName || row.attribute.description || row.code;
  return `
    <div class="rule-attribute-row" data-template-target-attribute-row>
      <div class="rule-attribute-name">
        <strong>${escapeHtml(label)}</strong>
        <span>${escapeHtml(row.code)}</span>
      </div>
      ${ruleTargetValueControlTemplate(row.attribute, row.value, { valueDataAttribute: 'data-template-target-value' })}
      <div class="rule-attribute-help" data-rule-attribute-help>
        ${escapeHtml(row.attribute.help || row.attribute.description || '')}
      </div>
    </div>
  `;
}

function templateTargetValuesFromDom(attributeList) {
  const values = {};
  attributeList?.querySelectorAll('[data-template-target-value]').forEach((field) => {
    const code = field.dataset.targetAttribute;
    if (code) {
      values[code] = field.value;
    }
  });
  return values;
}

function templateTargetValuesForEditor(layerKey, values) {
  const allowedTokens = new Set(allowedUserResponsibilityAttributes(layerKey).map(canonicalToken));
  return Object.fromEntries(Object.entries(values ?? {})
    .filter(([code]) => allowedTokens.has(canonicalToken(code)))
    .map(([code, value]) => [code, String(value ?? '')]));
}

function normalizeTemplateTargetInitialValues(layerKey, values) {
  return templateTargetValuesForEditor(layerKey, values);
}

function readTemplateTargetObjectValues(layerKey) {
  const config = templateEditorConfig(layerKey);
  const targetSelection = parseTargetSelection(config.targetClass.value);
  const values = {};
  const attributes = templateTargetObjectEditableAttributes(layerKey, targetSelection.classCode);
  const attributeByExactCode = new Map(attributes.map((attribute) => [attributeCode(attribute), attribute]));
  const attributeByToken = new Map(attributes.map((attribute) => [canonicalToken(attributeCode(attribute)), attribute]));

  for (const field of config.targetAttributeList?.querySelectorAll('[data-template-target-value]') ?? []) {
    const code = field.dataset.targetAttribute;
    if (!code) {
      continue;
    }

    const attribute = attributeByExactCode.get(code) ?? attributeByToken.get(canonicalToken(code));
    if (!attribute) {
      continue;
    }

    const rawValue = String(field.value ?? '').trim();
    if (!rawValue) {
      continue;
    }

    if (/\$\{source\.[A-Za-z_][A-Za-z0-9_]*\}/.test(rawValue)) {
      throw new Error(`Атрибут ${code} создаваемого целевого объекта шаблона не может использовать ${'${source.*}'}; конкретная карточка-источник еще не обрабатывается.`);
    }

    values[code] = rawValue;
  }

  validateTemplateTargetObjectValues(layerKey, values, attributes, targetSelection.classCode);
  state.templateEditorTargetValues[layerKey] = { ...values };
  return values;
}

function validateTemplateTargetObjectValues(layerKey, values, attributes = [], classCode = '') {
  validateLayerAggregationTargetValues(layerKey, values, attributes, classCode, { allowTemplateExpressions: true });
}

function handleTemplateEditorChange(layerKey, target) {
  if (target.matches('[data-template-select]')) {
    state.templateEditorSelected[layerKey] = target.value;
    loadSelectedTemplateIntoEditor(layerKey);
    return;
  }

  if (target.matches('[data-selection-filter-mode], [data-selection-filter-field]')) {
    ensureSelectionFilterDraftRow(layerKey, 'template');
    state.templateEditorSelectionFilters[layerKey] = selectionFilterRowsFromDom(templateEditorConfig(layerKey).selectionFilterList);
    return;
  }

  if (target.matches('[data-template-source-field-copy-select]')) {
    updateTemplateSourceFieldCopyFields(layerKey);
    return;
  }

  if (target.matches('[data-template-target-class]')) {
    state.templateEditorTargetValues[layerKey] = templateTargetValuesFromDom(
      templateEditorConfig(layerKey).targetAttributeList);
    renderTemplateTargetAttributeList(layerKey);
    renderTemplatePopulationDimensionPreview(layerKey);
    return;
  }

  if (target.matches('[data-template-target-value]')) {
    state.templateEditorTargetValues[layerKey] = templateTargetValuesFromDom(
      templateEditorConfig(layerKey).targetAttributeList);
    return;
  }

  if (target.matches('[data-template-population-type]')) {
    rememberTemplatePopulationSelectValue(target);
    renderTemplatePopulationDimensionOptions(layerKey);
    return;
  }

  if (target.matches('[data-template-population-source-field], [data-template-population-condition-field]')) {
    rememberTemplatePopulationSelectValue(target);
    renderTemplatePopulationDimensionPreview(layerKey);
  }
}

function renderTemplateEditor(layerKey) {
  const config = templateEditorConfig(layerKey);
  if (!config.status) {
    return;
  }

  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  state.templateDocuments[layerKey] = document;
  const selectedId = state.templateEditorSelected[layerKey];
  setSelectOptions(config.select, templateSelectOptions(document.templates), selectedId);
  setSelectOptions(config.targetClass, templateTargetClassOptions(layerKey), config.targetClass.value);
  if (selectedId && !document.templates.some((template) => template.template_id === selectedId)) {
    state.templateEditorSelected[layerKey] = '';
  }

  if (!config.id.value && !config.name.value && !config.sourceRegex.value) {
    resetTemplateEditorForCreate(layerKey);
  }

  renderTemplateSourceFieldOptions(layerKey);
  renderTemplateSourceFieldHelper(layerKey);
  renderTemplatePopulationDimensionOptions(layerKey);
  renderTemplateSelectionFilterList(layerKey);
  renderTemplateTargetAttributeList(layerKey);
  renderTemplateEditorStatus(layerKey);
}

function templateSelectOptions(templates) {
  return [
    { value: '', label: 'Новый шаблон' },
    ...templates.map((template, index) => ({
      value: template.template_id,
      label: `${index + 1}. ${template.name || template.template_id}${template.name && template.name !== template.template_id ? ` [${template.template_id}]` : ''}`
    }))
  ];
}

function loadSelectedTemplateIntoEditor(layerKey) {
  const config = templateEditorConfig(layerKey);
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const template = document.templates.find((item) => item.template_id === state.templateEditorSelected[layerKey]);
  if (!template) {
    resetTemplateEditorForCreate(layerKey);
    return;
  }

  config.id.value = template.template_id || '';
  config.id.readOnly = true;
  config.id.title = 'ID сохраненного шаблона является стабильным ключом управляемых правил. Для нового ID выберите "Новый шаблон".';
  config.name.value = template.name || '';
  config.sourceRegex.value = template.source_class_regex || '';
  state.templateEditorSelectionFilters[layerKey] = selectionFiltersFromTemplate(template);
  config.priority.value = String(template.priority ?? 100);
  setSelectOptions(config.targetClass, templateTargetClassOptions(layerKey), template.target?.class_code || '');
  config.targetName.value = template.target?.name_template || '';
  config.targetDescription.value = template.target?.description_template || '';
  state.templateEditorTargetValues[layerKey] = templateTargetValuesForEditor(layerKey, template.target?.initial_user_values ?? {});
  const populationDimension = templatePopulationDimension(template);
  config.sourceKey.value = isTemplateDimensionMaterialized(populationDimension)
    ? populationDimension.key_template
    : templatePopulationSourceKeyTemplate(template);
  setTemplatePopulationDimensionEditorValues(layerKey, populationDimension);
  if (config.deleteMode) {
    config.deleteMode.value = TEMPLATE_DELETE_MODES.detachRulesKeepObjects;
  }
  renderTemplateVariableList(layerKey, template.variables ?? []);
  renderTemplateSourceFieldOptions(layerKey);
  renderTemplateSourceFieldHelper(layerKey);
  renderTemplatePopulationDimensionOptions(layerKey);
  renderTemplateSelectionFilterList(layerKey);
  renderTemplateTargetAttributeList(layerKey);
  setTemplateEditorStatus(layerKey, '');
}

function resetTemplateEditorForCreate(layerKey) {
  const config = templateEditorConfig(layerKey);
  config.select.value = '';
  config.id.value = '';
  config.id.readOnly = false;
  config.id.title = 'Стабильный ID шаблона. После сохранения не редактируется.';
  config.name.value = '';
  config.sourceRegex.value = '';
  state.templateEditorSelectionFilters[layerKey] = [];
  config.priority.value = '';
  setSelectOptions(config.targetClass, templateTargetClassOptions(layerKey), '');
  config.targetName.value = '${dimension.name}';
  config.targetDescription.value = '';
  state.templateEditorTargetValues[layerKey] = {};
  const defaultDimension = defaultTemplatePopulationDimension();
  config.sourceKey.value = defaultDimension.key_template;
  setTemplatePopulationDimensionEditorValues(layerKey, defaultDimension);
  if (config.deleteMode) {
    config.deleteMode.value = TEMPLATE_DELETE_MODES.detachRulesKeepObjects;
  }
  renderTemplateSourceFieldOptions(layerKey);
  renderTemplateSourceFieldHelper(layerKey);
  renderTemplatePopulationDimensionOptions(layerKey);
  renderTemplateSelectionFilterList(layerKey);
  renderTemplateTargetAttributeList(layerKey);
  renderTemplateVariableList(layerKey, []);
}

function renderTemplateVariableList(layerKey, variables) {
  const config = templateEditorConfig(layerKey);
  const rows = Array.isArray(variables) ? variables : [];
  config.variableList.innerHTML = rows.concat([{ name: '', value: '' }])
    .map(templateVariableRowTemplate)
    .join('');
}

function templateVariableRowTemplate(variable) {
  return `
    <div class="template-variable-row" data-template-variable-row>
      <label class="text-field">
        <span>Переменная</span>
        <input data-template-variable-name value="${escapeHtml(variable.name || '')}" placeholder="siteName" autocomplete="off">
      </label>
      <label class="text-field">
        <span>Шаблон значения</span>
        <input data-template-variable-value value="${escapeHtml(variable.value || '')}" placeholder="${escapeHtml('${class.description}')}" autocomplete="off">
      </label>
    </div>
  `;
}

function ensureTemplateVariableDraftRow(layerKey) {
  const config = templateEditorConfig(layerKey);
  const rows = [...config.variableList.querySelectorAll('[data-template-variable-row]')];
  const lastRow = rows.at(-1);
  if (!lastRow || templateVariableDomRowValues(lastRow).name || templateVariableDomRowValues(lastRow).value) {
    config.variableList.insertAdjacentHTML('beforeend', templateVariableRowTemplate({ name: '', value: '' }));
  }
}

function templateVariableDomRowValues(row) {
  return {
    name: row.querySelector('[data-template-variable-name]')?.value.trim() ?? '',
    value: row.querySelector('[data-template-variable-value]')?.value.trim() ?? ''
  };
}

function readTemplateEditorValues(layerKey) {
  const config = templateEditorConfig(layerKey);
  const templateId = normalizeRuleId(config.id.value.trim() || config.name.value.trim());
  const priority = Number(config.priority.value || 100);
  if (!templateId) {
    throw new Error('ID шаблона обязателен.');
  }

  if (!config.targetClass.value) {
    throw new Error('Целевой класс обязателен.');
  }

  if (!Number.isFinite(priority) || priority < 1) {
    throw new Error('Приоритет должен быть положительным целым числом; 1 самый высокий.');
  }

  if (config.sourceRegex.value.trim()) {
    assertValidRegexPattern(config.sourceRegex.value.trim());
  }

  const targetSelection = parseTargetSelection(config.targetClass.value);
  if (targetSelection.kind !== 'class') {
    throw new Error('В шаблоне можно выбрать только целевой класс. Конкретный экземпляр выбирается в статических правилах.');
  }
  const targetInitialValues = readTemplateTargetObjectValues(layerKey);
  const populationDimension = readTemplatePopulationDimension(layerKey);
  const selectionFilters = readSelectionFilterRows(config.selectionFilterList, {
    layerKey,
    kind: 'template'
  });
  const variables = [];
  const seenVariables = new Set();
  for (const row of config.variableList.querySelectorAll('[data-template-variable-row]')) {
    const variable = templateVariableDomRowValues(row);
    if (!variable.name && !variable.value) {
      continue;
    }

    if (!variable.name || !variable.value) {
      throw new Error('В переменной шаблона заполните имя и значение.');
    }

    const key = canonicalToken(variable.name);
    if (seenVariables.has(key)) {
      throw new Error(`Переменная ${variable.name} указана несколько раз.`);
    }

    seenVariables.add(key);
    variables.push(variable);
  }

  return {
    template_id: templateId,
    name: config.name.value.trim() || templateId,
    layer: layerKey,
    enabled: true,
    source_class_regex: config.sourceRegex.value.trim() || '',
    population_dimension: populationDimension,
    filter: selectionFiltersToTemplateFilter(selectionFilters),
    priority,
    target: {
      class_code: targetSelection.classCode || config.targetClass.value,
      name_template: config.targetName.value.trim() || (isTemplateDimensionMaterialized(populationDimension) ? '${dimension.name}' : '${class.description}'),
      description_template: config.targetDescription.value.trim() || 'Автоматически создано для ${class.description}',
      population_source_key_template: isTemplateDimensionMaterialized(populationDimension) ? populationDimension.key_template : DEFAULT_TEMPLATE_POPULATION_SOURCE_KEY,
      initial_user_values: targetInitialValues
    },
    variables
  };
}

function templatePopulationSourceKeyTemplate(template) {
  return String(template?.target?.population_source_key_template || DEFAULT_TEMPLATE_POPULATION_SOURCE_KEY);
}

function saveTemplateEditorChange(layerKey, options = {}) {
  try {
    const template = normalizeTemplate(readTemplateEditorValues(layerKey), layerKey);
    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    const selectedId = state.templateEditorSelected[layerKey];
    if (selectedId && template.template_id !== selectedId) {
      throw new Error(`ID сохраненного шаблона нельзя изменить с ${selectedId} на ${template.template_id}. Выберите "Новый шаблон" и задайте новый ID.`);
    }

    const existingIndex = document.templates.findIndex((item) => item.template_id === template.template_id);
    if (!selectedId && existingIndex >= 0) {
      throw new Error(`Шаблон с ID ${template.template_id} уже существует. Выберите его для редактирования или задайте новый ID.`);
    }

    const index = selectedId
      ? document.templates.findIndex((item) => item.template_id === selectedId)
      : -1;
    if (selectedId && index < 0) {
      throw new Error(`Шаблон ${selectedId} не найден. Загрузите конфигурацию из папки и повторите изменение.`);
    }

    const previousTemplate = index >= 0 ? cloneJson(document.templates[index]) : null;
    preserveTemplateExternalMetadata(template, previousTemplate);
    const changeMode = templateChangeMode(previousTemplate, template);
    template.version = nextTemplateVersion(previousTemplate, changeMode);
    template.lifecycle = templateLifecycleMetadata(template, changeMode);
    appendTemplateVersionSnapshot(document, template, previousTemplate, changeMode);

    if (index >= 0) {
      document.templates[index] = template;
    } else {
      document.templates.push(template);
    }

    document.templates.sort((left, right) =>
      String(left.name || left.template_id).localeCompare(String(right.name || right.template_id), undefined, { sensitivity: 'base' }));
    state.templateDocuments[layerKey] = document;
    state.templateEditorSelected[layerKey] = template.template_id;
    state.templateEditorSelectionFilters[layerKey] = selectionFiltersFromTemplate(template);
    if (options.status !== false) {
      const lifecycleMessage = changeMode === 'candidate_set_modified'
        ? ' Набор классов-источников изменен; при следующем применении будет выполнена сверка только затронутых сгенерированных правил.'
        : changeMode === 'variables_modified'
          ? ' Переменные изменены; при следующем применении будут обновлены только правила с измененным fingerprint.'
          : changeMode === 'unchanged'
            ? ' Значимых изменений нет, версия сохранена.'
            : '';
      setTemplateEditorStatus(
        layerKey,
        `Шаблон ${template.name} сохранен в текущей конфигурации. Далее: Создать/обновить правила по шаблонам и связям, затем Синхронизация -> Конфигурации конвертации -> Сохранить правила, шаблоны и связи в папку.${lifecycleMessage}`);
    }

    if (options.render !== false) {
      renderTemplateEditor(layerKey);
      renderTemplateApplyView();
      renderTemplateAuditView();
      renderConversionConfigSyncView();
    }

    return { template, changeMode };
  } catch (error) {
    if (options.status !== false) {
      setTemplateEditorStatus(layerKey, error.message, 'error');
      return null;
    }

    throw error;
  }
}

function preserveTemplateExternalMetadata(template, previousTemplate) {
  if (!previousTemplate) {
    return;
  }

  template.available_for = Array.isArray(previousTemplate.available_for)
    ? cloneJson(previousTemplate.available_for)
    : template.available_for;
  template.managed_by = previousTemplate.managed_by || template.managed_by;
  if (!isTemplateDimensionMaterialized(template.population_dimension) && previousTemplate.target?.population_source_key_template) {
    template.target.population_source_key_template = previousTemplate.target.population_source_key_template;
  }

  template.managed_relations = Array.isArray(previousTemplate.managed_relations)
    ? cloneJson(previousTemplate.managed_relations)
    : template.managed_relations;
}

function deleteTemplateEditorSelection(layerKey) {
  try {
    const selectedId = state.templateEditorSelected[layerKey];
    if (!selectedId) {
      setTemplateEditorStatus(layerKey, 'Выберите шаблон для удаления.', 'error');
      return;
    }

    const config = templateEditorConfig(layerKey);
    const deleteMode = config.deleteMode?.value || TEMPLATE_DELETE_MODES.detachRulesKeepObjects;
    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    const template = document.templates.find((item) => item.template_id === selectedId) ?? { template_id: selectedId };
    let lifecycleResult = { detachedRules: 0, removedRules: 0, targets: 0 };
    if (deleteMode === TEMPLATE_DELETE_MODES.deleteRulesAndObjects) {
      lifecycleResult = removeGeneratedRulesForTemplate(layerKey, selectedId, template, 'template_deleted');
    } else {
      lifecycleResult = detachGeneratedRulesForTemplate(layerKey, selectedId, 'template_deleted_keep_objects');
    }

    document.templates = document.templates.filter((item) => item.template_id !== selectedId);
    state.templateDocuments[layerKey] = document;
    state.templateEditorSelected[layerKey] = '';
    resetTemplateEditorForCreate(layerKey);
    const status = deleteMode === TEMPLATE_DELETE_MODES.deleteRulesAndObjects
      ? `Удален шаблон ${selectedId}; удалено правил ${lifecycleResult.removedRules}, целей в плане удаления ${lifecycleResult.targets}.`
      : `Удален шаблон ${selectedId}; отвязано правил ${lifecycleResult.detachedRules}, объекты сохранены.`;
    setTemplateEditorStatus(layerKey, status);
    renderTemplateEditor(layerKey);
    renderTemplateApplyView();
    renderTemplateAuditView();
    renderConversionConfigSyncView();
  } catch (error) {
    setTemplateEditorStatus(layerKey, error.message, 'error');
  }
}

function templateChangeMode(previousTemplate, nextTemplate) {
  if (!previousTemplate) {
    return 'created';
  }

  if (templateFingerprint(previousTemplate) === templateFingerprint(nextTemplate)) {
    return 'unchanged';
  }

  if (templateRegexFingerprint(previousTemplate) !== templateRegexFingerprint(nextTemplate)) {
    return 'candidate_set_modified';
  }

  if (templateVariablesFingerprint(previousTemplate) !== templateVariablesFingerprint(nextTemplate)) {
    return 'variables_modified';
  }

  return 'modified';
}

function nextTemplateVersion(previousTemplate, changeMode) {
  const previousVersion = Number(previousTemplate?.version || 1);
  return ['candidate_set_modified', 'variables_modified', 'modified'].includes(changeMode)
    ? previousVersion + 1
    : previousTemplate
      ? previousVersion
      : 1;
}

function templateLifecycleMetadata(template, changeMode) {
  return {
    change_mode: changeMode,
    updated_at: new Date().toISOString(),
    source_regex_fingerprint: stableHash(template.source_class_regex || ''),
    regex_fingerprint: templateRegexFingerprint(template),
    population_dimension_fingerprint: stableHash(templatePopulationDimension(template)),
    variables_fingerprint: templateVariablesFingerprint(template),
    relation_fingerprint: stableHash(template.managed_relations ?? []),
    full_fingerprint: templateFingerprint(template)
  };
}

function appendTemplateVersionSnapshot(document, template, previousTemplate, changeMode) {
  document.templateVersions = Array.isArray(document.templateVersions)
    ? document.templateVersions
    : [];
  if (changeMode === 'unchanged' && previousTemplate) {
    return;
  }

  const contentHash = templateFingerprint(template);
  const version = Number(template.version || 1);
  const alreadyRecorded = document.templateVersions.some((snapshot) =>
    snapshot.template_id === template.template_id
    && Number(snapshot.template_version || 0) === version
    && snapshot.content_hash === contentHash);
  if (alreadyRecorded) {
    return;
  }

  document.templateVersions.push({
    snapshot_id: normalizeRuleId(`${template.template_id}-v${version}-${contentHash}`),
    layer: template.layer || '',
    template_id: template.template_id,
    template_version: version,
    template_name: template.name || template.template_id,
    content_hash: contentHash,
    candidate_selection_hash: templateRegexFingerprint(template),
    variables_hash: templateVariablesFingerprint(template),
    relation_hash: stableHash(template.managed_relations ?? []),
    change_mode: changeMode,
    created_at: new Date().toISOString(),
    definition: templateManagedDefinition(template),
    managed_relations: cloneJson(template.managed_relations ?? [])
  });
}

function detachGeneratedRuleFromTemplate(layerKey, ruleId) {
  if (!layerKey || !ruleId) {
    return;
  }

  try {
    const parsed = parseRuleDocument(layerKey);
    if (!parsed.ok) {
      throw new Error(parsed.error);
    }

    const document = parsed.document;
    const rule = (document.rules ?? []).find((item) => item.rule_id === ruleId);
    if (!rule || !rule.generated_from_template) {
      state.templateApplyMessage = 'Правило не найдено или уже отвязано от шаблона.';
      state.templateApplyError = '';
      renderTemplateApplyView();
      renderTemplateAuditView();
      return;
    }

    detachGeneratedRule(rule, 'manual_rule_detach');
    writeRuleDocument(layerKey, document);
    state.templateApplyMessage = `Правило ${ruleId} отвязано от шаблона, целевой объект сохранен.`;
    state.templateApplyError = '';
    renderTemplateApplyView();
    renderTemplateAuditView();
  } catch (error) {
    state.templateApplyMessage = '';
    state.templateApplyError = error.message;
    renderTemplateApplyView();
  }
}

function detachGeneratedRulesForTemplate(layerKey, templateId, reason) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    throw new Error(parsed.error);
  }

  const document = parsed.document;
  const rules = (document.rules ?? []).filter((rule) => managedRuleTemplateId(rule) === templateId);
  for (const rule of rules) {
    detachGeneratedRule(rule, reason);
  }

  if (rules.length > 0) {
    writeRuleDocument(layerKey, document);
  }

  return {
    detachedRules: rules.length,
    removedRules: 0,
    targets: 0
  };
}

function detachGeneratedRule(rule, reason) {
  const templateId = ruleTemplateId(rule);
  rule.detached_from_template = templateId;
  rule.detached_at = new Date().toISOString();
  rule.detach_reason = reason;
  delete rule.generated_from_template;
  if (rule.template_generation && typeof rule.template_generation === 'object' && !Array.isArray(rule.template_generation)) {
    rule.template_generation.status = 'detached';
    rule.template_generation.detached_at = rule.detached_at;
    rule.template_generation.detach_reason = reason;
  }

  if (rule.target && typeof rule.target === 'object' && !Array.isArray(rule.target)) {
    rule.target.preserve_on_template_delete = true;
  }
}

function removeGeneratedRulesForTemplate(layerKey, templateId, template, reason) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    throw new Error(parsed.error);
  }

  const document = parsed.document;
  const rulesToRemove = (document.rules ?? []).filter((rule) => managedRuleTemplateId(rule) === templateId);
  if (rulesToRemove.length === 0) {
    return { detachedRules: 0, removedRules: 0, targets: 0 };
  }

  appendTemplateDeletionPlan(document, layerKey, template, rulesToRemove, reason);
  document.rules = (document.rules ?? []).filter((rule) => managedRuleTemplateId(rule) !== templateId);
  writeRuleDocument(layerKey, document);
  return {
    detachedRules: 0,
    removedRules: rulesToRemove.length,
    targets: rulesToRemove.length
  };
}

function appendTemplateDeletionPlan(document, layerKey, template, rules, reason) {
  document.templateDeletionPlans = Array.isArray(document.templateDeletionPlans)
    ? document.templateDeletionPlans
    : [];
  const createdAt = new Date().toISOString();
  const firstTemplateRule = rules.find((rule) => ruleTemplateId(rule));
  const templateId = template?.template_id || ruleTemplateId(firstTemplateRule) || '';
  const actionId = normalizeRuleId(`${layerKey}-${templateId}-${reason}-${Date.now()}`) || `${layerKey}-${Date.now()}`;
  document.templateDeletionPlans.push({
    action_id: actionId,
    action: 'delete_generated_rules_and_objects',
    status: 'pending_manual_apply',
    delete_relations: true,
    layer: layerKey,
    template_id: templateId,
    template_name: template?.name || templateId,
    reason,
    created_at: createdAt,
    source_class_regex: template?.source_class_regex || '',
    targets: rules.map((rule) => ({
      rule_id: rule.rule_id || '',
      source_class_code: ruleSourceClassCode(rule),
      target_class_code: ruleTargetClassCode(rule),
      card_id: String(rule.target?.card_id ?? ''),
      idempotency_key: rule.target?.idempotency_key || '',
      card_description: rule.target?.card_description
        || rule.target?.initial_user_values?.description
        || rule.target?.attribute_mappings?.name
        || ''
    }))
  });
}

function ruleTemplateId(rule) {
  return String(rule?.generated_from_template
    || rule?.template_generation?.template_id
    || rule?.target?.created_by_template?.template_id
    || '').trim();
}

function isGeneratedTemplateRule(rule) {
  return Boolean(String(rule?.generated_from_template || '').trim())
    && String(rule?.template_generation?.status || '').trim().toLowerCase() !== 'detached';
}

function managedRuleTemplateId(rule) {
  return String(rule?.generated_from_template || '').trim();
}

function setTemplateEditorStatus(layerKey, message, type = '') {
  state.templateEditorStatus[layerKey] = { message, type };
  renderTemplateEditorStatus(layerKey);
}

function renderTemplateEditorStatus(layerKey) {
  const config = templateEditorConfig(layerKey);
  const status = state.templateEditorStatus[layerKey] ?? { message: '', type: '' };
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  config.status.textContent = status.message || `В наборе ${document.templates.length} шаблонов.`;
  config.status.classList.toggle('error', status.type === 'error');
}

async function loadZabbixApplyStatus(layerKey) {
  const stateItem = zabbixApplyState(layerKey);
  try {
    stateItem.loadingStatus = true;
    stateItem.error = '';
    renderZabbixApplyView(layerKey);

    const response = await fetch('/api/zabbix/apply/status', {
      headers: { accept: 'application/json' }
    });
    const result = await response.json();
    if (!response.ok) {
      throw new Error(result.detail || result.error || `статус применения Zabbix не получен: ${response.status}`);
    }

    stateItem.status = zabbixApplyLayerStatusFromPayload(result, layerKey);
    stateItem.message = 'Статус применения Zabbix обновлен.';
  } catch (error) {
    stateItem.error = error.message;
  } finally {
    stateItem.loadingStatus = false;
    renderZabbixApplyView(layerKey);
  }
}

async function loadZabbixTriggerDependenciesStatus(options = {}) {
  const renderDependenciesView = options.renderDependenciesView !== false;
  const stateItem = state.zabbixTriggerDependencies;
  try {
    stateItem.loadingStatus = true;
    stateItem.error = '';
    if (renderDependenciesView) {
      renderZabbixTriggerDependenciesView();
    }
    renderGeneralSettingsView();

    const response = await fetch('/api/zabbix/trigger-dependencies/status', {
      headers: { accept: 'application/json' }
    });
    const result = await response.json();
    if (!response.ok) {
      throw new Error(result.detail || result.error || `статус trigger dependencies не получен: ${response.status}`);
    }

    stateItem.status = result;
    syncTransitiveGroupDependencyDepthFromPayload(result);
    stateItem.message = 'Статус trigger dependencies обновлен.';
  } catch (error) {
    stateItem.error = error.message;
  } finally {
    stateItem.loadingStatus = false;
    if (renderDependenciesView) {
      renderZabbixTriggerDependenciesView();
    }
    renderGeneralSettingsView();
  }
}

async function runZabbixTriggerDependencies(options = {}) {
  const dryRun = Boolean(options.dryRun);
  const stateItem = state.zabbixTriggerDependencies;
  try {
    stateItem.applying = true;
    stateItem.error = '';
    stateItem.message = dryRun
      ? 'Dry-run конфигурации trigger dependencies...'
      : 'Публикация конфигурации trigger dependencies в Zabbix...';
    renderZabbixTriggerDependenciesView();

    const response = await fetch(
      dryRun ? '/api/zabbix/trigger-dependencies/dry-run' : '/api/zabbix/trigger-dependencies/apply',
      {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify({})
      });
    const result = await response.json();
    if (!response.ok) {
      throw new Error(result.detail || result.error || `trigger dependencies не выполнены: ${response.status}`);
    }

    stateItem.result = result;
    syncTransitiveGroupDependencyDepthFromPayload(result);
    const finalMessage = result.message || (dryRun ? 'Dry-run конфигурации dependencies завершен.' : 'Конфигурация dependencies опубликована в Zabbix.');
    stateItem.message = finalMessage;
    await loadZabbixTriggerDependenciesStatus();
    stateItem.message = finalMessage;
  } catch (error) {
    stateItem.error = error.message;
  } finally {
    stateItem.applying = false;
    renderZabbixTriggerDependenciesView();
  }
}

async function applyZabbixLayer(layerKey, options = {}) {
  const dryRun = Boolean(options.dryRun);
  const stateItem = zabbixApplyState(layerKey);
  const operationId = createClientOperationId('zbx');
  let progressTimer = null;
  try {
    stateItem.applying = true;
    stateItem.error = '';
    stateItem.planPage = 1;
    stateItem.progress = {
      operationId,
      status: 'starting',
      stage: 'starting',
      message: 'Операция отправлена на сервер.',
      dryRun
    };
    stateItem.message = dryRun
      ? `Dry-run ${zabbixLayerTitle(layerKey, 'genitive')} в Zabbix...`
      : `Публикация ${zabbixLayerTitle(layerKey, 'genitive')} в Zabbix...`;
    renderZabbixApplyView(layerKey);

    progressTimer = window.setInterval(() => {
      void loadZabbixApplyProgress(layerKey, operationId);
    }, 1200);

    const response = await fetch('/api/zabbix/apply-current', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json'
      },
      body: JSON.stringify({
        operationId,
        layer: layerKey,
        dryRun
      })
    });
    const result = await response.json();
    if (!response.ok) {
      throw new Error(result.detail || result.error || `применение в Zabbix не выполнено: ${response.status}`);
    }

    stateItem.result = result;
    if (result.operationId && result.operationId !== operationId) {
      await loadZabbixApplyProgress(layerKey, result.operationId);
    } else {
      await loadZabbixApplyProgress(layerKey, operationId);
    }
    const resultErrors = Array.isArray(result.errors)
      ? result.errors.filter((item) => item)
      : [];
    const finalMessage = dryRun
      ? `Dry-run завершен: карточек ${result.cardsScanned ?? 0}, команд ${result.commandsBuilt ?? 0}.`
      : (layerKey === 'suppression'
        ? `Отправлено в Zabbix-топик для обновления suppression membership: команд ${result.commandsPublished ?? 0}, карточек ${result.cardsScanned ?? 0}, дублей ${result.commandsSkippedAsDuplicates ?? 0}.`
        : `Опубликовано в Zabbix-топик: команд ${result.commandsPublished ?? 0}, карточек ${result.cardsScanned ?? 0}, дублей ${result.commandsSkippedAsDuplicates ?? 0}.`);
    await loadZabbixApplyStatus(layerKey);
    stateItem.message = finalMessage;
    stateItem.error = resultErrors.length
      ? `${finalMessage} Ошибки: ${resultErrors.slice(0, 3).join('; ')}${resultErrors.length > 3 ? '...' : ''}`
      : '';
  } catch (error) {
    stateItem.error = error.message;
  } finally {
    if (progressTimer) {
      window.clearInterval(progressTimer);
    }
    stateItem.applying = false;
    renderZabbixApplyView(layerKey);
  }
}

async function loadZabbixApplyProgress(layerKey, operationId) {
  if (!operationId) {
    return;
  }

  const stateItem = zabbixApplyState(layerKey);
  try {
    const response = await fetch(`/api/zabbix/apply-current/progress/${encodeURIComponent(operationId)}`, {
      headers: { accept: 'application/json' }
    });
    const result = await response.json();
    if (response.status === 404) {
      return;
    }
    if (!response.ok) {
      throw new Error(result.detail || result.error || `статус операции не получен: ${response.status}`);
    }

    stateItem.progress = result;
    if (stateItem.applying) {
      stateItem.message = zabbixApplyProgressText(result);
    }
    renderZabbixApplyView(layerKey);
  } catch (error) {
    if (stateItem.applying) {
      stateItem.message = `${stateItem.message || 'Операция выполняется.'} Статус операции временно недоступен: ${error.message}`;
      renderZabbixApplyView(layerKey);
    }
  }
}

function renderZabbixApplyView(layerKey) {
  const panel = document.querySelector(`[data-zabbix-apply-layer="${layerKey}"]`);
  if (!panel) {
    return;
  }

  const stateItem = zabbixApplyState(layerKey);
  const summary = panel.querySelector('[data-zabbix-apply-summary]');
  const status = panel.querySelector('[data-zabbix-apply-status]');
  const list = panel.querySelector('[data-zabbix-apply-list]');
  const refreshButton = panel.querySelector('[data-zabbix-apply-refresh]');
  const dryRunButton = panel.querySelector('[data-zabbix-apply-dry-run]');
  const publishButton = panel.querySelector('[data-zabbix-apply-publish]');
  if (!summary || !status || !list || !refreshButton || !dryRunButton || !publishButton) {
    return;
  }

  const busy = stateItem.applying || stateItem.loadingStatus;
  refreshButton.disabled = busy;
  dryRunButton.disabled = busy;
  publishButton.disabled = busy;

  const layerStatus = stateItem.status ?? {};
  const reconcile = layerStatus.reconcile ?? {};
  summary.innerHTML = `
    <div>
      <span class="metric-label">Zabbix-топик</span>
      <strong>${escapeHtml(layerStatus.topic || '-')}</strong>
    </div>
    <div>
      <span class="metric-label">Статус</span>
      <strong>${escapeHtml(zabbixApplyStatusLabel(layerStatus.lastStatus || '-'))}</strong>
    </div>
    <div>
      <span class="metric-label">Получено команд</span>
      <strong>${escapeHtml(layerStatus.commandsReceived ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Dry-run</span>
      <strong>${escapeHtml(layerStatus.dryRunCommands ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Применено</span>
      <strong>${escapeHtml(layerStatus.appliedCommands ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Частично</span>
      <strong>${escapeHtml(layerStatus.partialCommands ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Пропущено</span>
      <strong>${escapeHtml(layerStatus.skippedCommands ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Ожидает вручную</span>
      <strong>${escapeHtml(layerStatus.pendingManualCommands ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Ошибки</span>
      <strong>${escapeHtml(layerStatus.errorCommands ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Reconcile</span>
      <strong>${escapeHtml(zabbixReconcileText(reconcile, layerKey))}</strong>
    </div>
    ${layerKey === 'suppression' ? `
      <div>
        <span class="metric-label">Zabbix Services</span>
        <strong>${escapeHtml(layerStatus.createSuppressionServices ? 'создаются' : 'не создаются')}</strong>
      </div>
    ` : ''}
  `;

  const progressText = stateItem.applying && stateItem.progress
    ? zabbixApplyProgressText(stateItem.progress)
    : '';
  status.textContent = stateItem.error
    || progressText
    || stateItem.message
    || (layerKey === 'suppression'
      ? 'Команды этого слоя обновляют suppression membership для aggregate triggers и trigger dependencies; Zabbix Services по умолчанию не создаются.'
      : 'Команды этого слоя публикуются в отдельный Zabbix-топик; статус ведется независимо от второго слоя.');
  status.classList.toggle('error', Boolean(stateItem.error));

  list.innerHTML = renderZabbixApplyDetails(layerKey, stateItem);
}

function renderZabbixApplyDetails(layerKey, stateItem) {
  const result = stateItem.result;
  const layerStatus = stateItem.status ?? {};
  const progress = stateItem.progress;
  const zabbixPlan = progress?.zabbixPlan ?? result?.zabbixPlan;
  const samples = Array.isArray(result?.sampleCommands) ? result.sampleCommands : [];
  const classes = Array.isArray(result?.classes) ? result.classes : [];
  const operationErrors = Array.isArray(result?.errors) ? result.errors.filter((item) => item) : [];
  const errors = Array.isArray(layerStatus.errors) ? layerStatus.errors : [];
  const warnings = Array.isArray(layerStatus.warnings) ? layerStatus.warnings : [];
  const membershipTargets = Array.isArray(layerStatus.membershipTargets) ? layerStatus.membershipTargets : [];
  return `
    ${progress ? renderZabbixApplyProgress(progress) : ''}
    ${zabbixPlan ? renderZabbixObjectPlan(zabbixPlan, layerKey) : ''}
    ${membershipTargets.length > 0 ? renderZabbixMembershipTargets(membershipTargets, layerKey) : ''}
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(zabbixLayerTitle(layerKey))}</span>
      <strong>${escapeHtml(result ? (result.dryRun ? 'последний dry-run' : zabbixApplyLastRunLabel(layerKey)) : 'ожидание запуска')}</strong>
      <span>правил ${escapeHtml(result?.ruleCount ?? 0)} · классов-источников ${escapeHtml(result?.sourceClassCount ?? 0)} · карточек ${escapeHtml(result?.cardsScanned ?? 0)} · команд ${escapeHtml(result?.commandsBuilt ?? 0)}</span>
      <span>топики: ${escapeHtml((result?.topics ?? (result?.topic ? [result.topic] : [])).join(', ') || '-')}</span>
      <span>последняя команда: ${escapeHtml(layerStatus.lastRuleName || layerStatus.lastRuleId || '-')} -> ${escapeHtml(layerStatus.lastTargetClass || '-')}:${escapeHtml(layerStatus.lastTargetKey || '-')}</span>
    </div>
    ${operationErrors.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">ошибки последнего запуска</span>
        <strong>${escapeHtml(operationErrors.length)} ошибок</strong>
        <span>${escapeHtml(operationErrors.join('; '))}</span>
      </div>
    ` : ''}
    ${classes.map((item) => `
      <div class="rule-summary">
        <span class="structure-mark">${escapeHtml(item.sourceClass || '-')}</span>
        <strong>${escapeHtml(item.cards ?? 0)} карточек</strong>
        <span>${escapeHtml(applyCommandCountersText(result, item))}</span>
        ${item.error ? `<span>ошибка: ${escapeHtml(item.error)}</span>` : ''}
      </div>
    `).join('')}
    ${samples.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">примеры команд</span>
        <strong>${escapeHtml(samples.length)} примеров</strong>
        <span>${escapeHtml(samples.map((item) => `${item.ruleId} ${zabbixApplyCommandSourceLabel(item)} -> ${item.targetClass}:${item.targetKey}`).join('; '))}</span>
      </div>
    ` : ''}
    ${errors.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">ошибки контура</span>
        <strong>${escapeHtml(errors.length)} последних ошибок</strong>
        <span>${escapeHtml(errors.join('; '))}</span>
      </div>
    ` : ''}
    ${warnings.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">предупреждения контура</span>
        <strong>${escapeHtml(warnings.length)} последних предупреждений</strong>
        <span>${escapeHtml(warnings.join('; '))}</span>
      </div>
    ` : ''}
  `;
}

function renderZabbixTriggerDependenciesView() {
  const summary = document.querySelector('#zabbixTriggerDependenciesSummary');
  const status = document.querySelector('#zabbixTriggerDependenciesStatus');
  const list = document.querySelector('#zabbixTriggerDependenciesList');
  const refreshButton = document.querySelector('#zabbixTriggerDependenciesRefreshButton');
  const dryRunButton = document.querySelector('#zabbixTriggerDependenciesDryRunButton');
  const applyButton = document.querySelector('#zabbixTriggerDependenciesApplyButton');
  if (!summary || !status || !list || !refreshButton || !dryRunButton || !applyButton) {
    return;
  }

  const stateItem = state.zabbixTriggerDependencies;
  const busy = stateItem.applying || stateItem.loadingStatus;
  refreshButton.disabled = busy;
  dryRunButton.disabled = busy;
  applyButton.disabled = busy;

  const payload = stateItem.result ?? stateItem.status ?? {};
  const transitiveDepth = zabbixTransitiveGroupDependencyDepth(payload);
  summary.innerHTML = `
    <div>
      <span class="metric-label">Статус</span>
      <strong>${escapeHtml(zabbixApplyStatusLabel(payload.status || payload.lastStatus || '-'))}</strong>
    </div>
    <div>
      <span class="metric-label">Глубина групп N</span>
      <strong>${escapeHtml(transitiveDepth)}</strong>
    </div>
    <div>
      <span class="metric-label">Таймаут Zabbix</span>
      <strong>${escapeHtml(zabbixRequestTimeoutText(payload?.zabbixRequestTimeoutMs))}</strong>
    </div>
    <div>
      <span class="metric-label">Размер trigger.get</span>
      <strong>${escapeHtml(payload?.triggerGetBatchSize ?? '-')}</strong>
    </div>
    <div>
      <span class="metric-label">Пачек trigger.get</span>
      <strong>${escapeHtml(payload?.triggerGetBatchCount ?? '-')}</strong>
    </div>
    <div>
      <span class="metric-label">Время trigger.get</span>
      <strong>${escapeHtml(zabbixElapsedText(payload?.triggerGetElapsedMs))}</strong>
    </div>
    <div>
      <span class="metric-label">Макс. hosts/aggregate</span>
      <strong>${escapeHtml(zabbixLimitText(payload?.largestAggregateSourceHostCount, payload?.maxSourceHostsPerAggregate))}</strong>
    </div>
    <div>
      <span class="metric-label">Макс. formula</span>
      <strong>${escapeHtml(zabbixLimitText(payload?.largestAggregateFormulaLength, payload?.maxAggregateFormulaLength))}</strong>
    </div>
    <div>
      <span class="metric-label">Макс. trigger expr</span>
      <strong>${escapeHtml(zabbixLimitText(payload?.largestAggregateTriggerExpressionLength, payload?.maxAggregateFormulaLength))}</strong>
    </div>
    <div>
      <span class="metric-label">Сложность aggregate</span>
      <strong>${escapeHtml((payload?.aggregateComplexityErrorCount ?? 0) > 0 ? `${payload.aggregateComplexityErrorCount} ошибок` : `${payload?.aggregateComplexityWarningCount ?? 0} предупреждений`)}</strong>
    </div>
    <div>
      <span class="metric-label">Желаемых зависимостей</span>
      <strong>${escapeHtml(payload.desiredDependencyCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Зависимых триггеров</span>
      <strong>${escapeHtml(payload.dependentTriggerCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Причинные triggers</span>
      <strong>${escapeHtml(payload.dependencyTriggerCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Aggregate triggers</span>
      <strong>${escapeHtml(payload.aggregateCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Aggregate создано</span>
      <strong>${escapeHtml((payload.aggregateItemsCreated ?? 0) + (payload.aggregateTriggersCreated ?? 0))}</strong>
    </div>
    <div>
      <span class="metric-label">К обновлению</span>
      <strong>${escapeHtml(payload.triggersToUpdate ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Обновлено</span>
      <strong>${escapeHtml(payload.triggersUpdated ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Добавить</span>
      <strong>${escapeHtml(payload.dependenciesAdded ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Удалить stale</span>
      <strong>${escapeHtml(payload.dependenciesRemoved ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Ручные сохранены</span>
      <strong>${escapeHtml(payload.preservedManualDependencies ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Managed-зависимостей</span>
      <strong>${escapeHtml(payload.managedDependencyCount ?? payload.managedDependencyCountBefore ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Выбрано trigger-ов</span>
      <strong>${escapeHtml(payload.selectedSourceTriggerCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Пропущено trigger-ов</span>
      <strong>${escapeHtml(payload.skippedSourceTriggerCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Expression не приняты</span>
      <strong>${escapeHtml(payload.unsupportedTriggerExpressionCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Unsupported aggregate items</span>
      <strong>${escapeHtml(payload.unsupportedAggregateItemCount ?? 0)}</strong>
    </div>
    <div>
      <span class="metric-label">Hosts без выбранных trigger-ов</span>
      <strong>${escapeHtml(payload.hostsWithoutSelectedSourceTriggers ?? 0)}</strong>
    </div>
  `;

  status.textContent = stateItem.error
    || stateItem.message
    || payload.message
    || payload.lastMessage
    || 'Сначала примените suppression-модель в Zabbix, затем выполните dry-run dependencies.';
  status.classList.toggle('error', Boolean(stateItem.error) || (payload.status || payload.lastStatus) === 'error');
  list.innerHTML = renderZabbixTriggerDependenciesDetails(payload);
}

function renderZabbixTriggerDependenciesDetails(payload) {
  const samples = Array.isArray(payload?.sampleDependencies) ? payload.sampleDependencies : [];
  const aggregates = Array.isArray(payload?.sampleAggregates) ? payload.sampleAggregates : [];
  const unsupportedAggregateItems = Array.isArray(payload?.unsupportedAggregateItems) ? payload.unsupportedAggregateItems : [];
  const warnings = Array.isArray(payload?.warnings) ? payload.warnings : [];
  const errors = Array.isArray(payload?.errors) ? payload.errors : [];
  return `
    <div class="rule-summary">
      <span class="structure-mark">параметры Zabbix API</span>
      <span>таймаут=${escapeHtml(zabbixRequestTimeoutText(payload?.zabbixRequestTimeoutMs))} · размер trigger.get=${escapeHtml(payload?.triggerGetBatchSize ?? '-')} · пачек=${escapeHtml(payload?.triggerGetBatchCount ?? '-')} · время=${escapeHtml(zabbixElapsedText(payload?.triggerGetElapsedMs))}</span>
      <span>Если dry-run/apply завершается timeout, уменьшите <code>ZabbixTriggerDependencies:TriggerGetBatchSize</code> или увеличьте <code>Zabbix:RequestTimeoutMs</code> в настройках <code>zabbixconfig2api</code>.</span>
    </div>
    <div class="rule-summary">
      <span class="structure-mark">лимиты aggregate formula</span>
      <span>source-hosts=${escapeHtml(zabbixLimitText(payload?.largestAggregateSourceHostCount, payload?.maxSourceHostsPerAggregate))} · calculated formula=${escapeHtml(zabbixLimitText(payload?.largestAggregateFormulaLength, payload?.maxAggregateFormulaLength))} · trigger expression=${escapeHtml(zabbixLimitText(payload?.largestAggregateTriggerExpressionLength, payload?.maxAggregateFormulaLength))}</span>
      <span>Превышение лимитов блокирует публикацию aggregate trigger до Zabbix. Предупреждение появляется с 80% лимита; исправляйте шаблон/source filters, делите группу или уменьшайте транзитивную глубину N.</span>
    </div>
    <div class="rule-summary">
      <span class="structure-mark">глубина транзитивных связей</span>
      <strong>N=${escapeHtml(zabbixTransitiveGroupDependencyDepth(payload))}</strong>
      <span>Leaf/source trigger-ы зависят только от ближайшей suppression-группы. Upstream-причины включаются в выражения aggregate trigger-ов групп на N уровней, чтобы модель не росла полной матрицей от каждого host trigger-а до всех верхних причин.</span>
    </div>
    <div class="rule-summary">
      <span class="structure-mark">selector состояния группы</span>
      <span>${escapeHtml(payload?.aggregateStateTriggerSelector || payload?.aggregateStateTriggerSelectorSummary || '-')}</span>
      <span>Используется только для calculated item suppression-группы; сюда должны попадать корневые признаки недоступности.</span>
      <span>Порог aggregation_type считается по host-ам, чьи выбранные поддержанные trigger-ы реально попали в calculated item; host-ы без выбранных trigger-ов показываются как unknown/skipped и не считаются отказавшими.</span>
    </div>
    <div class="rule-summary">
      <span class="structure-mark">selector dependency-покрытия</span>
      <span>${escapeHtml(payload?.dependencyTriggerSelector || payload?.dependencyTriggerSelectorSummary || '-')}</span>
      <span>Определяет, какие leaf/source trigger-ы получают Zabbix dependencies от ближайшей suppression-группы.</span>
    </div>
    ${unsupportedAggregateItems.length > 0 ? renderUnsupportedAggregateItems(unsupportedAggregateItems) : ''}
    ${aggregates.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">aggregate triggers</span>
        <strong>${escapeHtml(aggregates.length)} примеров</strong>
        <span>Каждый suppression-объект получает calculated item и trigger причины; текущее состояние считает Zabbix.</span>
      </div>
      ${aggregates.slice(0, 100).map(renderZabbixSuppressionAggregateSample).join('')}
    ` : ''}
    ${samples.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">примеры dependencies</span>
        <strong>${escapeHtml(samples.length)} примеров</strong>
        <span>${escapeHtml(payload?.hasMoreSamples ? 'Список ограничен лимитом.' : 'Показаны рассчитанные примеры.')}</span>
      </div>
      ${samples.slice(0, 100).map(renderZabbixTriggerDependencySample).join('')}
    ` : ''}
    ${warnings.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">предупреждения</span>
        <strong>${escapeHtml(warnings.length)}</strong>
        <span>${escapeHtml(warnings.join('; '))}</span>
      </div>
    ` : ''}
    ${errors.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">ошибки</span>
        <strong>${escapeHtml(errors.length)}</strong>
        <span>${escapeHtml(errors.join('; '))}</span>
      </div>
    ` : ''}
  `;
}

function zabbixRequestTimeoutText(value) {
  const timeoutMs = Number(value);
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
    return '-';
  }

  return `${timeoutMs} ms`;
}

function zabbixElapsedText(value) {
  const elapsedMs = Number(value);
  if (!Number.isFinite(elapsedMs) || elapsedMs <= 0) {
    return '-';
  }

  return `${elapsedMs} ms`;
}

function zabbixLimitText(value, limit) {
  const numericValue = Number(value);
  const numericLimit = Number(limit);
  const valueText = Number.isFinite(numericValue) ? String(numericValue) : '-';
  const limitText = Number.isFinite(numericLimit) && numericLimit > 0 ? String(numericLimit) : '-';
  return `${valueText}/${limitText}`;
}

function zabbixTransitiveGroupDependencyDepth(payload) {
  return clampNumber(
    Number(payload?.transitiveGroupDependencyDepth ?? state.transitiveGroupDependencyDepth),
    state.transitiveGroupDependencyDepth,
    1,
    3);
}

function syncTransitiveGroupDependencyDepthFromPayload(payload) {
  const value = Number(payload?.transitiveGroupDependencyDepth);
  if (Number.isInteger(value) && value >= 1 && value <= 3) {
    state.transitiveGroupDependencyDepth = value;
  }
  return state.transitiveGroupDependencyDepth;
}

function renderZabbixSuppressionAggregateSample(item) {
  const stateText = Number(item?.stateValue ?? 0) === 1 ? 'PROBLEM' : 'OK';
  return `
    <details class="rule-summary zabbix-plan-object">
      <summary>
        <strong>${escapeHtml(item?.targetName || item?.targetManagedKey || '-')}</strong>
        <span>${escapeHtml(stateText)} · ${escapeHtml(item?.aggregationType || 'all')} · hosts ${escapeHtml(item?.hostCount ?? 0)}</span>
      </summary>
      <span>trigger: ${escapeHtml(item?.triggerId || '-')} ${escapeHtml(item?.triggerName || '')}</span>
      <span>calculated item: ${escapeHtml(item?.itemId || '-')} ${escapeHtml(item?.itemKey || '-')}</span>
      <span>item state: ${escapeHtml(zabbixAggregateItemStateText(item))}</span>
      <span>формула calculated item: ${escapeHtml(item?.calculationFormula || '-')}</span>
      <span>expression trigger-а: ${escapeHtml(item?.triggerExpression || '-')}</span>
      <span>сложность: hosts=${escapeHtml(zabbixLimitText(item?.hostCount, item?.maxSourceHostsPerAggregate))} · formula=${escapeHtml(item?.calculationFormulaLength ?? 0)} · own trigger=${escapeHtml(item?.ownProblemExpressionLength ?? 0)} · upstream=${escapeHtml(item?.upstreamProblemExpressionLength ?? 0)} · trigger expression=${escapeHtml(item?.triggerExpressionLength ?? 0)}</span>
      ${renderZabbixAggregateComplexityMessages(item?.complexityMessages)}
      <span>свои source-hosts: ${escapeHtml(zabbixAggregateOwnStateText(item))}</span>
      <span>причина состояния: ${escapeHtml(zabbixAggregateStateReasonText(item))}</span>
      ${item?.upstreamProblemExpression ? `<span>upstream condition: ${escapeHtml(item.upstreamProblemExpression)}</span>` : ''}
      <span>host: ${escapeHtml(item?.hostId || '-')} ${escapeHtml(item?.hostName || '-')}</span>
      <span>source-hosts: healthy=${escapeHtml(item?.healthyHostCount ?? 0)} problem=${escapeHtml(item?.problemHostCount ?? 0)} unknown=${escapeHtml(item?.unknownHostCount ?? 0)} required=${escapeHtml(item?.requiredHealthyHostCount ?? 0)}</span>
      <span>selector: ${escapeHtml(item?.triggerSelectorSummary || '-')}</span>
      ${renderZabbixAggregateUpstreamCauses(item?.upstreamCauses)}
      ${renderZabbixAggregateSourceTriggers(item?.selectedSourceTriggers)}
      ${renderZabbixAggregateSkippedTriggers(item?.skippedSourceTriggers)}
      <span>actions: host=${escapeHtml(item?.hostAction || 'planned')} item=${escapeHtml(item?.itemAction || 'planned')} trigger=${escapeHtml(item?.triggerAction || 'planned')}</span>
    </details>
  `;
}

function renderZabbixAggregateComplexityMessages(messages) {
  if (!Array.isArray(messages) || messages.length === 0) {
    return '';
  }

  return messages
    .slice(0, 10)
    .map(message => `<span>лимит: ${escapeHtml(message)}</span>`)
    .join('');
}

function renderUnsupportedAggregateItems(items) {
  return `
    <details class="rule-summary zabbix-plan-object" open>
      <summary>
        <span class="structure-mark">unsupported aggregate items</span>
        <strong>${escapeHtml(items.length)}</strong>
      </summary>
      ${items.slice(0, 100).map(item => `
        <span>${escapeHtml(item?.targetName || item?.targetManagedKey || '-')} · item ${escapeHtml(item?.itemId || '-')} · ${escapeHtml(item?.itemKey || '-')}</span>
        <span>state=${escapeHtml(item?.state || '-')} last=${escapeHtml(item?.lastValue || '-')} clock=${escapeHtml(item?.lastClock || '-')}</span>
        <span>ошибка: ${escapeHtml(item?.error || '-')}</span>
      `).join('')}
    </details>
  `;
}

function zabbixAggregateItemStateText(item) {
  const state = String(item?.itemState ?? '');
  const status = String(item?.itemStatus ?? '');
  const error = String(item?.itemError ?? '');
  const last = String(item?.itemLastValue ?? '');
  const clock = String(item?.itemLastClock ?? '');
  const availability = state === '1' || error ? 'UNSUPPORTED' : 'OK';
  const details = [
    `state=${state || '-'}`,
    `status=${status || '-'}`,
    `last=${last || '-'}`,
    `clock=${clock || '-'}`
  ];
  if (error) {
    details.push(`error=${error}`);
  }

  return `${availability} · ${details.join(' · ')}`;
}

function zabbixAggregateOwnStateText(item) {
  const ownState = Number(item?.ownStateValue ?? item?.stateValue ?? 0) === 1 ? 'PROBLEM' : 'OK';
  return `${ownState} · healthy=${item?.healthyHostCount ?? 0} problem=${item?.problemHostCount ?? 0} required=${item?.requiredHealthyHostCount ?? 0}`;
}

function zabbixAggregateStateReasonText(item) {
  const reason = String(item?.stateReason ?? '').toLowerCase();
  if (reason === 'own_and_upstream') {
    return 'свои source-hosts и upstream-причина';
  }

  if (reason === 'own') {
    return 'свои source-hosts';
  }

  if (reason === 'upstream') {
    const causes = Array.isArray(item?.upstreamCauses)
      ? item.upstreamCauses.filter((cause) => Number(cause?.stateValue ?? 0) === 1).map((cause) => cause?.targetName || cause?.targetManagedKey).filter(Boolean)
      : [];
    return causes.length > 0 ? `upstream: ${causes.join(', ')}` : 'upstream-причина';
  }

  return 'OK';
}

function renderZabbixAggregateUpstreamCauses(causes) {
  const items = Array.isArray(causes) ? causes : [];
  if (items.length === 0) {
    return '';
  }

  return `
    <details class="rule-summary zabbix-plan-object nested">
      <summary>upstream-группы: ${escapeHtml(items.length)}</summary>
      ${items.map(cause => `
        <span>L${escapeHtml(cause?.depth ?? '-')} · ${escapeHtml(cause?.targetName || cause?.targetManagedKey || '-')} · ${escapeHtml(Number(cause?.stateValue ?? 0) === 1 ? 'PROBLEM' : 'OK')} · own=${escapeHtml(Number(cause?.ownStateValue ?? 0) === 1 ? 'PROBLEM' : 'OK')}</span>
        <span>domain path: ${escapeHtml(cause?.domainPath || '-')}</span>
        <span>trigger: ${escapeHtml(cause?.triggerId || '-')} ${escapeHtml(cause?.triggerName || '')}</span>
        <span>condition: ${escapeHtml(cause?.problemExpression || '-')}</span>
      `).join('')}
    </details>
  `;
}

function renderZabbixAggregateSourceTriggers(triggers) {
  const items = Array.isArray(triggers) ? triggers : [];
  if (items.length === 0) {
    return '<span>source trigger-ы: -</span>';
  }

  return `
    <details class="rule-summary zabbix-plan-object nested">
      <summary>source trigger-ы: ${escapeHtml(items.length)}</summary>
      ${items.map(trigger => `
        <span>${escapeHtml(trigger?.host || trigger?.hostId || '-')} / ${escapeHtml(trigger?.triggerId || '-')} · P${escapeHtml(trigger?.priority || '-')} · value=${escapeHtml(trigger?.value ?? '-')} · ${escapeHtml(trigger?.name || '')}</span>
        <span>expression: ${escapeHtml(trigger?.expression || '-')}</span>
      `).join('')}
    </details>
  `;
}

function renderZabbixAggregateSkippedTriggers(triggers) {
  const items = Array.isArray(triggers) ? triggers : [];
  if (items.length === 0) {
    return '';
  }

  return `
    <details class="rule-summary zabbix-plan-object nested">
      <summary>пропущенные trigger-ы: ${escapeHtml(items.length)}</summary>
      ${items.map(trigger => `
        <span>${escapeHtml(trigger?.host || trigger?.hostId || '-')} / ${escapeHtml(trigger?.triggerId || '-')} · ${escapeHtml(trigger?.name || '')}</span>
        <span>причина: ${escapeHtml(trigger?.reason || '-')}</span>
      `).join('')}
    </details>
  `;
}

function renderZabbixTriggerDependencySample(item) {
  return `
    <details class="rule-summary zabbix-plan-object">
      <summary>
        <strong>${escapeHtml(item?.dependentTargetName || item?.dependentTargetManagedKey || '-')}</strong>
        <span>зависит от aggregate trigger ${escapeHtml(item?.dependencyTargetName || item?.dependencyTargetManagedKey || '-')}</span>
      </summary>
      <span>dependent trigger: ${escapeHtml(item?.dependentTriggerId || '-')} ${escapeHtml(item?.dependentTriggerName || '')}</span>
      <span>aggregate cause trigger: ${escapeHtml(item?.dependencyTriggerId || '-')} ${escapeHtml(item?.dependencyTriggerName || '')}</span>
      <span>hostid: ${escapeHtml(item?.dependentHostId || '-')} -> ${escapeHtml(item?.dependencyHostId || '-')}</span>
      <span>domain: ${escapeHtml(item?.relationDomainCode || '-')}</span>
    </details>
  `;
}

function renderZabbixMembershipTargets(targets, layerKey = 'service') {
  const hostAttr = zabbixHostIdAttributeName();
  const suppressionMode = layerKey === 'suppression';
  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(suppressionMode ? 'suppression membership' : 'membership Zabbix')}</span>
      <strong>${escapeHtml(targets.length)} последних объектов</strong>
      <span>${escapeHtml(suppressionMode
        ? 'Показывает source-карточки, из которых строятся aggregate triggers и trigger dependencies. Zabbix Services по умолчанию не создаются.'
        : 'Показывает, какие source-карточки сейчас закреплены за managed service и готовы попасть в Zabbix через source leaf/problem tags.')}</span>
    </div>
    ${targets.slice(0, 10).map((target) => {
      const sources = Array.isArray(target?.sources) ? target.sources : [];
      const pendingSources = Array.isArray(target?.pendingSources) ? target.pendingSources : [];
      const sourceText = sources.slice(0, 6).map(zabbixMembershipSourceText);
      const pendingText = pendingSources.slice(0, 6).map(zabbixMembershipSourceText);
      return `
        <details class="rule-summary zabbix-plan-object">
          <summary>
            <strong>${escapeHtml(target?.targetName || target?.targetManagedKey || '-')}</strong>
            <span>готовых source ${escapeHtml(target?.sourceCount ?? 0)} · с ${escapeHtml(hostAttr)} ${escapeHtml(target?.hostBindingCount ?? 0)} · ожидают ${escapeHtml(hostAttr)} ${escapeHtml(target?.pendingSourceCount ?? target?.missingHostBindingCount ?? 0)}</span>
          </summary>
          <span>ключ: ${escapeHtml(target?.targetManagedKey || '-')}</span>
          <span>агрегация: ${escapeHtml(zabbixMembershipAggregationText(target))}</span>
          ${suppressionMode ? '' : `<span>leaf children: ${escapeHtml((target?.sourceLeafManagedKeys ?? []).join(', ') || '-')}</span>`}
          <span>готовые source: ${escapeHtml(sourceText.join('; ') || '-')}</span>
          <span>ожидают ${escapeHtml(hostAttr)}: ${escapeHtml(pendingText.join('; ') || '-')}</span>
        </details>
      `;
    }).join('')}
  `;
}

function zabbixHostIdAttributeName() {
  return String(state.zabbixHostIdAttribute || 'zabbix_main_hostid').trim() || 'zabbix_main_hostid';
}

function zabbixMembershipAggregationText(target) {
  const type = String(target?.aggregationType ?? '').trim() || 'all';
  const details = [];
  if (String(target?.threshold ?? '').trim()) {
    details.push(`threshold=${target.threshold}`);
  }
  if (String(target?.n ?? '').trim()) {
    details.push(`n=${target.n}`);
  }

  return details.length > 0 ? `${type} (${details.join(', ')})` : type;
}

function zabbixMembershipSourceText(source) {
  const hostAttr = zabbixHostIdAttributeName();
  const identity = `${source?.sourceClass || '-'}/${source?.sourceCardId || '-'}`;
  const keyValue = String(source?.sourceKeyValue ?? '').trim();
  const key = keyValue ? ` ${source?.sourceKeyAttribute || 'key'}=${keyValue}` : '';
  const host = source?.zabbixHostId ? ` ${hostAttr}=${source.zabbixHostId}` : ` нет ${hostAttr}`;
  return `${identity}${key}${host}`;
}

function zabbixApplyCommandSourceLabel(item) {
  const hostAttr = zabbixHostIdAttributeName();
  const source = `${item?.sourceClass || '-'}${item?.sourceCardId ? `/${item.sourceCardId}` : ''}`;
  const keyAttribute = String(item?.sourceKeyAttribute ?? '').trim();
  const keyValue = String(item?.sourceKeyValue ?? '').trim();
  const hostId = String(item?.sourceZabbixHostId ?? '').trim();
  const hostSuffix = hostId ? ` ${hostAttr}=${hostId}` : ` без ${hostAttr}`;
  if (!keyValue || keyValue === String(item?.sourceCardId ?? '').trim()) {
    return `${source}${hostSuffix}`;
  }

  return `${source} ${keyAttribute || 'key'}=${keyValue}${hostSuffix}`;
}

function zabbixApplyLastRunLabel(layerKey) {
  return layerKey === 'suppression' ? 'последнее обновление membership' : 'последняя публикация';
}

function renderZabbixObjectPlan(plan, layerKey) {
  const objects = Array.isArray(plan?.objects) ? plan.objects : [];
  if (!plan || ((plan.objectCount ?? 0) === 0 && objects.length === 0)) {
    return '';
  }

  const suppressionMode = layerKey === 'suppression';
  const pageSize = 25;
  const pageCount = Math.max(1, Math.ceil(objects.length / pageSize));
  const stateItem = zabbixApplyState(layerKey);
  const page = Math.min(Math.max(1, Number(stateItem.planPage) || 1), pageCount);
  stateItem.planPage = page;
  const start = (page - 1) * pageSize;
  const visibleObjects = objects.slice(start, start + pageSize);
  const shownFrom = objects.length === 0 ? 0 : start + 1;
  const shownTo = start + visibleObjects.length;
  const hasMore = Boolean(plan.hasMoreObjects);
  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(suppressionMode ? 'план suppression membership' : 'план объектов Zabbix')}</span>
      <strong>${escapeHtml(plan.objectCount ?? objects.length)} ${escapeHtml(suppressionMode ? 'membership-объектов' : 'объектов')} · ${escapeHtml(plan.relationCount ?? 0)} связей</strong>
      <span>${escapeHtml(`страница ${page} из ${pageCount}; показаны ${shownFrom}-${shownTo} из ${objects.length}`)}${hasMore ? escapeHtml(`; полный список ограничен первыми ${plan.objectSamplesLimit ?? objects.length}`) : ''}</span>
      ${pageCount > 1 ? `
        <div class="rule-summary-actions">
          <button class="secondary-button" type="button" data-zabbix-plan-page="${escapeHtml(page - 1)}" ${page <= 1 ? 'disabled' : ''}>Назад</button>
          <button class="secondary-button" type="button" data-zabbix-plan-page="${escapeHtml(page + 1)}" ${page >= pageCount ? 'disabled' : ''}>Вперед</button>
        </div>
      ` : ''}
    </div>
    ${visibleObjects.map((item) => renderZabbixObjectPlanItem(item, layerKey)).join('')}
  `;
}

function renderZabbixObjectPlanItem(item, layerKey = 'service') {
  const hostAttr = zabbixHostIdAttributeName();
  const suppressionMode = layerKey === 'suppression';
  const attributes = Object.entries(item?.attributes ?? {})
    .slice(0, 8)
    .map(([key, value]) => `${key}=${value}`);
  const relations = Array.isArray(item?.relations) ? item.relations : [];
  const relationText = relations
    .slice(0, 6)
    .map((relation) => `${relation.domainCode || '-'} -> ${relation.targetClassCode || '-'}:${relation.targetLookup || '-'}`);
  const sources = Array.isArray(item?.sourceObjects) ? item.sourceObjects : [];
  const sourceBindings = Array.isArray(item?.sourceBindings) ? item.sourceBindings : [];
  const sourceBindingText = sourceBindings
    .slice(0, 8)
    .map((binding) => zabbixSourceBindingPlanText(binding, layerKey));
  const ruleIds = Array.isArray(item?.ruleIds) ? item.ruleIds : [];
  const ruleNames = Array.isArray(item?.ruleNames) ? item.ruleNames : [];
  const title = item?.targetName || item?.targetKey || '-';
  const target = `${item?.targetClass || '-'}:${item?.targetCardId || item?.targetKey || '-'}`;
  const bindingSummary = suppressionMode
    ? [
        `source ${item?.sourceCount ?? sources.length}`,
        `с ${hostAttr} ${item?.hostBindingCount ?? 0}`,
        `ожидают ${hostAttr} ${item?.missingHostBindingCount ?? 0}`
      ].join(' · ')
    : [
        `source ${item?.sourceCount ?? sources.length}`,
        `с ${hostAttr} ${item?.hostBindingCount ?? 0}`,
        `без ${hostAttr} ${item?.missingHostBindingCount ?? 0}`,
        `problem tags ${item?.problemTagCount ?? 0}`
      ].join(' · ');
  return `
    <details class="rule-summary zabbix-plan-object">
      <summary>
        <strong>${escapeHtml(title)}</strong>
        <span>цель: ${escapeHtml(target)} · команд ${escapeHtml(item?.commandCount ?? 0)} · связей ${escapeHtml(item?.relationCount ?? 0)} · ${escapeHtml(bindingSummary)}</span>
      </summary>
      <span>действие: ${escapeHtml(item?.actionLabel || zabbixObjectActionLabel(item?.action))}</span>
      <span>правила: ${escapeHtml(ruleNames.length > 0 ? ruleNames.join(', ') : ruleIds.join(', ') || '-')}</span>
      <span>источники: ${escapeHtml(sources.join(', ') || '-')}</span>
      ${sourceBindingText.length > 0 ? `<span>${escapeHtml(suppressionMode ? 'готовность к trigger dependencies' : 'привязка к проблемам Zabbix')}: ${escapeHtml(sourceBindingText.join('; '))}</span>` : ''}
      ${attributes.length > 0 ? `<span>атрибуты: ${escapeHtml(attributes.join('; '))}</span>` : ''}
      ${relationText.length > 0 ? `<span>связи: ${escapeHtml(relationText.join('; '))}</span>` : ''}
    </details>
  `;
}

function zabbixSourceBindingPlanText(binding, layerKey = 'service') {
  const hostAttr = zabbixHostIdAttributeName();
  const label = binding?.label || `${binding?.sourceClass || '-'}/${binding?.sourceCardId || '-'}`;
  const hostId = String(binding?.zabbixHostId ?? '').trim();
  if (layerKey === 'suppression') {
    return hostId
      ? `${label}: ${hostAttr}=${hostId}, готово для aggregate/dependencies`
      : `${label}: нет ${hostAttr}, будет pending membership`;
  }

  const problemTags = Array.isArray(binding?.problemTags) ? binding.problemTags : [];
  const leaf = binding?.sourceLeafManagedKey ? `leaf ${binding.sourceLeafManagedKey}` : 'leaf не задан';
  return hostId
    ? `${label}: ${leaf}, ${hostAttr}=${hostId}, problem tags ${problemTags.join(', ') || '-'}`
    : `${label}: ${leaf}, нет ${hostAttr}`;
}

function zabbixObjectActionLabel(action) {
  const value = String(action ?? '').toLowerCase();
  if (value === 'remove_membership') {
    return 'удалить связь';
  }

  return 'создать при отсутствии / обновить';
}

function renderZabbixApplyProgress(progress) {
  const completed = progress.sourceClassesCompleted ?? 0;
  const total = progress.sourceClassCount ?? 0;
  const currentClass = progress.currentSourceClass || '-';
  const currentDone = progress.currentClassCardsProcessed ?? 0;
  const currentTotal = progress.currentClassCardsTotal ?? 0;
  const classesRemaining = progress.sourceClassesRemaining ?? Math.max(0, total - completed);
  const currentRemaining = progress.currentClassCardsRemaining ?? Math.max(0, currentTotal - currentDone);
  const completedClasses = Array.isArray(progress.completedClasses) ? progress.completedClasses.slice(-5) : [];
  const progressLabel = zabbixApplyProgressStatusLabel(progress.status);
  return `
    <div class="rule-summary">
      <span class="structure-mark">ход операции</span>
      <strong>${escapeHtml(progressLabel)} · ${escapeHtml(progress.stage || '-')}</strong>
      <span>${escapeHtml(progress.message || zabbixApplyProgressText(progress))}</span>
      <span>классы: завершено ${escapeHtml(completed)} из ${escapeHtml(total || '?')} · осталось ${escapeHtml(classesRemaining)}</span>
      <span>текущий класс: ${escapeHtml(currentClass)} · карточек ${escapeHtml(currentDone)} из ${escapeHtml(currentTotal || '?')} · осталось ${escapeHtml(currentRemaining)}</span>
      <span>${escapeHtml(applyProgressCommandCountersText(progress))}</span>
      <span>обновлено: ${escapeHtml(formatCacheTimestamp(progress.updatedAtUtc || progress.updatedAt))}</span>
    </div>
    ${completedClasses.length > 0 ? `
      <div class="rule-summary">
        <span class="structure-mark">завершенные классы</span>
        <strong>${escapeHtml(completedClasses.length)} последних</strong>
        <span>${escapeHtml(completedClasses.map((item) => `${item.sourceClass}: карточек ${item.cards ?? 0}, ${applyCommandCountersText(progress, item)}`).join('; '))}</span>
      </div>
    ` : ''}
  `;
}

function zabbixApplyProgressText(progress) {
  if (!progress) {
    return '';
  }

  const completed = progress.sourceClassesCompleted ?? 0;
  const total = progress.sourceClassCount ?? 0;
  const currentClass = progress.currentSourceClass || 'подготовка';
  const currentDone = progress.currentClassCardsProcessed ?? 0;
  const currentTotal = progress.currentClassCardsTotal ?? 0;
  const classesRemaining = progress.sourceClassesRemaining ?? Math.max(0, total - completed);
  const currentRemaining = progress.currentClassCardsRemaining ?? Math.max(0, currentTotal - currentDone);
  return [
    progress.message || 'Операция выполняется.',
    `Классы: ${completed}/${total || '?'}, осталось ${classesRemaining}.`,
    `Сейчас: ${currentClass}, карточек ${currentDone}/${currentTotal || '?'}, осталось ${currentRemaining}.`,
    `Команды: ${applyCommandCountersText(progress)}.`
  ].join(' ');
}

function applyCommandCountersText(context, item = context) {
  const dryRun = Boolean(context?.dryRun);
  const built = item?.commandsBuilt ?? 0;
  const published = item?.commandsPublished ?? 0;
  const duplicates = item?.commandsSkippedAsDuplicates ?? 0;
  return dryRun
    ? `собрано ${built} · к публикации ${built} · дубли ${duplicates}`
    : `собрано ${built} · опубликовано ${published} · дубли ${duplicates}`;
}

function applyProgressCommandCountersText(progress) {
  return [
    `всего карточек обработано ${progress?.cardsScanned ?? 0}`,
    applyCommandCountersText(progress)
  ].join(' · ');
}

function zabbixApplyProgressStatusLabel(status) {
  const value = String(status ?? '').toLowerCase();
  if (value === 'running') {
    return 'выполняется';
  }
  if (value === 'completed') {
    return 'завершено';
  }
  if (value === 'error') {
    return 'ошибка';
  }
  if (value === 'starting') {
    return 'запуск';
  }

  return status || '-';
}

function createClientOperationId(prefix = 'op') {
  if (globalThis.crypto?.randomUUID) {
    return `${prefix}-${globalThis.crypto.randomUUID()}`;
  }

  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

function zabbixApplyLayerStatusFromPayload(payload, layerKey) {
  const layers = Array.isArray(payload?.layers) ? payload.layers : [];
  return layers.find((item) => String(item.layer).toLowerCase() === layerKey)
    ?? (String(payload?.layer ?? '').toLowerCase() === layerKey ? payload : null)
    ?? null;
}

function zabbixApplyState(layerKey) {
  return state.zabbixApply[layerKey === 'suppression' ? 'suppression' : 'service'];
}

function zabbixLayerTitle(layerKey, form = 'nominative') {
  if (layerKey === 'suppression') {
    return form === 'genitive' ? 'модели подавления' : 'Каскадное подавление';
  }

  return form === 'genitive' ? 'сервисной модели' : 'Сервис';
}

function zabbixApplyStatusLabel(status) {
  const value = String(status ?? '').toLowerCase();
  if (value === 'dry-run') {
    return 'dry-run';
  }
  if (value === 'accepted') {
    return 'принято';
  }
  if (value === 'applied') {
    return 'применено';
  }
  if (value === 'partial') {
    return 'частично';
  }
  if (value === 'skipped') {
    return 'пропущено';
  }
  if (value === 'pending_manual') {
    return 'ожидает вручную';
  }
  if (value === 'error') {
    return 'ошибка';
  }

  return status || '-';
}

function zabbixReconcileText(reconcile, layerKey = 'service') {
  if (layerKey === 'suppression'
      && ((reconcile.ensureMembershipTargets ?? 0) > 0
        || (reconcile.ensureMembershipSources ?? 0) > 0
        || (reconcile.ensureMembershipRelations ?? 0) > 0
        || (reconcile.removeMembershipSources ?? 0) > 0)) {
    return [
      `membership target +${reconcile.ensureMembershipTargets ?? 0}`,
      `source +${reconcile.ensureMembershipSources ?? 0}`,
      `связи +${reconcile.ensureMembershipRelations ?? 0}`,
      `source -${reconcile.removeMembershipSources ?? 0}`
    ].join(', ');
  }

  return [
    `объекты +${reconcile.ensureObjects ?? 0}`,
    `связи +${reconcile.ensureRelations ?? 0}`,
    `source leaf +${reconcile.ensureSourceLeafServices ?? 0}`,
    `problem tags +${reconcile.ensureProblemTags ?? 0}`,
    `host tags +${reconcile.ensureHostTags ?? 0}`,
    `объекты -${reconcile.removeObjects ?? 0}`,
    `связи -${reconcile.removeRelations ?? 0}`
  ].join(', ');
}

function renderZabbixPreflightView() {
  const layerSelect = document.querySelector('#zabbixPreflightLayerSelect');
  const directionSelect = document.querySelector('#zabbixPreflightDirectionSelect');
  const refreshButton = document.querySelector('#zabbixPreflightRefreshButton');
  const summary = document.querySelector('#zabbixPreflightSummary');
  const status = document.querySelector('#zabbixPreflightStatus');
  const list = document.querySelector('#zabbixPreflightList');
  if (!layerSelect || !directionSelect || !refreshButton || !summary || !status || !list) {
    return;
  }

  const config = state.zabbixPreflight;
  layerSelect.value = ['all', 'service', 'suppression'].includes(config.layer) ? config.layer : 'all';
  directionSelect.value = config.direction === 'configured' ? 'configured' : 'effect';
  refreshButton.disabled = false;

  const report = zabbixPreflightReport(config);
  summary.innerHTML = renderZabbixPreflightSummary(report);
  status.textContent = zabbixPreflightStatusText(report);
  status.classList.toggle('error', report.blockerCount > 0 || report.cycleCount > 0);
  list.innerHTML = renderZabbixPreflightList(report);
}

function zabbixPreflightReport(options = {}) {
  const layerFilter = ['all', 'service', 'suppression'].includes(options.layer) ? options.layer : 'all';
  const direction = options.direction === 'configured' ? 'configured' : 'effect';
  const layers = layerFilter === 'all' ? ['service', 'suppression'] : [layerFilter];
  const layerReports = layers.map((layerKey) => zabbixPreflightLayerReport(layerKey, direction));
  return {
    layerFilter,
    direction,
    layers: layerReports,
    objectNodeCount: layerReports.reduce((sum, item) => sum + item.objectNodeCount, 0),
    objectEdgeCount: layerReports.reduce((sum, item) => sum + item.objectEdgeCount, 0),
    cycleCount: layerReports.reduce((sum, item) => sum + item.cycles.length, 0),
    breakPlanCount: layerReports.reduce((sum, item) => sum + item.breakPlan.length, 0),
    blockerCount: layerReports.reduce((sum, item) => sum + item.blockerCount, 0)
  };
}

function zabbixPreflightLayerReport(layerKey, direction) {
  const graph = relationGraphData(layerKey);
  if (graph.error) {
    return {
      layerKey,
      direction,
      graph,
      graphError: graph.error,
      objectGraph: { nodes: [], edges: [], nodeByKey: new Map() },
      objectNodeCount: 0,
      objectEdgeCount: 0,
      missingTargetCount: 0,
      missingTargetEdges: [],
      relationErrorCount: 0,
      relationErrorItems: [],
      transitiveEdges: [],
      blockerCount: 1,
      cycles: [],
      breakPlan: []
    };
  }

  const objectGraph = zabbixObjectGraphFromRelationGraph(graph, direction);
  const cycles = zabbixDirectedCycles(objectGraph).map((cycle, index) => ({
    ...cycle,
    index: index + 1,
    breakCandidate: zabbixCycleBreakCandidate(cycle)
  }));
  const cycleEdgeIds = new Set(cycles.flatMap((cycle) => (cycle.edges ?? []).map((edge) => edge.id)));
  const acyclicEdges = objectGraph.edges.filter((edge) => !cycleEdgeIds.has(edge.id));
  const transitiveEdges = zabbixTransitiveObjectEdges(objectGraph, cycleEdgeIds);
  const unmaterializedEdges = graph.edges.filter((edge) =>
    !edge.missingTarget
    && (edge.expectedRelations?.length ?? 0) === 0
    && (edge.relationErrors?.length ?? 0) === 0);
  const missingTargetEdges = graph.edges.filter((edge) => edge.missingTarget);
  const relationErrorItems = graph.edges
    .flatMap((edge) => (edge.relationErrors ?? []).map((error) => ({ edge, error })))
    .filter(Boolean);
  const missingTargetCount = missingTargetEdges.length;
  const relationErrorCount = relationErrorItems.length;
  return {
    layerKey,
    direction,
    graph,
    graphError: '',
    objectGraph,
    objectNodeCount: objectGraph.nodes.length,
    objectEdgeCount: objectGraph.edges.length,
    missingTargetCount,
    missingTargetEdges,
    relationErrorCount,
    relationErrorItems,
    acyclicEdges,
    transitiveEdges,
    unmaterializedEdges,
    blockerCount: missingTargetCount + relationErrorCount,
    cycles,
    breakPlan: cycles.map((cycle) => cycle.breakCandidate).filter(Boolean)
  };
}

function zabbixTransitiveObjectEdges(objectGraph, ignoredEdgeIds = new Set()) {
  const edges = (objectGraph.edges ?? []).filter((edge) => !ignoredEdgeIds.has(edge.id));
  const adjacency = new Map();
  for (const edge of edges) {
    if (!adjacency.has(edge.sourceKey)) {
      adjacency.set(edge.sourceKey, []);
    }

    adjacency.get(edge.sourceKey).push(edge);
  }

  const results = [];
  const seen = new Set();
  for (const edge of edges) {
    const path = zabbixShortestObjectPath(adjacency, edge.sourceKey, edge.targetKey, edge.id, 6);
    if (!path || path.length < 2) {
      continue;
    }

    const key = `${edge.sourceKey}->${edge.targetKey}:${edge.id}`;
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    results.push({ edge, path });
  }

  return results;
}

function zabbixShortestObjectPath(adjacency, sourceKey, targetKey, excludedEdgeId, maxDepth = 6) {
  const queue = [{ key: sourceKey, path: [] }];
  const visited = new Set([sourceKey]);
  while (queue.length > 0) {
    const item = queue.shift();
    if (item.path.length >= maxDepth) {
      continue;
    }

    for (const edge of adjacency.get(item.key) ?? []) {
      if (edge.id === excludedEdgeId) {
        continue;
      }

      const nextPath = item.path.concat([edge]);
      if (edge.targetKey === targetKey) {
        return nextPath;
      }

      if (visited.has(edge.targetKey)) {
        continue;
      }

      visited.add(edge.targetKey);
      queue.push({ key: edge.targetKey, path: nextPath });
    }
  }

  return null;
}

function zabbixObjectGraphFromRelationGraph(graph, direction = 'effect') {
  const nodeByKey = new Map();
  const registerObject = (object, ownerNode = null) => {
    const key = String(object?.key || relationGraphObjectMatchKey(object?.classCode, object?.lookup)).trim();
    if (!key) {
      return null;
    }

    if (!nodeByKey.has(key)) {
      nodeByKey.set(key, {
        key,
        classCode: String(object?.classCode ?? '').trim(),
        lookup: String(object?.lookup ?? '').trim(),
        label: String(object?.label ?? object?.lookup ?? key).trim(),
        owners: []
      });
    }

    const node = nodeByKey.get(key);
    if (ownerNode && !node.owners.some((item) => item.id === ownerNode.id)) {
      node.owners.push(ownerNode);
    }
    return node;
  };

  for (const node of graph.nodes ?? []) {
    for (const object of node.expectedObjects ?? []) {
      registerObject(object, node);
    }
  }

  const edges = [];
  for (const edge of graph.edges ?? []) {
    for (const expected of edge.expectedRelations ?? []) {
      const configuredSourceObject = expected.sourceObject;
      const configuredTargetObject = expected.targetObject;
      if (!configuredSourceObject?.key || !configuredTargetObject?.key) {
        continue;
      }

      const reversedForEffect = direction === 'effect'
        && relationGraphRoleEffectDirection(edge.role) === 'target_to_source';
      const sourceObject = reversedForEffect ? configuredTargetObject : configuredSourceObject;
      const targetObject = reversedForEffect ? configuredSourceObject : configuredTargetObject;
      const sourceNode = registerObject(sourceObject);
      const targetNode = registerObject(targetObject);
      if (!sourceNode || !targetNode) {
        continue;
      }

      const sourcePriority = zabbixObjectPriority(sourceNode);
      const targetPriority = zabbixObjectPriority(targetNode);
      edges.push({
        id: stableHash({
          edge: edge.id,
          direction,
          domain: expected.domainCode,
          source: sourceNode.key,
          target: targetNode.key
        }),
        sourceKey: sourceNode.key,
        targetKey: targetNode.key,
        sourceNode,
        targetNode,
        configuredSourceObject,
        configuredTargetObject,
        sourceObject,
        targetObject,
        sourcePriority,
        targetPriority,
        priorityViolation: Number.isFinite(sourcePriority)
          && Number.isFinite(targetPriority)
          && sourcePriority > targetPriority,
        role: edge.role,
        roleLabel: edge.roleLabel,
        relation: edge.relation,
        configEdge: edge,
        expected,
        reversedForEffect
      });
    }
  }

  return {
    nodes: [...nodeByKey.values()],
    edges,
    nodeByKey
  };
}

function zabbixObjectPriority(node) {
  const values = (node?.owners ?? [])
    .map((owner) => Number(owner.priority))
    .filter((value) => Number.isFinite(value));
  return values.length > 0 ? Math.min(...values) : Number.NaN;
}

function zabbixDirectedCycles(objectGraph) {
  const nodeKeys = objectGraph.nodes.map((node) => node.key);
  const adjacency = new Map(nodeKeys.map((key) => [key, []]));
  for (const edge of objectGraph.edges) {
    if (!adjacency.has(edge.sourceKey)) {
      adjacency.set(edge.sourceKey, []);
    }
    adjacency.get(edge.sourceKey).push(edge);
  }

  let index = 0;
  const stack = [];
  const onStack = new Set();
  const indexes = new Map();
  const lowlinks = new Map();
  const components = [];

  const strongConnect = (key) => {
    indexes.set(key, index);
    lowlinks.set(key, index);
    index += 1;
    stack.push(key);
    onStack.add(key);

    for (const edge of adjacency.get(key) ?? []) {
      const target = edge.targetKey;
      if (!indexes.has(target)) {
        strongConnect(target);
        lowlinks.set(key, Math.min(lowlinks.get(key), lowlinks.get(target)));
      } else if (onStack.has(target)) {
        lowlinks.set(key, Math.min(lowlinks.get(key), indexes.get(target)));
      }
    }

    if (lowlinks.get(key) === indexes.get(key)) {
      const component = [];
      while (stack.length > 0) {
        const item = stack.pop();
        onStack.delete(item);
        component.push(item);
        if (item === key) {
          break;
        }
      }
      components.push(component);
    }
  };

  for (const key of nodeKeys) {
    if (!indexes.has(key)) {
      strongConnect(key);
    }
  }

  return components
    .map((component) => {
      const keys = new Set(component);
      const edges = objectGraph.edges.filter((edge) => keys.has(edge.sourceKey) && keys.has(edge.targetKey));
      const isCycle = component.length > 1 || edges.some((edge) => edge.sourceKey === edge.targetKey);
      if (!isCycle) {
        return null;
      }

      return {
        nodeKeys: component,
        nodes: component.map((key) => objectGraph.nodeByKey.get(key)).filter(Boolean),
        edges
      };
    })
    .filter(Boolean);
}

function zabbixCycleBreakCandidate(cycle) {
  const ranked = (cycle.edges ?? [])
    .map((edge) => ({
      edge,
      score: zabbixCycleBreakEdgeScore(edge),
      reasons: zabbixCycleBreakReasons(edge)
    }))
    .sort((left, right) => right.score - left.score);
  if (ranked.length === 0) {
    return null;
  }

  const candidate = ranked[0];
  return {
    ...candidate,
    reasons: candidate.reasons.length > 0
      ? candidate.reasons
      : ['первое доступное ребро цикла; требуется ручное подтверждение']
  };
}

function zabbixCycleBreakEdgeScore(edge) {
  const reasons = zabbixCycleBreakReasons(edge);
  let score = 0;
  for (const reason of reasons) {
    if (reason.includes('шаблон')) {
      score += 80;
    } else if (reason.includes('regexp')) {
      score += 30;
    } else if (reason.includes('приоритет')) {
      score += 50;
    } else if (reason.includes('слабая')) {
      score += 20;
    } else if (reason.includes('ручная')) {
      score -= 60;
    } else if (reason.includes('структурная')) {
      score -= 35;
    }
  }

  return score;
}

function zabbixCycleBreakReasons(edge) {
  const reasons = [];
  const sourceKind = edge.configEdge?.sourceKind ?? '';
  const targetKind = edge.configEdge?.targetKind ?? '';
  if (sourceKind === 'template' || targetKind === 'template') {
    reasons.push('связь порождена шаблоном и может быть восстановлена после корректировки шаблона');
  }
  if (sourceKind === 'manual_rule' && targetKind === 'manual_rule') {
    reasons.push('ручная связь правило-правило: не разрывать без подтверждения');
  }
  if ((edge.configEdge?.regexItems ?? []).length > 0) {
    reasons.push('есть regexp/отбор: вероятна широкая материализация связи');
  }
  if (edge.priorityViolation) {
    reasons.push(`нарушение приоритета: P${edge.sourcePriority} -> P${edge.targetPriority}`);
  }
  if (['uses', 'impacts'].includes(String(edge.role))) {
    reasons.push('слабая семантика связи, дешевле разорвать для DAG');
  }
  if (String(edge.role) === 'contains') {
    reasons.push('структурная связь contains: защищена от авторазрыва');
  }

  return reasons;
}

function renderZabbixPreflightSummary(report) {
  return `
    <div>
      <span class="metric-label">Слои</span>
      <strong>${escapeHtml(report.layers.length)}</strong>
    </div>
    <div>
      <span class="metric-label">Объекты графа</span>
      <strong>${escapeHtml(report.objectNodeCount)}</strong>
    </div>
    <div>
      <span class="metric-label">Связи Zabbix</span>
      <strong>${escapeHtml(report.objectEdgeCount)}</strong>
    </div>
    <div>
      <span class="metric-label">Циклы</span>
      <strong>${escapeHtml(report.cycleCount)}</strong>
    </div>
    <div>
      <span class="metric-label">План разрыва</span>
      <strong>${escapeHtml(report.breakPlanCount)}</strong>
    </div>
    <div>
      <span class="metric-label">Блокеры</span>
      <strong>${escapeHtml(report.blockerCount)}</strong>
    </div>
  `;
}

function zabbixPreflightStatusText(report) {
  const direction = report.direction === 'effect'
    ? 'проверяется направление влияния/подавления'
    : 'проверяется направление, записанное в правилах';
  if (report.cycleCount > 0) {
    return `${direction}. Найдены циклы: ${report.cycleCount}; пробный план разрыва: ${report.breakPlanCount}. Конфигурация не изменена.`;
  }
  if (report.blockerCount > 0) {
    return `${direction}. Циклов нет, но есть блокеры расчета связей: ${report.blockerCount}.`;
  }
  return `${direction}. Циклов в материализованном графе объектов не найдено; граф готов к следующему этапу отражения в Zabbix.`;
}

function renderZabbixPreflightList(report) {
  return report.layers.map((layerReport) => renderZabbixPreflightLayerReport(layerReport)).join('');
}

function renderZabbixPreflightLayerReport(report) {
  const layerLabel = report.layerKey === 'service' ? 'Сервис' : 'Подавление';
  if (report.graphError) {
    return `
      <article class="zabbix-preflight-card error">
        <strong>${escapeHtml(layerLabel)}: граф не построен</strong>
        <span>${escapeHtml(report.graphError)}</span>
      </article>
    `;
  }

  const blockerText = [
    report.missingTargetCount > 0 ? `Потерянные цели связей: ${report.missingTargetCount}` : '',
    report.relationErrorCount > 0 ? `Ошибки расчета связей времени обработки: ${report.relationErrorCount}` : ''
  ].filter(Boolean).join(' · ');
  return `
    <article class="zabbix-preflight-card ${report.cycles.length > 0 ? 'warn' : report.blockerCount > 0 ? 'error' : 'ok'}">
      <strong>${escapeHtml(layerLabel)}: ${escapeHtml(report.objectNodeCount)} объектов · ${escapeHtml(report.objectEdgeCount)} связей · циклы ${escapeHtml(report.cycles.length)}</strong>
      <span>${escapeHtml(blockerText || 'Блокеров расчета связей нет.')}</span>
      ${renderZabbixPreflightBlockers(report)}
      ${renderZabbixPreflightTransitiveEdges(report)}
      ${renderZabbixPreflightAcyclicEdges(report)}
      ${renderZabbixPreflightUnmaterializedEdges(report)}
      ${report.cycles.length > 0
        ? report.cycles.map((cycle) => renderZabbixCycleReport(cycle)).join('')
        : '<span>Циклические компоненты не найдены.</span>'}
    </article>
  `;
}

function renderZabbixPreflightBlockers(report) {
  const blocks = [];
  if (report.missingTargetEdges?.length > 0) {
    blocks.push(`
      <div class="zabbix-cycle">
        <strong>Потерянные цели связей: ${escapeHtml(report.missingTargetEdges.length)}</strong>
        ${renderZabbixResultLines(relationGraphMissingTargetDetailItems(report.missingTargetEdges))}
      </div>
    `);
  }
  if (report.relationErrorItems?.length > 0) {
    blocks.push(`
      <div class="zabbix-cycle">
        <strong>Ошибки расчета связей времени обработки: ${escapeHtml(report.relationErrorItems.length)}</strong>
        ${renderZabbixResultLines(relationGraphRuntimeErrorDetailItems(report.relationErrorItems))}
      </div>
    `);
  }

  return blocks.join('');
}

function renderZabbixPreflightTransitiveEdges(report) {
  if (!report.transitiveEdges?.length) {
    return '';
  }

  const items = report.transitiveEdges.slice(0, 10)
    .map((item) => `${zabbixObjectEdgeDisplayLabel(item.edge)}: есть транзитивный путь ${zabbixObjectPathDisplayLabel(item.path)}`);
  if (report.transitiveEdges.length > 10) {
    items.push(`Еще ${report.transitiveEdges.length - 10} транзитивных связей скрыто.`);
  }

  return `
    <div class="zabbix-cycle">
      <strong>Транзитивные связи вне циклов: ${escapeHtml(report.transitiveEdges.length)}</strong>
      ${renderZabbixResultLines(items)}
    </div>
  `;
}

function renderZabbixPreflightAcyclicEdges(report) {
  if (!report.acyclicEdges?.length) {
    return '';
  }

  const items = report.acyclicEdges.slice(0, 10)
    .map((edge) => `${zabbixObjectEdgeDisplayLabel(edge)}: проверена, в цикл не входит`);
  if (report.acyclicEdges.length > 10) {
    items.push(`Еще ${report.acyclicEdges.length - 10} связей скрыто.`);
  }
  return `
    <div class="zabbix-cycle">
      <strong>Проверенные связи вне циклов: ${escapeHtml(report.acyclicEdges.length)}</strong>
      ${renderZabbixResultLines(items)}
    </div>
  `;
}

function renderZabbixPreflightUnmaterializedEdges(report) {
  if (!report.unmaterializedEdges?.length) {
    return '';
  }

  const items = report.unmaterializedEdges.slice(0, 8)
    .map((edge) => `${relationGraphEdgeDisplayLabel(edge)} (${edge.roleLabel}): связь настроена, но не породила ребро между объектами`);
  if (report.unmaterializedEdges.length > 8) {
    items.push(`Еще ${report.unmaterializedEdges.length - 8} связей скрыто.`);
  }
  items.push('Возможные причины: regexp/переменные не нашли совпавшие сгенерированные правила, целевое правило не создает целевой объект, либо связь еще не материализована через "Создать/обновить правила по шаблонам и связям".');
  return `
    <div class="zabbix-cycle">
      <strong>Связи, не попавшие в граф объектов: ${escapeHtml(report.unmaterializedEdges.length)}</strong>
      ${renderZabbixResultLines(items)}
    </div>
  `;
}

function renderZabbixCycleReport(cycle) {
  const candidate = cycle.breakCandidate;
  const nodeItems = cycle.nodes.slice(0, 12).map((node, index) => `${index + 1}. ${zabbixObjectDisplayLabel(node)}`);
  if (cycle.nodes.length > 12) {
    nodeItems.push(`Еще ${cycle.nodes.length - 12} объектов скрыто.`);
  }
  const edgeItems = cycle.edges.slice(0, 10).map(zabbixObjectEdgeDisplayLabel);
  if (cycle.edges.length > 10) {
    edgeItems.push(`Еще ${cycle.edges.length - 10} связей скрыто.`);
  }
  const candidateText = candidate
    ? `${zabbixObjectEdgeDisplayLabel(candidate.edge)}. Причины: ${candidate.reasons.join('; ')}.`
    : 'Нет кандидата на авторазрыв.';
  return `
    <div class="zabbix-cycle">
      <strong>Цикл ${escapeHtml(cycle.index)}: ${escapeHtml(cycle.nodes.length)} объектов, ${escapeHtml(cycle.edges.length)} связей</strong>
      <span class="zabbix-result-label">Объекты:</span>
      ${renderZabbixResultLines(nodeItems.length ? nodeItems : ['-'])}
      <span class="zabbix-result-label">Связи:</span>
      ${renderZabbixResultLines(edgeItems.length ? edgeItems : ['-'])}
      <span class="zabbix-result-label">Пробный авторазрыв:</span>
      ${renderZabbixResultLines([candidateText])}
    </div>
  `;
}

function renderZabbixResultLines(items) {
  const lines = (items ?? []).map((item) => String(item ?? '').trim()).filter(Boolean);
  if (lines.length === 0) {
    return '<div class="zabbix-result-lines"><span>-</span></div>';
  }

  return `
    <div class="zabbix-result-lines">
      ${lines.map((item) => `<span>${escapeHtml(item)}</span>`).join('')}
    </div>
  `;
}

function zabbixObjectDisplayLabel(node) {
  const ownerText = (node?.owners ?? []).length > 0
    ? ` [${(node.owners ?? []).slice(0, 2).map((owner) => owner.label || owner.id).join(', ')}${node.owners.length > 2 ? ', ...' : ''}]`
    : '';
  return `${node?.classCode || 'object'}:${node?.lookup || node?.label || node?.key || '-'}${ownerText}`;
}

function zabbixObjectEdgeDisplayLabel(edge) {
  return `${zabbixObjectDisplayLabel(edge.sourceNode)} -> ${zabbixObjectDisplayLabel(edge.targetNode)} (${edge.roleLabel || edge.role || 'relation'})`;
}

function zabbixObjectPathDisplayLabel(edges) {
  const path = edges ?? [];
  if (path.length === 0) {
    return '-';
  }

  const nodes = [path[0].sourceNode, ...path.map((edge) => edge.targetNode)];
  return nodes.map(zabbixObjectDisplayLabel).join(' -> ');
}

async function applyTemplatesToRuleDocuments() {
  try {
    const syncedDrafts = syncDirtyTemplateEditorDraftsBeforeApply();
    state.templateApplyMessage = 'Проверка шаблонов перед материализацией...';
    state.templateApplyError = '';
    renderTemplateApplyView();
    const auditResult = await runTemplateAudit({ syncDrafts: false, render: false });
    if (!auditResult) {
      throw new Error(state.templateAudit.error || 'Проверка шаблонов не выполнена.');
    }

    const blockingErrors = templateAuditBlockingErrors(auditResult);
    if (blockingErrors.length > 0) {
      state.templateApplyMessage = '';
      state.templateApplyError = `Проверка шаблонов нашла блокирующие ошибки: ${blockingErrors.slice(0, 5).join('; ')}${blockingErrors.length > 5 ? `; еще ${blockingErrors.length - 5}` : ''}.`;
      state.templateApplyLastResult = null;
      renderTemplateApplyView();
      renderTemplateAuditView();
      return;
    }

    state.templateApplyMessage = 'Материализация шаблонов и управляемых связей...';
    renderTemplateApplyView();
    const servicePlan = templateMaterializationPlan('service');
    const suppressionPlan = templateMaterializationPlan('suppression');
    const serviceResult = materializeTemplatesForLayer('service', servicePlan);
    const suppressionResult = materializeTemplatesForLayer('suppression', suppressionPlan);
    state.templateApplyLastResult = {
      appliedAt: new Date().toISOString(),
      service: templateApplyResultSummary(serviceResult),
      suppression: templateApplyResultSummary(suppressionResult)
    };
    state.templateApplyMessage = [
      syncedDrafts.length > 0
        ? `Перед применением сохранены измененные шаблоны: ${syncedDrafts.map((item) => `${item.layer}:${item.templateId}`).join(', ')}.`
        : '',
      `${templateReconcileMessage(serviceResult, suppressionResult)}.`,
      templateDeletionPlanMessage(serviceResult, suppressionResult),
      `Сформировано правил: сервис ${serviceResult.generatedRules.length}, подавление ${suppressionResult.generatedRules.length}.`,
      'Далее сохраните правила, шаблоны и связи в папку и перечитайте конфигурацию applier\'ов.',
      'CMDBuild-карточки по этим правилам создаются после новых webhooks классов-источников.'
    ].filter(Boolean).join(' ');
    state.templateApplyError = '';
    renderRulesPreviews();
    renderRuleEditors();
    renderTemplateApplyView();
    renderTemplateAuditView();
    renderConversionConfigSyncView();
  } catch (error) {
    state.templateApplyMessage = '';
    state.templateApplyError = error.message;
    state.templateApplyLastResult = null;
    renderTemplateApplyView();
  }
}

function syncDirtyTemplateEditorDraftsBeforeApply() {
  const synced = [];
  for (const layerKey of ['service', 'suppression']) {
    const config = templateEditorConfig(layerKey);
    const selectedId = state.templateEditorSelected[layerKey];
    if (!config.status || !selectedId) {
      continue;
    }

    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    const storedTemplate = document.templates.find((item) => item.template_id === selectedId);
    if (!storedTemplate) {
      continue;
    }

    const draftTemplate = normalizeTemplate(readTemplateEditorValues(layerKey), layerKey);
    preserveTemplateExternalMetadata(draftTemplate, storedTemplate);
    if (templateFingerprint(storedTemplate) === templateFingerprint(draftTemplate)) {
      continue;
    }

    const result = saveTemplateEditorChange(layerKey, { render: false, status: false });
    synced.push({
      layer: layerKey,
      templateId: result.template?.template_id || selectedId
    });
  }

  return synced;
}

async function ensureTemplateMaterializationSourceCards(layerKey, options = {}) {
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const classCodes = new Set();
  for (const template of document.templates.filter((item) => item.enabled !== false)) {
    const dimension = templatePopulationDimension(template);
    if (!templateDimensionNeedsSourceCards(dimension)) {
      continue;
    }

    let candidates = [];
    try {
      candidates = templateCandidateClasses(template);
    } catch (error) {
      if (!options.safe) {
        throw error;
      }
      continue;
    }

    for (const candidate of candidates) {
      classCodes.add(candidate.code);

      for (const dependencyClass of templatePopulationDimensionDependencyClasses(candidate.code, dimension)) {
        classCodes.add(dependencyClass);
      }
    }
  }

  for (const classCode of classCodes) {
    await loadSourceClassCards(classCode);
  }
}

function templateDimensionNeedsSourceCards(dimension) {
  return templatePopulationDimensionUsesSourceCards(dimension);
}

function sourceClassCardsAvailable(classCode) {
  return sourceClassInstanceItems(classCode).some((item) =>
    String(item.layer).toLowerCase() === 'source' && Array.isArray(item.cards) && item.cards.length > 0);
}

function sourceClassCardsLoaded(classCode) {
  return sourceClassInstanceItems(classCode).some((item) =>
    String(item.layer).toLowerCase() === 'source' && Array.isArray(item.cards));
}

function templatePopulationDimensionDependencyClasses(sourceClass, dimension) {
  const fields = [
    dimension.source_field,
    dimension.condition_field
  ].map((field) => String(field ?? '').trim()).filter(Boolean);
  const dependencies = new Set();
  for (const field of fields) {
    for (const dependencyClass of sourceFieldDependencyClasses(sourceClass, field)) {
      dependencies.add(dependencyClass);
    }
  }

  return [...dependencies];
}

function sourceFieldDependencyClasses(sourceClass, field) {
  const option = sourceFieldOptionForClass(sourceClass, field);
  return sourceFieldDependencyClassesByRule(sourceClass, option?.fieldRule);
}

function sourceFieldDependencyClassesByRule(sourceClass, fieldRule) {
  const path = cmdbPathSegmentsForFieldRule(sourceClass, fieldRule);
  if (path.length === 0 || path.some((segment) => segment.startsWith('{domain:'))) {
    return [];
  }

  const dependencies = [];
  let currentClass = sourceClass;
  for (const segment of path.slice(0, -1)) {
    const attribute = sourceAttributeByCode(currentClass, segment);
    const targetClass = referenceTargetClass(attribute, currentClass);
    if (!targetClass) {
      break;
    }

    dependencies.push(targetClass);
    currentClass = targetClass;
  }

  return dependencies;
}

function templateApplyResultSummary(result) {
  const reconcile = result?.reconcile ?? {};
  return {
    templates: result?.templates?.length ?? 0,
    candidates: result?.candidateCount ?? 0,
    relations: relationReconcileSummary(reconcile.relations),
    generatedRules: result?.generatedRules?.map((rule) => ({
      rule_id: rule.rule_id || '',
      source_class_code: ruleSourceClassCode(rule),
      target_class_code: ruleTargetClassCode(rule),
      managed_key: generatedRuleManagedKeyFromRule(rule, rule.layer || ''),
      relation_count: runtimeRelationsFromRule(rule).length
    })) ?? [],
    reconcile: {
      created: reconcile.created ?? 0,
      updated: reconcile.updated ?? 0,
      unchanged: reconcile.unchanged ?? 0,
      removed: reconcile.removed ?? 0
    }
  };
}

function materializeTemplatesForLayer(layerKey, plan) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    throw new Error(parsed.error);
  }

  const document = parsed.document;
  const reconcile = reconcileGeneratedRulesForLayer(document, layerKey, plan);
  for (const rule of reconcile.generatedRules) {
    const fields = sourceFieldsForRule(ruleValuesFromRule(rule))
      .concat(sourceFieldsFromMappings(rule.target?.initial_user_values ?? {}));
    ensureRuleDocumentSource(document, ruleSourceClassCode(rule), fields);
  }

  appendTemplateApplicationSnapshot(document, layerKey, plan, reconcile);
  writeRuleDocument(layerKey, document);
  return { ...plan, reconcile };
}

function reconcileGeneratedRulesForLayer(document, layerKey, plan) {
  const desiredRules = plan.generatedRules.map((rule) => enrichGeneratedRuleOwnership(rule, layerKey));
  const desiredByKey = new Map(desiredRules.map((rule) => [generatedRuleManagedKeyFromRule(rule, layerKey), rule]));
  const templatesById = new Map((plan.templates ?? [])
    .map((item) => item.template)
    .filter(Boolean)
    .map((template) => [template.template_id, template]));
  const existingGenerated = (document.rules ?? [])
    .filter((rule) => rule.generated_from_template);
  const existingByKey = new Map();
  for (const rule of existingGenerated) {
    const key = generatedRuleManagedKeyFromRule(rule, layerKey);
    if (key && !existingByKey.has(key)) {
      existingByKey.set(key, enrichGeneratedRuleOwnership(rule, layerKey));
    }
  }

  const matchedKeys = new Set();
  const nextGeneratedRules = [];
  const summary = {
    created: 0,
    updated: 0,
    unchanged: 0,
    removed: 0,
    relations: emptyRelationReconcileSummary()
  };

  for (const desiredRule of desiredRules) {
    const key = generatedRuleManagedKeyFromRule(desiredRule, layerKey);
    const existingRule = existingByKey.get(key);
    if (!existingRule) {
      summary.created += 1;
      mergeRelationReconcileSummary(summary.relations, {
        created: runtimeRelationsFromRule(desiredRule).length
      });
      nextGeneratedRules.push(desiredRule);
      continue;
    }

    matchedKeys.add(key);
    mergeRelationReconcileSummary(summary.relations, runtimeRelationReconcileSummary(existingRule, desiredRule));
    const desiredFingerprint = generatedRuleArtifactFingerprint(desiredRule);
    const existingFingerprint = String(existingRule.template_generation?.artifact_fingerprint ?? '')
      || generatedRuleArtifactFingerprint(existingRule);
    const desiredMetadataFingerprint = generatedRuleTemplateMetadataFingerprint(desiredRule);
    const existingMetadataFingerprint = generatedRuleTemplateMetadataFingerprint(existingRule);
    if (desiredFingerprint === existingFingerprint && desiredMetadataFingerprint === existingMetadataFingerprint) {
      summary.unchanged += 1;
      nextGeneratedRules.push(existingRule);
      continue;
    }

    summary.updated += 1;
    desiredRule.template_generation.previous_artifact_fingerprint = existingFingerprint;
    desiredRule.template_generation.previous_template_version = existingRule.template_generation?.template_version ?? existingRule.template_version ?? '';
    nextGeneratedRules.push(desiredRule);
  }

  const removedRules = existingGenerated.filter((rule) =>
    !matchedKeys.has(generatedRuleManagedKeyFromRule(rule, layerKey))
    && !desiredByKey.has(generatedRuleManagedKeyFromRule(rule, layerKey)));
  if (removedRules.length > 0) {
    appendTemplateDeletionPlansForRemovedRules(document, layerKey, removedRules, 'template_reconcile_removed', templatesById);
    mergeRelationReconcileSummary(summary.relations, {
      removed: removedRules.reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0)
    });
  }

  summary.removed = removedRules.length;
  summary.relations.total = nextGeneratedRules.reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0);
  const manualRules = (document.rules ?? []).filter((rule) => !rule.generated_from_template);
  mergeRelationReconcileSummary(
    summary.relations,
    reconcileManualRuleRuntimeRelations(layerKey, manualRules, nextGeneratedRules, plan.errors ?? []));
  document.rules = manualRules.concat(nextGeneratedRules.sort(compareGeneratedRules));
  return {
    ...summary,
    generatedRules: nextGeneratedRules,
    removedRules
  };
}

function reconcileManualRuleRuntimeRelations(layerKey, manualRules, generatedRules, errors = [], options = {}) {
  const rulesByTemplate = new Map();
  for (const rule of generatedRules ?? []) {
    const templateId = ruleTemplateId(rule);
    if (!templateId) {
      continue;
    }

    if (!rulesByTemplate.has(templateId)) {
      rulesByTemplate.set(templateId, []);
    }

    rulesByTemplate.get(templateId).push(rule);
  }

  const rulesById = new Map([...(manualRules ?? []), ...(generatedRules ?? [])]
    .filter((rule) => rule?.rule_id)
    .map((rule) => [rule.rule_id, rule]));
  const summary = emptyRelationReconcileSummary();
  for (const sourceRule of manualRules ?? []) {
    const managedRelations = (sourceRule.managed_relations ?? [])
      .filter((relation) =>
        (relation.kind === 'template' && relation.target_template_id)
        || (relation.kind === 'rule' && relation.target_rule_id));
    if (managedRelations.length === 0) {
      continue;
    }

    const existingRelations = Array.isArray(sourceRule.relations) ? sourceRule.relations : [];
    const existingManagedRelations = existingRelations.filter((relation) =>
      String(relation?.managed_relation_key ?? '').trim());
    const unmanagedRelations = existingRelations.filter((relation) =>
      !String(relation?.managed_relation_key ?? '').trim());
    const desiredManagedRelations = [];

    for (const relation of managedRelations) {
      const targetRules = relation.kind === 'template'
        ? rulesByTemplate.get(relation.target_template_id) ?? []
        : [rulesById.get(relation.target_rule_id)].filter(Boolean);
      if (targetRules.length === 0) {
        const missingTarget = relation.kind === 'template'
          ? `целевой шаблон ${relation.target_template_id} не породил правил для связи правило-шаблон`
          : `целевое правило ${relation.target_rule_id} для связи правило-правило не найдено`;
        errors.push(`${sourceRule.rule_id || sourceRule.name}: ${missingTarget}`);
        continue;
      }

      for (const targetRule of targetRules) {
        if (sourceRule.rule_id && sourceRule.rule_id === targetRule.rule_id) {
          continue;
        }

        if (!templateManagedRelationMatchesRulePair(sourceRule, targetRule, relation)) {
          continue;
        }

        try {
          desiredManagedRelations.push(runtimeRelationFromTemplateRelation(layerKey, sourceRule, targetRule, relation));
        } catch (error) {
          const message = `${sourceRule.rule_id || sourceRule.name}: ${error.message}`;
          errors.push(message);
          if (!options.safe) {
            throw new Error(message);
          }
        }
      }
    }

    mergeRelationReconcileSummary(summary, runtimeRelationReconcileSummary(
      { relations: existingManagedRelations },
      { relations: desiredManagedRelations }));
    sourceRule.relations = mergeRuntimeRelations(unmanagedRelations, desiredManagedRelations);
  }

  return summary;
}

function mergeRuntimeRelations(unmanagedRelations, managedRelations) {
  const byKey = new Map();
  for (const relation of unmanagedRelations ?? []) {
    byKey.set(runtimeRelationKey(relation), relation);
  }

  for (const relation of managedRelations ?? []) {
    byKey.set(runtimeRelationKey(relation), relation);
  }

  return [...byKey.values()];
}

function enrichGeneratedRuleOwnership(rule, layerKey) {
  const enriched = cloneJson(rule);
  const managedKey = generatedRuleManagedKeyFromRule(enriched, layerKey);
  const artifactFingerprint = generatedRuleArtifactFingerprint(enriched);
  enriched.template_generation = enriched.template_generation && typeof enriched.template_generation === 'object' && !Array.isArray(enriched.template_generation)
    ? enriched.template_generation
    : {};
  enriched.template_generation.managed_key = managedKey;
  enriched.template_generation.artifact_kind = enriched.template_generation.artifact_kind || 'rule';
  enriched.template_generation.artifact_fingerprint = artifactFingerprint;
  if (enriched.target && typeof enriched.target === 'object' && !Array.isArray(enriched.target)) {
    enriched.target.created_by_template = enriched.target.created_by_template && typeof enriched.target.created_by_template === 'object' && !Array.isArray(enriched.target.created_by_template)
      ? enriched.target.created_by_template
      : {};
    enriched.target.created_by_template.managed_key = managedKey;
    enriched.target.created_by_template.artifact_fingerprint = artifactFingerprint;
    enriched.target.created_by_template.reconcile_policy = 'managed_key_fingerprint';
  }

  return enriched;
}

function compareGeneratedRules(left, right) {
  return (Number(left.priority || 100) - Number(right.priority || 100))
    || String(left.generated_from_template || '').localeCompare(String(right.generated_from_template || ''), undefined, { sensitivity: 'base' })
    || String(ruleSourceClassCode(left)).localeCompare(String(ruleSourceClassCode(right)), undefined, { sensitivity: 'base' })
    || String(left.rule_id || '').localeCompare(String(right.rule_id || ''), undefined, { sensitivity: 'base' });
}

function appendTemplateDeletionPlansForRemovedRules(document, layerKey, rules, reason, templatesById = new Map()) {
  const byTemplate = new Map();
  for (const rule of rules) {
    const templateId = ruleTemplateId(rule);
    if (!templateId) {
      continue;
    }

    if (!byTemplate.has(templateId)) {
      byTemplate.set(templateId, []);
    }

    byTemplate.get(templateId).push(rule);
  }

  for (const [templateId, templateRules] of byTemplate.entries()) {
    const firstRule = templateRules[0] ?? {};
    const currentTemplate = templatesById.get(templateId);
    appendTemplateDeletionPlan(document, layerKey, {
      template_id: templateId,
      name: currentTemplate?.name || firstRule.template_generation?.template_name || templateId,
      version: currentTemplate?.version || firstRule.template_generation?.template_version || firstRule.template_version || '',
      source_class_regex: currentTemplate?.source_class_regex || firstRule.template_generation?.template_source_regex || ''
    }, templateRules, reason);
  }
}

function appendTemplateApplicationSnapshot(document, layerKey, plan, reconcile) {
  document.templateApplications = Array.isArray(document.templateApplications)
    ? document.templateApplications
    : [];
  const appliedAt = new Date().toISOString();
  const applicationId = normalizeRuleId(`${layerKey}-${appliedAt}-${stableHash(reconcile)}`);
  document.templateApplications.push({
    application_id: applicationId,
    layer: layerKey,
    applied_at: appliedAt,
    reconcile: {
      created: reconcile.created,
      updated: reconcile.updated,
      unchanged: reconcile.unchanged,
      removed: reconcile.removed,
      relations: relationReconcileSummary(reconcile.relations)
    },
    templates: plan.templates.map((item) => ({
      template_id: item.template.template_id,
      template_name: item.template.name || item.template.template_id,
      template_version: Number(item.template.version || 1),
      content_hash: templateFingerprint(item.template),
      candidate_count: item.candidates.length,
      candidates: item.candidates.map((candidate) => candidate.code || '').filter(Boolean),
      generated_rules: item.rules.map((rule) => ({
        managed_key: generatedRuleManagedKeyFromRule(rule, layerKey),
        rule_id: rule.rule_id || '',
        artifact_fingerprint: generatedRuleArtifactFingerprint(rule),
        source_class_code: ruleSourceClassCode(rule),
        dimension_key: rule.template_generation?.dimension_key || '',
        target_class_code: ruleTargetClassCode(rule),
        relations: runtimeRelationsFromRule(rule).map((relation) => ({
          domain_code: relation.domain_code || '',
          target_class_code: relation.target_class_code || '',
          target_lookup: relation.target_lookup || ''
        }))
      }))
    }))
  });
  document.templateApplications = document.templateApplications.slice(-TEMPLATE_APPLICATION_HISTORY_LIMIT);
}

function templateReconcileMessage(serviceResult, suppressionResult) {
  const service = serviceResult.reconcile ?? {};
  const suppression = suppressionResult.reconcile ?? {};
  return [
    `сервис: создано ${service.created ?? 0}, обновлено ${service.updated ?? 0}, без изменений ${service.unchanged ?? 0}, снято ${service.removed ?? 0}; ${relationReconcileText(service.relations)}`,
    `подавление: создано ${suppression.created ?? 0}, обновлено ${suppression.updated ?? 0}, без изменений ${suppression.unchanged ?? 0}, снято ${suppression.removed ?? 0}; ${relationReconcileText(suppression.relations)}`
  ].join('; ');
}

function templateDeletionPlanMessage(serviceResult, suppressionResult) {
  const removed = Number(serviceResult.reconcile?.removed ?? 0) + Number(suppressionResult.reconcile?.removed ?? 0);
  if (removed === 0) {
    return '';
  }

  return `Для ${removed} снятых сгенерированных правил создан ожидающий план удаления: он фиксирует старые связи/объекты для очистки после изменения набора классов-источников.`;
}

function templateGeneratedRelationCount(plan) {
  return (plan?.generatedRules ?? [])
    .reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0);
}

function runtimeRelationsFromRule(rule) {
  return Array.isArray(rule?.relations)
    ? rule.relations
        .map((relation) => normalizeRuntimeRelationForCompare(relation))
        .filter((relation) => relation.domain_code && relation.target_class_code && relation.target_lookup)
    : [];
}

function emptyRelationReconcileSummary() {
  return {
    created: 0,
    updated: 0,
    unchanged: 0,
    removed: 0,
    total: 0
  };
}

function relationReconcileSummary(summary) {
  const next = emptyRelationReconcileSummary();
  if (!summary || typeof summary !== 'object' || Array.isArray(summary)) {
    return next;
  }

  next.created = Number(summary.created ?? 0) || 0;
  next.updated = Number(summary.updated ?? 0) || 0;
  next.unchanged = Number(summary.unchanged ?? 0) || 0;
  next.removed = Number(summary.removed ?? 0) || 0;
  next.total = Number(summary.total ?? (next.created + next.updated + next.unchanged)) || 0;
  return next;
}

function mergeRelationReconcileSummary(target, source) {
  const normalizedTarget = relationReconcileSummary(target);
  const normalizedSource = relationReconcileSummary(source);
  target.created = normalizedTarget.created + normalizedSource.created;
  target.updated = normalizedTarget.updated + normalizedSource.updated;
  target.unchanged = normalizedTarget.unchanged + normalizedSource.unchanged;
  target.removed = normalizedTarget.removed + normalizedSource.removed;
  target.total = normalizedTarget.total + normalizedSource.total;
  return target;
}

function runtimeRelationReconcileSummary(existingRule, desiredRule) {
  const summary = emptyRelationReconcileSummary();
  const existingByKey = new Map(runtimeRelationsFromRule(existingRule)
    .map((relation) => [runtimeRelationKey(relation), relation]));
  const desiredByKey = new Map(runtimeRelationsFromRule(desiredRule)
    .map((relation) => [runtimeRelationKey(relation), relation]));

  for (const [key, desiredRelation] of desiredByKey.entries()) {
    const existingRelation = existingByKey.get(key);
    if (!existingRelation) {
      summary.created += 1;
      continue;
    }

    if (runtimeRelationFingerprint(existingRelation) === runtimeRelationFingerprint(desiredRelation)) {
      summary.unchanged += 1;
    } else {
      summary.updated += 1;
    }
  }

  for (const key of existingByKey.keys()) {
    if (!desiredByKey.has(key)) {
      summary.removed += 1;
    }
  }

  summary.total = desiredByKey.size;
  return summary;
}

function relationReconcileText(summary) {
  const relationSummary = relationReconcileSummary(summary);
  return `связи: создано ${relationSummary.created}, обновлено ${relationSummary.updated}, без изменений ${relationSummary.unchanged}, снято ${relationSummary.removed}, всего ${relationSummary.total}`;
}

function runtimeRelationKey(relation) {
  const normalized = normalizeRuntimeRelationForCompare(relation);
  return stableHash({
    domain_code: normalized.domain_code,
    target_class_code: normalized.target_class_code,
    target_lookup: normalized.target_lookup
  });
}

function runtimeRelationFingerprint(relation) {
  return stableHash(normalizeRuntimeRelationForCompare(relation));
}

function normalizeRuntimeRelationForCompare(relation) {
  const normalized = relation && typeof relation === 'object' && !Array.isArray(relation)
    ? relation
    : {};
  return {
    domain_code: String(normalized.domain_code ?? '').trim(),
    target_class_code: String(normalized.target_class_code ?? '').trim(),
    target_lookup: String(normalized.target_lookup ?? '').trim(),
    attribute_mappings: normalizeStringMapForCompare(normalized.attribute_mappings)
  };
}

function normalizeStringMapForCompare(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return {};
  }

  return Object.fromEntries(Object.entries(value)
    .map(([key, item]) => [String(key).trim(), String(item ?? '').trim()])
    .filter(([key]) => key)
    .sort(([left], [right]) => left.localeCompare(right, undefined, { sensitivity: 'base' })));
}

function linkRelationViewContext(view) {
  const config = LINK_RELATION_VIEW_CONFIG[String(view ?? '')];
  return config ? { ...config } : null;
}

function linkRelationEditorConfig() {
  return {
    title: document.querySelector('#relationManagementTitle'),
    lead: document.querySelector('#relationManagementLead'),
    purpose: document.querySelector('#relationManagementPurpose'),
    sourceLabel: document.querySelector('#relationSourceLabel'),
    source: document.querySelector('#relationSourceSelect'),
    targetLabel: document.querySelector('#relationTargetLabel'),
    target: document.querySelector('#relationTargetSelect'),
    role: document.querySelector('#relationRoleSelect'),
    sourceVariable: document.querySelector('#relationSourceVariableSelect'),
    targetVariable: document.querySelector('#relationTargetVariableSelect'),
    sourceMatchRegex: document.querySelector('#relationSourceMatchRegexInput'),
    targetMatchRegex: document.querySelector('#relationTargetMatchRegexInput'),
    sourceFilterList: document.querySelector('#relationSourceFilterList'),
    targetFilterList: document.querySelector('#relationTargetFilterList'),
    ruleRuleFilterBar: document.querySelector('#relationRuleRuleFilterBar'),
    hideTemplateLinks: document.querySelector('#relationHideTemplateLinks'),
    description: document.querySelector('#relationDescriptionInput'),
    status: document.querySelector('#relationManagementStatus'),
    list: document.querySelector('#relationManagementList')
  };
}

function linkRelationKindConfig(kind, layerKey) {
  const layerTitle = layerKey === 'service' ? 'Сервис' : 'Каскадное подавление';
  if (kind === 'template_rule') {
    return {
      title: `${layerTitle}: связи шаблон-правило`,
      lead: 'Связь фиксирует направленную зависимость между шаблоном и конкретным правилом текущего слоя.',
      purpose: [
        'Нужна, когда один конец связи задается разверткой шаблона, а второй является уже выбранным правилом.',
        'Источник и назначение могут быть шаблоном или правилом; выберите ровно один шаблон и одно правило, чтобы направление связи было однозначным.',
        'Если шаблон выбран источником, фильтр ограничивает сгенерированные правила шаблона-источника. Если шаблон выбран назначением, фильтр ограничивает сгенерированные правила целевого шаблона.'
      ],
      sourceType: 'mixed',
      targetType: 'mixed',
      relationKind: 'mixed',
      allowedPairs: ['template:rule', 'rule:template'],
      sourceLabel: 'Источник связи',
      targetLabel: 'Назначение связи'
    };
  }

  if (kind === 'rule_rule') {
    return {
      title: `${layerTitle}: связи правило-правило`,
      lead: 'Связь фиксирует зависимость или порядок между двумя правилами текущего слоя.',
      purpose: [
        'Нужна для прямой зависимости между двумя конкретными правилами.',
        'Связь правило-правило однозначная: один выбранный источник связан с одним выбранным целевым правилом.',
        'Галочка "Не показывать связи шаблонов" скрывает производные связи правило-правило и сгенерированные правила шаблонов в списках выбора. Снимите галку, чтобы работать с материализованными правилами шаблонов.',
        'Переменные и regex здесь не используются.'
      ],
      sourceType: 'rule',
      targetType: 'rule',
      relationKind: 'rule',
      sourceLabel: 'Правило-источник',
      targetLabel: 'Целевое правило'
    };
  }

  return {
    title: `${layerTitle}: связи шаблон-шаблон`,
    lead: 'Связь фиксирует зависимость между шаблонами и входит в fingerprint шаблона-источника.',
    purpose: [
      'Нужна для каскада между объектами, которые будут созданы разными шаблонами одного слоя.',
      'Перед сопоставлением можно отфильтровать сгенерированные правила левого и правого шаблона строками Включить/Исключить по переменным шаблона.',
      'Сопоставление выполняется по переменным шаблона-источника и целевого шаблона; при пустом regex применяется точное совпадение значений.',
      'Сервисные шаблоны связываются только с сервисными, шаблоны подавления только с шаблонами подавления.'
    ],
    sourceType: 'template',
    targetType: 'template',
    relationKind: 'template',
    sourceLabel: 'Шаблон-источник',
    targetLabel: 'Целевой шаблон'
  };
}

function renderLinkRelationEditor() {
  const config = linkRelationEditorConfig();
  if (!config.title || !config.source || !config.target || !config.list) {
    return;
  }

  const context = state.linkRelationContext ?? { layer: 'service', kind: 'template_template' };
  const kindConfig = linkRelationKindConfig(context.kind, context.layer);
  config.title.textContent = kindConfig.title;
  config.lead.textContent = kindConfig.lead;
  if (config.purpose) {
    config.purpose.innerHTML = kindConfig.purpose.map((item) => `<li>${escapeHtml(item)}</li>`).join('');
  }
  config.sourceLabel.textContent = kindConfig.sourceLabel;
  config.targetLabel.textContent = kindConfig.targetLabel;
  const currentRole = config.role?.value || 'uses';
  setSelectOptions(config.role, linkRelationRoleOptions(context.layer), currentRole);
  const ruleRuleMode = kindConfig.sourceType === 'rule' && kindConfig.targetType === 'rule';
  config.ruleRuleFilterBar?.classList.toggle('hidden', !ruleRuleMode);
  if (config.hideTemplateLinks) {
    config.hideTemplateLinks.checked = state.linkRelationHideTemplateLinks !== false;
  }

  const currentSource = config.source.value;
  const currentTarget = config.target.value;
  setSelectOptions(config.source, linkRelationEntityOptions(context.layer, kindConfig.sourceType, kindConfig), currentSource);
  setSelectOptions(config.target, linkRelationEntityOptions(context.layer, kindConfig.targetType, kindConfig), currentTarget);
  ensureLinkRelationMixedPairSelection(config, kindConfig);
  renderLinkRelationVariableControls(context, kindConfig);
  renderLinkRelationStatus();
  renderLinkRelationList(context.layer, context.kind, kindConfig);
}

function ensureLinkRelationMixedPairSelection(config, kindConfig) {
  if (!kindConfig.allowedPairs?.length || !config.source || !config.target) {
    return;
  }

  const source = selectedLinkRelationEntity(config.source.value, kindConfig.sourceType);
  const target = selectedLinkRelationEntity(config.target.value, kindConfig.targetType);
  if (kindConfig.allowedPairs.includes(`${source.type}:${target.type}`)) {
    return;
  }

  const preferredTargetType = source.type === 'template' ? 'rule' : 'template';
  const targetOption = [...config.target.options]
    .find((option) => !option.disabled && selectedLinkRelationEntity(option.value, kindConfig.targetType).type === preferredTargetType);
  if (targetOption) {
    config.target.value = targetOption.value;
    return;
  }

  const sourceOption = [...config.source.options]
    .find((option) => !option.disabled && selectedLinkRelationEntity(option.value, kindConfig.sourceType).type === 'template');
  const fallbackTargetOption = [...config.target.options]
    .find((option) => !option.disabled && selectedLinkRelationEntity(option.value, kindConfig.targetType).type === 'rule');
  if (sourceOption && fallbackTargetOption) {
    config.source.value = sourceOption.value;
    config.target.value = fallbackTargetOption.value;
  }
}

function renderLinkRelationVariableControls(
  context = state.linkRelationContext ?? { layer: 'service', kind: 'template_template' },
  kindConfig = linkRelationKindConfig(context.kind, context.layer)) {
  const config = linkRelationEditorConfig();
  const sourceField = config.sourceVariable?.closest('[data-link-match-field]');
  const targetField = config.targetVariable?.closest('[data-link-match-field]');
  const sourceRegexField = config.sourceMatchRegex?.closest('[data-link-match-field]');
  const targetRegexField = config.targetMatchRegex?.closest('[data-link-match-field]');
  const sourceFilterField = config.sourceFilterList?.closest('[data-link-match-field]');
  const targetFilterField = config.targetFilterList?.closest('[data-link-match-field]');
  const sourceEntity = selectedLinkRelationEntity(config.source?.value, kindConfig.sourceType);
  const targetEntity = selectedLinkRelationEntity(config.target?.value, kindConfig.targetType);
  const sourceType = sourceEntity.type || kindConfig.sourceType;
  const targetType = targetEntity.type || kindConfig.targetType;
  const sourceIsTemplate = sourceType === 'template';
  const sourceIsRule = sourceType === 'rule';
  const targetIsTemplate = targetType === 'template';
  const targetIsRule = targetType === 'rule';
  const showSourceFilters = sourceIsTemplate && (targetIsTemplate || targetIsRule);
  const showTargetFilters = targetIsTemplate && (sourceIsTemplate || sourceIsRule);

  sourceField?.classList.toggle('hidden', !(sourceIsTemplate && targetIsTemplate));
  targetField?.classList.toggle('hidden', !targetIsTemplate);
  sourceRegexField?.classList.toggle('hidden', !(sourceIsTemplate && targetIsTemplate));
  targetRegexField?.classList.toggle('hidden', !(sourceIsTemplate && targetIsTemplate));
  sourceFilterField?.classList.toggle('hidden', !showSourceFilters);
  targetFilterField?.classList.toggle('hidden', !showTargetFilters);

  if (sourceIsTemplate && config.sourceVariable) {
    setSelectOptions(
      config.sourceVariable,
      templateVariableOptions(context.layer, sourceEntity.id),
      config.sourceVariable.value);
  }

  if (targetIsTemplate && config.targetVariable) {
    setSelectOptions(
      config.targetVariable,
      templateVariableOptions(context.layer, targetEntity.id),
      config.targetVariable.value);
  }

  if (config.sourceMatchRegex) {
    config.sourceMatchRegex.placeholder = targetIsRule
      ? 'пусто = все правила шаблона'
      : targetIsTemplate
        ? 'пусто = значение источника целиком'
        : '';
  }

  if (config.targetMatchRegex) {
    config.targetMatchRegex.placeholder = targetIsTemplate
      ? 'пусто = значение цели целиком'
      : '';
  }

  if (showSourceFilters) {
    renderLinkRelationFilterList(context, 'source');
  } else if (config.sourceFilterList) {
    config.sourceFilterList.innerHTML = '';
  }

  if (showTargetFilters) {
    renderLinkRelationFilterList(context, 'target');
  } else if (config.targetFilterList) {
    config.targetFilterList.innerHTML = '';
  }
}

function renderLinkRelationFilterList(context = state.linkRelationContext ?? { layer: 'service', kind: 'template_rule' }, side = 'source') {
  const config = linkRelationEditorConfig();
  const kindConfig = linkRelationKindConfig(context.kind, context.layer);
  const list = linkRelationFilterListElement(config, side);
  if (!list) {
    return;
  }

  const templateId = linkRelationSelectedTemplateId(config, kindConfig, side);
  const rows = linkRelationFilterRowsFromDom(list, side)
    .filter((row) => row.variable || row.regex);
  const options = linkRelationFilterVariableOptions(context.layer, templateId);
  list.innerHTML = linkRelationFilterListTemplate(
    rows.length > 0 ? rows : [{ mode: 'include', variable: '', regex: '' }],
    options,
    side);
}

function linkRelationFilterListElement(config, side) {
  return side === 'target'
    ? config.targetFilterList
    : config.sourceFilterList;
}

function linkRelationFilterListTemplate(rows, variableOptions, side = 'source') {
  return `
    <div class="selection-filter-header" role="row">
      <span>Режим</span>
      <span>Переменная</span>
      <span>Regexp</span>
    </div>
    ${rows.map((row) => linkRelationFilterRowTemplate(row, variableOptions, side)).join('')}
  `;
}

function linkRelationFilterRowTemplate(row, variableOptions, side = 'source') {
  const prefix = side === 'target' ? 'target' : 'source';
  const mode = row.mode === 'exclude' ? 'exclude' : 'include';
  return `
    <div class="selection-filter-row" data-link-${prefix}-filter-row>
      <select data-link-${prefix}-filter-mode aria-label="режим">
        <option value="include" ${mode === 'include' ? 'selected' : ''}>Включить</option>
        <option value="exclude" ${mode === 'exclude' ? 'selected' : ''}>Исключить</option>
      </select>
      <select data-link-${prefix}-filter-variable aria-label="переменная шаблона">
        ${linkRelationFilterVariableOptionsTemplate(variableOptions, row.variable || '', side)}
      </select>
      <input data-link-${prefix}-filter-regex value="${escapeHtml(row.regex || '')}" placeholder="(?i)^City(04|14)$" aria-label="регулярное выражение" autocomplete="off">
    </div>
  `;
}

function linkRelationFilterVariableOptionsTemplate(variableOptions, selectedVariable, side = 'source') {
  const options = variableOptions ?? [];
  const hasSelected = options.some((option) => canonicalToken(option.value) === canonicalToken(selectedVariable));
  const selectedFallback = selectedVariable && !hasSelected
    ? `<option value="${escapeHtml(selectedVariable)}" selected>${escapeHtml(selectedVariable)} (не найден)</option>`
    : '';
  const placeholderLabel = options.length === 0
    ? side === 'target' ? 'Сначала выберите целевой шаблон' : 'Сначала выберите шаблон-источник'
    : 'Выберите переменную';
  return `
    <option value="">${placeholderLabel}</option>
    ${selectedFallback}
    ${options.map((option) => {
      const selected = canonicalToken(option.value) === canonicalToken(selectedVariable) ? 'selected' : '';
      return `<option value="${escapeHtml(option.value)}" title="${escapeHtml(option.label || option.value)}" ${selected}>${escapeHtml(option.label || option.value)}</option>`;
    }).join('')}
  `;
}

function linkRelationFilterVariableOptions(layerKey, templateId) {
  return linkRelationTemplateVariableOptions(findLayerTemplate(layerKey, String(templateId ?? '').trim()))
    .sort(compareLinkRelationEntityLabels);
}

function handleLinkRelationFilterInput(target) {
  if (target?.matches?.('[data-link-source-filter-mode], [data-link-source-filter-variable], [data-link-source-filter-regex]')) {
    ensureLinkRelationFilterDraftRow('source');
  }

  if (target?.matches?.('[data-link-target-filter-mode], [data-link-target-filter-variable], [data-link-target-filter-regex]')) {
    ensureLinkRelationFilterDraftRow('target');
  }
}

function ensureLinkRelationFilterDraftRow(side = 'source') {
  const context = state.linkRelationContext ?? { layer: 'service', kind: 'template_rule' };
  const kindConfig = linkRelationKindConfig(context.kind, context.layer);
  const config = linkRelationEditorConfig();
  const sourceEntity = selectedLinkRelationEntity(config.source?.value, kindConfig.sourceType);
  const targetEntity = selectedLinkRelationEntity(config.target?.value, kindConfig.targetType);
  const sourceType = sourceEntity.type || kindConfig.sourceType;
  const targetType = targetEntity.type || kindConfig.targetType;
  const sourceIsTemplate = sourceType === 'template';
  const sourceIsRule = sourceType === 'rule';
  const targetIsTemplate = targetType === 'template';
  const targetIsRule = targetType === 'rule';
  if (side === 'target' && !(sourceIsTemplate && targetIsTemplate)) {
    if (!(sourceIsRule && targetIsTemplate)) {
      return;
    }
  }

  if (side !== 'target' && !(sourceIsTemplate && (targetIsTemplate || targetIsRule))) {
    return;
  }

  const list = linkRelationFilterListElement(config, side);
  if (!list) {
    return;
  }

  const prefix = side === 'target' ? 'target' : 'source';
  const rows = [...list.querySelectorAll(`[data-link-${prefix}-filter-row]`)];
  const lastRow = rows.at(-1);
  if (!lastRow || linkRelationFilterDomRowValues(lastRow, side).variable || linkRelationFilterDomRowValues(lastRow, side).regex) {
    const templateId = linkRelationSelectedTemplateId(config, kindConfig, side);
    list.insertAdjacentHTML(
      'beforeend',
      linkRelationFilterRowTemplate(
        { mode: 'include', variable: '', regex: '' },
        linkRelationFilterVariableOptions(context.layer, templateId),
        side));
  }
}

function renderLinkRelationStatus() {
  const config = linkRelationEditorConfig();
  if (!config.status) {
    return;
  }

  const context = state.linkRelationContext ?? { layer: 'service', kind: 'template_template' };
  const kindConfig = linkRelationKindConfig(context.kind, context.layer);
  const rows = linkRelationRows(context.layer, context.kind, kindConfig);
  const hiddenRows = hiddenTemplateRelationRowsCount(context.layer, context.kind, kindConfig);
  const status = state.linkRelationStatus ?? { message: '', type: '' };
  const hiddenText = hiddenRows > 0
    ? ` Скрыто шаблонных связей: ${hiddenRows}.`
    : '';
  config.status.textContent = status.message || `В наборе ${rows.length} связей.${hiddenText}`;
  config.status.classList.toggle('error', status.type === 'error');
}

function renderLinkRelationList(layerKey, kind, kindConfig = linkRelationKindConfig(kind, layerKey)) {
  const config = linkRelationEditorConfig();
  if (!config.list) {
    return;
  }

  const rows = linkRelationRows(layerKey, kind, kindConfig);
  if (rows.length === 0) {
    config.list.innerHTML = '<p class="status-line">Связей этого типа пока нет.</p>';
    return;
  }

  config.list.innerHTML = rows.map((row) => `
    <div class="relation-row">
      <div class="relation-row-main">
        <strong>${escapeHtml(row.sourceLabel)} → ${escapeHtml(row.targetLabel)}</strong>
        <span>Направление: ${escapeHtml(linkRelationEntityTypeLabel(row.sourceType || kindConfig.sourceType))} → ${escapeHtml(linkRelationEntityTypeLabel(row.targetType || kindConfig.targetType))}</span>
        <span>${escapeHtml(linkRelationRoleLabel(row.relation.relation_role))} · ${escapeHtml(row.relation.managed_key || '')}</span>
        ${row.matchLabel ? `<span>${escapeHtml(row.matchLabel)}</span>` : ''}
        ${row.description ? `<span>${escapeHtml(row.description)}</span>` : ''}
      </div>
      <button
        class="secondary-button compact-button"
        type="button"
        data-link-delete
        data-layer="${escapeHtml(layerKey)}"
        data-kind="${escapeHtml(kind)}"
        data-source-type="${escapeHtml(row.sourceType || kindConfig.sourceType)}"
        data-source-id="${escapeHtml(row.sourceId)}"
        data-managed-key="${escapeHtml(row.relation.managed_key || '')}"
      >Удалить</button>
    </div>
  `).join('');
}

function linkRelationEntityOptions(layerKey, type, kindConfig = null) {
  if (type === 'mixed') {
    const templates = linkRelationEntities(layerKey, 'template')
      .map((item) => ({
        value: linkRelationEntityValue('template', item.id),
        label: `Шаблон: ${item.label}`
      }));
    const rules = linkRelationEntities(layerKey, 'rule', { hideGeneratedTemplateRules: true })
      .map((item) => ({
        value: linkRelationEntityValue('rule', item.id),
        label: `Правило: ${item.label}`
      }));
    const items = templates.concat(rules).sort(compareLinkRelationEntityLabels);
    return items.length > 0
      ? items
      : [{ value: '', label: 'Нет шаблонов или правил', disabled: true }];
  }

  const hideGeneratedTemplateRules = shouldHideGeneratedTemplateRulesInRelationEditor(kindConfig, type);
  const items = linkRelationEntities(layerKey, type, { hideGeneratedTemplateRules });
  if (items.length === 0) {
    const label = type === 'template'
      ? 'Нет шаблонов'
      : hideGeneratedTemplateRules
        ? 'Нет ручных правил; снимите галку, чтобы выбрать сгенерированные правила'
        : 'Нет правил';
    return [{ value: '', label, disabled: true }];
  }

  return items.map((item) => ({
    value: item.id,
    label: item.label
  }));
}

function shouldHideGeneratedTemplateRulesInRelationEditor(kindConfig, type) {
  return type === 'rule'
    && kindConfig?.sourceType === 'rule'
    && kindConfig?.targetType === 'rule'
    && state.linkRelationHideTemplateLinks !== false;
}

function linkRelationEntities(layerKey, type, options = {}) {
  if (type === 'template') {
    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    return document.templates.map((template) => ({
      id: template.template_id,
      label: `${template.name || template.template_id} [${template.template_id}]`
    })).sort(compareLinkRelationEntityLabels);
  }

  const parsed = parseRuleDocument(layerKey);
  const rules = (parsed.ok ? parsed.document.rules : [])
    .filter((rule) => !(options.hideGeneratedTemplateRules && isGeneratedTemplateRule(rule)));
  return rules.map((rule) => ({
    id: rule.rule_id || '',
    label: `${rule.name || rule.rule_id} [${rule.rule_id || '-'}]`
  })).filter((item) => item.id)
    .sort(compareLinkRelationEntityLabels);
}

function linkRelationEntityValue(type, id) {
  return `${type}:${String(id ?? '').trim()}`;
}

function selectedLinkRelationEntity(value, fallbackType = '') {
  const text = String(value ?? '').trim();
  const separatorIndex = text.indexOf(':');
  if (separatorIndex > 0) {
    const type = text.slice(0, separatorIndex);
    const id = text.slice(separatorIndex + 1);
    if (type === 'template' || type === 'rule') {
      return { type, id };
    }
  }

  return {
    type: fallbackType === 'mixed' ? '' : fallbackType,
    id: text
  };
}

function linkRelationSelectedTemplateId(config, kindConfig, side = 'source') {
  const value = side === 'target'
    ? config.target?.value
    : config.source?.value;
  const fallbackType = side === 'target' ? kindConfig.targetType : kindConfig.sourceType;
  const entity = selectedLinkRelationEntity(value, fallbackType);
  return entity.type === 'template' ? entity.id : '';
}

function findLayerTemplate(layerKey, templateId) {
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  return document.templates.find((template) => template.template_id === templateId) ?? null;
}

function templateVariableOptions(layerKey, templateId) {
  const template = findLayerTemplate(layerKey, String(templateId ?? '').trim());
  const variables = linkRelationTemplateVariableOptions(template);
  if (variables.length === 0) {
    return [{ value: '', label: 'В шаблоне нет переменных', disabled: true }];
  }

  return variables.sort(compareLinkRelationEntityLabels);
}

function linkRelationTemplateVariableOptions(template) {
  if (!template) {
    return [];
  }

  const variables = (template?.variables ?? [])
    .map((variable) => ({
      value: String(variable.name ?? '').trim(),
      label: `${String(variable.name ?? '').trim()} = ${String(variable.value ?? '').trim()}`
    }))
    .filter((item) => item.value);

  const builtIn = [
    { value: 'dimension_key', label: 'dimension_key = ${dimension.key}' },
    { value: 'dimension_value', label: 'dimension_value = ${dimension.value}' },
    { value: 'dimension_name', label: 'dimension_name = ${dimension.name}' },
    { value: 'key', label: 'key = ${dimension.key}' },
    { value: 'value', label: 'value = ${dimension.value}' },
    { value: 'name', label: 'name = ${dimension.name}' }
  ];
  const seen = new Set(variables.map((item) => canonicalToken(item.value)));
  for (const item of builtIn) {
    if (!seen.has(canonicalToken(item.value))) {
      variables.push(item);
    }
  }

  return variables;
}

function compareLinkRelationEntityLabels(left, right) {
  return String(left.label).localeCompare(String(right.label), undefined, { sensitivity: 'base' });
}

function linkRelationEntityLabel(layerKey, type, id) {
  return linkRelationEntities(layerKey, type).find((item) => item.id === id)?.label || id || '-';
}

function linkRelationRows(layerKey, kind, kindConfig = linkRelationKindConfig(kind, layerKey)) {
  if (kindConfig.sourceType === 'mixed' || kindConfig.targetType === 'mixed') {
    return linkRelationMixedRows(layerKey, kindConfig);
  }

  if (kindConfig.sourceType === 'template') {
    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    return document.templates.flatMap((template) =>
      (template.managed_relations ?? [])
        .filter((relation) => linkRelationMatchesKind(relation, kindConfig))
        .map((relation) => ({
          sourceId: template.template_id,
          sourceType: 'template',
          targetType: kindConfig.targetType,
          sourceLabel: linkRelationEntityLabel(layerKey, 'template', template.template_id),
          targetLabel: linkRelationEntityLabel(layerKey, kindConfig.targetType, linkRelationTargetId(relation, kindConfig.targetType)),
          description: String(relation.attributes?.description ?? '').trim(),
          matchLabel: linkRelationMatchLabel(relation),
          relation
        })));
  }

  const parsed = parseRuleDocument(layerKey);
  const rules = parsed.ok ? parsed.document.rules : [];
  const rulesById = new Map(rules
    .filter((rule) => rule?.rule_id)
    .map((rule) => [rule.rule_id, rule]));
  return rules.flatMap((rule) =>
    (rule.managed_relations ?? [])
      .filter((relation) => linkRelationMatchesKind(relation, kindConfig))
      .filter((relation) => !hideTemplateRelationInRuleRuleView(
        kindConfig,
        relation,
        rule,
        rulesById.get(linkRelationTargetId(relation, kindConfig.targetType))))
      .map((relation) => ({
        sourceId: rule.rule_id || '',
        sourceType: 'rule',
        targetType: kindConfig.targetType,
        sourceLabel: linkRelationEntityLabel(layerKey, 'rule', rule.rule_id || ''),
        targetLabel: linkRelationEntityLabel(layerKey, kindConfig.targetType, linkRelationTargetId(relation, kindConfig.targetType)),
        description: String(relation.attributes?.description ?? '').trim(),
        matchLabel: linkRelationMatchLabel(relation),
        relation
      })));
}

function linkRelationMixedRows(layerKey, kindConfig) {
  const templateDocument = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const parsed = parseRuleDocument(layerKey);
  const rules = parsed.ok
    ? parsed.document.rules.filter((rule) => !isGeneratedTemplateRule(rule))
    : [];
  const templateRows = templateDocument.templates.flatMap((template) =>
    (template.managed_relations ?? [])
      .filter((relation) => linkRelationMatchesPair(relation, 'template', 'rule'))
      .map((relation) => ({
        sourceId: template.template_id,
        sourceType: 'template',
        targetType: 'rule',
        sourceLabel: linkRelationEntityLabel(layerKey, 'template', template.template_id),
        targetLabel: linkRelationEntityLabel(layerKey, 'rule', relation.target_rule_id || ''),
        description: String(relation.attributes?.description ?? '').trim(),
        matchLabel: linkRelationMatchLabel(relation),
        relation
      })));
  const ruleRows = rules.flatMap((rule) =>
    (rule.managed_relations ?? [])
      .filter((relation) => linkRelationMatchesPair(relation, 'rule', 'template'))
      .map((relation) => ({
        sourceId: rule.rule_id || '',
        sourceType: 'rule',
        targetType: 'template',
        sourceLabel: linkRelationEntityLabel(layerKey, 'rule', rule.rule_id || ''),
        targetLabel: linkRelationEntityLabel(layerKey, 'template', relation.target_template_id || ''),
        description: String(relation.attributes?.description ?? '').trim(),
        matchLabel: linkRelationMatchLabel(relation),
        relation
      })));

  return templateRows.concat(ruleRows)
    .sort((left, right) =>
      left.sourceLabel.localeCompare(right.sourceLabel, undefined, { sensitivity: 'base' })
      || left.targetLabel.localeCompare(right.targetLabel, undefined, { sensitivity: 'base' }));
}

function hideTemplateRelationInRuleRuleView(kindConfig, relation, sourceRule = null, targetRule = null) {
  return kindConfig.sourceType === 'rule'
    && kindConfig.targetType === 'rule'
    && state.linkRelationHideTemplateLinks !== false
    && (isTemplateDerivedRuleRelation(relation)
      || isGeneratedTemplateRule(sourceRule)
      || isGeneratedTemplateRule(targetRule));
}

function hiddenTemplateRelationRowsCount(layerKey, kind, kindConfig = linkRelationKindConfig(kind, layerKey)) {
  if (kindConfig.sourceType !== 'rule'
    || kindConfig.targetType !== 'rule'
    || state.linkRelationHideTemplateLinks === false) {
    return 0;
  }

  const parsed = parseRuleDocument(layerKey);
  const rules = parsed.ok ? parsed.document.rules : [];
  const rulesById = new Map(rules
    .filter((rule) => rule?.rule_id)
    .map((rule) => [rule.rule_id, rule]));
  return rules.reduce((sum, rule) => sum + (rule.managed_relations ?? [])
    .filter((relation) => linkRelationMatchesKind(relation, kindConfig))
    .filter((relation) => hideTemplateRelationInRuleRuleView(
      kindConfig,
      relation,
      rule,
      rulesById.get(linkRelationTargetId(relation, kindConfig.targetType))))
    .length, 0);
}

function isTemplateDerivedRuleRelation(relation) {
  const attributes = relation?.attributes;
  return Boolean(attributes && typeof attributes === 'object' && !Array.isArray(attributes)
    && String(attributes.inherited_from_template_relation ?? '').trim());
}

async function refreshRelationGraphOnlineLayer() {
  state.relationGraph.loadingOnline = true;
  state.relationGraph.onlineError = '';
  state.relationGraph.onlineMessage = 'Загружаю управляемые объекты и domain-связи из CMDBuild...';
  renderRelationsGraphView();

  try {
    const instancesUrl = new URL('/api/cmdbuild/classes/instances', window.location.origin);
    instancesUrl.searchParams.set('prefix', state.prefix);
    instancesUrl.searchParams.set('serviceModelRoot', state.serviceModelRoot || defaultModelRoot(state.language));
    instancesUrl.searchParams.set('suppressionModelRoot', state.suppressionModelRoot || defaultModelRoot(state.language));
    const relationsUrl = new URL('/api/cmdbuild/domains/relations', window.location.origin);
    relationsUrl.searchParams.set('prefix', state.prefix);

    const [instancesResponse, relationsResponse] = await Promise.all([
      fetch(instancesUrl, { headers: { accept: 'application/json' } }),
      fetch(relationsUrl, { headers: { accept: 'application/json' } })
    ]);
    if (!instancesResponse.ok) {
      const text = await instancesResponse.text();
      throw new Error(text || `запрос экземпляров CMDBuild не выполнен: ${instancesResponse.status}`);
    }
    if (!relationsResponse.ok) {
      const text = await relationsResponse.text();
      throw new Error(text || `запрос связей CMDBuild не выполнен: ${relationsResponse.status}`);
    }

    const instancesCatalog = await instancesResponse.json();
    const relationsCatalog = await relationsResponse.json();
    state.relationGraph.onlineInstances = instancesCatalog.classes ?? [];
    state.relationGraph.onlineRelations = relationsCatalog.relations ?? [];
    state.relationGraph.onlineCheckedAt = new Date().toISOString();
    state.relationGraph.onlineMessage = `Онлайн-сверка обновлена: классов ${state.relationGraph.onlineInstances.length}, связей ${state.relationGraph.onlineRelations.length}.`;
  } catch (error) {
    state.relationGraph.onlineError = `Онлайн-сверка недоступна: ${error.message}`;
    state.relationGraph.onlineMessage = '';
  } finally {
    state.relationGraph.loadingOnline = false;
    renderRelationsGraphView();
  }
}

function renderRelationsGraphView() {
  const layerSelect = document.querySelector('#relationGraphLayerSelect');
  const directionSelect = document.querySelector('#relationGraphDirectionSelect');
  const regexToggle = document.querySelector('#relationGraphShowRegex');
  const diagnosticsToggle = document.querySelector('#relationGraphShowDiagnostics');
  const onlineToggle = document.querySelector('#relationGraphShowOnline');
  const onlineButton = document.querySelector('#relationGraphRefreshOnlineButton');
  const filterInput = document.querySelector('#relationGraphFilterInput');
  const summary = document.querySelector('#relationGraphSummary');
  const status = document.querySelector('#relationGraphStatus');
  const diagnostics = document.querySelector('#relationGraphDiagnostics');
  const canvas = document.querySelector('#relationGraphCanvas');
  if (!layerSelect || !directionSelect || !regexToggle || !diagnosticsToggle || !onlineToggle || !onlineButton || !filterInput || !summary || !status || !diagnostics || !canvas) {
    return;
  }

  const graphState = state.relationGraph;
  layerSelect.value = graphState.layer;
  directionSelect.value = graphState.direction;
  regexToggle.checked = graphState.showRegex;
  diagnosticsToggle.checked = graphState.showDiagnostics;
  onlineToggle.checked = graphState.showOnline;
  onlineButton.disabled = graphState.loadingOnline;
  onlineButton.textContent = graphState.loadingOnline ? 'Загрузка...' : 'Обновить онлайн';
  if (filterInput.value !== graphState.filter) {
    filterInput.value = graphState.filter;
  }

  const graph = relationGraphData(graphState.layer);
  if (graph.error) {
    summary.innerHTML = '';
    status.textContent = graph.error;
    status.classList.add('error');
    diagnostics.innerHTML = '';
    canvas.innerHTML = '<div class="empty-state">Граф не построен.</div>';
    return;
  }

  const filteredGraph = relationGraphFilter(graph, graphState.filter);
  const layout = relationGraphLayout(filteredGraph, graphState.direction);
  const online = relationGraphOnlineSnapshot(filteredGraph, graphState);
  graphState.currentOnline = online;
  const findings = relationGraphFindings(filteredGraph, online);
  summary.innerHTML = renderRelationGraphSummary(graph, filteredGraph, online);
  status.textContent = relationGraphStatusText(graph, filteredGraph, graphState);
  status.classList.toggle('error', Boolean(graphState.onlineError));
  diagnostics.innerHTML = graphState.showDiagnostics
    ? renderRelationGraphFindings(findings)
    : '';
  canvas.innerHTML = renderRelationGraphCanvas(layout, graphState);
}

function relationGraphData(layerKey) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return { error: parsed.error, nodes: [], edges: [], layer: layerKey };
  }

  const rules = parsed.document.rules ?? [];
  const rulesById = new Map(rules
    .filter((rule) => rule?.rule_id)
    .map((rule) => [rule.rule_id, rule]));
  const manualRules = rules
    .filter((rule) => rule?.rule_id && !isGeneratedTemplateRule(rule));
  const manualRuleById = new Map(manualRules.map((rule) => [rule.rule_id, rule]));
  const templateDocument = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const plan = templateMaterializationPlan(layerKey, { safe: true });
  const planByTemplateId = new Map(plan.templates.map((item) => [item.template.template_id, item]));
  const nodes = [
    ...templateDocument.templates.map((template) =>
      relationGraphNodeFromTemplate(layerKey, template, planByTemplateId.get(template.template_id))),
    ...manualRules.map((rule) => relationGraphNodeFromManualRule(rule))
  ];
  const nodeById = new Map(nodes.map((node) => [node.id, node]));
  const edges = [];
  edges.push(...relationGraphTemplateEdges(
    layerKey,
    templateDocument,
    planByTemplateId,
    manualRuleById,
    rulesById));
  edges.push(...relationGraphManualRuleEdges(layerKey, manualRules, manualRuleById, rulesById, templateDocument, planByTemplateId));

  return {
    layer: layerKey,
    rules,
    manualRules,
    templates: templateDocument.templates,
    generatedRules: plan.generatedRules,
    templateErrors: plan.errors ?? [],
    nodes,
    edges,
    nodeById
  };
}

function relationGraphTemplateNodeId(templateId) {
  return `template:${String(templateId ?? '').trim()}`;
}

function relationGraphRuleNodeId(ruleId) {
  return `rule:${String(ruleId ?? '').trim()}`;
}

function relationGraphNodeFromTemplate(layerKey, template, planItem = null) {
  const generatedRules = planItem?.rules ?? [];
  const expectedObjects = relationGraphUniqueExpectedObjects(generatedRules);
  return {
    id: relationGraphTemplateNodeId(template.template_id),
    nodeType: 'template',
    label: String(template.name || template.template_id || '').trim(),
    priority: Number(template.priority ?? 0) || 0,
    templateId: template.template_id,
    templateName: String(template.name || template.template_id || '').trim(),
    sourceClass: String(template.source_class_regex || '').trim(),
    targetClass: String(template.target?.class_code || '').trim(),
    objectCount: expectedObjects.length,
    objectKey: expectedObjects.length === 1 ? expectedObjects[0].key : '',
    objectLookup: expectedObjects.length === 1 ? expectedObjects[0].lookup : '',
    objectLabel: `${expectedObjects.length} объектов`,
    generatedRuleCount: generatedRules.length,
    runtimeRelationCount: generatedRules.reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0),
    expectedObjects,
    regexItems: relationGraphRegexItemsFromTemplate(template),
    searchText: relationGraphSearchText([
      template.template_id,
      template.name,
      template.source_class_regex,
      template.target?.class_code,
      expectedObjects.map((item) => `${item.classCode}:${item.lookup}`).join(' ')
    ]),
    template,
    generatedRules,
    layer: layerKey
  };
}

function relationGraphNodeFromManualRule(rule) {
  const expectedObject = relationGraphExpectedObjectFromRule(rule);
  const expectedObjects = expectedObject ? [expectedObject] : [];
  return {
    id: relationGraphRuleNodeId(rule.rule_id),
    nodeType: 'manual_rule',
    label: String(rule.name || rule.rule_id || '').trim(),
    priority: Number(rule.priority ?? 0) || 0,
    ruleId: String(rule.rule_id ?? '').trim(),
    templateId: '',
    templateName: '',
    sourceClass: ruleSourceClassCode(rule),
    sourceKeyAttribute: String(rule.source?.key_attribute ?? rule.when?.fieldExists ?? '').trim(),
    targetClass: ruleTargetClassCode(rule),
    objectCount: expectedObjects.length,
    objectKey: expectedObject?.key ?? '',
    objectLookup: expectedObject?.lookup ?? '',
    objectLabel: expectedObject?.label ?? 'цель не задана',
    generatedRuleCount: 0,
    runtimeRelationCount: runtimeRelationsFromRule(rule).length,
    expectedObjects,
    regexItems: relationGraphRegexItemsFromRule(rule),
    searchText: relationGraphSearchText([
      rule.rule_id,
      rule.name,
      ruleSourceClassCode(rule),
      rule.source?.key_attribute,
      ruleTargetClassCode(rule),
      expectedObject?.key,
      expectedObject?.label
    ]),
    rule
  };
}

function relationGraphRuleObjectIdentity(rule) {
  const targetClass = ruleTargetClassCode(rule);
  const target = rule?.target ?? {};
  const mappings = target.attribute_mappings ?? {};
  const key = String(target.card_id
    || target.idempotency_key
    || mappings.Code
    || mappings.code
    || mappings.name
    || '').trim();
  if (!targetClass) {
    return { count: 0, key: '', lookup: '', label: 'цель не задана' };
  }

  const label = target.card_id
    ? `карточка ${target.card_id}`
    : key || 'ключ не задан';
  return {
    count: key ? 1 : 0,
    key: key ? `${targetClass}:${key}` : '',
    lookup: key,
    label
  };
}

function relationGraphExpectedObjectFromRule(rule) {
  const identity = relationGraphRuleObjectIdentity(rule);
  if (!identity.key || !identity.lookup) {
    return null;
  }

  return {
    key: relationGraphObjectMatchKey(ruleTargetClassCode(rule), identity.lookup),
    rawKey: identity.key,
    classCode: ruleTargetClassCode(rule),
    lookup: identity.lookup,
    label: identity.label,
    ruleId: String(rule?.rule_id ?? '').trim(),
    ruleName: String(rule?.name || rule?.rule_id || '').trim()
  };
}

function relationGraphUniqueExpectedObjects(rules) {
  const byKey = new Map();
  for (const rule of rules ?? []) {
    const object = relationGraphExpectedObjectFromRule(rule);
    if (object?.key && !byKey.has(object.key)) {
      byKey.set(object.key, object);
    }
  }

  return [...byKey.values()];
}

function relationGraphTemplateEdges(layerKey, templateDocument, planByTemplateId, manualRuleById, rulesById) {
  const templateById = new Map((templateDocument.templates ?? [])
    .map((template) => [template.template_id, template]));
  const edges = [];
  for (const template of templateDocument.templates ?? []) {
    const sourceRules = planByTemplateId.get(template.template_id)?.rules ?? [];
    const sourceNodeId = relationGraphTemplateNodeId(template.template_id);
    for (const relation of template.managed_relations ?? []) {
      const role = String(relation.relation_role ?? 'uses').trim() || 'uses';
      if (['template', 'rule_template'].includes(relation.kind) && relation.target_template_id) {
        const targetTemplateId = String(relation.target_template_id ?? '').trim();
        const targetTemplate = templateById.get(targetTemplateId) ?? null;
        const targetRules = planByTemplateId.get(targetTemplateId)?.rules ?? [];
        const bundle = relationGraphExpectedRelationsForRulePairs(layerKey, sourceRules, targetRules, relation);
        edges.push(relationGraphEdge({
          sourceId: sourceNodeId,
          targetId: relationGraphTemplateNodeId(targetTemplateId),
          sourceKind: 'template',
          targetKind: 'template',
          sourceTemplate: template,
          targetTemplate,
          role,
          relation,
          missingTarget: !targetTemplate,
          expectedRelations: bundle.relations,
          relationErrors: bundle.errors
        }));
        continue;
      }

      if (relation.kind === 'rule' && relation.target_rule_id) {
        const targetRuleId = String(relation.target_rule_id ?? '').trim();
        const targetRule = manualRuleById.get(targetRuleId) ?? null;
        if (!targetRule && rulesById.has(targetRuleId)) {
          continue;
        }

        const bundle = targetRule
          ? relationGraphExpectedRelationsForRulePairs(layerKey, sourceRules, [targetRule], relation)
          : { relations: [], errors: [] };
        edges.push(relationGraphEdge({
          sourceId: sourceNodeId,
          targetId: relationGraphRuleNodeId(targetRuleId),
          sourceKind: 'template',
          targetKind: 'manual_rule',
          sourceTemplate: template,
          targetRule,
          role,
          relation,
          missingTarget: !targetRule,
          expectedRelations: bundle.relations,
          relationErrors: bundle.errors
        }));
      }
    }
  }

  return edges;
}

function relationGraphManualRuleEdges(layerKey, manualRules, manualRuleById, rulesById, templateDocument = defaultTemplateDocument(layerKey), planByTemplateId = new Map()) {
  const templateById = new Map((templateDocument.templates ?? [])
    .map((template) => [template.template_id, template]));
  const edges = [];
  for (const rule of manualRules) {
    const sourceId = relationGraphRuleNodeId(rule.rule_id);
    for (const relation of rule.managed_relations ?? []) {
      if (linkRelationMatchesPair(relation, 'rule', 'template')) {
        const targetTemplateId = String(relation.target_template_id ?? '').trim();
        const targetTemplate = templateById.get(targetTemplateId) ?? null;
        const targetRules = planByTemplateId.get(targetTemplateId)?.rules ?? [];
        const bundle = targetTemplate
          ? relationGraphExpectedRelationsForRulePairs(layerKey, [rule], targetRules, relation)
          : { relations: [], errors: [] };
        const role = String(relation.relation_role ?? 'uses').trim() || 'uses';
        edges.push(relationGraphEdge({
          sourceId,
          targetId: relationGraphTemplateNodeId(targetTemplateId),
          sourceKind: 'manual_rule',
          targetKind: 'template',
          sourceRule: rule,
          targetTemplate,
          role,
          relation,
          missingTarget: !targetTemplate,
          expectedRelations: bundle.relations,
          relationErrors: bundle.errors
        }));
        continue;
      }

      if (!linkRelationMatchesKind(relation, { targetType: 'rule', relationKind: 'rule' })) {
        continue;
      }

      const targetRuleId = String(relation.target_rule_id ?? '').trim();
      const targetRule = manualRuleById.get(targetRuleId) ?? null;
      if (!targetRule && rulesById.has(targetRuleId)) {
        continue;
      }

      const bundle = targetRule
        ? relationGraphExpectedRelationsForRulePairs(layerKey, [rule], [targetRule], relation)
        : { relations: [], errors: [] };
      const role = String(relation.relation_role ?? 'uses').trim() || 'uses';
      edges.push(relationGraphEdge({
        sourceId,
        targetId: relationGraphRuleNodeId(targetRuleId),
        sourceKind: 'manual_rule',
        targetKind: 'manual_rule',
        sourceRule: rule,
        targetRule,
        role,
        relation,
        missingTarget: !targetRule,
        expectedRelations: bundle.relations,
        relationErrors: bundle.errors
      }));
    }
  }

  return edges;
}

function relationGraphExpectedRelationsForRulePairs(layerKey, sourceRules, targetRules, relation) {
  const relations = [];
  const errors = [];
  const seen = new Set();
  for (const sourceRule of sourceRules ?? []) {
    for (const targetRule of targetRules ?? []) {
      if (!sourceRule || !targetRule || sourceRule === targetRule || sourceRule.rule_id === targetRule.rule_id) {
        continue;
      }

      if (!templateManagedRelationMatchesRulePair(sourceRule, targetRule, relation)) {
        continue;
      }

      try {
        const expected = relationGraphExpectedRelationFromRules(layerKey, sourceRule, targetRule, relation);
        if (!expected) {
          continue;
        }

        const key = stableHash({
          domainCode: expected.domainCode,
          source: expected.sourceObject.key,
          target: expected.targetObject.key
        });
        if (!seen.has(key)) {
          seen.add(key);
          relations.push(expected);
        }
      } catch (error) {
        errors.push({
          sourceRule,
          targetRule,
          message: error.message
        });
      }
    }
  }

  return { relations, errors };
}

function relationGraphExpectedRelationFromRules(layerKey, sourceRule, targetRule, relation) {
  const sourceObject = relationGraphExpectedObjectFromRule(sourceRule);
  const targetObject = relationGraphExpectedObjectFromRule(targetRule);
  if (!sourceObject || !targetObject) {
    return null;
  }

  const runtimeRelation = runtimeRelationFromTemplateRelation(layerKey, sourceRule, targetRule, relation);
  return {
    domainCode: runtimeRelation.domain_code,
    targetClass: runtimeRelation.target_class_code,
    targetLookup: runtimeRelation.target_lookup,
    sourceObject,
    targetObject,
    sourceRuleId: String(sourceRule.rule_id ?? '').trim(),
    targetRuleId: String(targetRule.rule_id ?? '').trim()
  };
}

function relationGraphEdge({
  sourceId,
  targetId,
  sourceKind,
  targetKind,
  sourceRule = null,
  targetRule = null,
  sourceTemplate = null,
  targetTemplate = null,
  role,
  relation,
  missingTarget = false,
  expectedRelations = [],
  relationErrors = []
}) {
  const relationKey = relation.managed_key || stableHash({
    sourceId,
    targetId,
    role,
    match: relation.attributes?.match ?? {}
  });
  return {
    id: `${sourceId}:${relationKey}`,
    sourceId,
    targetId,
    sourceKind,
    targetKind,
    role,
    roleLabel: linkRelationRoleLabel(role),
    sourceRule,
    targetRule,
    sourceTemplate,
    targetTemplate,
    missingTarget,
    regexItems: relationGraphRegexItemsFromRelation(relation),
    relation,
    expectedRelations,
    relationErrors,
    count: Math.max(1, expectedRelations.length),
    searchText: relationGraphSearchText([
      sourceId,
      targetId,
      sourceTemplate?.name,
      targetTemplate?.name,
      sourceRule?.name,
      targetRule?.name,
      role,
      linkRelationRoleLabel(role),
      relationGraphRegexItemsFromRelation(relation).join(' ')
    ])
  };
}

function relationGraphOnlineSnapshot(graph, graphState) {
  if (!graphState.showOnline) {
    return null;
  }

  const layerLabel = graph.layer === 'service' ? 'Service' : 'Suppression';
  const classItems = (graphState.onlineInstances ?? [])
    .filter((item) => String(item.layer ?? '').toLowerCase() === layerLabel.toLowerCase());
  const cardsByClass = new Map();
  const actualObjectKeys = new Map();
  for (const classItem of classItems) {
    const classCode = classItem.classCode ?? classItem.ClassCode ?? '';
    if (!classCode) {
      continue;
    }

    const key = canonicalToken(classCode);
    const cards = cardsByClass.get(key) ?? [];
    for (const card of classItem.cards ?? []) {
      const normalizedCard = { ...card, classCode };
      cards.push(normalizedCard);
      for (const identityKey of relationGraphCardIdentityKeys(normalizedCard, classCode)) {
        actualObjectKeys.set(identityKey, normalizedCard);
      }
    }
    cardsByClass.set(key, cards);
  }

  const expectedObjectKeys = new Map();
  const cardByExpectedObjectKey = new Map();
  const nodeOnlineById = new Map();
  for (const node of graph.nodes) {
    const expectedObjects = node.expectedObjects ?? [];
    const stateItem = {
      expected: expectedObjects.length,
      found: 0,
      missing: []
    };
    for (const object of expectedObjects) {
      if (object.key) {
        expectedObjectKeys.set(object.key, node);
      }

      const card = relationGraphActualCardForExpectedObject(object, { cardsByClass });
      if (card) {
        stateItem.found += 1;
        cardByExpectedObjectKey.set(object.key, card);
      } else {
        stateItem.missing.push(object);
      }
    }
    nodeOnlineById.set(node.id, stateItem);
  }

  const relationStates = relationGraphOnlineRelationStates(graph, {
    cardByExpectedObjectKey,
    relations: graphState.onlineRelations ?? []
  });
  const missingObjects = graph.nodes.flatMap((node) =>
    (nodeOnlineById.get(node.id)?.missing ?? []).map((object) => ({ node, object })));
  const extraObjects = [...actualObjectKeys.entries()]
    .filter(([key]) => !expectedObjectKeys.has(key))
    .map(([, card]) => card)
    .filter((card, index, items) => items.findIndex((item) =>
      item.classCode === card.classCode && String(item.id) === String(card.id)) === index);
  const expectedRelationCount = relationStates.reduce((sum, item) => sum + item.expectedCount, 0);
  const foundRelationCount = relationStates.reduce((sum, item) => sum + item.foundCount, 0);
  const missingRelationCount = relationStates.reduce((sum, item) => sum + item.missingCount, 0);
  const blockedRelationCount = relationStates.reduce((sum, item) => sum + item.blockedCount, 0);
  const expectedObjectCount = graph.nodes.reduce((sum, node) => sum + (node.expectedObjects?.length ?? 0), 0);
  const foundObjectCount = [...nodeOnlineById.values()].reduce((sum, item) => sum + item.found, 0);

  return {
    enabled: true,
    checkedAt: graphState.onlineCheckedAt,
    error: graphState.onlineError,
    message: graphState.onlineMessage,
    classItems,
    cardsByClass,
    cardByExpectedObjectKey,
    nodeOnlineById,
    actualObjectKeys,
    expectedObjectKeys,
    missingObjects,
    extraObjects,
    relations: graphState.onlineRelations ?? [],
    relationStates,
    relationStateByEdgeId: new Map(relationStates.map((item) => [item.edge.id, item])),
    expectedObjectCount,
    foundObjectCount,
    expectedRelationCount,
    foundRelationCount,
    missingRelationCount,
    blockedRelationCount
  };
}

function relationGraphActualCardForExpectedObject(object, online) {
  if (!object?.classCode || !object.lookup) {
    return null;
  }

  const cards = online.cardsByClass.get(canonicalToken(object.classCode)) ?? [];
  const expectedKey = relationGraphObjectMatchKey(object.classCode, object.lookup);
  return cards.find((card) => relationGraphCardIdentityKeys(card, object.classCode).has(expectedKey)) ?? null;
}

function relationGraphCardIdentityKeys(card, classCode) {
  const values = [
    card?.id,
    card?.description,
    cardAttributeValue(card, 'Code'),
    cardAttributeValue(card, 'code'),
    cardAttributeValue(card, 'name'),
    cardAttributeValue(card, POPULATION_SOURCE_KEY_ATTRIBUTE)
  ];
  return new Set(values
    .map((value) => relationGraphObjectMatchKey(classCode, value))
    .filter((value) => value && !value.endsWith(':')));
}

function relationGraphObjectMatchKey(classCode, value) {
  const normalizedClass = relationGraphMatchToken(classCode);
  const normalizedValue = relationGraphMatchToken(value);
  return normalizedClass && normalizedValue ? `${normalizedClass}:${normalizedValue}` : '';
}

function relationGraphMatchToken(value) {
  return String(value ?? '').trim().toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, '');
}

function relationGraphOnlineRelationStates(graph, online) {
  return graph.edges.map((edge) => {
    const expectedRelations = edge.expectedRelations ?? [];
    if (expectedRelations.length === 0) {
      return {
        edge,
        expected: null,
        expectedRelations,
        expectedCount: 0,
        foundCount: 0,
        missingCount: 0,
        blockedCount: 0,
        status: 'unknown'
      };
    }

    let foundCount = 0;
    let missingCount = 0;
    let blockedCount = 0;
    const samples = [];
    for (const expected of expectedRelations) {
      const sourceCard = online.cardByExpectedObjectKey.get(expected.sourceObject.key);
      const targetCard = online.cardByExpectedObjectKey.get(expected.targetObject.key);
      if (!sourceCard || !targetCard) {
        blockedCount += 1;
        samples.push({ expected, sourceCard, targetCard, status: 'blocked' });
        continue;
      }

      const found = relationGraphActualRelationExists(expected, sourceCard, targetCard, online.relations);
      if (found) {
        foundCount += 1;
      } else {
        missingCount += 1;
      }
      samples.push({ expected, sourceCard, targetCard, status: found ? 'found' : 'missing' });
    }

    const status = foundCount === expectedRelations.length
      ? 'found'
      : blockedCount > 0
        ? 'blocked'
        : missingCount > 0
          ? 'missing'
          : 'unknown';
    return {
      edge,
      expected: expectedRelations[0],
      expectedRelations,
      expectedCount: expectedRelations.length,
      foundCount,
      missingCount,
      blockedCount,
      samples,
      status
    };
  });
}

function relationGraphActualRelationExists(expected, sourceCard, targetCard, relations) {
  const sourceClass = expected.sourceObject?.classCode ?? '';
  const targetClass = expected.targetObject?.classCode || expected.targetClass || '';
  return (relations ?? []).some((relation) => {
    if (canonicalToken(relation.domainCode ?? relation.DomainCode) !== canonicalToken(expected.domainCode)) {
      return false;
    }

    const sourceType = relation.sourceType ?? relation.SourceType ?? '';
    const sourceId = String(relation.sourceId ?? relation.SourceId ?? '');
    const destinationType = relation.destinationType ?? relation.DestinationType ?? '';
    const destinationId = String(relation.destinationId ?? relation.DestinationId ?? '');
    const direct = canonicalToken(sourceType) === canonicalToken(sourceClass)
      && sourceId === String(sourceCard.id)
      && canonicalToken(destinationType) === canonicalToken(targetClass)
      && destinationId === String(targetCard.id);
    const reverse = canonicalToken(sourceType) === canonicalToken(targetClass)
      && sourceId === String(targetCard.id)
      && canonicalToken(destinationType) === canonicalToken(sourceClass)
      && destinationId === String(sourceCard.id);
    return direct || reverse;
  });
}

function relationGraphRegexItemsFromTemplate(template) {
  const items = [];
  const sourceRegex = String(template?.source_class_regex ?? '').trim();
  if (sourceRegex) {
    items.push(`класс-источник: ${sourceRegex}`);
  }

  for (const filter of selectionFiltersFromTemplate(template)) {
    if (filter.field && filter.regex) {
      items.push(`${filter.mode === 'exclude' ? 'исключить ' : ''}${filter.field}: ${filter.regex}`);
    }
  }

  const dimension = templatePopulationDimension(template);
  if (dimension?.source_field) {
    items.push(`измерение ${dimension.source_field}`);
  }
  if (dimension?.regex) {
    items.push(`regex измерения: ${dimension.regex}`);
  }

  return [...new Set(items)].slice(0, 8);
}

function relationGraphRegexItemsFromRule(rule) {
  const items = [];
  const sourceRegex = String(rule?.template_generation?.template_source_regex ?? '').trim();
  if (sourceRegex) {
    items.push(`класс-источник: ${sourceRegex}`);
  }

  for (const matcher of rule?.when?.allRegex ?? []) {
    if (matcher?.pattern) {
      items.push(`${matcher.field || 'поле'}: ${matcher.pattern}`);
    }
  }

  for (const matcher of rule?.when?.noneRegex ?? []) {
    if (matcher?.pattern) {
      items.push(`исключить ${matcher.field || 'поле'}: ${matcher.pattern}`);
    }
  }

  return [...new Set(items)].slice(0, 8);
}

function relationGraphRegexItemsFromRelation(relation) {
  const match = relation?.attributes?.match;
  if (!match || typeof match !== 'object' || Array.isArray(match)) {
    return [];
  }

  const items = [];
  const sourcePattern = String(match.source_pattern ?? match.pattern ?? '').trim();
  const targetPattern = String(match.target_pattern ?? '').trim();
  if (sourcePattern) {
    items.push(`${match.source_variable || 'источник'}: ${sourcePattern}`);
  }
  if (targetPattern) {
    items.push(`${match.target_variable || 'цель'}: ${targetPattern}`);
  }
  for (const filter of linkRelationSourceFiltersFromMatch(match)) {
    items.push(`источник ${filter.mode === 'exclude' ? 'исключить' : 'включить'} ${filter.variable}: ${filter.regex}`);
  }
  for (const filter of linkRelationTargetFiltersFromMatch(match)) {
    items.push(`цель ${filter.mode === 'exclude' ? 'исключить' : 'включить'} ${filter.variable}: ${filter.regex}`);
  }
  return [...new Set(items)].slice(0, 6);
}

function relationGraphFilter(graph, filterValue) {
  const query = relationGraphMatchToken(filterValue);
  if (!query) {
    return graph;
  }

  const visibleNodeIds = new Set(graph.nodes
    .filter((node) => relationGraphMatchToken(node.searchText).includes(query))
    .map((node) => node.id));
  const matchingEdges = graph.edges.filter((edge) => relationGraphMatchToken([
    edge.sourceId,
    edge.targetId,
    edge.roleLabel,
    edge.searchText,
    edge.regexItems.join(' ')
  ].join(' ')).includes(query));
  for (const edge of matchingEdges) {
    visibleNodeIds.add(edge.sourceId);
    if (edge.targetId) {
      visibleNodeIds.add(edge.targetId);
    }
  }

  const edges = graph.edges.filter((edge) =>
    (visibleNodeIds.has(edge.sourceId) && (!edge.targetId || visibleNodeIds.has(edge.targetId)))
    || matchingEdges.includes(edge));
  const nodes = graph.nodes.filter((node) => visibleNodeIds.has(node.id));
  return {
    ...graph,
    nodes,
    edges,
    nodeById: new Map(nodes.map((node) => [node.id, node]))
  };
}

function relationGraphLayout(graph, directionMode) {
  const nodeWidth = 300;
  const nodeHeight = 108;
  const columnGap = 120;
  const rowGap = 56;
  const margin = 32;
  const headerHeight = 42;
  const sortedNodes = [...graph.nodes].sort(compareRelationGraphNodes);
  const columns = new Map();
  for (const node of sortedNodes) {
    const key = `${node.priority}\u0000${node.targetClass || '-'}`;
    if (!columns.has(key)) {
      columns.set(key, {
        key,
        priority: node.priority,
        targetClass: node.targetClass || '-',
        nodes: []
      });
    }
    columns.get(key).nodes.push(node);
  }

  const columnList = [...columns.values()].sort((left, right) =>
    left.priority - right.priority
    || left.targetClass.localeCompare(right.targetClass, undefined, { sensitivity: 'base' }));
  const positionedNodes = [];
  for (const [columnIndex, column] of columnList.entries()) {
    for (const [rowIndex, node] of column.nodes.entries()) {
      positionedNodes.push({
        ...node,
        x: margin + columnIndex * (nodeWidth + columnGap),
        y: margin + headerHeight + rowIndex * (nodeHeight + rowGap),
        width: nodeWidth,
        height: nodeHeight,
        columnKey: column.key
      });
    }
  }

  const nodeById = new Map(positionedNodes.map((node) => [node.id, node]));
  const visibleEdges = relationGraphVisibleEdges(graph.edges, nodeById, directionMode);
  const maxRows = Math.max(1, ...columnList.map((column) => column.nodes.length));
  return {
    width: Math.max(900, margin * 2 + Math.max(1, columnList.length) * nodeWidth + Math.max(0, columnList.length - 1) * columnGap),
    height: Math.max(360, margin * 2 + headerHeight + maxRows * nodeHeight + Math.max(0, maxRows - 1) * rowGap),
    nodeWidth,
    nodeHeight,
    columnGap,
    rowGap,
    margin,
    headerHeight,
    columns: columnList,
    nodes: positionedNodes,
    nodeById,
    edges: visibleEdges,
    missingEdges: graph.edges.filter((edge) => edge.missingTarget)
  };
}

function compareRelationGraphNodes(left, right) {
  return left.priority - right.priority
    || String(left.targetClass).localeCompare(String(right.targetClass), undefined, { sensitivity: 'base' })
    || String(left.templateName || left.templateId).localeCompare(String(right.templateName || right.templateId), undefined, { sensitivity: 'base' })
    || String(left.sourceClass).localeCompare(String(right.sourceClass), undefined, { sensitivity: 'base' })
    || String(left.label).localeCompare(String(right.label), undefined, { sensitivity: 'base' });
}

function relationGraphVisibleEdges(edges, nodeById, directionMode) {
  return edges.flatMap((edge) => {
    const endpoints = relationGraphEdgeEndpoints(edge, directionMode);
    const source = nodeById.get(endpoints.sourceId);
    const target = nodeById.get(endpoints.targetId);
    if (!source || !target) {
      return [];
    }

    return [{
      ...edge,
      sourceId: endpoints.sourceId,
      targetId: endpoints.targetId,
      configuredSourceId: edge.sourceId,
      configuredTargetId: edge.targetId,
      reversedForEffect: endpoints.reversedForEffect,
      source,
      target,
      count: edge.count ?? 1,
      regexItems: [...new Set(edge.regexItems)]
    }];
  });
}

function relationGraphEdgeEndpoints(edge, directionMode) {
  if (directionMode === 'effect' && relationGraphRoleEffectDirection(edge.role) === 'target_to_source') {
    return {
      sourceId: edge.targetId,
      targetId: edge.sourceId,
      reversedForEffect: true
    };
  }

  return {
    sourceId: edge.sourceId,
    targetId: edge.targetId,
    reversedForEffect: false
  };
}

function relationGraphRoleEffectDirection(role) {
  const normalized = String(role ?? '').trim();
  if (normalized === 'depends_on' || normalized === 'uses') {
    return 'target_to_source';
  }

  return 'source_to_target';
}

function renderRelationGraphSummary(graph, filteredGraph, online = null) {
  const templates = graph.nodes.filter((node) => node.nodeType === 'template').length;
  const manual = graph.nodes.filter((node) => node.nodeType === 'manual_rule').length;
  const generatedRules = graph.nodes.reduce((sum, node) => sum + (node.generatedRuleCount ?? 0), 0);
  const expectedObjects = new Set(graph.nodes
    .flatMap((node) => node.expectedObjects ?? [])
    .map((object) => object.key)
    .filter(Boolean)).size;
  const filteredText = filteredGraph.nodes.length === graph.nodes.length
    ? ''
    : `<span class="relation-graph-filter-note">Фильтр: ${escapeHtml(filteredGraph.nodes.length)} узлов · ${escapeHtml(filteredGraph.edges.length)} связей</span>`;
  return `
    <div>
      <span class="metric-label">Узлы графа</span>
      <strong>${escapeHtml(graph.nodes.length)}</strong>
    </div>
    <div>
      <span class="metric-label">Шаблоны/ручные</span>
      <strong>${escapeHtml(templates)} / ${escapeHtml(manual)}</strong>
    </div>
    <div>
      <span class="metric-label">Порождено правил</span>
      <strong>${escapeHtml(generatedRules)}</strong>
    </div>
    <div>
      <span class="metric-label">Ожидаемые объекты</span>
      <strong>${escapeHtml(expectedObjects)}</strong>
    </div>
    <div>
      <span class="metric-label">Связи</span>
      <strong>${escapeHtml(graph.edges.length)}</strong>
    </div>
    ${online ? `
      <div>
        <span class="metric-label">Объекты онлайн</span>
        <strong>${escapeHtml(online.foundObjectCount)} / ${escapeHtml(online.expectedObjectCount)}</strong>
      </div>
      <div>
        <span class="metric-label">Связи онлайн</span>
        <strong>${escapeHtml(online.foundRelationCount)} / ${escapeHtml(online.expectedRelationCount)}</strong>
      </div>
      <div>
        <span class="metric-label">Нет онлайн</span>
        <strong>${escapeHtml(online.missingObjects.length)} объектов · ${escapeHtml(online.missingRelationCount)} связей</strong>
      </div>
      <div>
        <span class="metric-label">Онлайн проверен</span>
        <strong>${escapeHtml(formatCacheTimestamp(online.checkedAt) || 'нет')}</strong>
      </div>
    ` : ''}
    ${filteredText}
  `;
}

function relationGraphStatusText(graph, filteredGraph, graphState) {
  const layerLabel = graphState.layer === 'service' ? 'сервис' : 'подавление';
  const directionLabel = graphState.direction === 'effect'
    ? 'стрелки показывают направление влияния/подавления'
    : 'стрелки показывают направление, записанное в правилах';
  const filterLabel = filteredGraph.nodes.length === graph.nodes.length
    ? ''
    : ` Отфильтровано до ${filteredGraph.nodes.length} узлов.`;
  const onlineLabel = graphState.showOnline
    ? graphState.onlineError
      ? ` ${graphState.onlineError}`
      : graphState.loadingOnline
        ? ' Онлайн-сверка загружается.'
        : graphState.onlineCheckedAt
          ? ` Онлайн-сверка: ${graphState.onlineMessage || formatCacheTimestamp(graphState.onlineCheckedAt)}.`
          : ' Онлайн-сверка включена; нажмите "Обновить онлайн".'
    : '';
  return `${layerLabel}: узлы графа это шаблоны правил и ручные правила; сгенерированные правила свернуты в счетчики шаблонов. ${directionLabel}.${filterLabel}${onlineLabel}`;
}

function renderRelationGraphCanvas(layout, graphState) {
  if (layout.nodes.length === 0) {
    return '<div class="empty-state">Нет шаблонов или ручных правил для отображения.</div>';
  }

  return `
    <div class="relation-graph-stage" style="width:${layout.width}px;height:${layout.height}px">
      ${renderRelationGraphSvg(layout, graphState)}
      ${layout.columns.map((column, index) => `
        <div class="relation-graph-column-label" style="left:${layout.margin + index * (layout.nodeWidth + layout.columnGap)}px;top:12px;width:${layout.nodeWidth}px">
          P${escapeHtml(column.priority)} · ${escapeHtml(column.targetClass)}
        </div>
      `).join('')}
      ${layout.nodes.map((node) => renderRelationGraphNode(node, graphState)).join('')}
      ${layout.edges.map((edge, index) => renderRelationGraphEdgeLabel(edge, index, graphState)).join('')}
    </div>
  `;
}

function renderRelationGraphSvg(layout, graphState) {
  return `
    <svg class="relation-graph-svg" width="${layout.width}" height="${layout.height}" viewBox="0 0 ${layout.width} ${layout.height}" aria-hidden="true">
      <defs>
        <marker id="relationGraphArrow" markerWidth="10" markerHeight="8" refX="9" refY="4" orient="auto" markerUnits="strokeWidth">
          <path d="M 0 0 L 10 4 L 0 8 z"></path>
        </marker>
      </defs>
      ${layout.edges.map((edge, index) => renderRelationGraphEdge(edge, index, graphState)).join('')}
    </svg>
  `;
}

function renderRelationGraphEdge(edge, index, graphState) {
  const geometry = relationGraphEdgeGeometry(edge, index);
  return `
    <g class="relation-graph-edge relation-role-${escapeHtml(canonicalToken(edge.role) || 'uses')} ${escapeHtml(relationGraphEdgeOnlineState(edge).className)}">
      <path d="${escapeHtml(geometry.d)}" marker-end="url(#relationGraphArrow)"></path>
    </g>
  `;
}

function renderRelationGraphEdgeLabel(edge, index, graphState) {
  const onlineState = relationGraphEdgeOnlineState(edge);
  const geometry = relationGraphEdgeGeometry(edge, index);
  const label = `${edge.roleLabel}${edge.count > 1 ? ` x${edge.count}` : ''}${edge.reversedForEffect ? ' · влияние' : ''}`;
  const onlineLabel = onlineState.label ? ` · ${onlineState.label}` : '';
  return `
    <div
      class="relation-graph-edge-label relation-role-${escapeHtml(canonicalToken(edge.role) || 'uses')} ${escapeHtml(onlineState.className)}"
      style="left:${geometry.midX}px;top:${geometry.midY}px"
    >
      <span>${escapeHtml(label + onlineLabel)}</span>
      ${graphState.showRegex ? renderRelationGraphRegexChip(edge.regexItems, 'Регулярные выражения связи') : ''}
    </div>
  `;
}

function relationGraphEdgeGeometry(edge, index) {
  const source = edge.source;
  const target = edge.target;
  const sourceCenterX = source.x + source.width / 2;
  const sourceCenterY = source.y + source.height / 2;
  const targetCenterX = target.x + target.width / 2;
  const targetCenterY = target.y + target.height / 2;
  const sameColumn = Math.abs(source.x - target.x) < 2;
  const targetIsRight = targetCenterX >= sourceCenterX;
  const side = sameColumn || targetIsRight ? 'right' : 'left';
  const sourceAnchor = relationGraphNodeAnchor(source, side);
  const targetAnchor = relationGraphNodeAnchor(target, sameColumn ? side : targetIsRight ? 'left' : 'right');
  const edgeOffset = ((index % 5) - 2) * 7;
  const exitPadding = 22 + Math.abs(edgeOffset);
  const sourceExitX = side === 'right'
    ? source.x + source.width + exitPadding
    : source.x - exitPadding;
  const targetExitX = sameColumn
    ? sourceExitX
    : targetIsRight
      ? target.x - exitPadding
      : target.x + target.width + exitPadding;
  const laneY = relationGraphEdgeLaneY(source, target, index);
  const points = [
    sourceAnchor,
    { x: sourceExitX, y: sourceAnchor.y },
    { x: sourceExitX, y: laneY },
    { x: targetExitX, y: laneY },
    { x: targetExitX, y: targetAnchor.y },
    targetAnchor
  ];
  const path = relationGraphPolylinePath(points);
  const labelSegment = Math.abs(sourceExitX - targetExitX) > 24
    ? {
        from: { x: sourceExitX, y: laneY },
        to: { x: targetExitX, y: laneY }
      }
    : {
        from: { x: sourceExitX, y: sourceAnchor.y },
        to: { x: targetExitX, y: targetAnchor.y }
      };
  const midX = (labelSegment.from.x + labelSegment.to.x) / 2;
  const midY = (labelSegment.from.y + labelSegment.to.y) / 2;
  return {
    sx: sourceAnchor.x,
    sy: sourceAnchor.y,
    tx: targetAnchor.x,
    ty: targetAnchor.y,
    midX,
    midY,
    d: path
  };
}

function relationGraphNodeAnchor(node, side) {
  const centerY = node.y + node.height / 2;
  if (side === 'left') {
    return { x: node.x, y: centerY };
  }

  if (side === 'top') {
    return { x: node.x + node.width / 2, y: node.y };
  }

  if (side === 'bottom') {
    return { x: node.x + node.width / 2, y: node.y + node.height };
  }

  return { x: node.x + node.width, y: centerY };
}

function relationGraphEdgeLaneY(source, target, index) {
  const rowGapHalf = 28;
  const laneOffset = ((index % 5) - 2) * 6;
  if (Math.abs(source.y - target.y) < 2) {
    const useAbove = index % 2 === 0 && source.y - rowGapHalf > 28;
    return (useAbove
      ? source.y - rowGapHalf
      : source.y + source.height + rowGapHalf) + laneOffset;
  }

  if (target.y > source.y) {
    return source.y + source.height + rowGapHalf + laneOffset;
  }

  return Math.max(24, source.y - rowGapHalf + laneOffset);
}

function relationGraphPolylinePath(points) {
  const compact = [];
  for (const point of points) {
    const previous = compact[compact.length - 1];
    if (previous && Math.abs(previous.x - point.x) < 0.5 && Math.abs(previous.y - point.y) < 0.5) {
      continue;
    }

    compact.push(point);
  }

  return compact.map((point, index) =>
    `${index === 0 ? 'M' : 'L'} ${Number(point.x).toFixed(1)} ${Number(point.y).toFixed(1)}`).join(' ');
}

function renderRelationGraphNode(node, graphState) {
  const onlineState = relationGraphNodeOnlineState(node);
  const typeLabel = node.nodeType === 'template' ? 'шаблон' : 'ручное';
  const sourceTargetLabel = node.nodeType === 'template'
    ? `${node.sourceClass || 'regex источника не задан'} -> ${node.targetClass || '-'}`
    : `${node.sourceClass || '-'} -> ${node.targetClass || '-'}`;
  const title = relationGraphNodeTitle(node);
  return `
    <article
      class="relation-graph-node ${node.nodeType === 'template' ? 'template' : 'manual'} ${escapeHtml(onlineState.className)}"
      style="left:${node.x}px;top:${node.y}px;width:${node.width}px;height:${node.height}px"
      title="${escapeHtml(title)}"
    >
      <div class="relation-graph-priority" title="Приоритет ${escapeHtml(node.priority)}">P${escapeHtml(node.priority)}</div>
      <strong>${escapeHtml(shortText(node.label || node.id, 54))}</strong>
      <span>${escapeHtml(shortText(sourceTargetLabel, 62))}</span>
      <div class="relation-graph-badges">
        <b>${escapeHtml(typeLabel)}</b>
        ${node.nodeType === 'template' ? `<b>правил ${escapeHtml(node.generatedRuleCount)}</b>` : ''}
        <b>объектов ${escapeHtml(node.objectCount)}</b>
        <b>связей ${escapeHtml(node.runtimeRelationCount)}</b>
        ${onlineState.label ? `<b class="${escapeHtml(onlineState.badgeClass)}">${escapeHtml(onlineState.label)}</b>` : ''}
      </div>
      ${graphState.showRegex ? renderRelationGraphRegexChip(node.regexItems, 'Регулярные выражения узла') : ''}
    </article>
  `;
}

function relationGraphNodeTitle(node) {
  const lines = [
    node.label || node.id,
    node.nodeType === 'template'
      ? `Шаблон: ${node.templateId || '-'}`
      : `Правило: ${node.ruleId || '-'}`,
    `Источник: ${node.sourceClass || '-'}`,
    node.sourceKeyAttribute ? `Ключ источника: ${node.sourceKeyAttribute}` : '',
    `Цель модели: ${node.targetClass || '-'}:${node.objectLookup || '-'}`,
    node.nodeType === 'manual_rule' && node.objectLookup
      ? 'Ручное правило связывает все подходящие source-карточки с этим целевым объектом модели.'
      : '',
    `Ожидаемых объектов: ${node.objectCount}`,
    `Runtime-связей: ${node.runtimeRelationCount}`
  ];
  return lines.filter(Boolean).join('\n');
}

function renderRelationGraphRegexChip(regexItems, title) {
  const items = [...new Set(regexItems ?? [])].map((item) => String(item ?? '').trim()).filter(Boolean);
  if (items.length === 0) {
    return '';
  }

  const first = items[0];
  const suffix = items.length > 1 ? ` +${items.length - 1}` : '';
  return `
    <span class="relation-graph-regex-chip" tabindex="0">
      <span class="relation-graph-regex-chip-text">regex: ${escapeHtml(shortText(first, 28))}${escapeHtml(suffix)}</span>
      ${renderRelationGraphRegexTooltip(items, title)}
    </span>
  `;
}

function renderRelationGraphRegexTooltip(items, title) {
  return `
    <span class="relation-graph-tooltip" role="tooltip">
      <span class="relation-graph-tooltip-title">${escapeHtml(title)}</span>
      ${items.map((item) => `<code>${escapeHtml(item)}</code>`).join('')}
    </span>
  `;
}

function relationGraphNodeOnlineState(node) {
  const online = state.relationGraph.currentOnline;
  if (!online?.enabled || node.objectCount === 0) {
    return { className: '', badgeClass: '', label: '' };
  }

  const stateItem = online.nodeOnlineById.get(node.id);
  if (!stateItem || stateItem.expected === 0) {
    return { className: '', badgeClass: '', label: '' };
  }

  if (stateItem.found === stateItem.expected) {
    return { className: 'online-found', badgeClass: 'online-ok', label: `онлайн ${stateItem.found}/${stateItem.expected}` };
  }

  if (stateItem.found > 0) {
    return { className: 'online-blocked', badgeClass: 'online-warn', label: `онлайн ${stateItem.found}/${stateItem.expected}` };
  }

  return { className: 'online-missing', badgeClass: 'online-error', label: `нет ${stateItem.expected}` };
}

function relationGraphEdgeOnlineState(edge) {
  const online = state.relationGraph.currentOnline;
  if (!online?.enabled) {
    return { className: '', label: '' };
  }

  const stateItem = online.relationStateByEdgeId.get(edge.id);
  if (!stateItem || stateItem.status === 'unknown') {
    return { className: '', label: '' };
  }

  if (stateItem.status === 'found') {
    return { className: 'online-found', label: `онлайн ${stateItem.foundCount}/${stateItem.expectedCount}` };
  }

  if (stateItem.status === 'blocked') {
    return { className: 'online-blocked', label: `нет объекта ${stateItem.blockedCount}` };
  }

  return { className: 'online-missing', label: `нет ${stateItem.missingCount}/${stateItem.expectedCount}` };
}

function relationGraphFindings(graph, online = null) {
  const findings = [];
  const nodeById = graph.nodeById;
  const degree = new Map(graph.nodes.map((node) => [node.id, 0]));
  const missingTargets = graph.edges.filter((edge) => edge.missingTarget);
  for (const edge of graph.edges) {
    if (nodeById.has(edge.sourceId)) {
      degree.set(edge.sourceId, (degree.get(edge.sourceId) ?? 0) + 1);
    }
    if (nodeById.has(edge.targetId)) {
      degree.set(edge.targetId, (degree.get(edge.targetId) ?? 0) + 1);
    }
  }

  if (missingTargets.length > 0) {
    findings.push({
      severity: 'error',
      title: `Отсутствующие целевые шаблоны/ручные правила: ${missingTargets.length}`,
      detail: relationGraphMissingTargetDetails(missingTargets)
    });
  }

  const relationErrors = graph.edges
    .flatMap((edge) => (edge.relationErrors ?? []).map((error) => ({ edge, error })))
    .filter(Boolean);
  if (relationErrors.length > 0) {
    findings.push({
      severity: 'error',
      title: `Ошибки расчета связей времени обработки: ${relationErrors.length}`,
      detail: relationGraphRuntimeErrorDetails(relationErrors)
    });
  }

  const orphans = graph.nodes.filter((node) => node.objectCount > 0 && (degree.get(node.id) ?? 0) === 0);
  if (orphans.length > 0) {
    findings.push({
      severity: 'warn',
      title: `Серые зоны без связей графа: ${orphans.length}`,
      detail: orphans.slice(0, 10).map((node) => `${node.label || node.id} (${node.objectLabel})`).join('; ')
    });
  }

  const priorityFindings = relationGraphPriorityFindings(graph);
  findings.push(...priorityFindings);
  if (online?.enabled) {
    findings.push(...relationGraphOnlineFindings(online));
  }

  const fanIn = relationGraphObjectFanIn(graph)
    .filter((item) => item.nodes.length > 1)
    .sort((left, right) => right.nodes.length - left.nodes.length);
  if (fanIn.length > 0) {
    findings.push({
      severity: 'info',
      title: `Общие управляемые целевые объекты: ${fanIn.length}`,
      detail: fanIn.slice(0, 8).map((item) =>
        `${item.key}: ${item.nodes.length} узлов (${item.nodes.slice(0, 4).map((node) => relationGraphNodeDisplayLabel(node)).join(', ')}${item.nodes.length > 4 ? ', ...' : ''})`).join('; ')
    });
  }

  if (findings.length === 0) {
    findings.push({
      severity: 'ok',
      title: 'Критичных выводов по текущему графу нет.',
      detail: 'Связи имеют целевые шаблоны/ручные правила, а изолированные целевые объекты не обнаружены.'
    });
  }

  return findings;
}

function relationGraphPriorityFindings(graph) {
  const findings = [];
  let samePriority = 0;
  const reversed = [];
  for (const edge of graph.edges.filter((item) => !item.missingTarget)) {
    const endpoints = relationGraphEdgeEndpoints(edge, 'effect');
    const source = graph.nodeById.get(endpoints.sourceId);
    const target = graph.nodeById.get(endpoints.targetId);
    if (!source || !target) {
      continue;
    }

    if (source.priority === target.priority) {
      samePriority += 1;
      continue;
    }

    if (source.priority > target.priority) {
      reversed.push(`${relationGraphNodeDisplayLabel(source)} P${source.priority} -> ${relationGraphNodeDisplayLabel(target)} P${target.priority} (${edge.roleLabel})`);
    }
  }

  if (reversed.length > 0) {
    findings.push({
      severity: 'warn',
      title: `Возможные ошибки направленности по приоритету: ${reversed.length}`,
      detail: reversed.slice(0, 8).join('; ')
    });
  }

  if (samePriority > 0) {
    findings.push({
      severity: 'info',
      title: `Связи с одинаковым приоритетом: ${samePriority}`,
      detail: 'Приоритет не помогает проверить порядок влияния; нужна явная проверка бизнес-направления связи.'
    });
  }

  return findings;
}

function relationGraphMissingTargetDetails(edges) {
  return relationGraphMissingTargetDetailItems(edges).join('; ');
}

function relationGraphMissingTargetDetailItems(edges) {
  return edges.slice(0, 8).map((edge) => {
    const targetLabel = relationGraphEndpointDisplayLabel(edge, 'target');
    const hint = edge.targetKind === 'template'
      ? 'проверьте, что шаблон есть в текущем слое'
      : 'проверьте, что ручное правило есть в текущем слое; для сгенерированного правила связывайте шаблон, а не конкретный rule_id';
    return `${relationGraphEndpointDisplayLabel(edge, 'source')} -> ${targetLabel}: цель не найдена, ${hint}`;
  }).concat(edges.length > 8 ? [`Еще ${edges.length - 8} потерянных целей скрыто.`] : []);
}

function relationGraphRuntimeErrorDetails(items) {
  return relationGraphRuntimeErrorDetailItems(items).join('; ');
}

function relationGraphRuntimeErrorDetailItems(items) {
  const groups = new Map();
  for (const item of items) {
    const normalized = relationGraphRuntimeErrorItem(item);
    const key = stableHash({
      edge: normalized.edge.id,
      message: normalized.message
    });
    if (!groups.has(key)) {
      groups.set(key, {
        edge: normalized.edge,
        message: normalized.message,
        samples: [],
        count: 0
      });
    }

    const group = groups.get(key);
    group.count += 1;
    if (group.samples.length < 4) {
      group.samples.push(normalized);
    }
  }

  const groupList = [...groups.values()];
  return groupList.slice(0, 6).map((group) => {
    const sampleText = group.samples
      .map((sample) => `${relationGraphRuleDisplayLabel(sample.sourceRule)} -> ${relationGraphRuleDisplayLabel(sample.targetRule)}`)
      .join(', ');
    const suffix = group.count > group.samples.length
      ? `, еще ${group.count - group.samples.length}`
      : '';
    return `${relationGraphEdgeDisplayLabel(group.edge)}: ${group.message}. Примеры: ${sampleText}${suffix}`;
  }).concat(groupList.length > 6 ? [`Еще ${groupList.length - 6} групп ошибок скрыто.`] : []);
}

function relationGraphRuntimeErrorItem(item) {
  const edge = item?.edge ?? {};
  const error = item?.error;
  if (error && typeof error === 'object' && !Array.isArray(error)) {
    return {
      edge,
      sourceRule: error.sourceRule ?? null,
      targetRule: error.targetRule ?? null,
      message: String(error.message ?? '').trim() || 'ошибка расчета связи'
    };
  }

  const parsed = relationGraphParseLegacyRuntimeError(String(error ?? ''));
  return {
    edge,
    sourceRule: parsed.sourceRule,
    targetRule: parsed.targetRule,
    message: parsed.message
  };
}

function relationGraphParseLegacyRuntimeError(text) {
  const [pair, ...messageParts] = String(text ?? '').split(': ');
  const [sourceId, targetId] = String(pair ?? '').split(' -> ');
  return {
    sourceRule: sourceId ? { rule_id: sourceId, name: sourceId } : null,
    targetRule: targetId ? { rule_id: targetId, name: targetId } : null,
    message: messageParts.join(': ') || text
  };
}

function relationGraphEdgeDisplayLabel(edge) {
  return `${relationGraphEndpointDisplayLabel(edge, 'source')} -> ${relationGraphEndpointDisplayLabel(edge, 'target')}`;
}

function relationGraphEndpointDisplayLabel(edge, side) {
  const isTarget = side === 'target';
  const kind = isTarget ? edge.targetKind : edge.sourceKind;
  const template = isTarget ? edge.targetTemplate : edge.sourceTemplate;
  const rule = isTarget ? edge.targetRule : edge.sourceRule;
  const nodeId = isTarget ? edge.targetId : edge.sourceId;
  if (kind === 'template') {
    return template
      ? `шаблон "${template.name || template.template_id}" [${template.template_id || '-'}]`
      : `шаблон "${relationGraphRawNodeIdLabel(nodeId)}"`;
  }

  if (kind === 'manual_rule') {
    return rule
      ? relationGraphRuleDisplayLabel(rule, { manualPrefix: true })
      : `правило "${relationGraphRawNodeIdLabel(nodeId)}"`;
  }

  return relationGraphRawNodeIdLabel(nodeId);
}

function relationGraphNodeDisplayLabel(nodeOrId) {
  if (nodeOrId && typeof nodeOrId === 'object' && !Array.isArray(nodeOrId)) {
    const node = nodeOrId;
    if (node.nodeType === 'template') {
      return `шаблон "${node.templateName || node.label || node.templateId}" [${node.templateId || relationGraphRawNodeIdLabel(node.id)}]`;
    }

    if (node.nodeType === 'manual_rule') {
      return `правило "${node.label || node.ruleId}" [${node.ruleId || relationGraphRawNodeIdLabel(node.id)}]`;
    }

    return node.label || relationGraphRawNodeIdLabel(node.id);
  }

  return relationGraphRawNodeIdLabel(nodeOrId);
}

function relationGraphRuleDisplayLabel(rule, options = {}) {
  if (!rule) {
    return 'правило не найдено';
  }

  const ruleId = String(rule.rule_id ?? '').trim();
  const generation = rule.template_generation ?? {};
  if (isGeneratedTemplateRule(rule)) {
    const templateName = String(generation.template_name || rule.generated_from_template || '').trim();
    const dimension = String(generation.dimension_name || generation.dimension_key || '').trim();
    const target = relationGraphRuleTargetLabel(rule);
    return `сгенерированное "${[templateName, dimension].filter(Boolean).join(' / ') || rule.name || ruleId}"${target ? ` (${target})` : ''}`;
  }

  const name = String(rule.name || ruleId || '').trim();
  const prefix = options.manualPrefix ? 'правило ' : '';
  return `${prefix}"${name || 'без имени'}"${ruleId && ruleId !== name ? ` [${ruleId}]` : ''}`;
}

function relationGraphRuleTargetLabel(rule) {
  const classCode = ruleTargetClassCode(rule);
  const lookup = String(rule?.target?.card_id
    || rule?.target?.idempotency_key
    || rule?.target?.attribute_mappings?.Code
    || rule?.target?.attribute_mappings?.code
    || '').trim();
  return [classCode, lookup].filter(Boolean).join(':');
}

function relationGraphRawNodeIdLabel(value) {
  const text = String(value ?? '').trim();
  const separator = text.indexOf(':');
  return separator >= 0 ? text.slice(separator + 1) || text : text || '-';
}

function relationGraphOnlineFindings(online) {
  const findings = [];
  if (online.error) {
    findings.push({
      severity: 'error',
      title: 'Онлайн-сверка не выполнена.',
      detail: online.error
    });
    return findings;
  }

  if (!online.checkedAt) {
    findings.push({
      severity: 'info',
      title: 'Онлайн-сверка еще не загружена.',
      detail: 'Включите слой и нажмите "Обновить онлайн", чтобы сравнить граф с CMDBuild.'
    });
    return findings;
  }

  if (online.missingObjects.length > 0) {
    findings.push({
      severity: 'error',
      title: `CMDBuild: отсутствуют целевые объекты правил: ${online.missingObjects.length}`,
      detail: online.missingObjects.slice(0, 10).map((item) =>
        `${item.node.label || item.node.id} -> ${item.object.classCode}:${item.object.lookup}`).join('; ')
    });
  }

  if (online.missingRelationCount > 0) {
    const samples = online.relationStates
      .filter((item) => item.status === 'missing')
      .slice(0, 10)
      .map((item) => `${relationGraphEdgeDisplayLabel(item.edge)} (${item.expected?.domainCode || '-'})`);
    findings.push({
      severity: 'error',
      title: `CMDBuild: отсутствуют domain-связи: ${online.missingRelationCount}`,
      detail: samples.join('; ')
    });
  }

  if (online.blockedRelationCount > 0) {
    findings.push({
      severity: 'warn',
      title: `Связи не проверить из-за отсутствующих объектов: ${online.blockedRelationCount}`,
      detail: 'Сначала нужно создать или найти управляемые целевые объекты правил-источников и правил-назначений.'
    });
  }

  if (online.extraObjects.length > 0) {
    findings.push({
      severity: 'info',
      title: `CMDBuild: управляемые объекты без видимого правила графа: ${online.extraObjects.length}`,
      detail: online.extraObjects.slice(0, 10).map((card) =>
        `${targetCardDisplayLabel(card, card.classCode)}`).join('; ')
    });
  }

  if (findings.length === 0) {
    findings.push({
      severity: 'ok',
      title: 'Онлайн-сверка CMDBuild без расхождений.',
      detail: `${online.foundObjectCount} объектов и ${online.foundRelationCount} связей найдены по текущему графу.`
    });
  }

  return findings;
}

function relationGraphObjectFanIn(graph) {
  const byObject = new Map();
  for (const node of graph.nodes) {
    for (const object of node.expectedObjects ?? []) {
      if (!object.key) {
        continue;
      }

      if (!byObject.has(object.key)) {
        byObject.set(object.key, { key: object.key, nodes: [] });
      }
      byObject.get(object.key).nodes.push(node);
    }
  }

  return [...byObject.values()];
}

function renderRelationGraphFindings(findings) {
  return `
    <div class="relation-graph-finding-list">
      ${findings.map((finding) => `
        <div class="relation-graph-finding ${escapeHtml(finding.severity)}">
          <strong>${escapeHtml(finding.title)}</strong>
          <span>${escapeHtml(finding.detail || '')}</span>
        </div>
      `).join('')}
    </div>
  `;
}

function relationGraphSearchText(parts) {
  return parts.map((part) => String(part ?? '').trim()).filter(Boolean).join(' ');
}

function shortText(value, maxLength = 80) {
  const text = String(value ?? '');
  return text.length <= maxLength ? text : `${text.slice(0, Math.max(0, maxLength - 1))}…`;
}

function linkRelationMatchesKind(relation, kindConfig) {
  if (kindConfig.sourceType === 'mixed' || kindConfig.targetType === 'mixed') {
    return linkRelationMatchesPair(relation, 'template', 'rule')
      || linkRelationMatchesPair(relation, 'rule', 'template');
  }

  const targetType = kindConfig.targetType;
  const relationKind = kindConfig.relationKind || targetType;
  if (relation.kind !== relationKind) {
    return false;
  }

  if (targetType === 'template') {
    return Boolean(relation.target_template_id);
  }

  return Boolean(relation.target_rule_id);
}

function linkRelationMatchesPair(relation, sourceType, targetType) {
  if (!relation || typeof relation !== 'object' || Array.isArray(relation)) {
    return false;
  }

  if (targetType === 'template') {
    return relation.kind === 'template' && Boolean(relation.target_template_id);
  }

  if (targetType === 'rule') {
    return relation.kind === 'rule' && Boolean(relation.target_rule_id);
  }

  return false;
}

function linkRelationTargetId(relation, targetType) {
  return targetType === 'template'
    ? relation.target_template_id || ''
    : relation.target_rule_id || '';
}

function linkRelationEntityTypeLabel(type) {
  const normalized = String(type ?? '').trim();
  if (normalized === 'template') {
    return 'шаблон';
  }

  if (normalized === 'rule') {
    return 'правило';
  }

  return normalized || 'объект';
}

function linkRelationRoleLabel(role) {
  const labels = {
    uses: 'Использует',
    depends_on: 'Зависит от',
    contains: 'Содержит',
    suppresses: 'Подавляет',
    impacts: 'Влияет на'
  };
  return labels[String(role ?? '').trim()] || String(role ?? '').trim() || 'Использует';
}

function linkRelationRoleOptions(layerKey) {
  const options = [
    { value: 'uses', label: 'Использует' },
    { value: 'depends_on', label: 'Зависит от' },
    { value: 'contains', label: 'Содержит' },
    { value: 'suppresses', label: 'Подавляет' },
    { value: 'impacts', label: 'Влияет на' }
  ];
  if (layerKey === 'suppression') {
    return options.filter((option) => option.value !== 'contains');
  }

  return options;
}

function assertLinkRelationRoleAllowed(layerKey, role) {
  const allowed = new Set(linkRelationRoleOptions(layerKey).map((option) => option.value));
  if (!allowed.has(role)) {
    throw new Error(`Тип связи "${linkRelationRoleLabel(role)}" недоступен для слоя ${layerHumanLabel(layerKey)}: в CMDBuild-схеме нет соответствующего domain.`);
  }
}

function linkRelationMatchLabel(relation) {
  const match = relation?.attributes?.match;
  if (!match || typeof match !== 'object' || Array.isArray(match)) {
    return '';
  }

  const source = String(match.source_variable ?? '').trim();
  const target = String(match.target_variable ?? '').trim();
  const sourcePattern = String(match.source_pattern ?? match.pattern ?? '').trim();
  const targetPattern = String(match.target_pattern ?? '').trim();
  const sourceFilters = linkRelationSourceFiltersFromMatch(match);
  const targetFilters = linkRelationTargetFiltersFromMatch(match);
  const labels = [];
  if (sourceFilters.length > 0) {
    const title = target || targetFilters.length > 0
      ? 'Отбор источника'
      : 'Отбор правил шаблона';
    labels.push(`${title}: ${linkRelationFiltersLabel(sourceFilters)}`);
  }

  if (targetFilters.length > 0) {
    labels.push(`Отбор цели: ${linkRelationFiltersLabel(targetFilters)}`);
  }

  if (target) {
    const compareLabel = sourcePattern || targetPattern
      ? `Сопоставление: ${source} / ${sourcePattern || 'целиком'} = ${target} / ${targetPattern || 'целиком'}`
      : `Сопоставление: ${source} = ${target}`;
    labels.push(compareLabel);
    return labels.join(' · ');
  }

  if (labels.length > 0) {
    return labels.join(' · ');
  }

  return sourcePattern
    ? `Отбор правил шаблона: ${source}, regex ${sourcePattern}`
    : `Отбор правил шаблона: ${source}, все значения`;
}

function linkRelationFiltersLabel(filters) {
  return filters.map((filter) =>
    `${filter.mode === 'exclude' ? 'Исключить' : 'Включить'} ${filter.variable} / ${filter.regex}`).join('; ');
}

function applyLinkRelationEditorChange() {
  const context = state.linkRelationContext ?? { layer: 'service', kind: 'template_template' };
  const kindConfig = linkRelationKindConfig(context.kind, context.layer);
  const config = linkRelationEditorConfig();
  const sourceEntity = selectedLinkRelationEntity(config.source?.value, kindConfig.sourceType);
  const targetEntity = selectedLinkRelationEntity(config.target?.value, kindConfig.targetType);
  const sourceId = sourceEntity.id;
  const targetId = targetEntity.id;
  const relationRole = String(config.role?.value ?? 'uses').trim() || 'uses';
  const description = String(config.description?.value ?? '').trim();

  try {
    assertLinkRelationRoleAllowed(context.layer, relationRole);
    if (!sourceId || !targetId) {
      throw new Error('Выберите источник и цель связи.');
    }

    const effectiveSourceType = sourceEntity.type || kindConfig.sourceType;
    const effectiveTargetType = targetEntity.type || kindConfig.targetType;
    if (kindConfig.allowedPairs?.length) {
      const pair = `${effectiveSourceType}:${effectiveTargetType}`;
      if (!kindConfig.allowedPairs.includes(pair)) {
        throw new Error('Для связи шаблон-правило выберите один шаблон и одно правило. Направление задается полями Источник и Назначение.');
      }
    }

    if (effectiveSourceType === effectiveTargetType && sourceId === targetId) {
      throw new Error('Источник и цель связи должны быть разными.');
    }

    if ((effectiveSourceType === 'rule' && isGenericRuleId(sourceId))
      || (effectiveTargetType === 'rule' && isGenericRuleId(targetId))) {
      throw new Error('Связь шаблон-правило должна ссылаться на конкретное ручное правило, а не на заглушку "rule". Выберите правило из списка заново.');
    }

    assertLinkRelationEntityExists(context.layer, effectiveSourceType, sourceId, 'источник');
    assertLinkRelationEntityExists(context.layer, effectiveTargetType, targetId, 'цель');
    assertLinkRelationDomainAvailable(context.layer, effectiveSourceType, sourceId, effectiveTargetType, targetId, relationRole);
    const effectiveKindConfig = {
      ...kindConfig,
      sourceType: effectiveSourceType,
      targetType: effectiveTargetType,
      relationKind: effectiveTargetType
    };
    const match = readLinkRelationMatchValues(context, effectiveKindConfig, sourceEntity, targetEntity);
    const attributes = {
      ...(description ? { description } : {}),
      ...(match ? { match } : {})
    };
    const relation = {
      kind: effectiveTargetType,
      relation_role: relationRole,
      target_template_id: effectiveTargetType === 'template' ? targetId : '',
      target_rule_id: effectiveTargetType === 'rule' ? targetId : '',
      attributes
    };

    if (effectiveSourceType === 'template') {
      addTemplateLinkRelation(context.layer, sourceId, relation);
    } else {
      addRuleLinkRelation(context.layer, sourceId, relation);
    }

    if (config.description) {
      config.description.value = '';
    }
    setLinkRelationStatus('Связь добавлена.');
  } catch (error) {
    setLinkRelationStatus(error.message, 'error');
  }
}

function assertLinkRelationEntityExists(layerKey, type, id, label) {
  if (!linkRelationEntities(layerKey, type).some((item) => item.id === id)) {
    const layerLabel = layerKey === 'service' ? 'сервисного слоя' : 'каскадного подавления';
    throw new Error(`${label} связи не найден среди объектов ${layerLabel}.`);
  }
}

function assertLinkRelationDomainAvailable(layerKey, sourceType, sourceId, targetType, targetId, relationRole) {
  const sourceClass = linkRelationEntityTargetClassCode(layerKey, sourceType, sourceId);
  const targetClass = linkRelationEntityTargetClassCode(layerKey, targetType, targetId);
  if (!sourceClass || !targetClass) {
    throw new Error('Не удалось определить целевые классы источника и назначения связи.');
  }

  const relationType = managedRelationTypeForRole(layerKey, relationRole, targetClass);
  const domain = managedRelationDomainForTargets(layerKey, sourceClass, targetClass, relationRole);
  if (domain) {
    return;
  }

  throw new Error(`Для связи ${linkRelationEntityTypeLabel(sourceType)} "${linkRelationEntityDisplayName(layerKey, sourceType, sourceId)}" -> ${linkRelationEntityTypeLabel(targetType)} "${linkRelationEntityDisplayName(layerKey, targetType, targetId)}" нет CMDBuild domain (${sourceClass} -> ${targetClass}, тип "${linkRelationRoleLabel(relationRole)}", relationType "${relationType}"). Выберите другой тип связи или добавьте domain в схему.`);
}

function linkRelationEntityTargetClassCode(layerKey, type, id) {
  if (type === 'template') {
    return String(findLayerTemplate(layerKey, id)?.target?.class_code ?? '').trim();
  }

  if (type === 'rule') {
    return ruleTargetClassCode(findLayerRule(layerKey, id));
  }

  return '';
}

function findLayerRule(layerKey, ruleId) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return null;
  }

  return (parsed.document.rules ?? []).find((rule) => rule.rule_id === ruleId) ?? null;
}

function linkRelationEntityDisplayName(layerKey, type, id) {
  if (type === 'template') {
    const template = findLayerTemplate(layerKey, id);
    return templateHumanLabel(template);
  }

  if (type === 'rule') {
    const rule = findLayerRule(layerKey, id);
    return rule?.name || rule?.rule_id || id;
  }

  return id;
}

function readLinkRelationMatchValues(context, kindConfig, sourceEntity = null, targetEntity = null) {
  if (kindConfig.sourceType !== 'template' && kindConfig.targetType !== 'template') {
    return null;
  }

  const config = linkRelationEditorConfig();
  const sourceVariable = String(config.sourceVariable?.value ?? '').trim();
  const targetVariable = String(config.targetVariable?.value ?? '').trim();
  const sourcePattern = String(config.sourceMatchRegex?.value ?? '').trim();
  const targetPattern = String(config.targetMatchRegex?.value ?? '').trim();

  if (kindConfig.targetType === 'rule') {
    const filters = readLinkRelationFilters(
      config.sourceFilterList,
      context.layer,
      sourceEntity?.id || sourceIdFromConfig(config),
      'шаблона-источника',
      'source');
    if (filters.length === 0) {
      return null;
    }

    return {
      mode: 'source_filters',
      filters
    };
  }

  if (kindConfig.sourceType === 'rule' && kindConfig.targetType === 'template') {
    const filters = readLinkRelationFilters(
      config.targetFilterList,
      context.layer,
      targetEntity?.id || String(config.target?.value ?? '').trim(),
      'целевого шаблона',
      'target');
    if (filters.length === 0) {
      return null;
    }

    return {
      mode: 'target_filters',
      target_filters: filters
    };
  }

  if (kindConfig.targetType !== 'template') {
    return null;
  }

  if (!sourceVariable) {
    throw new Error('Для связи с шаблоном выберите переменную шаблона-источника.');
  }

  if (!targetVariable) {
    throw new Error('Для связи с шаблоном выберите переменную целевого шаблона.');
  }

  if (sourcePattern) {
    assertValidRegexPattern(sourcePattern);
  }

  if (targetPattern) {
    assertValidRegexPattern(targetPattern);
  }

  const sourceFilters = readLinkRelationFilters(
    config.sourceFilterList,
    context.layer,
    sourceIdFromConfig(config),
    'шаблона-источника',
    'source');
  const targetFilters = readLinkRelationFilters(
    config.targetFilterList,
    context.layer,
    String(config.target?.value ?? '').trim(),
    'целевого шаблона',
    'target');

  return {
    mode: sourcePattern || targetPattern ? 'regex_compare' : 'exact',
    ...(sourceFilters.length ? { source_filters: sourceFilters } : {}),
    ...(targetFilters.length ? { target_filters: targetFilters } : {}),
    source_variable: sourceVariable,
    target_variable: targetVariable,
    source_pattern: sourcePattern,
    target_pattern: targetPattern
  };
}

function sourceIdFromConfig(config) {
  return String(config.source?.value ?? '').trim();
}

function readLinkRelationFilters(list, layerKey, templateId, label, side = 'source') {
  const rows = [];
  const seen = new Set();
  const variableOptions = linkRelationFilterVariableOptions(layerKey, templateId);
  for (const values of linkRelationFilterRowsFromDom(list, side)) {
    if (!values.variable && !values.regex) {
      continue;
    }

    if (!values.variable || !values.regex) {
      throw new Error(`В отборе ${label} заполните переменную шаблона и regexp.`);
    }

    if (!variableOptions.some((option) => canonicalToken(option.value) === canonicalToken(values.variable))) {
      throw new Error(`Переменная шаблона ${values.variable} не найдена среди переменных ${label}.`);
    }

    assertValidRegexPattern(values.regex);
    const key = `${values.mode}\u0000${canonicalToken(values.variable)}\u0000${values.regex}`;
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    rows.push(values);
  }

  return rows;
}

function linkRelationFilterRowsFromDom(list, side = 'source') {
  const prefix = side === 'target' ? 'target' : 'source';
  return [...(list?.querySelectorAll(`[data-link-${prefix}-filter-row]`) ?? [])]
    .map((row) => linkRelationFilterDomRowValues(row, side));
}

function linkRelationFilterDomRowValues(row, side = 'source') {
  const prefix = side === 'target' ? 'target' : 'source';
  return {
    mode: row.querySelector(`[data-link-${prefix}-filter-mode]`)?.value === 'exclude' ? 'exclude' : 'include',
    variable: row.querySelector(`[data-link-${prefix}-filter-variable]`)?.value.trim() ?? '',
    regex: row.querySelector(`[data-link-${prefix}-filter-regex]`)?.value.trim() ?? ''
  };
}

function addTemplateLinkRelation(layerKey, templateId, relation) {
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const template = document.templates.find((item) => item.template_id === templateId);
  if (!template) {
    throw new Error(`Шаблон ${templateId} не найден.`);
  }

  const previousTemplate = cloneJson(template);
  const normalizedRelation = normalizeTemplateManagedRelation(relation, template, layerKey);
  if (!normalizedRelation.managed_key) {
    throw new Error('Не удалось сформировать стабильный ключ связи.');
  }

  if ((template.managed_relations ?? []).some((item) => item.managed_key === normalizedRelation.managed_key)) {
    throw new Error('Такая связь уже существует.');
  }

  template.managed_relations = (template.managed_relations ?? []).concat([normalizedRelation]);
  const changeMode = templateChangeMode(previousTemplate, template);
  template.version = nextTemplateVersion(previousTemplate, changeMode);
  template.lifecycle = templateLifecycleMetadata(template, changeMode);
  appendTemplateVersionSnapshot(document, template, previousTemplate, changeMode);
  state.templateDocuments[layerKey] = document;
  renderTemplateEditor(layerKey);
  renderTemplateAuditView();
  renderConversionConfigSyncView();
}

function addRuleLinkRelation(layerKey, ruleId, relation) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    throw new Error(parsed.error);
  }

  const rule = parsed.document.rules.find((item) => item.rule_id === ruleId);
  if (!rule) {
    throw new Error(`Правило ${ruleId} не найдено.`);
  }

  const normalizedRelation = normalizeRuleManagedRelation(relation, rule, layerKey);
  if (!normalizedRelation.managed_key) {
    throw new Error('Не удалось сформировать стабильный ключ связи.');
  }

  if ((rule.managed_relations ?? []).some((item) => item.managed_key === normalizedRelation.managed_key)) {
    throw new Error('Такая связь уже существует.');
  }

  rule.managed_relations = (rule.managed_relations ?? []).concat([normalizedRelation]);
  writeRuleDocument(layerKey, parsed.document);
}

function deleteLinkRelation(layerKey, kind, sourceId, managedKey, sourceType = '') {
  const kindConfig = linkRelationKindConfig(kind, layerKey);
  try {
    if (!sourceId || !managedKey) {
      throw new Error('Связь для удаления не определена.');
    }

    const effectiveSourceType = kindConfig.sourceType === 'mixed'
      ? String(sourceType ?? '').trim()
      : kindConfig.sourceType;
    if (effectiveSourceType === 'template') {
      deleteTemplateLinkRelation(layerKey, sourceId, managedKey);
    } else {
      deleteRuleLinkRelation(layerKey, sourceId, managedKey);
    }

    setLinkRelationStatus('Связь удалена.');
  } catch (error) {
    setLinkRelationStatus(error.message, 'error');
  }
}

function deleteTemplateLinkRelation(layerKey, templateId, managedKey) {
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const template = document.templates.find((item) => item.template_id === templateId);
  if (!template) {
    throw new Error(`Шаблон ${templateId} не найден.`);
  }

  const previousTemplate = cloneJson(template);
  const nextRelations = (template.managed_relations ?? []).filter((relation) => relation.managed_key !== managedKey);
  if (nextRelations.length === (template.managed_relations ?? []).length) {
    throw new Error('Связь не найдена.');
  }

  template.managed_relations = nextRelations;
  const changeMode = templateChangeMode(previousTemplate, template);
  template.version = nextTemplateVersion(previousTemplate, changeMode);
  template.lifecycle = templateLifecycleMetadata(template, changeMode);
  appendTemplateVersionSnapshot(document, template, previousTemplate, changeMode);
  state.templateDocuments[layerKey] = document;
  renderTemplateEditor(layerKey);
  renderTemplateAuditView();
  renderConversionConfigSyncView();
}

function deleteRuleLinkRelation(layerKey, ruleId, managedKey) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    throw new Error(parsed.error);
  }

  const rule = parsed.document.rules.find((item) => item.rule_id === ruleId);
  if (!rule) {
    throw new Error(`Правило ${ruleId} не найдено.`);
  }

  const nextRelations = (rule.managed_relations ?? []).filter((relation) => relation.managed_key !== managedKey);
  if (nextRelations.length === (rule.managed_relations ?? []).length) {
    throw new Error('Связь не найдена.');
  }

  rule.managed_relations = nextRelations;
  writeRuleDocument(layerKey, parsed.document);
}

function setLinkRelationStatus(message, type = '') {
  state.linkRelationStatus = { message, type };
  renderLinkRelationEditor();
}

function ruleValuesFromRule(rule) {
  return {
    keyField: rule.source?.key_attribute || rule.when?.fieldExists || '',
    selectionFilters: selectionFiltersFromRule(rule),
    mappings: rule.target?.attribute_mappings ?? {}
  };
}

function renderTemplateApplyView() {
  const plan = templateMaterializationPlan('service', { safe: true });
  const suppressionPlan = templateMaterializationPlan('suppression', { safe: true });
  const serviceCount = document.querySelector('#templateApplyServiceCount');
  const suppressionCount = document.querySelector('#templateApplySuppressionCount');
  const ruleCount = document.querySelector('#templateApplyRuleCount');
  const relationCount = document.querySelector('#templateApplyRelationCount');
  const candidateCount = document.querySelector('#templateApplyCandidateCount');
  const status = document.querySelector('#templateApplyStatus');
  const list = document.querySelector('#templateApplyPlanList');
  const applyButton = document.querySelector('#applyTemplatesButton');
  const auditButton = document.querySelector('#runTemplateAuditButton');
  if (!serviceCount || !suppressionCount || !ruleCount || !relationCount || !candidateCount || !status || !list) {
    return;
  }

  const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates;
  const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates;
  serviceCount.textContent = String(serviceTemplates.length);
  suppressionCount.textContent = String(suppressionTemplates.length);
  ruleCount.textContent = String(plan.generatedRules.length + suppressionPlan.generatedRules.length);
  relationCount.textContent = String(templateGeneratedRelationCount(plan) + templateGeneratedRelationCount(suppressionPlan));
  candidateCount.textContent = String(plan.candidateCount + suppressionPlan.candidateCount);
  if (applyButton) {
    const canApply = templateAuditCanApply();
    applyButton.disabled = !canApply;
    applyButton.title = canApply
      ? ''
      : templateAuditGateMessage();
  }
  if (auditButton) {
    auditButton.disabled = state.templateAudit.checking;
    auditButton.textContent = state.templateAudit.checking ? 'Проверка...' : 'Проверить шаблоны';
  }
  status.textContent = state.templateApplyError || state.templateApplyMessage || templateAuditGateMessage();
  status.classList.toggle('error', Boolean(state.templateApplyError || templateAuditBlockingErrors().length > 0));
  const auditResult = state.templateAudit.result;
  list.innerHTML = [
    renderTemplateAuditGateCard(),
    auditResult ? renderTemplateAuditLayer('service', auditResult.service) : '',
    auditResult ? renderTemplateAuditLayer('suppression', auditResult.suppression) : '',
    renderTemplateApplyLastResultCard(),
    renderTemplateApplicationCard('service'),
    renderTemplateApplicationCard('suppression'),
    renderTemplatePlanErrorsCard('service', plan),
    renderTemplatePlanErrorsCard('suppression', suppressionPlan),
    renderTemplateDeletionPlansCard('service'),
    renderTemplateDeletionPlansCard('suppression'),
    ...(auditResult ? [] : renderTemplatePlanCards('service', plan)),
    ...(auditResult ? [] : renderTemplatePlanCards('suppression', suppressionPlan)),
    renderCurrentGeneratedRulesCard('service'),
    renderCurrentGeneratedRulesCard('suppression')
  ].filter(Boolean).join('')
    || '<div class="empty-state">Шаблоны не настроены или нет подходящих классов-источников.</div>';
}

function renderTemplatePlanErrorsCard(layerKey, plan) {
  if (!plan.errors?.length) {
    return '';
  }

  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey === 'service' ? 'сервис' : 'подавление')} ошибки шаблонов</span>
      <strong>${escapeHtml(plan.errors.length)} ошибок</strong>
      <span>${escapeHtml(plan.errors.join('; '))}</span>
    </div>
  `;
}

function renderTemplateDeletionPlansCard(layerKey) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return '';
  }

  const plans = (parsed.document.templateDeletionPlans ?? [])
    .filter((plan) => plan.status !== 'done');
  if (plans.length === 0) {
    return '';
  }

  const targets = plans.flatMap((plan) => plan.targets ?? []);
  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey === 'service' ? 'сервис' : 'подавление')} ожидающие планы удаления</span>
      <strong>${escapeHtml(plans.length)} планов · ${escapeHtml(targets.length)} целей</strong>
      <span>${escapeHtml(plans.map((plan) => `${plan.template_id || '-'}: ${plan.reason || '-'} · ${plan.status || '-'}`).join('; '))}</span>
      <span>${escapeHtml(targets.map((target) => `${target.source_class_code || '-'} -> ${target.target_class_code || '-'} · ${target.idempotency_key || target.card_id || '-'}`).join(', ') || '-')}</span>
    </div>
  `;
}

function renderTemplateApplyLastResultCard() {
  const result = state.templateApplyLastResult;
  if (!result) {
    return '';
  }

  return `
    <div class="rule-summary preview-linked">
      <span class="structure-mark">последнее применение</span>
      <strong>${escapeHtml(formatCacheTimestamp(result.appliedAt) || result.appliedAt)}</strong>
      <span>сервис: ${escapeHtml(templateApplyLayerResultText(result.service))}</span>
      <span>подавление: ${escapeHtml(templateApplyLayerResultText(result.suppression))}</span>
    </div>
  `;
}

function templateApplyLayerResultText(result) {
  const reconcile = result?.reconcile ?? {};
  return [
    `шаблонов ${result?.templates ?? 0}`,
    `кандидатов ${result?.candidates ?? 0}`,
    `правил ${result?.generatedRules?.length ?? 0}`,
    `связей ${result?.relations?.total ?? 0}`,
    `создано ${reconcile.created ?? 0}`,
    `обновлено ${reconcile.updated ?? 0}`,
    `без изменений ${reconcile.unchanged ?? 0}`,
    `снято ${reconcile.removed ?? 0}`,
    relationReconcileText(result?.relations)
  ].join(' · ');
}

function renderTemplateApplicationCard(layerKey) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return '';
  }

  const applications = Array.isArray(parsed.document.templateApplications)
    ? parsed.document.templateApplications
    : [];
  const application = applications.at(-1);
  if (!application) {
    return '';
  }

  const reconcile = application.reconcile ?? {};
  const relationSummary = relationReconcileSummary(reconcile.relations);
  const generatedRules = (application.templates ?? [])
    .flatMap((template) => template.generated_rules ?? []);
  const candidates = (application.templates ?? [])
    .flatMap((template) => template.candidates ?? []);
  const relationExamples = generatedRules
    .flatMap((rule) => (rule.relations ?? []).map((relation) =>
      `${rule.rule_id || '-'} -> ${relation.target_lookup || relation.target_class_code || '-'} (${relation.domain_code || '-'})`))
    .slice(0, 8);

  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey === 'service' ? 'сервис' : 'подавление')} применение</span>
      <strong>${escapeHtml(formatCacheTimestamp(application.applied_at) || application.applied_at || '')}</strong>
      <span>создано ${escapeHtml(reconcile.created ?? 0)} · обновлено ${escapeHtml(reconcile.updated ?? 0)} · без изменений ${escapeHtml(reconcile.unchanged ?? 0)} · снято ${escapeHtml(reconcile.removed ?? 0)}</span>
      <span>${escapeHtml(relationReconcileText(relationSummary))}</span>
      <span>кандидаты: ${escapeHtml(candidates.join(', ') || '-')}</span>
      <span>правила: ${escapeHtml(generatedRules.map((rule) => `${rule.rule_id} (${rule.source_class_code} -> ${rule.target_class_code})`).join(', ') || '-')}</span>
      <span>связи: ${escapeHtml(relationExamples.join(', ') || '-')}</span>
    </div>
  `;
}

function renderTemplateAuditView() {
  renderTemplateApplyView();
}

function renderTemplatePlanCards(layerKey, plan) {
  const reconcile = plan.reconcile ?? {};
  return plan.templates.map((item) => `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey === 'service' ? 'сервис' : 'подавление')} шаблон</span>
      <strong>${escapeHtml(item.template.name || item.template.template_id)}</strong>
      <span>кандидатов-источников ${item.candidates.length} · сгенерированных правил ${item.rules.length} · материализованных связей ${templateGeneratedRelationCount({ generatedRules: item.rules })}</span>
      <span>${escapeHtml(templatePlanDimensionText(item))}</span>
      <span>сверка слоя: создать ${escapeHtml(reconcile.created ?? 0)} · обновить ${escapeHtml(reconcile.updated ?? 0)} · без изменений ${escapeHtml(reconcile.unchanged ?? 0)} · снять ${escapeHtml(reconcile.removed ?? 0)}</span>
      <span>${escapeHtml(relationReconcileText(reconcile.relations))}</span>
      <span>кандидаты: ${escapeHtml(item.candidates.map((candidate) => candidate.code).join(', ') || '-')}</span>
      <span>правила: ${escapeHtml(item.rules.map((rule) => `${rule.rule_id} (${ruleSourceClassCode(rule)} -> ${ruleTargetClassCode(rule)})`).join(', ') || '-')}</span>
      <span>цель ${escapeHtml(item.template.target?.class_code || '')} · regex источника ${escapeHtml(item.template.source_class_regex || 'все')}</span>
      ${plan.errors?.length ? `<span>ошибки: ${escapeHtml(plan.errors.join('; '))}</span>` : ''}
    </div>
  `);
}

function templatePlanDimensionText(item) {
  const dimension = templatePopulationDimension(item.template);
  if (!isTemplateDimensionMaterialized(dimension)) {
    return 'измерение: режим совместимости, population_dimension отсутствует; будет одно сгенерированное правило на класс-источник';
  }

  const examples = item.rules
    .map((rule) => rule.template_generation?.dimension_name || rule.template_generation?.dimension_key || '')
    .filter(Boolean)
    .slice(0, 6);
  return `измерение: ${dimension.type}, поле ${dimension.source_field || dimension.condition_field || '-'}, значений ${item.rules.length}, примеры ${examples.join(', ') || '-'}`;
}

function renderCurrentGeneratedRulesCard(layerKey) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return '';
  }

  const rules = (parsed.document.rules ?? []).filter((rule) => rule.generated_from_template);
  if (rules.length === 0) {
    return '';
  }

  const relationCount = rules.reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0);
  const relationExamples = rules
    .flatMap((rule) => runtimeRelationsFromRule(rule).map((relation) =>
      `${rule.rule_id || '-'} -> ${relation.target_lookup || relation.target_class_code || '-'} (${relation.domain_code || '-'})`))
    .slice(0, 10);

  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey === 'service' ? 'сервис' : 'подавление')} · сгенерированные правила</span>
      <strong>${escapeHtml(rules.length)} активных сгенерированных правил</strong>
      <span>материализованных связей: ${escapeHtml(relationCount)}</span>
      <span>${escapeHtml(rules.map((rule) => `${rule.rule_id} (${ruleSourceClassCode(rule)} -> ${ruleTargetClassCode(rule)})`).join(', '))}</span>
      <span>связи: ${escapeHtml(relationExamples.join(', ') || '-')}</span>
    </div>
  `;
}

async function runTemplateAudit(options = {}) {
  if (state.templateAudit.checking) {
    return state.templateAudit.result;
  }

  state.templateAudit.checking = true;
  state.templateAudit.message = 'Подгрузка карточек-источников и расчет шаблонов...';
  state.templateAudit.error = '';
  if (options.render !== false) {
    renderTemplateAuditView();
    renderTemplateApplyView();
  }

  try {
    const syncedDrafts = options.syncDrafts === false
      ? []
      : syncDirtyTemplateEditorDraftsBeforeApply();
    await ensureTemplateMaterializationSourceCards('service', { safe: true });
    await ensureTemplateMaterializationSourceCards('suppression', { safe: true });
    const result = buildTemplateAuditResult();
    const blockingErrors = templateAuditBlockingErrors(result);
    const syncedText = syncedDrafts.length > 0
      ? ` Сохранены измененные шаблоны: ${syncedDrafts.map((item) => `${item.layer}:${item.templateId}`).join(', ')}.`
      : '';
    state.templateAudit.result = result;
    state.templateAudit.checkedAt = result.checkedAt;
    state.templateAudit.fingerprint = templateAuditFingerprint();
    state.templateAudit.message = blockingErrors.length > 0
      ? `Проверка завершена: ${blockingErrors.length} блокирующих ошибок.${syncedText}`
      : `Проверка завершена: блокирующих ошибок нет.${syncedText}`;
    state.templateAudit.error = '';
    return result;
  } catch (error) {
    state.templateAudit.error = error.message;
    state.templateAudit.message = '';
    state.templateAudit.result = null;
    state.templateAudit.checkedAt = '';
    state.templateAudit.fingerprint = '';
    return null;
  } finally {
    state.templateAudit.checking = false;
    if (options.render !== false) {
      renderTemplateAuditView();
      renderTemplateApplyView();
      renderConversionConfigSyncView();
    }
  }
}

function buildTemplateAuditResult() {
  const checkedAt = new Date().toISOString();
  const service = templateAuditForLayer('service');
  const suppression = templateAuditForLayer('suppression');
  const warnings = service.warnings.concat(suppression.warnings);
  const errors = service.errors.concat(suppression.errors);
  return {
    checkedAt,
    service,
    suppression,
    templates: service.templates + suppression.templates,
    candidates: service.candidates + suppression.candidates,
    generatedRules: service.generatedRules + suppression.generatedRules,
    generatedRelations: service.generatedRelations + suppression.generatedRelations,
    warnings,
    errors,
    hasBlockingErrors: errors.length > 0
  };
}

function templateAuditForLayer(layerKey) {
  const templateDocument = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const enabledTemplates = templateDocument.templates.filter((template) => template.enabled !== false);
  const plan = templateMaterializationPlan(layerKey, { safe: true });
  const parsed = parseRuleDocument(layerKey);
  const document = parsed.ok ? parsed.document : defaultRuleDocument(layerKey);
  const currentGeneratedRules = (document.rules ?? [])
    .filter((rule) => rule.generated_from_template)
    .map((rule) => enrichGeneratedRuleOwnership(rule, layerKey));
  const currentByKey = new Map(currentGeneratedRules.map((rule) => [generatedRuleManagedKeyFromRule(rule, layerKey), rule]));
  const desiredByKey = new Map(plan.generatedRules
    .map((rule) => enrichGeneratedRuleOwnership(rule, layerKey))
    .map((rule) => [generatedRuleManagedKeyFromRule(rule, layerKey), rule]));
  const planByTemplate = new Map(plan.templates.map((item) => [item.template.template_id, item]));
  const errors = uniqueTextList(parsed.ok ? plan.errors : [parsed.error].concat(plan.errors));
  const planWarnings = uniqueTextList(plan.warnings ?? []);
  const warnings = [];
  const duplicateMessages = templateAuditDuplicateMessages(layerKey, plan.generatedRules);
  errors.push(...duplicateMessages);

  const rows = enabledTemplates.map((template) => {
    const planItem = planByTemplate.get(template.template_id);
    return templateAuditRowForTemplate(layerKey, template, planItem, {
      currentGeneratedRules,
      currentByKey,
      desiredByKey,
      planErrors: errors,
      planWarnings
    });
  });

  for (const row of rows) {
    warnings.push(...row.warnings.map((message) => `${row.templateLabel}: ${message}`));
  }

  const currentTemplateIds = new Set(enabledTemplates.map((template) => template.template_id));
  const staleRules = currentGeneratedRules.filter((rule) =>
    !currentTemplateIds.has(ruleTemplateId(rule))
    || !desiredByKey.has(generatedRuleManagedKeyFromRule(rule, layerKey)));
  const detachedRules = currentGeneratedRules.filter((rule) => rule.detached_from_template
    || rule.template_generation?.status === 'detached');
  const deletionPlans = (document.templateDeletionPlans ?? []).filter((deletionPlan) => deletionPlan.status !== 'done');
  const reconcile = plan.reconcile ?? {};
  if (enabledTemplates.length === 0) {
    warnings.push(`${layerHumanLabel(layerKey)}: активные шаблоны не настроены.`);
  }

  return {
    layerKey,
    templates: enabledTemplates.length,
    candidates: plan.candidateCount,
    generatedRules: plan.generatedRules.length,
    generatedRelations: templateGeneratedRelationCount(plan),
    reconcile: {
      created: reconcile.created ?? 0,
      updated: reconcile.updated ?? 0,
      unchanged: reconcile.unchanged ?? 0,
      removed: reconcile.removed ?? 0,
      relations: relationReconcileSummary(reconcile.relations)
    },
    staleRules: staleRules.length,
    detachedRules: detachedRules.length,
    deletionPlans: deletionPlans.length,
    errors: uniqueTextList(errors),
    warnings: uniqueTextList(warnings),
    rows
  };
}

function templateAuditRowForTemplate(layerKey, template, planItem, context) {
  const candidates = planItem?.candidates ?? safeTemplateCandidateClasses(template).candidates;
  const rules = planItem?.rules ?? [];
  const desiredKeys = new Set(rules.map((rule) => generatedRuleManagedKeyFromRule(rule, layerKey)));
  const currentTemplateRules = context.currentGeneratedRules.filter((rule) => ruleTemplateId(rule) === template.template_id);
  const rowErrors = context.planErrors.filter((message) =>
    templateAuditMessageBelongsToTemplate(message, template, rules));
  const warnings = (context.planWarnings ?? []).filter((message) =>
    templateAuditMessageBelongsToTemplate(message, template, rules));
  const dimension = templatePopulationDimension(template);
  if (candidates.length === 0) {
    warnings.push(`регулярное выражение source-класса не выбрало ни одного класса (${template.source_class_regex || 'пустое выражение'}).`);
  }
  if (!template.source_class_regex && candidates.length > 10) {
    warnings.push(`source regex пустой и выбирает ${candidates.length} классов; проверьте, что такой широкий охват нужен.`);
  } else if (template.source_class_regex && candidates.length > 50) {
    warnings.push(`source regex выбирает ${candidates.length} классов; это может породить тяжелый набор правил.`);
  }
  const maxRules = dimension.max_rules || TEMPLATE_DIMENSION_DEFAULT_MAX_RULES;
  if (rules.length > 0 && rules.length >= Math.floor(maxRules * 0.8)) {
    warnings.push(`правил ${rules.length} при лимите ${maxRules}; проверьте кардинальность dimension.`);
  }
  if (planItem && candidates.length > 0 && rules.length === 0) {
    warnings.push('шаблон выбрал source-классы, но не породил правил.');
  }

  const reconcile = {
    created: 0,
    updated: 0,
    unchanged: 0,
    removed: currentTemplateRules.filter((rule) =>
      !desiredKeys.has(generatedRuleManagedKeyFromRule(rule, layerKey))).length
  };
  for (const rule of rules.map((item) => enrichGeneratedRuleOwnership(item, layerKey))) {
    const existingRule = context.currentByKey.get(generatedRuleManagedKeyFromRule(rule, layerKey));
    if (!existingRule) {
      reconcile.created += 1;
      continue;
    }

    const desiredFingerprint = generatedRuleArtifactFingerprint(rule);
    const existingFingerprint = String(existingRule.template_generation?.artifact_fingerprint ?? '')
      || generatedRuleArtifactFingerprint(existingRule);
    const desiredMetadataFingerprint = generatedRuleTemplateMetadataFingerprint(rule);
    const existingMetadataFingerprint = generatedRuleTemplateMetadataFingerprint(existingRule);
    if (desiredFingerprint === existingFingerprint && desiredMetadataFingerprint === existingMetadataFingerprint) {
      reconcile.unchanged += 1;
    } else {
      reconcile.updated += 1;
    }
  }

  return {
    layerKey,
    template,
    templateLabel: templateHumanLabel(template),
    candidates,
    rules,
    dimension,
    reconcile,
    relationCount: rules.reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0),
    staleRules: reconcile.removed,
    detachedRules: currentTemplateRules.filter((rule) => rule.detached_from_template
      || rule.template_generation?.status === 'detached').length,
    errors: uniqueTextList(rowErrors),
    warnings: uniqueTextList(warnings)
  };
}

function safeTemplateCandidateClasses(template) {
  try {
    return { candidates: templateCandidateClasses(template), error: '' };
  } catch (error) {
    return { candidates: [], error: error.message };
  }
}

function templateAuditMessageBelongsToTemplate(message, template, rules) {
  const text = String(message ?? '');
  const templateId = String(template.template_id ?? '').trim();
  const templateName = String(template.name ?? '').trim();
  if (templateId && (text.startsWith(`${templateId}:`) || text.includes(`[${templateId}]`) || text.includes(`"${templateId}"`))) {
    return true;
  }
  if (templateName && text.includes(`"${templateName}"`)) {
    return true;
  }

  return rules.some((rule) => {
    const ruleId = String(rule.rule_id ?? '').trim();
    return ruleId && text.startsWith(`${ruleId}:`);
  });
}

function templateAuditDuplicateMessages(layerKey, rules) {
  const ruleIds = duplicateNonEmptyValues(rules.map((rule) => rule.rule_id));
  const managedKeys = duplicateNonEmptyValues(rules.map((rule) => generatedRuleManagedKeyFromRule(rule, layerKey)));
  return [
    ...ruleIds.map((ruleId) => `${layerHumanLabel(layerKey)}: rule_id "${ruleId}" дублируется в сгенерированных правилах.`),
    ...managedKeys.map((managedKey) => `${layerHumanLabel(layerKey)}: managed_key "${managedKey}" дублируется в сгенерированных правилах.`)
  ];
}

function duplicateNonEmptyValues(values) {
  const counts = new Map();
  for (const value of values) {
    const text = String(value ?? '').trim();
    if (!text) {
      continue;
    }

    counts.set(text, (counts.get(text) ?? 0) + 1);
  }

  return [...counts.entries()].filter(([, count]) => count > 1).map(([value]) => value);
}

function renderTemplateAuditLayer(layerKey, audit) {
  if (!audit) {
    return '';
  }

  const layerLabel = layerKey === 'service' ? 'Сервисный слой' : 'Каскадное подавление';
  const relationText = relationReconcileText(audit.reconcile.relations);
  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerLabel)} · проверка</span>
      <strong>${escapeHtml(audit.templates)} шаблонов · ${escapeHtml(audit.generatedRules)} правил · ${escapeHtml(audit.generatedRelations)} связей</strong>
      <span>кандидатов ${escapeHtml(audit.candidates)} · создать ${escapeHtml(audit.reconcile.created)} · обновить ${escapeHtml(audit.reconcile.updated)} · без изменений ${escapeHtml(audit.reconcile.unchanged)} · снять ${escapeHtml(audit.reconcile.removed)}</span>
      <span>${escapeHtml(relationText)} · устаревших правил ${escapeHtml(audit.staleRules)} · отвязанных ${escapeHtml(audit.detachedRules)} · планов удаления ${escapeHtml(audit.deletionPlans)}</span>
      <span>${escapeHtml(audit.errors.length ? `Ошибки: ${audit.errors.slice(0, 6).join('; ')}` : 'Блокирующих ошибок нет.')}</span>
      ${audit.warnings.length ? `<span>${escapeHtml(`Предупреждения: ${audit.warnings.slice(0, 6).join('; ')}`)}</span>` : ''}
    </div>
    ${audit.rows.map((row) => renderTemplateAuditRow(row)).join('')}
  `;
}

function renderTemplateAuditRow(row) {
  const template = row.template;
  const targetClass = template.target?.class_code || '-';
  const errors = row.errors.length ? row.errors : [];
  const warnings = row.warnings.length ? row.warnings : [];
  const details = [
    `source regex: ${template.source_class_regex || 'пусто'}`,
    `source-классы: ${row.candidates.map((candidate) => `${candidate.code}${classDisplayName(candidate) !== candidate.code ? ` (${classDisplayName(candidate)})` : ''}`).join(', ') || '-'}`,
    templatePlanDimensionText({ template, rules: row.rules }),
    `target: ${targetClass} · атрибуты цели: ${templateAuditTargetAttributesText(row.layerKey, template, row.rules[0])}`,
    `dimension.*: ${templateAuditDimensionExamples(row.rules)}`,
    `target keys: ${templateAuditTargetKeyExamples(row.rules)}`,
    `первые правила: ${templateAuditRuleExamples(row.rules)}`,
    `связи: ${templateAuditRelationExamples(row.rules)}`
  ];

  return `
    <details class="rule-summary template-audit-row${errors.length ? ' template-audit-error' : warnings.length ? ' template-audit-warning' : ''}">
      <summary>
        <span class="structure-mark">${escapeHtml(row.layerKey === 'service' ? 'сервис' : 'подавление')} · шаблон</span>
        <strong>${escapeHtml(template.name || template.template_id)}</strong>
        <span>source-классов ${escapeHtml(row.candidates.length)} · правил ${escapeHtml(row.rules.length)} · связей ${escapeHtml(row.relationCount)} · цель ${escapeHtml(targetClass)}</span>
        <span>создать ${escapeHtml(row.reconcile.created)} · обновить ${escapeHtml(row.reconcile.updated)} · без изменений ${escapeHtml(row.reconcile.unchanged)} · снять ${escapeHtml(row.reconcile.removed)} · отвязанных ${escapeHtml(row.detachedRules)}</span>
        ${errors.length ? `<span class="template-audit-message-error">${escapeHtml(`Ошибки: ${errors.join('; ')}`)}</span>` : ''}
        ${warnings.length ? `<span class="template-audit-message-warning">${escapeHtml(`Предупреждения: ${warnings.join('; ')}`)}</span>` : ''}
      </summary>
      <div class="template-audit-details">
        ${details.map((line) => `<span>${escapeHtml(line)}</span>`).join('')}
      </div>
    </details>
  `;
}

function templateAuditTargetAttributesText(layerKey, template, sampleRule) {
  const rendered = sampleRule?.target?.initial_user_values ?? {};
  const raw = template.target?.initial_user_values ?? {};
  const keys = uniqueTextList(allowedUserResponsibilityAttributes(layerKey)
    .concat(Object.keys(rendered))
    .concat(Object.keys(raw)));
  const values = keys
    .filter((key) => Object.hasOwn(rendered, key) || Object.hasOwn(raw, key))
    .map((key) => `${key}=${String(rendered[key] ?? raw[key] ?? '').trim() || 'пусто'}`);
  return values.join(', ') || '-';
}

function templateAuditDimensionExamples(rules) {
  const values = rules
    .map((rule) => {
      const generation = rule.template_generation ?? {};
      const key = generation.dimension_key || '';
      const value = generation.dimension_value || '';
      const name = generation.dimension_name || '';
      return key || value || name
        ? `key=${key || '-'}, value=${value || '-'}, name=${name || '-'}`
        : '';
    })
    .filter(Boolean)
    .slice(0, 6);
  return values.join('; ') || '-';
}

function templateAuditTargetKeyExamples(rules) {
  const values = uniqueTextList(rules.map((rule) => rule.target?.idempotency_key).filter(Boolean)).slice(0, 8);
  return values.join(', ') || '-';
}

function templateAuditRuleExamples(rules) {
  const values = rules.slice(0, 8).map((rule) =>
    `${rule.rule_id || '-'} (${ruleSourceClassCode(rule) || '-'} -> ${ruleTargetClassCode(rule) || '-'})`);
  const hidden = rules.length > values.length ? `; еще ${rules.length - values.length}` : '';
  return `${values.join('; ') || '-'}${hidden}`;
}

function templateAuditRelationExamples(rules) {
  const values = rules
    .flatMap((rule) => runtimeRelationsFromRule(rule).map((relation) =>
      `${rule.rule_id || '-'} -> ${relation.target_lookup || relation.target_class_code || '-'} (${relation.domain_code || '-'})`))
    .slice(0, 8);
  const total = rules.reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0);
  const hidden = total > values.length ? `; еще ${total - values.length}` : '';
  return `${values.join('; ') || '-'}${hidden}`;
}

function templateAuditEmptySummary() {
  const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates
    .filter((template) => template.enabled !== false).length;
  const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates
    .filter((template) => template.enabled !== false).length;
  return {
    templates: serviceTemplates + suppressionTemplates,
    generatedRules: 0,
    generatedRelations: 0,
    warnings: [],
    errors: []
  };
}

function templateAuditBlockingErrors(result = state.templateAudit.result) {
  return result?.errors ?? [];
}

function templateAuditCanApply() {
  const result = state.templateAudit.result;
  return Boolean(result)
    && !state.templateAudit.checking
    && !state.templateAudit.error
    && !isTemplateAuditStale()
    && templateAuditBlockingErrors(result).length === 0;
}

function templateAuditGateMessage(result = state.templateAudit.result, stale = isTemplateAuditStale(), blockingErrors = templateAuditBlockingErrors(result)) {
  if (state.templateAudit.checking) {
    return 'Идет проверка шаблонов.';
  }
  if (!result) {
    return 'Перед применением выполните "Проверить шаблоны".';
  }
  if (stale) {
    return 'После последней проверки изменились шаблоны, правила или данные CMDBuild; выполните проверку заново.';
  }
  if (blockingErrors.length > 0) {
    return `Исправьте блокирующие ошибки проверки: ${blockingErrors.slice(0, 3).join('; ')}${blockingErrors.length > 3 ? `; еще ${blockingErrors.length - 3}` : ''}.`;
  }

  return `Последняя проверка ${formatCacheTimestamp(state.templateAudit.checkedAt) || state.templateAudit.checkedAt}: можно создавать/обновлять правила.`;
}

function renderTemplateAuditGateCard() {
  const result = state.templateAudit.result;
  const blockingErrors = templateAuditBlockingErrors(result);
  const stale = result ? isTemplateAuditStale() : false;
  const canApply = templateAuditCanApply();
  return `
    <div class="rule-summary${canApply ? ' preview-linked' : blockingErrors.length ? ' template-audit-error' : ''}">
      <span class="structure-mark">проверка шаблонов</span>
      <strong>${escapeHtml(canApply ? 'Проверка пройдена' : 'Перед применением нужна проверка')}</strong>
      <span>${escapeHtml(templateAuditGateMessage(result, stale, blockingErrors))}</span>
      ${result ? `<span>шаблонов ${escapeHtml(result.templates)} · правил ${escapeHtml(result.generatedRules)} · связей ${escapeHtml(result.generatedRelations)} · предупреждений ${escapeHtml(result.warnings.length)} · ошибок ${escapeHtml(blockingErrors.length)}</span>` : ''}
    </div>
  `;
}

function isTemplateAuditStale() {
  return Boolean(state.templateAudit.result)
    && state.templateAudit.fingerprint
    && state.templateAudit.fingerprint !== templateAuditFingerprint();
}

function templateAuditFingerprint() {
  const serviceRuleDocument = parseRuleDocument('service');
  const suppressionRuleDocument = parseRuleDocument('suppression');
  return stableHash({
    templates: {
      service: normalizeTemplateDocument(state.templateDocuments.service, 'service'),
      suppression: normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression')
    },
    rules: {
      service: serviceRuleDocument.ok ? serviceRuleDocument.document : serviceRuleDocument.error,
      suppression: suppressionRuleDocument.ok ? suppressionRuleDocument.document : suppressionRuleDocument.error
    },
    sourceClasses: availableSourceClasses().map((item) => ({
      code: item.code,
      name: classDisplayName(item),
      path: item.hierarchyPath || ''
    })),
    sourceCards: state.cmdbClassInstances.map((item) => ({
      classCode: item.classCode ?? item.code ?? item.class_code ?? item.name ?? '',
      layer: item.layer ?? '',
      count: Array.isArray(item.cards) ? item.cards.length : 0,
      hash: stableHash(item.cards ?? [])
    })),
    maxTraversalDepth: state.maxTraversalDepth
  });
}

function uniqueTextList(values) {
  return [...new Set((values ?? [])
    .map((value) => String(value ?? '').trim())
    .filter(Boolean))];
}

function templateMaterializationPlan(layerKey, options = {}) {
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const errors = [];
  const warnings = [];
  const templates = [];
  const generatedRules = [];
  let candidateCount = 0;
  for (const template of document.templates.filter((item) => item.enabled !== false)) {
    try {
      const candidates = templateCandidateClasses(template);
      const rules = [];
      const dimensionPlans = [];
      for (const candidate of candidates) {
        try {
          const candidateRules = rulesFromTemplate(layerKey, template, candidate);
          rules.push(...candidateRules);
          dimensionPlans.push({
            source_class_code: candidate.code || '',
            generated_rules: candidateRules.length
          });
        } catch (error) {
          const message = templateCandidateMaterializationMessage(template, candidate, error);
          dimensionPlans.push({
            source_class_code: candidate.code || '',
            generated_rules: 0,
            warning: error.message
          });
          if (isSkippableTemplateCandidateError(error)) {
            warnings.push(message);
            continue;
          }

          errors.push(message);
          if (!options.safe) {
            throw new Error(message);
          }
        }
      }
      const maxRules = templatePopulationDimension(template).max_rules || TEMPLATE_DIMENSION_DEFAULT_MAX_RULES;
      if (rules.length > maxRules) {
        throw new Error(`Шаблон породил ${rules.length} правил при лимите ${maxRules}; сузьте regex, значения измерения или увеличьте лимит.`);
      }
      const templateWarnings = warnings.filter((message) => templateAuditMessageBelongsToTemplate(message, template, rules));
      if (candidates.length > 0 && rules.length === 0 && templateWarnings.length > 0) {
        errors.push(...templateWarnings);
        for (const warning of templateWarnings) {
          const index = warnings.indexOf(warning);
          if (index >= 0) {
            warnings.splice(index, 1);
          }
        }
        if (!options.safe) {
          throw new Error(templateWarnings[0]);
        }
      }
      candidateCount += candidates.length;
      generatedRules.push(...rules);
      templates.push({ template, candidates, rules, dimensions: dimensionPlans });
    } catch (error) {
      errors.push(`${template.template_id}: ${error.message}`);
      if (!options.safe) {
        throw error;
      }
    }
  }

  applyTemplateManagedRelationsToGeneratedRules(layerKey, generatedRules, document, errors, options);

  return {
    templates,
    generatedRules,
    candidateCount,
    errors,
    warnings,
    reconcile: templateReconcilePreview(layerKey, generatedRules)
  };
}

function templateCandidateMaterializationMessage(template, candidate, error) {
  return `${template.template_id}: ${candidate.code || 'класс-источник'}: ${error.message}`;
}

function isSkippableTemplateCandidateError(error) {
  return Boolean(error?.templateDimensionNoValues);
}

function applyTemplateManagedRelationsToGeneratedRules(layerKey, generatedRules, templateDocument, errors, options = {}) {
  const templatesById = new Map((templateDocument.templates ?? [])
    .map((template) => [template.template_id, template]));
  const existingRulesById = readCurrentRulesById(layerKey);
  const rulesById = new Map(existingRulesById);
  const rulesByTemplate = new Map();
  for (const rule of generatedRules) {
    if (rule.rule_id) {
      rulesById.set(rule.rule_id, rule);
    }

    const templateId = ruleTemplateId(rule);
    if (!templateId) {
      continue;
    }

    if (!rulesByTemplate.has(templateId)) {
      rulesByTemplate.set(templateId, []);
    }

    rulesByTemplate.get(templateId).push(rule);
  }

  for (const sourceRule of generatedRules) {
    const sourceTemplate = templatesById.get(ruleTemplateId(sourceRule));
    if (!sourceTemplate) {
      continue;
    }

    for (const relation of sourceTemplate.managed_relations ?? []) {
      const targetRules = targetRulesForTemplateRelation(relation, rulesByTemplate, rulesById, errors, options, {
        layerKey,
        sourceTemplate,
        sourceRule,
        targetTemplate: templatesById.get(relation.target_template_id)
      });
      for (const targetRule of targetRules) {
        if (sourceRule === targetRule || sourceRule.rule_id === targetRule.rule_id) {
          continue;
        }

        if (!templateManagedRelationMatchesRulePair(sourceRule, targetRule, relation)) {
          continue;
        }

        try {
          const runtimeRelation = runtimeRelationFromTemplateRelation(layerKey, sourceRule, targetRule, relation);
          appendUniqueRuntimeRelation(sourceRule, runtimeRelation);
          appendDerivedRuleManagedRelation(layerKey, sourceRule, targetRule, relation, runtimeRelation);
        } catch (error) {
          const message = `${sourceRule.rule_id || sourceRule.name}: ${error.message}`;
          errors.push(message);
          if (!options.safe) {
            throw new Error(message);
          }
        }
      }
    }
  }
}

function readCurrentRulesById(layerKey) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return new Map();
  }

  return new Map((parsed.document.rules ?? [])
    .filter((rule) => rule?.rule_id)
    .map((rule) => [rule.rule_id, rule]));
}

function targetRulesForTemplateRelation(relation, rulesByTemplate, rulesById, errors, options = {}, context = {}) {
  if (['template', 'rule_template'].includes(relation.kind) && relation.target_template_id) {
    const targetRules = rulesByTemplate.get(relation.target_template_id) ?? [];
    if (targetRules.length > 0) {
      return targetRules;
    }

    const message = templateRelationMissingTargetTemplateMessage(relation, context);
    errors.push(message);
    if (!options.safe) {
      throw new Error(message);
    }
    return [];
  }

  if (relation.kind === 'rule' && relation.target_rule_id) {
    const targetRule = rulesById.get(relation.target_rule_id);
    if (targetRule) {
      return [targetRule];
    }

    const message = templateRelationMissingTargetRuleMessage(relation, context);
    errors.push(message);
    if (!options.safe) {
      throw new Error(message);
    }
  }

  return [];
}

function templateRelationMissingTargetRuleMessage(relation, context = {}) {
  const targetRuleId = String(relation?.target_rule_id ?? '').trim();
  const placeholderText = isGenericRuleId(targetRuleId)
    ? ' В связи сохранена заглушка "rule" вместо реального rule_id.'
    : '';
  return [
    `Шаблон ${templateHumanLabel(context.sourceTemplate)} не может применить связь ${templateRelationHumanLabel(relation, context)}: целевое ручное правило ${targetRuleId ? `"${targetRuleId}"` : 'не задано'} не найдено.`,
    placeholderText,
    `Источник связи: ${generatedRuleHumanLabel(context.sourceRule)}.`,
    `Что сделать: откройте ${layerHumanLabel(context.layerKey)} -> Управление связями -> Шаблон-правило, удалите эту связь и создайте заново, выбрав существующее ручное правило.`
  ].filter(Boolean).join(' ');
}

function templateRelationMissingTargetTemplateMessage(relation, context = {}) {
  const targetTemplateId = String(relation?.target_template_id ?? '').trim();
  const targetTemplateText = context.targetTemplate
    ? templateHumanLabel(context.targetTemplate)
    : targetTemplateId
      ? `"${targetTemplateId}"`
      : 'не задан';
  const reasonText = context.targetTemplate
    ? `целевой шаблон ${targetTemplateText} найден, но не породил сгенерированные правила`
    : `целевой шаблон ${targetTemplateText} не найден`;
  return [
    `Шаблон ${templateHumanLabel(context.sourceTemplate)} не может применить связь ${templateRelationHumanLabel(relation, context)}: ${reasonText}.`,
    `Источник связи: ${generatedRuleHumanLabel(context.sourceRule)}.`,
    `Что сделать: откройте ${layerHumanLabel(context.layerKey)} -> Управление связями -> Шаблон-шаблон или Шаблон-правило и перенастройте целевой шаблон.`
  ].join(' ');
}

function templateHumanLabel(template) {
  if (!template) {
    return '"шаблон не найден"';
  }

  const name = String(template.name || template.template_id || '').trim();
  const id = String(template.template_id ?? '').trim();
  return `"${name || id || 'без имени'}"${id && id !== name ? ` [${id}]` : ''}`;
}

function generatedRuleHumanLabel(rule) {
  if (!rule) {
    return 'сгенерированное правило не определено';
  }

  const ruleId = String(rule.rule_id ?? '').trim();
  const generation = rule.template_generation ?? {};
  const dimension = String(generation.dimension_name || generation.dimension_key || '').trim();
  const sourceClass = ruleSourceClassCode(rule);
  return [
    rule.name || ruleId || 'сгенерированное правило',
    dimension ? `dimension ${dimension}` : '',
    sourceClass ? `источник ${sourceClass}` : '',
    ruleId ? `rule_id ${ruleId}` : ''
  ].filter(Boolean).join(', ');
}

function templateRelationHumanLabel(relation, context = {}) {
  const role = linkRelationRoleLabel(relation?.relation_role);
  const targetRule = String(relation?.target_rule_id ?? '').trim();
  const targetTemplate = String(relation?.target_template_id ?? '').trim();
  const target = targetRule
    ? `ручное правило "${targetRule}"`
    : context.targetTemplate
      ? `шаблон ${templateHumanLabel(context.targetTemplate)}`
      : targetTemplate
      ? `шаблон "${targetTemplate}"`
      : 'цель не задана';
  return `"${role}" -> ${target}`;
}

function layerHumanLabel(layerKey) {
  return layerKey === 'suppression'
    ? 'Каскадное подавление'
    : 'Сервис';
}

function templateManagedRelationMatchesRulePair(sourceRule, targetRule, relation) {
  const match = relation?.attributes?.match;
  if (!match || typeof match !== 'object' || Array.isArray(match)) {
    return true;
  }

  const sourceFilters = linkRelationSourceFiltersFromMatch(match);
  if (sourceFilters.length > 0 && !linkRelationFiltersMatchRule(sourceRule, sourceFilters)) {
    return false;
  }

  const targetFilters = linkRelationTargetFiltersFromMatch(match);
  if (targetFilters.length > 0 && !linkRelationFiltersMatchRule(targetRule, targetFilters)) {
    return false;
  }

  const sourceVariable = String(match.source_variable ?? '').trim();
  const targetVariable = String(match.target_variable ?? '').trim();
  if (relation?.kind === 'rule' && sourceFilters.length > 0 && !targetVariable) {
    return true;
  }

  if (relation?.kind === 'template' && targetFilters.length > 0 && !sourceVariable) {
    return true;
  }

  if (relation?.kind === 'template' && !sourceVariable && !targetVariable) {
    return true;
  }

  if (!sourceVariable) {
    return false;
  }

  const sourceValue = generatedRuleVariableValue(sourceRule, sourceVariable);
  const sourceComparable = relationRegexComparableValue(sourceValue, match.source_pattern ?? match.pattern ?? '');
  if (!targetVariable) {
    return sourceComparable.matched;
  }

  const targetValue = generatedRuleVariableValue(targetRule, targetVariable);
  const targetComparable = relationRegexComparableValue(targetValue, match.target_pattern ?? '');
  return sourceComparable.matched
    && targetComparable.matched
    && sourceComparable.value === targetComparable.value;
}

function linkRelationSourceFiltersFromMatch(match) {
  return linkRelationFiltersFromMatch(match?.source_filters ?? match?.filters);
}

function linkRelationTargetFiltersFromMatch(match) {
  return linkRelationFiltersFromMatch(match?.target_filters);
}

function linkRelationFiltersFromMatch(filters) {
  return Array.isArray(filters)
    ? filters.map((filter) => ({
      mode: filter?.mode === 'exclude' ? 'exclude' : 'include',
      variable: String(filter?.variable ?? filter?.source_variable ?? '').trim(),
      regex: String(filter?.regex ?? filter?.source_pattern ?? filter?.pattern ?? '').trim()
    })).filter((filter) => filter.variable && filter.regex)
    : [];
}

function linkRelationFiltersMatchRule(rule, filters) {
  for (const filter of filters) {
    let matched = false;
    try {
      const value = generatedRuleVariableValue(rule, filter.variable);
      matched = regexFromUiPattern(filter.regex).test(value);
    } catch {
      matched = false;
    }

    if (filter.mode === 'exclude' && matched) {
      return false;
    }

    if (filter.mode !== 'exclude' && !matched) {
      return false;
    }
  }

  return true;
}

function generatedRuleVariableValue(rule, variableName) {
  const key = String(variableName ?? '').trim();
  if (!key) {
    return '';
  }

  const variables = rule?.template_generation?.variables;
  if (variables && typeof variables === 'object' && !Array.isArray(variables) && Object.hasOwn(variables, key)) {
    return String(variables[key] ?? '');
  }

  const generation = rule?.template_generation ?? {};
  const dimensionAliases = {
    dimension_key: generation.dimension_key,
    dimension_value: generation.dimension_value,
    dimension_name: generation.dimension_name,
    key: generation.dimension_key,
    value: generation.dimension_value,
    name: generation.dimension_name
  };
  return String(dimensionAliases[key] ?? '');
}

function relationRegexComparableValue(value, pattern) {
  const text = String(value ?? '');
  const regexPattern = String(pattern ?? '').trim();
  if (!regexPattern) {
    return { matched: true, value: text };
  }

  const match = regexFromUiPattern(regexPattern).exec(text);
  if (!match) {
    return { matched: false, value: '' };
  }

  return { matched: true, value: match[1] ?? match[0] ?? '' };
}

function runtimeRelationFromTemplateRelation(layerKey, sourceRule, targetRule, relation) {
  const sourceTargetClass = ruleTargetClassCode(sourceRule);
  const targetClass = ruleTargetClassCode(targetRule);
  const relationRole = String(relation?.relation_role ?? '').trim();
  const relationType = managedRelationTypeForRole(layerKey, relationRole, targetClass);
  const domain = managedRelationDomainForTargets(layerKey, sourceTargetClass, targetClass, relation.relation_role);
  if (!domain) {
    throw new Error(`не найден domain для связи ${generatedRuleHumanLabel(sourceRule)} -> ${generatedRuleHumanLabel(targetRule)} (${sourceTargetClass} -> ${targetClass}, тип "${linkRelationRoleLabel(relationRole)}", relationType "${relationType}")`);
  }

  const targetLookup = String(targetRule.target?.card_id
    || targetRule.target?.idempotency_key
    || targetRule.target?.attribute_mappings?.Code
    || targetRule.target?.attribute_mappings?.code
    || '').trim();
  if (!targetLookup) {
    throw new Error(`у целевого правила ${targetRule.rule_id || targetRule.name} нет lookup цели`);
  }

  return {
    domain_code: domain.code,
    target_class_code: targetClass,
    target_lookup: targetLookup,
    managed_relation_key: relation.managed_key || '',
    attribute_mappings: managedRelationAttributeMappings(domain, sourceRule, relation)
  };
}

function managedRelationDomainForTargets(layerKey, sourceClassCode, targetClassCode, relationRole) {
  const relationType = managedRelationTypeForRole(layerKey, relationRole, targetClassCode);
  const domains = state.domains.concat(state.suggestedDomains);
  return domains.find((domain) =>
    sameClassCode(domain.sourceClassCode, sourceClassCode)
    && sameClassCode(domain.targetClassCode, targetClassCode)
    && String(domain.relationType ?? '').trim() === relationType)
    ?? fallbackManagedRelationDomain(layerKey, sourceClassCode, targetClassCode, relationType);
}

function managedRelationTypeForRole(layerKey, relationRole, targetClassCode) {
  const role = String(relationRole ?? '').trim();
  if (role === 'depends_on' || role === 'uses' || role === 'impacts') {
    if (layerKey === 'service') {
      return 'service_depends_on';
    }

    return /NetworkAccessZone/i.test(targetClassCode) ? 'depends_on_network' : 'depends_on';
  }

  if (role === 'suppresses') {
    if (layerKey === 'service') {
      return 'service_depends_on';
    }

    return /NetworkAccessZone/i.test(targetClassCode) ? 'depends_on_network' : 'depends_on';
  }

  if (role === 'contains') {
    return 'aggregates_to';
  }

  return role || (layerKey === 'service' ? 'service_depends_on' : 'depends_on');
}

function fallbackManagedRelationDomain(layerKey, sourceClassCode, targetClassCode, relationType) {
  const prefix = state.prefix || '';
  const sourceBase = stripManagedPrefix(sourceClassCode, prefix);
  const targetBase = stripManagedPrefix(targetClassCode, prefix);
  const standardBase = {
    'service|ServiceResource|ServiceUserEndpointFleet|member_of': 'ServiceResourceMemberOfFleet',
    'service|ServiceUserEndpointFleet|ServiceWorkplaceGroup|aggregates_to': 'ServiceFleetAggregatesToWorkplaceGroup',
    'service|ServiceWorkplaceGroup|ServicePlatformService|aggregates_to': 'ServiceWorkplaceGroupAggregatesToPlatformService',
    'service|ServicePlatformService|ServiceDatabaseService|service_depends_on': 'ServicePlatformDependsOnDatabase',
    'service|ServicePlatformService|ServiceStoragePool|service_depends_on': 'ServicePlatformDependsOnStoragePool',
    'service|ServicePlatformService|ServiceNetworkAccessZone|service_depends_on': 'ServicePlatformDependsOnNetworkZone',
    'service|ServiceUserEndpointFleet|ServiceNetworkAccessZone|service_depends_on': 'ServiceFleetDependsOnNetworkZone',
    'service|ServiceNetworkAccessZone|ServiceNetworkAccessZone|service_depends_on': 'ServiceNetworkZoneDependsOnNetworkZone',
    'service|ServiceDatabaseService|ServiceComputeCluster|service_depends_on': 'ServiceDatabaseDependsOnComputeCluster',
    'suppression|SuppressionResource|SuppressionNetworkAccessZone|depends_on_network': 'SuppressionResourceDependsOnNetwork',
    'suppression|SuppressionResource|SuppressionComputeCluster|runs_on_compute': 'SuppressionResourceRunsOnCompute',
    'suppression|SuppressionResource|SuppressionStoragePool|depends_on': 'SuppressionResourceDependsOnStoragePool',
    'suppression|SuppressionResource|SuppressionProxyGroup|monitored_via': 'SuppressionResourceMonitoredViaProxyGroup',
    'suppression|SuppressionComputeCluster|SuppressionStoragePool|depends_on': 'SuppressionComputeDependsOnStoragePool',
    'suppression|SuppressionNetworkAccessZone|SuppressionNetworkAccessZone|depends_on_network': 'SuppressionNetworkZoneDependsOnNetworkZone',
    'suppression|SuppressionResource|SuppressionResource|depends_on': 'SuppressionResourceSuppressesResource',
    'suppression|SuppressionNetworkAccessZone|SuppressionResource|depends_on': 'SuppressionNetworkZoneSuppressesResource',
    'suppression|SuppressionComputeCluster|SuppressionResource|depends_on': 'SuppressionComputeSuppressesResource',
    'suppression|SuppressionStoragePool|SuppressionResource|depends_on': 'SuppressionStoragePoolSuppressesResource',
    'suppression|SuppressionProxyGroup|SuppressionResource|depends_on': 'SuppressionProxyGroupSuppressesResource'
  }[`${layerKey}|${sourceBase}|${targetBase}|${relationType}`];
  const fallbackBase = standardBase
    || fallbackServiceDependencyDomainBase(layerKey, sourceClassCode, targetClassCode, sourceBase, targetBase, relationType)
    || fallbackSuppressionSuppressDomainBase(
      layerKey,
      sourceClassCode,
      targetClassCode,
      sourceBase,
      targetBase,
      relationType);
  if (!fallbackBase) {
    return null;
  }

  return {
    code: `${prefix}${fallbackBase}`,
    sourceClassCode,
    targetClassCode,
    relationType,
    attributes: [{ code: 'is_active' }]
  };
}

function fallbackServiceDependencyDomainBase(layerKey, sourceClassCode, targetClassCode, sourceBase, targetBase, relationType) {
  if (layerKey !== 'service' || relationType !== 'service_depends_on') {
    return '';
  }

  if (!isServiceManagedClassCode(sourceClassCode) || !isServiceManagedClassCode(targetClassCode)) {
    return '';
  }

  const sourcePart = domainCodePart(sourceBase);
  const targetPart = domainCodePart(targetBase);
  if (!sourcePart || !targetPart) {
    return '';
  }

  return `${sourcePart}DependsOn${targetPart}`;
}

function fallbackSuppressionSuppressDomainBase(layerKey, sourceClassCode, targetClassCode, sourceBase, targetBase, relationType) {
  if (layerKey !== 'suppression' || !['depends_on', 'depends_on_network'].includes(relationType)) {
    return '';
  }

  if (!isSuppressionManagedClassCode(sourceClassCode) || !isSuppressionManagedClassCode(targetClassCode)) {
    return '';
  }

  const sourcePart = domainCodePart(sourceBase);
  const targetPart = domainCodePart(targetBase);
  if (!sourcePart || !targetPart) {
    return '';
  }

  return `${sourcePart}Suppresses${targetPart}`;
}

function isServiceManagedClassCode(classCode) {
  const item = state.classes.find((classItem) => sameClassCode(classItem.code, classCode));
  if (item) {
    return item.layer === 'Service' && !item.isSuperclass;
  }

  return /^C2M_Service|^Service/i.test(String(classCode ?? '').trim());
}

function isSuppressionManagedClassCode(classCode) {
  const item = state.classes.find((classItem) => sameClassCode(classItem.code, classCode));
  if (item) {
    return item.layer === 'Suppression' && !item.isSuperclass;
  }

  return /^C2M_Suppression|^Suppression/i.test(String(classCode ?? '').trim());
}

function domainCodePart(value) {
  return String(value ?? '').replaceAll(/[^A-Za-z0-9]+/g, '');
}

function stripManagedPrefix(classCode, prefix) {
  const text = String(classCode ?? '');
  return prefix && text.startsWith(prefix) ? text.slice(prefix.length) : text;
}

function sameClassCode(left, right) {
  return String(left ?? '').trim().toLowerCase() === String(right ?? '').trim().toLowerCase();
}

function managedRelationAttributeMappings(domain, sourceRule, relation) {
  const mappings = { is_active: 'true' };
  const attributes = new Set((domain.attributes ?? []).map((attribute) => canonicalToken(attribute.code)));
  if (attributes.has('source')) {
    mappings.source = 'cmdb2monitoring';
  }
  if (attributes.has('populationruleid')) {
    mappings.population_rule_id = sourceRule.rule_id || '';
  }
  if (attributes.has('priority') && ['depends_on', 'suppresses'].includes(relation.relation_role)) {
    mappings.priority = '100';
  }

  return mappings;
}

function appendUniqueRuntimeRelation(rule, relation) {
  rule.relations = Array.isArray(rule.relations) ? rule.relations : [];
  const key = stableHash({
    domain_code: relation.domain_code,
    target_class_code: relation.target_class_code,
    target_lookup: relation.target_lookup
  });
  const exists = rule.relations.some((item) => stableHash({
    domain_code: item.domain_code,
    target_class_code: item.target_class_code,
    target_lookup: item.target_lookup
  }) === key);
  if (!exists) {
    rule.relations.push(relation);
  }
}

function appendDerivedRuleManagedRelation(layerKey, sourceRule, targetRule, templateRelation, runtimeRelation) {
  sourceRule.managed_relations = Array.isArray(sourceRule.managed_relations)
    ? sourceRule.managed_relations
    : [];
  const relation = normalizeRuleManagedRelation({
    relation_role: templateRelation.relation_role,
    target_rule_id: targetRule.rule_id || '',
    managed_key: ruleManagedRelationKey(layerKey, sourceRule.rule_id || '', templateRelation.relation_role, targetRule.rule_id || ''),
    attributes: {
      inherited_from_template_relation: templateRelation.managed_key || '',
      domain_code: runtimeRelation.domain_code,
      target_lookup: runtimeRelation.target_lookup,
      match: templateRelation.attributes?.match ?? {}
    }
  }, sourceRule, layerKey);
  if (!sourceRule.managed_relations.some((item) => item.managed_key === relation.managed_key)) {
    sourceRule.managed_relations.push(relation);
  }
}

function templateReconcilePreview(layerKey, desiredRules) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    return {
      created: 0,
      updated: 0,
      unchanged: 0,
      removed: 0,
      error: parsed.error
    };
  }

  const desiredByKey = new Map(desiredRules
    .map((rule) => enrichGeneratedRuleOwnership(rule, layerKey))
    .map((rule) => [generatedRuleManagedKeyFromRule(rule, layerKey), rule]));
  const existingGenerated = (parsed.document.rules ?? [])
    .filter((rule) => rule.generated_from_template)
    .map((rule) => enrichGeneratedRuleOwnership(rule, layerKey));
  const existingByKey = new Map(existingGenerated.map((rule) => [generatedRuleManagedKeyFromRule(rule, layerKey), rule]));
  const summary = {
    created: 0,
    updated: 0,
    unchanged: 0,
    removed: 0,
    relations: emptyRelationReconcileSummary()
  };

  for (const [key, desiredRule] of desiredByKey.entries()) {
    const existingRule = existingByKey.get(key);
    if (!existingRule) {
      summary.created += 1;
      mergeRelationReconcileSummary(summary.relations, {
        created: runtimeRelationsFromRule(desiredRule).length
      });
      continue;
    }

    mergeRelationReconcileSummary(summary.relations, runtimeRelationReconcileSummary(existingRule, desiredRule));
    const desiredFingerprint = generatedRuleArtifactFingerprint(desiredRule);
    const existingFingerprint = String(existingRule.template_generation?.artifact_fingerprint ?? '')
      || generatedRuleArtifactFingerprint(existingRule);
    const desiredMetadataFingerprint = generatedRuleTemplateMetadataFingerprint(desiredRule);
    const existingMetadataFingerprint = generatedRuleTemplateMetadataFingerprint(existingRule);
    if (desiredFingerprint === existingFingerprint && desiredMetadataFingerprint === existingMetadataFingerprint) {
      summary.unchanged += 1;
    } else {
      summary.updated += 1;
    }
  }

  summary.removed = existingGenerated.filter((rule) =>
    !desiredByKey.has(generatedRuleManagedKeyFromRule(rule, layerKey))).length;
  mergeRelationReconcileSummary(summary.relations, {
    removed: existingGenerated
      .filter((rule) => !desiredByKey.has(generatedRuleManagedKeyFromRule(rule, layerKey)))
      .reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0)
  });
  summary.relations.total = [...desiredByKey.values()]
    .reduce((sum, rule) => sum + runtimeRelationsFromRule(rule).length, 0);
  return summary;
}

function templateCandidateClasses(template) {
  const sourceRegex = template.source_class_regex || '';
  const regex = sourceRegex ? regexFromUiPattern(sourceRegex) : null;
  return availableSourceClasses().filter((item) => {
    if (!regex) {
      return true;
    }

    return regex.test(item.code)
      || regex.test(classDisplayName(item))
      || regex.test(item.hierarchyPath || '');
  });
}

function regexFromUiPattern(pattern) {
  let text = String(pattern ?? '').trim();
  let flags = '';
  if (text.startsWith('(?i)')) {
    flags = 'i';
    text = text.slice(4);
  }

  try {
    return new RegExp(text, flags);
  } catch (error) {
    const wildcardText = normalizeWildcardUiPattern(text);
    if (wildcardText.includes('*')) {
      return new RegExp(wildcardText.split('*').map(escapeRegex).join('.*'), flags);
    }

    throw error;
  }
}

function normalizeWildcardUiPattern(pattern) {
  return String(pattern ?? '').trim().replace(/^\(\?!\)\*/, '*');
}

function rulesFromTemplate(layerKey, template, candidate) {
  const dimension = templatePopulationDimension(template);
  if (!isTemplateDimensionMaterialized(dimension)) {
    return [ruleFromTemplate(layerKey, template, candidate)];
  }

  return templateDimensionValues(template, candidate, dimension)
    .map((dimensionValue) => ruleFromTemplate(layerKey, template, candidate, dimensionValue));
}

function renderTemplateTargetInitialValues(layerKey, template, context, materializedDimension) {
  const targetClassCode = String(template.target?.class_code ?? '').trim();
  const attributes = templateTargetObjectEditableAttributes(layerKey, targetClassCode);
  const attributeByExactCode = new Map(attributes.map((attribute) => [attributeCode(attribute), attribute]));
  const attributeByToken = new Map(attributes.map((attribute) => [canonicalToken(attributeCode(attribute)), attribute]));
  const values = {};

  for (const [code, rawValue] of Object.entries(template.target?.initial_user_values ?? {})) {
    const attribute = attributeByExactCode.get(code) ?? attributeByToken.get(canonicalToken(code));
    if (!attribute) {
      continue;
    }

    const renderedValue = renderTemplateString(rawValue, context);
    if (materializedDimension) {
      assertMaterializedTemplateValue(renderedValue, `Целевой атрибут ${code}`);
    }

    values[attributeCode(attribute)] = coerceRuleTargetObjectValue(attribute, renderedValue);
  }

  validateLayerAggregationTargetValues(layerKey, values, attributes, targetClassCode);
  return values;
}

function ruleFromTemplate(layerKey, template, candidate, dimensionValue = null) {
  const dimension = templatePopulationDimension(template);
  const materializedDimension = Boolean(dimensionValue) && isTemplateDimensionMaterialized(dimension);
  const context = templateContext(template, candidate, dimensionValue);
  const keyExpression = materializedDimension
    ? renderTemplateString(dimension.key_template || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template, context)
    : renderTemplateString(templatePopulationSourceKeyTemplate(template), context);
  if (materializedDimension) {
    assertMaterializedTemplateValue(keyExpression, 'Шаблон ключа измерения');
  }

  const selectionFilters = selectionFiltersFromTemplate(template);
  const dimensionCondition = materializedDimension
    ? templateDimensionConditionMatcher(dimension, dimensionValue, context)
    : null;
  const keyField = firstSourceAttributeFromTemplate(keyExpression)
    || firstSourceAttributeFromTemplate(templatePopulationSourceKeyTemplate(template))
    || dimensionCondition?.field
    || selectionFilters.find((filter) => filter.field)?.field
    || 'id';
  const renderedTargetName = renderTemplateString(template.target?.name_template || (materializedDimension ? '${dimension.name}' : '${class.description}'), context);
  const renderedTargetDescription = renderTemplateString(template.target?.description_template || '', context);
  if (materializedDimension) {
    assertMaterializedTemplateValue(renderedTargetName, 'Шаблон name');
    assertMaterializedTemplateValue(renderedTargetDescription, 'Шаблон description');
  }
  const renderedTargetInitialValues = renderTemplateTargetInitialValues(layerKey, template, context, materializedDimension);

  const ruleName = materializedDimension
    ? `${renderTemplateString(template.name || template.template_id, context)} / ${dimensionValue.name || dimensionValue.key}`
    : renderTemplateString(template.name || template.template_id, context);
  const ruleId = normalizeRuleId([
    layerKey,
    template.template_id,
    candidate.code,
    materializedDimension ? dimensionValue.key : ''
  ].filter(Boolean).join('-'));
  const generatedAt = new Date().toISOString();
  const fullFingerprint = templateFingerprint(template);
  const variablesFingerprint = templateVariablesFingerprint(template);
  const allRegex = [
    {
      field: 'className',
      pattern: `(?i)^${escapeRegex(candidate.code)}$`
    }
  ].concat(selectionFiltersToRegexMatchers(selectionFilters, 'include'))
    .concat(dimensionCondition ? [dimensionCondition] : []);
  const noneRegex = selectionFiltersToRegexMatchers(selectionFilters, 'exclude');
  const managedKey = generatedRuleManagedKey(
    layerKey,
    template.template_id,
    candidate.code,
    template.target?.class_code || '',
    materializedDimension ? dimensionValue.key : '');

  const rule = normalizeBindingRuleTarget({
    rule_id: ruleId,
    name: ruleName,
    layer: layerKey,
    priority: template.priority ?? 100,
    generated_from_template: template.template_id,
    template_version: template.version ?? 1,
    template_generation: {
      status: 'managed',
      template_id: template.template_id,
      template_name: template.name || template.template_id,
      template_version: template.version ?? 1,
      managed_key: managedKey,
      artifact_kind: 'rule',
      template_source_regex: template.source_class_regex || '',
      template_fingerprint: fullFingerprint,
      variables_fingerprint: variablesFingerprint,
      variables: cloneJson(context.vars),
      relation_fingerprint: stableHash(template.managed_relations ?? []),
      generated_at: generatedAt,
      candidate_class_code: candidate.code || '',
      dimension_key: materializedDimension ? dimensionValue.key : '',
      dimension_name: materializedDimension ? dimensionValue.name : '',
      dimension_value: materializedDimension ? dimensionValue.value : '',
      target_class_code: template.target?.class_code || ''
    },
    source: {
      class_code: candidate.code,
      key_attribute: keyField
    },
    when: {
      allRegex,
      ...(noneRegex.length > 0 ? { noneRegex } : {}),
      fieldExists: keyField
    },
    target: {
      class_code: template.target?.class_code || '',
      create_instance: true,
      idempotency_key: keyExpression,
      attribute_mappings: {
        name: renderedTargetName,
        [POPULATION_SOURCE_KEY_ATTRIBUTE]: keyExpression
      },
      initial_user_values: {
        description: renderedTargetDescription,
        ...renderedTargetInitialValues
      },
      user_responsibility_attributes: allowedUserResponsibilityAttributes(layerKey),
      created_by_template: {
        template_id: template.template_id,
        template_name: template.name || template.template_id,
        template_version: template.version ?? 1,
        managed_key: managedKey,
        template_fingerprint: fullFingerprint,
        generated_at: generatedAt,
        candidate_class_code: candidate.code || '',
        dimension_key: materializedDimension ? dimensionValue.key : ''
      }
    }
  }, layerKey);
  const artifactFingerprint = generatedRuleArtifactFingerprint(rule);
  rule.template_generation.artifact_fingerprint = artifactFingerprint;
  rule.target.created_by_template.artifact_fingerprint = artifactFingerprint;
  rule.target.created_by_template.reconcile_policy = 'managed_key_fingerprint';
  return rule;
}

function templateDimensionValues(template, candidate, dimension = templatePopulationDimension(template)) {
  const values = (() => {
    if (dimension.type === 'source_lookup') {
      return lookupDimensionValues(candidate, dimension);
    }
    if (dimension.type === 'source_bool') {
      return boolDimensionValues(dimension);
    }
    if (dimension.type === 'static_list') {
      return staticListDimensionValues(dimension);
    }
    if (dimension.type === 'range') {
      return rangeDimensionValues(dimension);
    }
    if (dimension.type === 'regex_capture') {
      return regexCaptureDimensionValues(candidate, dimension);
    }

    return sourceFieldDimensionValues(candidate, dimension);
  })();

  const seen = new Set();
  const normalizedValues = values.map((value, index) =>
    normalizeTemplateDimensionValue(template, candidate, dimension, value, index))
    .filter((value) => {
      const key = canonicalToken(value.key);
      if (!key || seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    });

  if (normalizedValues.length === 0) {
    throw templateDimensionNoValuesError(`Не удалось получить значения population dimension для ${candidate.code || 'класса-источника'}.`);
  }

  return normalizedValues;
}

function normalizeTemplateDimensionValue(template, candidate, dimension, value, index) {
  const raw = value && typeof value === 'object' && !Array.isArray(value)
    ? value
    : { key: value, value };
  const key = String(raw.key ?? raw.value ?? '').trim();
  const rawValue = String(raw.value ?? key).trim();
  const baseName = String((raw.name ?? raw.label ?? rawValue) || key).trim();
  const preliminary = {
    key,
    value: rawValue,
    name: baseName,
    condition_pattern: String(raw.condition_pattern ?? raw.pattern ?? '').trim(),
    source_field: String(raw.source_field ?? dimension.source_field ?? '').trim(),
    index
  };
  const context = templateContext(template, candidate, preliminary);
  const renderedName = renderTemplateString(dimension.name_template || DEFAULT_TEMPLATE_POPULATION_DIMENSION.name_template, context)
    || baseName
    || key;
  assertMaterializedTemplateValue(renderedName, 'Шаблон имени измерения');
  return {
    ...preliminary,
    name: renderedName
  };
}

function sourceFieldDimensionValues(candidate, dimension) {
  const values = sourceCardFieldValues(candidate.code, dimension.source_field);
  if (values.length === 0) {
    const reason = sourceFieldDimensionEmptyReason(candidate.code, dimension.source_field);
    if (reason) {
      throw templateDimensionNoValuesError(reason);
    }
  }

  return values.map((value) => ({ key: value, value, name: value, source_field: dimension.source_field }));
}

function regexCaptureDimensionValues(candidate, dimension) {
  const regex = regexFromUiPattern(dimension.regex);
  const values = [];
  const sourceValues = sourceCardFieldValues(candidate.code, dimension.source_field);
  if (sourceValues.length === 0) {
    const reason = sourceFieldDimensionEmptyReason(candidate.code, dimension.source_field);
    if (reason) {
      throw templateDimensionNoValuesError(reason);
    }
  }

  for (const value of sourceValues) {
    regex.lastIndex = 0;
    const match = String(value).match(regex);
    if (!match) {
      continue;
    }

    const capture = /^\d+$/.test(dimension.capture_group)
      ? match[Number(dimension.capture_group)]
      : match.groups?.[dimension.capture_group];
    if (String(capture ?? '').trim()) {
      values.push({
        key: String(capture).trim(),
        value: String(capture).trim(),
        name: String(capture).trim(),
        source_field: dimension.source_field
      });
    }
  }
  return values;
}

function templateDimensionNoValuesError(message) {
  const error = new Error(message);
  error.templateDimensionNoValues = true;
  return error;
}

function sourceFieldDimensionEmptyReason(sourceClass, sourceField) {
  const field = String(sourceField ?? '').trim();
  if (!field) {
    return 'поле population dimension не выбрано.';
  }

  const items = sourceClassInstanceItems(sourceClass);
  const sourceItems = items.filter((item) => String(item.layer).toLowerCase() === 'source');
  const cards = (sourceItems.length > 0 ? sourceItems : items).flatMap((item) => item.cards ?? []);
  if (!sourceClassCardsLoaded(sourceClass)) {
    return `карточки класса-источника ${sourceClass || ''} не загружены.`;
  }
  if (cards.length === 0) {
    return `в классе-источнике ${sourceClass || ''} нет карточек.`;
  }

  const option = sourceFieldOptionForClass(sourceClass, field);
  if (!option) {
    return `поле ${field} не найдено в схеме ${sourceClass || 'класса-источника'} при глубине рекурсии ${state.maxTraversalDepth}.`;
  }

  const dependencyClasses = sourceFieldDependencyClasses(sourceClass, field);
  const missingDependencies = dependencyClasses.filter((dependencyClass) => !sourceClassCardsLoaded(dependencyClass));
  if (missingDependencies.length > 0) {
    return `для поля ${field} не загружены связанные классы: ${missingDependencies.join(', ')}.`;
  }
  const emptyDependencies = dependencyClasses.filter((dependencyClass) => !sourceClassCardsAvailable(dependencyClass));
  if (emptyDependencies.length > 0) {
    return `для поля ${field} связанные классы не содержат карточек: ${emptyDependencies.join(', ')}.`;
  }

  const path = option.fieldRule?.cmdbPath ? ` путь ${option.fieldRule.cmdbPath}` : '';
  const cardSummary = sourceFieldPathCardSummary(sourceClass, option.fieldRule);
  const probe = sourceFieldPathResolutionProbe(sourceClass, option.fieldRule, cards);
  const fallbackValues = sourceCardFieldValuesByPathProbe(sourceClass, option.fieldRule);
  return [
    `по полю ${field}${path} нет непустых значений в загруженных карточках.`,
    fallbackValues.length > 0 ? `Резервная проверка пути видит значений: ${fallbackValues.length} (${fallbackValues.slice(0, 5).join(', ')}).` : '',
    cardSummary ? `Загружено карточек: ${cardSummary}.` : '',
    probe ? `Проверка первой карточки: ${probe}.` : ''
  ].filter(Boolean).join(' ');
}

function sourceFieldPathCardSummary(sourceClass, fieldRule) {
  const classes = [sourceClass].concat(sourceFieldDependencyClassesByRule(sourceClass, fieldRule));
  const seen = new Set();
  return classes
    .filter((classCode) => {
      const key = canonicalToken(classCode);
      if (!key || seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    })
    .map((classCode) => `${classCode}:${sourceClassCardCount(classCode)}`)
    .join(', ');
}

function sourceClassCardCount(classCode) {
  const items = sourceClassInstanceItems(classCode);
  const sourceItems = items.filter((item) => String(item.layer).toLowerCase() === 'source');
  return (sourceItems.length > 0 ? sourceItems : items)
    .reduce((count, item) => count + (Array.isArray(item.cards) ? item.cards.length : 0), 0);
}

function sourceFieldPathResolutionProbe(sourceClass, fieldRule, cards) {
  const path = cmdbPathSegmentsForFieldRule(sourceClass, fieldRule);
  if (path.length < 2 || path.some((segment) => segment.startsWith('{domain:'))) {
    return '';
  }

  let currentClass = sourceClass;
  let currentCard = cards.find(Boolean);
  if (!currentCard) {
    return '';
  }

  const steps = [];
  for (let index = 0; index < path.length; index += 1) {
    const segment = path[index];
    const last = index === path.length - 1;
    if (last) {
      const value = normalizeCardFieldValues(rawCardFieldValue(currentCard, segment))[0] || 'пусто';
      steps.push(`${currentClass}.${segment}=${value}`);
      break;
    }

    const attribute = sourceAttributeByCode(currentClass, segment);
    const targetClass = referenceTargetClass(attribute, currentClass);
    if (!targetClass) {
      steps.push(`${currentClass}.${segment}=нет целевого класса`);
      break;
    }

    const referenceIds = normalizeReferenceIds(rawCardFieldValue(currentCard, segment));
    if (referenceIds.length === 0) {
      steps.push(`${currentClass}.${segment}=пустая reference-ссылка`);
      break;
    }

    const targetCard = sourceClassCardById(targetClass, referenceIds[0]);
    steps.push(`${currentClass}.${segment}=${referenceIds[0]} -> ${targetClass}:${targetCard ? 'найдено' : 'не найдено'}`);
    if (!targetCard) {
      break;
    }

    currentClass = targetClass;
    currentCard = targetCard;
  }

  return steps.join('; ');
}

function lookupDimensionValues(candidate, dimension) {
  const lookupValues = lookupValuesForTemplateSourceField(candidate.code, dimension.source_field);
  if (lookupValues.length > 0) {
    return lookupValues;
  }

  return sourceFieldDimensionValues(candidate, dimension);
}

function lookupValuesForTemplateSourceField(sourceClass, sourceField) {
  const option = sourceFieldOptionsForClass(sourceClass).find((item) =>
    canonicalToken(item.value) === canonicalToken(sourceField));
  const lookupType = option?.fieldRule?.lookupType
    || option?.fieldRule?.resolve?.lookupType
    || '';
  if (!lookupType) {
    return [];
  }

  const lookup = state.lookups.find((item) =>
    canonicalToken(item.code || item.name) === canonicalToken(lookupType));
  return (lookup?.values ?? []).map((value) => {
    const key = String(value.code ?? value.name ?? value.value ?? '').trim();
    const name = String(value.displayName ?? value.description ?? key).trim();
    return { key, value: key, name };
  }).filter((value) => value.key);
}

function boolDimensionValues(dimension) {
  return [
    {
      key: 'true',
      value: 'true',
      name: normalizeLanguage(state.language) === 'en' ? 'Yes' : 'Да',
      source_field: dimension.source_field,
      condition_pattern: '^(true|True|TRUE|1|yes|Yes|YES|да|Да|ДА)$'
    },
    {
      key: 'false',
      value: 'false',
      name: normalizeLanguage(state.language) === 'en' ? 'No' : 'Нет',
      source_field: dimension.source_field,
      condition_pattern: '^(false|False|FALSE|0|no|No|NO|нет|Нет|НЕТ)$'
    }
  ];
}

function staticListDimensionValues(dimension) {
  return splitDimensionValuesText(dimension.values).map((line) => {
    const parts = line.split('|').map((part) => part.trim());
    const key = parts[0] || '';
    const name = parts[1] || '';
    const conditionPattern = parts.slice(2).join('|').trim();
    return {
      key,
      value: key,
      name: name || key,
      condition_pattern: conditionPattern || '',
      source_field: dimension.source_field
    };
  }).filter((value) => value.key);
}

function rangeDimensionValues(dimension) {
  const text = String(dimension.values ?? '').trim();
  const match = text.match(/^(\d+)\s*(?:-|\.\.)\s*(\d+)$/);
  if (!match) {
    return staticListDimensionValues(dimension);
  }

  const startText = match[1];
  const endText = match[2];
  const start = Number(startText);
  const end = Number(endText);
  if (!Number.isInteger(start) || !Number.isInteger(end) || end < start) {
    throw new Error(`Некорректный диапазон population dimension: ${text}.`);
  }

  const count = end - start + 1;
  if (count > TEMPLATE_DIMENSION_MAX_RULES) {
    throw new Error(`Range ${text} содержит ${count} значений, лимит ${TEMPLATE_DIMENSION_MAX_RULES}.`);
  }

  const width = Math.max(startText.length, endText.length);
  return Array.from({ length: count }, (_, index) => {
    const key = String(start + index).padStart(width, '0');
    return {
      key,
      value: key,
      name: key,
      source_field: dimension.source_field
    };
  });
}

function splitDimensionValuesText(text) {
  const source = String(text ?? '').trim();
  if (!source) {
    return [];
  }

  const lines = source.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  if (lines.length > 1) {
    return lines;
  }

  return source.split(',').map((item) => item.trim()).filter(Boolean);
}

function sourceCardFieldValues(sourceClass, field) {
  const values = [];
  const option = sourceFieldOptionForClass(sourceClass, field);
  for (const card of sourceClassCards(sourceClass)) {
    const directValues = normalizeCardFieldValues(rawCardFieldValue(card, field));
    if (directValues.length > 0) {
      values.push(...directValues);
      continue;
    }

    values.push(...resolveCardFieldValuesByCmdbPath(sourceClass, card, option?.fieldRule));
  }

  const normalizedValues = uniqueNonEmptyValues(values);
  if (normalizedValues.length > 0) {
    return normalizedValues;
  }

  return sourceCardFieldValuesByPathProbe(sourceClass, option?.fieldRule);
}

function sourceFieldOptionForClass(sourceClass, field) {
  const token = canonicalToken(field);
  if (!token) {
    return null;
  }

  return sourceFieldOptionsForClass(sourceClass).find((option) =>
    canonicalToken(option.value) === token) ?? null;
}

function sourceClassInstanceItems(sourceClass) {
  return state.cmdbClassInstances.filter((item) =>
    canonicalToken(item.classCode ?? item.code ?? item.class_code ?? item.name) === canonicalToken(sourceClass));
}

function sourceClassCards(sourceClass) {
  const items = sourceClassInstanceItems(sourceClass);
  const sourceItems = items.filter((item) => String(item.layer).toLowerCase() === 'source');
  return (sourceItems.length > 0 ? sourceItems : items)
    .flatMap((item) => Array.isArray(item.cards) ? item.cards : []);
}

function uniqueNonEmptyValues(values) {
  return [...new Set(values.map((value) => String(value ?? '').trim()).filter(Boolean))];
}

function rawCardFieldValue(card, field) {
  if (!card || !field) {
    return '';
  }

  const token = canonicalToken(field);
  const direct = card[field] ?? card.values?.[field] ?? card.Values?.[field] ?? card.attributes?.[field] ?? card.Attributes?.[field];
  if (direct !== undefined) {
    return direct;
  }

  for (const attribute of Array.isArray(card.attributes) ? card.attributes : []) {
    const code = attribute.code ?? attribute.name ?? attribute.Code ?? attribute.Name ?? '';
    if (canonicalToken(code) === token) {
      if (Object.hasOwn(attribute, 'value')) {
        return attribute.value ?? '';
      }

      if (Object.hasOwn(attribute, 'Value')) {
        return attribute.Value ?? '';
      }

      return attribute.displayValue ?? attribute.DisplayValue ?? '';
    }
  }

  return '';
}

function resolveCardFieldValuesByCmdbPath(sourceClass, card, fieldRule) {
  const path = cmdbPathSegmentsForFieldRule(sourceClass, fieldRule);
  if (path.length === 0 || path.some((segment) => segment.startsWith('{domain:'))) {
    return [];
  }

  let currentClass = sourceClass;
  let currentCards = [card];
  for (let index = 0; index < path.length; index += 1) {
    const segment = path[index];
    const last = index === path.length - 1;
    if (last) {
      return currentCards.flatMap((item) =>
        normalizeCardFieldValues(rawCardFieldValue(item, segment)));
    }

    const attribute = sourceAttributeByCode(currentClass, segment);
    const targetClass = referenceTargetClass(attribute, currentClass);
    if (!targetClass) {
      return [];
    }

    const nextCards = [];
    for (const item of currentCards) {
      const referenceValue = rawCardFieldValue(item, segment);
      for (const referenceId of normalizeReferenceIds(referenceValue)) {
        const targetCard = sourceClassCardById(targetClass, referenceId);
        if (targetCard) {
          nextCards.push(targetCard);
        }
      }
    }

    if (nextCards.length === 0) {
      return [];
    }

    currentClass = targetClass;
    currentCards = nextCards;
  }

  return [];
}

function sourceCardFieldValuesByPathProbe(sourceClass, fieldRule) {
  const path = cmdbPathSegmentsForFieldRule(sourceClass, fieldRule);
  if (path.length === 0 || path.some((segment) => segment.startsWith('{domain:'))) {
    return [];
  }

  const values = [];
  for (const card of sourceClassCards(sourceClass)) {
    values.push(...resolveSingleCardFieldValuesByCmdbPath(sourceClass, card, path));
  }

  return uniqueNonEmptyValues(values);
}

function resolveSingleCardFieldValuesByCmdbPath(sourceClass, card, path) {
  let currentClass = sourceClass;
  let currentCard = card;
  for (let index = 0; index < path.length; index += 1) {
    const segment = path[index];
    const last = index === path.length - 1;
    if (last) {
      return normalizeCardFieldValues(rawCardFieldValue(currentCard, segment));
    }

    const attribute = sourceAttributeByCode(currentClass, segment);
    const targetClass = referenceTargetClass(attribute, currentClass);
    if (!targetClass) {
      return [];
    }

    const referenceIds = normalizeReferenceIds(rawCardFieldValue(currentCard, segment));
    const targetCard = referenceIds
      .map((referenceId) => sourceClassCardById(targetClass, referenceId))
      .find(Boolean);
    if (!targetCard) {
      return [];
    }

    currentClass = targetClass;
    currentCard = targetCard;
  }

  return [];
}

function cmdbPathSegmentsForFieldRule(sourceClass, fieldRule) {
  if (fieldRule?.resolve?.mode !== 'cmdbPath') {
    return [];
  }

  const path = String(fieldRule.cmdbPath ?? '').trim();
  if (!path) {
    return [];
  }

  const segments = path.split('.').map((segment) => segment.trim()).filter(Boolean);
  const rootToken = canonicalToken(sourceClass);
  if (segments.length > 0 && canonicalToken(segments[0]) === rootToken) {
    return segments.slice(1);
  }

  return segments;
}

function sourceAttributeByCode(sourceClass, code) {
  const token = canonicalToken(code);
  return sourceDirectAttributes(sourceClass).find((attribute) =>
    canonicalToken(attributeCode(attribute)) === token) ?? null;
}

function sourceClassCardById(classCode, cardId) {
  const id = String(cardId ?? '').trim();
  if (!id) {
    return null;
  }

  const items = sourceClassInstanceItems(classCode);
  const sourceItems = items.filter((item) => String(item.layer).toLowerCase() === 'source');
  for (const classItem of sourceItems.length > 0 ? sourceItems : items) {
    const card = (classItem.cards ?? []).find((item) =>
      cardIdentityValues(item).some((value) => String(value) === id));
    if (card) {
      return card;
    }
  }

  return null;
}

function cardIdentityValues(card) {
  if (!card) {
    return [];
  }

  return [
    card.id,
    card.Id,
    card._id,
    rawCardFieldValue(card, 'Id'),
    rawCardFieldValue(card, 'id')
  ].map((value) => String(value ?? '').trim()).filter(Boolean);
}

function normalizeReferenceIds(value) {
  if (value === null || value === undefined) {
    return [];
  }

  if (Array.isArray(value)) {
    return value.flatMap(normalizeReferenceIds);
  }

  if (typeof value === 'object') {
    const candidates = [
      value._id,
      value.id,
      value.Id,
      value.value,
      value.Value,
      value.code,
      value.Code
    ].map((item) => String(item ?? '').trim()).filter(Boolean);
    return candidates.length > 0 ? [candidates[0]] : [];
  }

  const text = String(value ?? '').trim();
  if (!text) {
    return [];
  }

  if ((text.startsWith('{') && text.endsWith('}')) || (text.startsWith('[') && text.endsWith(']'))) {
    try {
      return normalizeReferenceIds(JSON.parse(text));
    } catch {
      return [text];
    }
  }

  return [text];
}

function normalizeCardFieldValues(value) {
  if (value === null || value === undefined) {
    return [];
  }
  if (Array.isArray(value)) {
    return value.flatMap(normalizeCardFieldValues);
  }
  if (typeof value === 'object') {
    const candidate = value.code ?? value.Code ?? value.name ?? value.Name ?? value.value ?? value.Value ?? value.description ?? value.Description ?? value.id ?? value.Id;
    return normalizeCardFieldValues(candidate);
  }

  return [String(value)];
}

function templateDimensionConditionMatcher(dimension, dimensionValue, context) {
  const field = String(dimension.condition_field || dimensionValue.source_field || dimension.source_field || '').trim();
  if (!field) {
    throw new Error('Для population dimension заполните поле условия или атрибут/путь источника.');
  }

  const patternTemplate = dimension.condition_pattern_template || dimensionValue.condition_pattern || '';
  const pattern = patternTemplate
    ? renderTemplateString(patternTemplate, context)
    : `(?i)^${escapeRegex(dimensionValue.value || dimensionValue.key)}$`;
  assertMaterializedTemplateValue(pattern, 'Regex условия population dimension');
  assertValidRegexPattern(pattern);
  return { field, pattern };
}

function assertMaterializedTemplateValue(value, label) {
  const text = String(value ?? '');
  if (/\$\{source\.[A-Za-z_][A-Za-z0-9_]*\}/.test(text)) {
    throw new Error(`${label} не может использовать ${'${source.*}'}: шаблон должен породить статическое правило до обработки конкретной карточки.`);
  }

  if (/\$\{[^}]+\}/.test(text)) {
    throw new Error(`${label} содержит неразрешенное выражение ${text}.`);
  }
}

function generatedRuleManagedKey(layerKey, templateId, sourceClassCode, targetClassCode, dimensionKey = '') {
  return normalizeRuleId([
    layerKey,
    'template',
    templateId,
    'rule',
    sourceClassCode,
    dimensionKey ? `dimension-${dimensionKey}` : '',
    targetClassCode
  ].filter(Boolean).join('-'));
}

function generatedRuleManagedKeyFromRule(rule, layerKey = '') {
  return String(rule?.template_generation?.managed_key
    || rule?.target?.created_by_template?.managed_key
    || '').trim()
    || generatedRuleManagedKey(
      rule?.layer || layerKey,
      ruleTemplateId(rule),
      ruleSourceClassCode(rule),
      ruleTargetClassCode(rule),
      rule?.template_generation?.dimension_key || rule?.target?.created_by_template?.dimension_key || '');
}

function generatedRuleArtifactFingerprint(rule) {
  const target = cloneJson(rule?.target ?? {});
  delete target.created_by_template;
  return stableHash({
    artifact_kind: 'rule',
    rule_id: rule?.rule_id || '',
    name: rule?.name || '',
    layer: rule?.layer || '',
    priority: rule?.priority ?? 100,
    source: rule?.source ?? {},
    when: rule?.when ?? {},
    target,
    relations: rule?.relations ?? []
  });
}

function generatedRuleTemplateMetadataFingerprint(rule) {
  const generation = rule?.template_generation ?? {};
  const createdByTemplate = rule?.target?.created_by_template ?? {};
  return stableHash({
    generated_from_template: rule?.generated_from_template || '',
    template_version: rule?.template_version ?? '',
    generation: {
      template_id: generation.template_id || '',
      template_name: generation.template_name || '',
      template_version: generation.template_version ?? '',
      template_source_regex: generation.template_source_regex || '',
      template_fingerprint: generation.template_fingerprint || '',
      variables_fingerprint: generation.variables_fingerprint || '',
      relation_fingerprint: generation.relation_fingerprint || '',
      candidate_class_code: generation.candidate_class_code || '',
      dimension_key: generation.dimension_key || '',
      dimension_name: generation.dimension_name || '',
      dimension_value: generation.dimension_value || '',
      target_class_code: generation.target_class_code || ''
    },
    created_by_template: {
      template_id: createdByTemplate.template_id || '',
      template_name: createdByTemplate.template_name || '',
      template_version: createdByTemplate.template_version ?? '',
      managed_key: createdByTemplate.managed_key || '',
      template_fingerprint: createdByTemplate.template_fingerprint || '',
      candidate_class_code: createdByTemplate.candidate_class_code || '',
      dimension_key: createdByTemplate.dimension_key || '',
      artifact_fingerprint: createdByTemplate.artifact_fingerprint || '',
      reconcile_policy: createdByTemplate.reconcile_policy || ''
    }
  });
}

function templateContext(template, candidate, dimension = null) {
  const context = {
    template: {
      id: template?.template_id || '',
      name: template?.name || template?.template_id || '',
      layer: template?.layer || ''
    },
    class: {
      code: candidate.code || '',
      name: candidate.name || candidate.code || '',
      description: classDisplayName(candidate),
      hierarchyPath: candidate.hierarchyPath || ''
    },
    dimension: templateDimensionContext(dimension),
    source: {},
    vars: {}
  };
  const dimensionSourceField = String(dimension?.source_field ?? '').trim();
  if (dimensionSourceField) {
    context.source[dimensionSourceField] = String(dimension?.value ?? dimension?.key ?? '').trim();
  }

  for (let pass = 0; pass < 4; pass += 1) {
    for (const variable of template.variables ?? []) {
      context.vars[variable.name] = renderTemplateString(variable.value, context);
    }
  }

  return context;
}

function templateDimensionContext(dimension) {
  const key = String(dimension?.key ?? '').trim();
  const value = String(dimension?.value ?? key).trim();
  const name = String((dimension?.name ?? value) || key).trim();
  const pattern = String(dimension?.condition_pattern ?? '').trim();
  return {
    key,
    value,
    name,
    label: name,
    pattern,
    regexKey: escapeRegex(key),
    regexValue: escapeRegex(value),
    regexName: escapeRegex(name)
  };
}

function renderTemplateString(template, context) {
  const text = String(template ?? '');
  let output = '';
  for (let index = 0; index < text.length; index += 1) {
    if (text[index] !== '$' || text[index + 1] !== '{') {
      output += text[index];
      continue;
    }

    const endIndex = findTemplateExpressionEnd(text, index + 2);
    if (endIndex < 0) {
      output += text[index];
      continue;
    }

    const original = text.slice(index, endIndex + 1);
    const expression = text.slice(index + 2, endIndex);
    const result = evaluateTemplateExpression(expression, context);
    output += result.unresolved ? original : String(result.value ?? '');
    index = endIndex;
  }

  return output;
}

function findTemplateExpressionEnd(text, startIndex) {
  let quote = '';
  let escaped = false;
  let depth = 0;
  for (let index = startIndex; index < text.length; index += 1) {
    const char = text[index];
    if (escaped) {
      escaped = false;
      continue;
    }

    if (char === '\\') {
      escaped = true;
      continue;
    }

    if (quote) {
      if (char === quote) {
        quote = '';
      }
      continue;
    }

    if (char === '"' || char === "'") {
      quote = char;
      continue;
    }

    if (char === '(') {
      depth += 1;
      continue;
    }

    if (char === ')') {
      depth = Math.max(0, depth - 1);
      continue;
    }

    if (char === '}' && depth === 0) {
      return index;
    }
  }

  return -1;
}

function evaluateTemplateExpression(expression, context) {
  const text = String(expression ?? '').trim();
  if (!text) {
    return { value: '', unresolved: false };
  }

  const literal = parseTemplateLiteral(text);
  if (literal.matched) {
    return { value: literal.value, unresolved: false };
  }

  if (/^-?\d+(\.\d+)?$/.test(text)) {
    return { value: text, unresolved: false };
  }

  const fn = parseTemplateFunction(text);
  if (fn) {
    return evaluateTemplateFunction(fn, context);
  }

  const path = templatePathValue(text, context);
  if (path.resolved || path.unresolved) {
    return path;
  }

  return { value: '', unresolved: true };
}

function parseTemplateLiteral(text) {
  const quote = text[0];
  if ((quote !== '"' && quote !== "'") || text.at(-1) !== quote) {
    return { matched: false, value: '' };
  }

  let value = '';
  let escaped = false;
  for (let index = 1; index < text.length - 1; index += 1) {
    const char = text[index];
    if (escaped) {
      if (char === 'n') {
        value += '\n';
      } else if (char === 't') {
        value += '\t';
      } else if (char === quote || char === '\\') {
        value += char;
      } else {
        value += `\\${char}`;
      }
      escaped = false;
      continue;
    }

    if (char === '\\') {
      escaped = true;
      continue;
    }

    value += char;
  }

  return { matched: true, value };
}

function parseTemplateFunction(text) {
  const openIndex = text.indexOf('(');
  if (openIndex <= 0 || text.at(-1) !== ')') {
    return null;
  }

  const name = text.slice(0, openIndex).trim();
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(name)) {
    return null;
  }

  return {
    name: name.toLowerCase(),
    args: splitTemplateArguments(text.slice(openIndex + 1, -1))
  };
}

function splitTemplateArguments(text) {
  const args = [];
  let current = '';
  let quote = '';
  let escaped = false;
  let depth = 0;
  for (const char of String(text ?? '')) {
    if (escaped) {
      current += char;
      escaped = false;
      continue;
    }

    if (char === '\\') {
      current += char;
      escaped = true;
      continue;
    }

    if (quote) {
      current += char;
      if (char === quote) {
        quote = '';
      }
      continue;
    }

    if (char === '"' || char === "'") {
      current += char;
      quote = char;
      continue;
    }

    if (char === '(') {
      current += char;
      depth += 1;
      continue;
    }

    if (char === ')') {
      if (depth === 0) {
        throw new Error('Некорректные скобки в transform/extract выражении.');
      }

      current += char;
      depth -= 1;
      continue;
    }

    if (char === ',' && depth === 0) {
      args.push(current.trim());
      current = '';
      continue;
    }

    current += char;
  }

  if (quote || depth !== 0) {
    throw new Error('Некорректные аргументы transform/extract выражения.');
  }

  if (current.trim() || text.includes(',')) {
    args.push(current.trim());
  }

  return args;
}

function templatePathValue(expression, context) {
  const match = String(expression ?? '').match(/^(template|class|vars|dimension|source)\.([A-Za-z_][A-Za-z0-9_]*)$/);
  if (!match) {
    return { value: '', resolved: false, unresolved: false };
  }

  const [, scope, key] = match;
  if (scope === 'source') {
    if (Object.hasOwn(context?.source ?? {}, key)) {
      return {
        value: context.source[key],
        resolved: true,
        unresolved: false
      };
    }

    return { value: '', resolved: false, unresolved: true };
  }

  return {
    value: context?.[scope]?.[key] ?? '',
    resolved: true,
    unresolved: false
  };
}

function evaluateTemplateFunction(fn, context) {
  const values = fn.args.map((argument) => {
    const result = evaluateTemplateExpression(argument, context);
    if (result.unresolved) {
      throw new Error(`Функция ${fn.name} не может использовать отложенное выражение ${argument}; transform/extract выполняется при применении шаблона.`);
    }

    assertResolvedTemplateTransformValue(result.value, fn.name);
    return String(result.value ?? '');
  });

  if (fn.name === 'extract') {
    return { value: templateExtract(values), unresolved: false };
  }

  if (fn.name === 'replace') {
    return { value: templateReplace(values), unresolved: false };
  }

  if (fn.name === 'lower') {
    requireTemplateFunctionArgs(fn.name, values, 1);
    return { value: values[0].toLocaleLowerCase(), unresolved: false };
  }

  if (fn.name === 'upper') {
    requireTemplateFunctionArgs(fn.name, values, 1);
    return { value: values[0].toLocaleUpperCase(), unresolved: false };
  }

  if (fn.name === 'trim') {
    requireTemplateFunctionArgs(fn.name, values, 1);
    return { value: values[0].trim(), unresolved: false };
  }

  if (fn.name === 'default') {
    requireTemplateFunctionArgs(fn.name, values, 2);
    return { value: values[0] || values[1] || '', unresolved: false };
  }

  throw new Error(`Неизвестная функция шаблона ${fn.name}.`);
}

function templateExtract(values) {
  requireTemplateFunctionArgs('extract', values, 2);
  const source = values[0] ?? '';
  const pattern = values[1] ?? '';
  const group = values[2] || '1';
  const fallback = values[3] ?? '';
  const match = source.match(regexFromTemplateTransformPattern(pattern));
  if (!match) {
    return fallback;
  }

  if (/^\d+$/.test(group)) {
    return match[Number(group)] ?? fallback;
  }

  return match.groups?.[group] ?? fallback;
}

function templateReplace(values) {
  requireTemplateFunctionArgs('replace', values, 2);
  const source = values[0] ?? '';
  const pattern = values[1] ?? '';
  const replacement = values[2] ?? '';
  const flags = values.length >= 4 ? values[3] : 'g';
  return source.replace(regexFromTemplateTransformPattern(pattern, flags), replacement);
}

function regexFromTemplateTransformPattern(pattern, flags = '') {
  let text = String(pattern ?? '');
  let regexFlags = String(flags ?? '');
  if (text.startsWith('(?i)')) {
    text = text.slice(4);
    regexFlags += 'i';
  }

  regexFlags = [...new Set(regexFlags.split(''))].join('');
  return new RegExp(text, regexFlags);
}

function requireTemplateFunctionArgs(name, values, minimum) {
  if (values.length < minimum) {
    throw new Error(`Функция ${name} требует минимум ${minimum} аргументов.`);
  }
}

function assertResolvedTemplateTransformValue(value, functionName) {
  if (/\$\{source\.[A-Za-z_][A-Za-z0-9_]*\}/.test(String(value ?? ''))) {
    throw new Error(`Функция ${functionName} не может изменять ${'${source.*}'} при материализации шаблона; сохраните source-значение целиком или добавьте runtime-преобразование.`);
  }
}

function firstSourceAttributeFromTemplate(template) {
  const match = String(template ?? '').match(/\$\{source\.([A-Za-z_][A-Za-z0-9_]*)\}/);
  return match?.[1] || '';
}

function templateFingerprint(template) {
  return stableHash(templateManagedDefinition(template));
}

function templateManagedDefinition(template) {
  return {
    template_id: template?.template_id || '',
    name: template?.name || '',
    layer: template?.layer || '',
    enabled: template?.enabled !== false,
    available_for: template?.available_for ?? [],
    managed_by: template?.managed_by || '',
    source_class_regex: template?.source_class_regex || '',
    population_dimension: templatePopulationDimension(template),
    filter: template?.filter ?? {},
    priority: template?.priority ?? 100,
    target: template?.target ?? {},
    variables: template?.variables ?? [],
    managed_relations: template?.managed_relations ?? []
  };
}

function templateVariablesFingerprint(template) {
  return stableHash(template?.variables ?? []);
}

function templateRegexFingerprint(template) {
  return stableHash({
    source_class_regex: template?.source_class_regex || '',
    filter: selectionFiltersToTemplateFilter(selectionFiltersFromTemplate(template)),
    population_dimension: templatePopulationDimension(template)
  });
}

function stableHash(value) {
  const text = stableStringify(value);
  let hash = 5381;
  for (let index = 0; index < text.length; index += 1) {
    hash = ((hash << 5) + hash) ^ text.charCodeAt(index);
  }

  return `${(hash >>> 0).toString(16)}:${text.length}`;
}

function stableStringify(value) {
  if (Array.isArray(value)) {
    return `[${value.map(stableStringify).join(',')}]`;
  }

  if (value && typeof value === 'object') {
    return `{${Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${stableStringify(value[key])}`)
      .join(',')}}`;
  }

  return JSON.stringify(value);
}

function normalizeTemplateDocument(document, layerKey) {
  const normalized = document && typeof document === 'object' && !Array.isArray(document)
    ? document
    : defaultTemplateDocument(layerKey);
  normalized.layer = normalized.layer || layerKey;
  normalized.templates = Array.isArray(normalized.templates)
    ? normalized.templates.map((template) => normalizeTemplate(template, layerKey)).filter((template) => template.template_id)
    : [];
  normalized.templateVersions = Array.isArray(normalized.templateVersions)
    ? normalized.templateVersions.map((snapshot) => normalizeTemplateVersionSnapshot(snapshot, layerKey))
        .filter((snapshot) => snapshot.template_id && snapshot.template_version)
    : [];
  return normalized;
}

function normalizeTemplate(template, layerKey) {
  const normalized = template && typeof template === 'object' && !Array.isArray(template)
    ? template
    : {};
  const hasPopulationDimension = Object.hasOwn(normalized, 'population_dimension');
  normalized.template_id = normalizeRuleId(normalized.template_id || normalized.name || '');
  normalized.name = normalized.name || normalized.template_id;
  normalized.layer = normalized.layer || layerKey;
  normalized.available_for = Array.isArray(normalized.available_for)
    ? normalized.available_for.map((item) => String(item).trim().toLowerCase()).filter((item) => ['service', 'suppression'].includes(item))
    : (layerKey === 'shared' ? ['service', 'suppression'] : [layerKey]);
  normalized.managed_by = normalized.managed_by || (layerKey === 'shared' ? 'shared' : layerKey);
  normalized.enabled = normalized.enabled !== false;
  normalized.source_class_regex = normalized.source_class_regex || '';
  if (hasPopulationDimension) {
    normalized.population_dimension = normalizeTemplatePopulationDimension(
      normalized.population_dimension,
      { missingMode: 'default' });
  }
  normalized.filter = selectionFiltersToTemplateFilter(selectionFiltersFromTemplate(normalized));
  normalized.priority = Number(normalized.priority || 100);
  normalized.version = Number(normalized.version || 1);
  normalized.lifecycle = normalized.lifecycle && typeof normalized.lifecycle === 'object' && !Array.isArray(normalized.lifecycle)
    ? normalized.lifecycle
    : {};
  normalized.target = normalized.target && typeof normalized.target === 'object' && !Array.isArray(normalized.target)
    ? normalized.target
    : {};
  normalized.target.class_code = normalized.target.class_code || '';
  normalized.target.name_template = normalized.target.name_template || '${class.description}';
  normalized.target.description_template = normalized.target.description_template || 'Автоматически создано для ${class.description}';
  normalized.target.population_source_key_template = normalized.target.population_source_key_template || DEFAULT_TEMPLATE_POPULATION_SOURCE_KEY;
  normalized.target.initial_user_values = normalized.target.initial_user_values
    && typeof normalized.target.initial_user_values === 'object'
    && !Array.isArray(normalized.target.initial_user_values)
    ? normalizeTemplateTargetInitialValues(normalized.layer || layerKey, normalized.target.initial_user_values)
    : {};
  normalized.variables = Array.isArray(normalized.variables)
    ? normalized.variables
        .filter((variable) => variable?.name)
        .map((variable) => ({ name: String(variable.name), value: String(variable.value ?? '') }))
    : [];
  normalized.managed_relations = Array.isArray(normalized.managed_relations)
    ? normalized.managed_relations.map((relation) => normalizeTemplateManagedRelation(relation, normalized, layerKey))
        .filter((relation) => relation.managed_key)
    : [];
  return normalized;
}

function normalizeTemplatePopulationDimension(dimension, options = {}) {
  const missingMode = options.missingMode || 'default';
  const source = dimension && typeof dimension === 'object' && !Array.isArray(dimension)
    ? dimension
    : {};
  const type = normalizeTemplatePopulationDimensionType(source.type || (missingMode === 'legacy' ? 'legacy' : DEFAULT_TEMPLATE_POPULATION_DIMENSION.type));
  const materialized = type !== 'legacy' && source.enabled !== false;
  const maxRules = clampNumber(Number(source.max_rules || source.maxRules || TEMPLATE_DIMENSION_DEFAULT_MAX_RULES), TEMPLATE_DIMENSION_DEFAULT_MAX_RULES, 1, TEMPLATE_DIMENSION_MAX_RULES);

  if (!materialized) {
    return {
      enabled: false,
      type: 'legacy',
      source_field: '',
      values: '',
      regex: '',
      capture_group: '1',
      key_template: DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template,
      name_template: DEFAULT_TEMPLATE_POPULATION_DIMENSION.name_template,
      condition_field: '',
      condition_pattern_template: '',
      max_rules: maxRules
    };
  }

  return {
    enabled: true,
    type,
    source_field: String(source.source_field ?? source.sourceField ?? '').trim(),
    values: String(source.values ?? '').trim(),
    regex: String(source.regex ?? source.pattern ?? '').trim(),
    capture_group: String(source.capture_group ?? source.captureGroup ?? '1').trim() || '1',
    key_template: String(source.key_template ?? source.keyTemplate ?? DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template).trim()
      || DEFAULT_TEMPLATE_POPULATION_DIMENSION.key_template,
    name_template: String(source.name_template ?? source.nameTemplate ?? DEFAULT_TEMPLATE_POPULATION_DIMENSION.name_template).trim()
      || DEFAULT_TEMPLATE_POPULATION_DIMENSION.name_template,
    condition_field: String(source.condition_field ?? source.conditionField ?? '').trim(),
    condition_pattern_template: String(source.condition_pattern_template ?? source.conditionPatternTemplate ?? '').trim(),
    max_rules: maxRules
  };
}

function normalizeTemplatePopulationDimensionType(type) {
  const normalized = String(type ?? '').trim().toLowerCase();
  return TEMPLATE_POPULATION_DIMENSION_TYPES.has(normalized)
    ? normalized
    : DEFAULT_TEMPLATE_POPULATION_DIMENSION.type;
}

function templatePopulationDimension(template) {
  const hasPopulationDimension = Object.hasOwn(template ?? {}, 'population_dimension');
  return normalizeTemplatePopulationDimension(template?.population_dimension, {
    missingMode: hasPopulationDimension ? 'default' : 'legacy'
  });
}

function isTemplateDimensionMaterialized(dimension) {
  const normalized = normalizeTemplatePopulationDimension(dimension, { missingMode: 'legacy' });
  return normalized.enabled !== false && normalized.type !== 'legacy';
}

function defaultTemplateDocument(layerKey) {
  return {
    layer: layerKey,
    templates: [],
    templateVersions: []
  };
}

function normalizeTemplateVersionSnapshot(snapshot, layerKey) {
  const normalized = snapshot && typeof snapshot === 'object' && !Array.isArray(snapshot)
    ? snapshot
    : {};
  normalized.layer = normalized.layer || layerKey;
  normalized.template_id = String(normalized.template_id ?? '').trim();
  normalized.template_version = Number(normalized.template_version || normalized.version || 0);
  normalized.content_hash = String(normalized.content_hash ?? '').trim();
  normalized.change_mode = String(normalized.change_mode ?? '').trim();
  normalized.created_at = String(normalized.created_at ?? '').trim();
  normalized.definition = normalized.definition && typeof normalized.definition === 'object' && !Array.isArray(normalized.definition)
    ? normalized.definition
    : {};
  normalized.managed_relations = Array.isArray(normalized.managed_relations)
    ? normalized.managed_relations.map((relation) => normalizeTemplateManagedRelation(relation, normalized, layerKey))
        .filter((relation) => relation.managed_key)
    : [];
  return normalized;
}

function normalizeTemplateManagedRelation(relation, template, layerKey) {
  const normalized = relation && typeof relation === 'object' && !Array.isArray(relation)
    ? relation
    : {};
  const kind = ['template', 'rule', 'rule_template', 'static_class'].includes(String(normalized.kind ?? '').trim())
    ? String(normalized.kind).trim()
    : '';
  const targetTemplateId = String(normalized.target_template_id ?? normalized.template_id ?? '').trim();
  const targetRuleId = String(normalized.target_rule_id ?? normalized.rule_id ?? '').trim();
  const targetClassCode = String(normalized.target_class_code ?? normalized.class_code ?? '').trim();
  const targetCardId = String(normalized.target_card_id ?? normalized.card_id ?? '').trim();
  const relationRole = String(normalized.relation_role ?? normalized.role ?? 'uses').trim() || 'uses';
  const effectiveKind = kind || (targetTemplateId ? 'template' : targetRuleId ? 'rule' : targetClassCode ? 'static_class' : '');
  const attributes = normalized.attributes && typeof normalized.attributes === 'object' && !Array.isArray(normalized.attributes)
    ? normalized.attributes
    : {};
  const relationQualifier = effectiveKind === 'static_class'
    ? targetCardId
    : stableHash(attributes.match ?? {});
  const managedKey = String(normalized.managed_key ?? '').trim()
    || templateManagedRelationKey(layerKey, template?.template_id || '', effectiveKind, relationRole, targetTemplateId || targetRuleId || targetClassCode, relationQualifier);
  const fingerprint = String(normalized.artifact_fingerprint ?? '').trim()
    || stableHash({
      kind: effectiveKind,
      relation_role: relationRole,
      target_template_id: targetTemplateId,
      target_rule_id: targetRuleId,
      target_class_code: targetClassCode,
      target_card_id: targetCardId,
      attributes
    });

  return {
    kind: effectiveKind,
    relation_role: relationRole,
    target_template_id: targetTemplateId,
    target_rule_id: targetRuleId,
    target_class_code: targetClassCode,
    target_card_id: targetCardId,
    managed_key: managedKey,
    artifact_fingerprint: fingerprint,
    attributes
  };
}

function normalizeRuleManagedRelation(relation, rule, layerKey) {
  const normalized = relation && typeof relation === 'object' && !Array.isArray(relation)
    ? relation
    : {};
  const normalizedKind = String(normalized.kind ?? '').trim();
  const targetTemplateId = String(normalized.target_template_id ?? normalized.template_id ?? '').trim();
  const targetRuleId = String(normalized.target_rule_id ?? normalized.rule_id ?? '').trim();
  const effectiveKind = ['template', 'rule'].includes(normalizedKind)
    ? normalizedKind
    : targetTemplateId ? 'template' : 'rule';
  const relationRole = String(normalized.relation_role ?? normalized.role ?? 'uses').trim() || 'uses';
  const targetId = effectiveKind === 'template' ? targetTemplateId : targetRuleId;
  const managedKey = String(normalized.managed_key ?? '').trim()
    || ruleManagedRelationKey(layerKey, rule?.rule_id || '', relationRole, targetId, effectiveKind);
  const attributes = normalized.attributes && typeof normalized.attributes === 'object' && !Array.isArray(normalized.attributes)
    ? normalized.attributes
    : {};
  const fingerprint = String(normalized.artifact_fingerprint ?? '').trim()
    || stableHash({
      kind: effectiveKind,
      relation_role: relationRole,
      target_template_id: targetTemplateId,
      target_rule_id: targetRuleId,
      attributes
    });

  return {
    kind: effectiveKind,
    relation_role: relationRole,
    target_template_id: targetTemplateId,
    target_rule_id: targetRuleId,
    managed_key: managedKey,
    artifact_fingerprint: fingerprint,
    attributes
  };
}

function templateManagedRelationKey(layerKey, templateId, kind, relationRole, target, targetCardId = '') {
  if (!templateId || !kind || !target) {
    return '';
  }

  return normalizeRuleId([
    layerKey,
    'template',
    templateId,
    'relation',
    kind,
    relationRole,
    target,
    targetCardId
  ].filter(Boolean).join('-'));
}

function ruleManagedRelationKey(layerKey, ruleId, relationRole, targetId, targetKind = 'rule') {
  if (!ruleId || !targetId) {
    return '';
  }

  return normalizeRuleId([
    layerKey,
    'rule',
    ruleId,
    'relation',
    targetKind,
    relationRole,
    targetId
  ].filter(Boolean).join('-'));
}

function templateEditorConfig(layerKey) {
  const prefix = layerKey === 'service' ? 'serviceTemplate' : 'suppressionTemplate';
  return {
    select: document.querySelector(`#${prefix}Select`),
    id: document.querySelector(`#${prefix}Id`),
    name: document.querySelector(`#${prefix}Name`),
    sourceRegex: document.querySelector(`#${prefix}SourceRegex`),
    priority: document.querySelector(`#${prefix}Priority`),
    targetClass: document.querySelector(`#${prefix}TargetClass`),
    targetAttributeList: document.querySelector(`#${prefix}AttributeList`),
    targetName: document.querySelector(`#${prefix}TargetName`),
    targetDescription: document.querySelector(`#${prefix}TargetDescription`),
    sourceKey: document.querySelector(`#${prefix}SourceKey`),
    populationType: document.querySelector(`#${prefix}PopulationType`),
    populationSourceField: document.querySelector(`#${prefix}PopulationSourceField`),
    populationSourceFieldOptions: document.querySelector(`#${prefix}PopulationSourceFieldOptions`),
    populationValues: document.querySelector(`#${prefix}PopulationValues`),
    populationRegex: document.querySelector(`#${prefix}PopulationRegex`),
    populationCaptureGroup: document.querySelector(`#${prefix}PopulationCaptureGroup`),
    populationKeyTemplate: document.querySelector(`#${prefix}PopulationKeyTemplate`),
    populationNameTemplate: document.querySelector(`#${prefix}PopulationNameTemplate`),
    populationConditionField: document.querySelector(`#${prefix}PopulationConditionField`),
    populationConditionFieldOptions: document.querySelector(`#${prefix}PopulationConditionFieldOptions`),
    populationConditionPattern: document.querySelector(`#${prefix}PopulationConditionPattern`),
    populationMaxRules: document.querySelector(`#${prefix}PopulationMaxRules`),
    populationPreview: document.querySelector(`#${prefix}PopulationPreview`),
    deleteMode: document.querySelector(`#${prefix}DeleteMode`),
    selectionFilterList: document.querySelector(`#${prefix}SelectionFilterList`),
    variableList: document.querySelector(`#${prefix}VariableList`),
    fieldOptions: document.querySelector(`#${prefix}SourceFieldOptions`),
    sourceFieldCopySelect: document.querySelector(`#${prefix}SourceFieldCopySelect`),
    sourceFieldCopyValue: document.querySelector(`#${prefix}SourceFieldCopyValue`),
    sourceFieldCopyExpression: document.querySelector(`#${prefix}SourceFieldCopyExpression`),
    status: document.querySelector(`#${prefix}Status`)
  };
}

function sourceFieldOptionsForClass(sourceClass) {
  if (!sourceClass) {
    return [];
  }

  const rootClass = sourceClass;
  const options = [];
  for (const attribute of sourceDirectAttributes(sourceClass)) {
    if (!isReadableSourceAttribute(attribute)) {
      continue;
    }

    if (isReferenceSourceAttribute(attribute)) {
      options.push(...sourceReferenceLeafFieldOptions(rootClass, attribute));
      continue;
    }

    const fieldKey = attributeCode(attribute);
    options.push({
      value: fieldKey,
      label: `${fieldKey}${attribute.type ? ` / ${attribute.type}` : ''}`,
      meta: attribute.description || '',
      fieldRule: sourceFieldRuleForDirectAttribute(rootClass, attribute, fieldKey)
    });
  }

  options.push(...sourceDomainLeafFieldOptions(rootClass));
  return uniqueSourceFieldOptions(options)
    .sort((left, right) => left.label.localeCompare(right.label, undefined, { sensitivity: 'base' }));
}

function uniqueSourceFieldOptions(options) {
  const seen = new Set();
  return options.filter((option) => {
    const key = canonicalToken(option.value);
    if (!key || seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

function sourceDirectAttributes(sourceClass) {
  const schema = state.cmdbClassSchemas.find((item) =>
    canonicalToken(item.code) === canonicalToken(sourceClass));
  return [...(schema?.attributes ?? [])].sort((left, right) =>
    String(left.description || left.code || left.name).localeCompare(String(right.description || right.code || right.name), undefined, {
      sensitivity: 'base'
    }));
}

function sourceReferenceLeafFieldOptions(rootClass, attribute, prefix = [], depth = 1, seen = new Set(), currentClass = rootClass) {
  const path = [...prefix, attribute];
  const targetClass = referenceTargetClass(attribute, currentClass);
  if (!targetClass) {
    return [unresolvedReferenceFieldOption(rootClass, path, 'целевой класс не найден')];
  }

  if (depth > state.maxTraversalDepth) {
    return [unresolvedReferenceFieldOption(rootClass, path, `достигнута максимальная глубина ${state.maxTraversalDepth}`)];
  }

  const visitKey = `${targetClass}:${attributeCode(attribute)}:${depth}`;
  if (seen.has(visitKey)) {
    return [unresolvedReferenceFieldOption(rootClass, path, 'обнаружен цикл reference-ссылок')];
  }

  const nextSeen = new Set(seen);
  nextSeen.add(visitKey);
  const options = [];
  for (const targetAttribute of sourceDirectAttributes(targetClass)) {
    if (!isReadableSourceAttribute(targetAttribute)) {
      continue;
    }

    if (isReferenceSourceAttribute(targetAttribute)) {
      const nextPath = [...path, targetAttribute];
      if (depth >= state.maxTraversalDepth) {
        options.push(unresolvedReferenceFieldOption(rootClass, nextPath, `достигнута максимальная глубина ${state.maxTraversalDepth}`));
      } else {
        options.push(...sourceReferenceLeafFieldOptions(rootClass, targetAttribute, path, depth + 1, nextSeen, targetClass));
      }
      continue;
    }

    const leafPath = [...path, targetAttribute];
    const fieldKey = fieldKeyForCmdbPath(leafPath);
    options.push({
      value: fieldKey,
      label: `${leafPath.map(attributeCode).join(' -> ')}${targetAttribute.type ? ` / ${targetAttribute.type}` : ''}`,
      meta: `путь ${[rootClass, ...leafPath.map(attributeCode)].join('.')}`,
      fieldRule: sourceFieldRuleForCmdbPath(rootClass, leafPath)
    });
  }

  return options;
}

function unresolvedReferenceFieldOption(rootClass, path, reason) {
  const fieldKey = fieldKeyForCmdbPath(path);
  const readablePath = path.map(attributeCode).join(' -> ');
  return {
    value: fieldKey,
    label: `${readablePath} / неразрешенная reference-ссылка`,
    meta: `путь ${[rootClass, ...path.map(attributeCode)].join('.')} · ${reason}`,
    fieldRule: sourceFieldRuleForUnresolvedPath(rootClass, path, 'unresolved_reference', reason)
  };
}

function sourceDomainLeafFieldOptions(rootClass) {
  const options = [];
  for (const targetClass of sourceDomainTargetClassesForSourceClass(rootClass)) {
    for (const attribute of sourceDirectAttributes(targetClass)) {
      if (!isReadableSourceAttribute(attribute)) {
        continue;
      }

      if (isReferenceSourceAttribute(attribute)) {
        options.push(...sourceDomainReferenceLeafFieldOptions(rootClass, targetClass, attribute));
        continue;
      }

      const leafPath = [attribute];
      options.push({
        value: fieldKeyForDomainPath(targetClass, leafPath),
        label: `domain-связь ${sourceClassDisplayName(targetClass)} -> ${attributeCode(attribute)}${attribute.type ? ` / ${attribute.type}` : ''}`,
        meta: `путь ${rootClass}.{domain:${targetClass}}.${attributeCode(attribute)}`,
        fieldRule: sourceFieldRuleForDomainPath(rootClass, targetClass, leafPath)
      });
    }
  }

  return options;
}

function sourceDomainReferenceLeafFieldOptions(rootClass, domainTargetClass, attribute, prefix = [], depth = 1, seen = new Set(), currentClass = domainTargetClass) {
  const path = [...prefix, attribute];
  const targetClass = referenceTargetClass(attribute, currentClass);
  if (!targetClass) {
    return [unresolvedDomainFieldOption(rootClass, domainTargetClass, path, 'целевой класс не найден')];
  }

  if (depth > state.maxTraversalDepth) {
    return [unresolvedDomainFieldOption(rootClass, domainTargetClass, path, `достигнута максимальная глубина ${state.maxTraversalDepth}`)];
  }

  const visitKey = `${domainTargetClass}:${targetClass}:${attributeCode(attribute)}:${depth}`;
  if (seen.has(visitKey)) {
    return [unresolvedDomainFieldOption(rootClass, domainTargetClass, path, 'обнаружен цикл reference-ссылок')];
  }

  const nextSeen = new Set(seen);
  nextSeen.add(visitKey);
  const options = [];
  for (const targetAttribute of sourceDirectAttributes(targetClass)) {
    if (!isReadableSourceAttribute(targetAttribute)) {
      continue;
    }

    if (isReferenceSourceAttribute(targetAttribute)) {
      const nextPath = [...path, targetAttribute];
      if (depth >= state.maxTraversalDepth) {
        options.push(unresolvedDomainFieldOption(rootClass, domainTargetClass, nextPath, `достигнута максимальная глубина ${state.maxTraversalDepth}`));
      } else {
        options.push(...sourceDomainReferenceLeafFieldOptions(rootClass, domainTargetClass, targetAttribute, path, depth + 1, nextSeen, targetClass));
      }
      continue;
    }

    const leafPath = [...path, targetAttribute];
    options.push({
      value: fieldKeyForDomainPath(domainTargetClass, leafPath),
      label: `domain-связь ${sourceClassDisplayName(domainTargetClass)} -> ${leafPath.map(attributeCode).join(' -> ')}${targetAttribute.type ? ` / ${targetAttribute.type}` : ''}`,
      meta: `путь ${rootClass}.{domain:${domainTargetClass}}.${leafPath.map(attributeCode).join('.')}`,
      fieldRule: sourceFieldRuleForDomainPath(rootClass, domainTargetClass, leafPath)
    });
  }

  return options;
}

function unresolvedDomainFieldOption(rootClass, domainTargetClass, path, reason) {
  const fieldKey = fieldKeyForDomainPath(domainTargetClass, path);
  const readablePath = path.map(attributeCode).join(' -> ');
  return {
    value: fieldKey,
    label: `domain-связь ${sourceClassDisplayName(domainTargetClass)} -> ${readablePath} / неразрешенная reference-ссылка`,
    meta: `путь ${rootClass}.{domain:${domainTargetClass}}.${path.map(attributeCode).join('.')} · ${reason}`,
    fieldRule: sourceFieldRuleForUnresolvedDomainPath(rootClass, domainTargetClass, path, 'unresolved_domain', reason)
  };
}

function sourceDomainTargetClassesForSourceClass(rootClass) {
  const targets = new Map();
  const parentByCode = new Map(state.cmdbClasses.map((item) => [item.code, item.parent || '']));
  const aggregationCodes = aggregationClassCodes();
  for (const domain of state.cmdbSourceDomains) {
    const targetClass = sourceDomainOtherClass(domain, rootClass);
    if (!targetClass
      || isAggregationClassCode(targetClass, parentByCode, aggregationCodes)
      || sourceDirectAttributes(targetClass).length === 0) {
      continue;
    }

    const key = canonicalToken(targetClass);
    if (!targets.has(key)) {
      targets.set(key, targetClass);
    }
  }

  const sourceOrder = sourceClassOrderMap();
  return [...targets.values()].sort((left, right) => compareSourceClassCodesByHierarchy(left, right, sourceOrder));
}

function sourceDomainOtherClass(domain, rootClass) {
  const sourceClass = domain.sourceClassCode || domain.source || '';
  const targetClass = domain.targetClassCode || domain.destination || '';
  if (canonicalToken(sourceClass) === canonicalToken(rootClass) && targetClass) {
    return targetClass;
  }
  if (canonicalToken(targetClass) === canonicalToken(rootClass) && sourceClass) {
    return sourceClass;
  }

  return '';
}

function sourceFieldRuleForDirectAttribute(rootClass, attribute, fieldKey) {
  const code = attributeCode(attribute) || fieldKey;
  const rule = {
    source: code,
    cmdbAttribute: code,
    cmdbPath: `${rootClass}.${code}`,
    leafAttribute: code,
    leafKind: normalizedRuleFieldKind(attribute),
    leafType: attribute?.type || 'string',
    leafLookupType: lookupTypeCode(attribute),
    required: false,
    type: attribute?.type || 'string',
    resolve: {
      mode: 'none'
    }
  };

  if (isLookupSourceAttribute(attribute)) {
    rule.lookupType = lookupTypeCode(attribute);
    rule.resolve = {
      mode: 'lookup',
      lookupType: rule.lookupType,
      valueMode: 'code'
    };
  }

  return rule;
}

function sourceFieldRuleForCmdbPath(rootClass, path) {
  const first = path[0] ?? {};
  const leaf = path[path.length - 1] ?? {};
  const rule = {
    source: attributeCode(first),
    cmdbAttribute: attributeCode(first),
    cmdbPath: [rootClass, ...path.map(attributeCode)].join('.'),
    leafAttribute: attributeCode(leaf),
    leafKind: normalizedRuleFieldKind(leaf),
    leafType: leaf.type || '',
    leafLookupType: lookupTypeCode(leaf),
    type: leaf.type || '',
    required: false,
    resolve: {
      mode: 'cmdbPath',
      valueMode: isLookupSourceAttribute(leaf) ? 'code' : 'leaf',
      leafKind: normalizedRuleFieldKind(leaf),
      maxDepth: state.maxTraversalDepth
    }
  };

  if (isLookupSourceAttribute(leaf)) {
    rule.lookupType = lookupTypeCode(leaf);
    rule.resolve.leafType = 'lookup';
    rule.resolve.lookupType = rule.lookupType;
  }

  return rule;
}

function sourceFieldRuleForDomainPath(rootClass, targetClass, path) {
  const leaf = path[path.length - 1] ?? {};
  const rule = {
    source: 'id',
    cmdbAttribute: `{domain:${targetClass}}${path[0] ? `.${attributeCode(path[0])}` : ''}`,
    cmdbPath: [rootClass, `{domain:${targetClass}}`, ...path.map(attributeCode)].join('.'),
    leafAttribute: attributeCode(leaf),
    leafKind: normalizedRuleFieldKind(leaf),
    leafType: leaf.type || '',
    leafLookupType: lookupTypeCode(leaf),
    type: leaf.type || '',
    required: false,
    resolve: {
      mode: 'cmdbPath',
      valueMode: isLookupSourceAttribute(leaf) ? 'code' : 'leaf',
      leafKind: normalizedRuleFieldKind(leaf),
      collectionMode: 'join',
      collectionSeparator: '; ',
      maxDepth: state.maxTraversalDepth
    }
  };

  if (isLookupSourceAttribute(leaf)) {
    rule.lookupType = lookupTypeCode(leaf);
    rule.resolve.leafType = 'lookup';
    rule.resolve.lookupType = rule.lookupType;
  }

  return rule;
}

function sourceFieldRuleForUnresolvedPath(rootClass, path, leafKind, reason) {
  const first = path[0] ?? {};
  const leaf = path[path.length - 1] ?? {};
  return {
    source: attributeCode(first),
    cmdbAttribute: attributeCode(first),
    cmdbPath: [rootClass, ...path.map(attributeCode)].join('.'),
    leafAttribute: attributeCode(leaf),
    leafKind,
    leafType: 'reference',
    required: false,
    type: 'reference',
    unresolved: true,
    unresolvedReason: reason,
    resolve: {
      mode: 'unresolvedReference',
      leafKind,
      reason,
      maxDepth: state.maxTraversalDepth
    }
  };
}

function sourceFieldRuleForUnresolvedDomainPath(rootClass, targetClass, path, leafKind, reason) {
  const leaf = path[path.length - 1] ?? {};
  return {
    source: 'id',
    cmdbAttribute: `{domain:${targetClass}}${path[0] ? `.${attributeCode(path[0])}` : ''}`,
    cmdbPath: [rootClass, `{domain:${targetClass}}`, ...path.map(attributeCode)].join('.'),
    leafAttribute: attributeCode(leaf),
    leafKind,
    leafType: 'reference',
    required: false,
    type: 'reference',
    unresolved: true,
    unresolvedReason: reason,
    resolve: {
      mode: 'unresolvedDomainReference',
      leafKind,
      reason,
      maxDepth: state.maxTraversalDepth
    }
  };
}

function fieldKeyForCmdbPath(path) {
  const text = path
    .map(attributeCode)
    .filter(Boolean)
    .map((item, index) => camelPathSegment(item, index === 0))
    .join('');
  return text || 'cmdbPathField';
}

function fieldKeyForDomainPath(targetClass, path) {
  const targetSegment = camelPathSegment(targetClass, false);
  const leafSegment = fieldKeyForCmdbPath(path);
  const normalizedLeaf = leafSegment.charAt(0).toUpperCase() + leafSegment.slice(1);
  return `domain${targetSegment}${normalizedLeaf}`;
}

function camelPathSegment(value, lowerFirst) {
  const text = String(value ?? '')
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
  return lowerFirst ? text.charAt(0).toLowerCase() + text.slice(1) : text;
}

function sourceClassDisplayName(classCode) {
  const schema = state.cmdbClassSchemas.find((item) => canonicalToken(item.code) === canonicalToken(classCode));
  return schema?.description || schema?.name || classCode;
}

function sourceClassOrderMap() {
  return new Map(availableSourceClasses()
    .map((item, index) => [canonicalToken(item.code), index]));
}

function compareSourceClassCodesByHierarchy(left, right, sourceOrder = new Map()) {
  const leftOrder = sourceOrder.get(canonicalToken(left)) ?? Number.MAX_SAFE_INTEGER;
  const rightOrder = sourceOrder.get(canonicalToken(right)) ?? Number.MAX_SAFE_INTEGER;
  return (leftOrder - rightOrder)
    || sourceClassDisplayName(left).localeCompare(sourceClassDisplayName(right), undefined, { sensitivity: 'base' })
    || String(left).localeCompare(String(right), undefined, { sensitivity: 'base' });
}

function attributeCode(attribute) {
  return attribute?.code || attribute?.name || '';
}

function isReadableSourceAttribute(attribute) {
  const code = attributeCode(attribute);
  return Boolean(code)
    && attribute.active !== false
    && !['idclass', 'idtenant'].includes(canonicalToken(code));
}

function isReferenceSourceAttribute(attribute) {
  return String(attribute?.type ?? '').toLowerCase() === 'reference';
}

function isLookupSourceAttribute(attribute) {
  return Boolean(lookupTypeCode(attribute))
    || String(attribute?.type ?? '').toLowerCase().includes('lookup');
}

function lookupTypeCode(attribute) {
  return attribute?.lookupTypeCode || attribute?.lookupType || '';
}

function referenceTargetClass(attribute, sourceClass = '') {
  const directTarget = attribute?.targetClassCode
    || attribute?.targetClass
    || attribute?.target
    || attribute?.referenceTargetClass
    || '';
  if (directTarget) {
    return directTarget;
  }

  const domainCode = attribute?.domainCode || attribute?.domain || '';
  const domain = state.cmdbSourceDomains.find((item) => canonicalToken(item.code || item.name) === canonicalToken(domainCode));
  return domain ? sourceDomainOtherClass(domain, sourceClass) : '';
}

function targetClassAttributes(classCode) {
  const token = canonicalToken(classCode);
  const byCode = new Map(state.classes.map((item) => [item.code, item]));
  const attributes = [];
  const seenClasses = new Set();
  let current = byCode.get(classCode);
  while (current && !seenClasses.has(current.code)) {
    seenClasses.add(current.code);
    attributes.unshift(...(current.attributes ?? []));
    current = byCode.get(current.parentClassCode);
  }

  const byAttributeCode = new Map();
  for (const attribute of attributes) {
    const code = attribute.code || attribute.name;
    if (code) {
      byAttributeCode.set(code, attribute);
    }
  }

  const plannedAttributes = withTargetCardSystemAttributes([...byAttributeCode.values()]);
  if (plannedAttributes.length > 0) {
    return sortedUniqueTargetAttributes(plannedAttributes);
  }

  const instanceClass = state.cmdbClassInstances.find((item) =>
    canonicalToken(item.classCode) === token);
  const instanceAttributes = sortedUniqueTargetAttributes(withTargetCardSystemAttributes(instanceClass?.attributes ?? []));
  if (instanceAttributes.length > 0) {
    return instanceAttributes;
  }

  const cmdbSchemaClass = state.cmdbClassSchemas.find((item) =>
    canonicalToken(item.code || item.name) === token);
  return sortedUniqueTargetAttributes(withTargetCardSystemAttributes(cmdbSchemaClass?.attributes ?? []));
}

function withTargetCardSystemAttributes(attributes) {
  const byToken = new Set((attributes ?? [])
    .map((attribute) => canonicalToken(attributeCode(attribute)))
    .filter(Boolean));
  return [
    ...(attributes ?? []),
    ...TARGET_CARD_SYSTEM_ATTRIBUTES.filter((attribute) => !byToken.has(canonicalToken(attribute.code)))
  ];
}

function sortedUniqueTargetAttributes(attributes) {
  const byAttributeCode = new Map();
  for (const attribute of attributes ?? []) {
    const code = attributeCode(attribute);
    if (code) {
      byAttributeCode.set(canonicalToken(code), attribute);
    }
  }

  return [...byAttributeCode.values()].sort((left, right) =>
    String(left.displayName || left.description || left.code || left.name)
      .localeCompare(String(right.displayName || right.description || right.code || right.name), undefined, {
        sensitivity: 'base'
      }));
}

function syncRulesFromDocument(layerKey, options = {}) {
  const parsed = parseRuleDocument(layerKey);
  if (parsed.ok) {
    state.ruleDocuments[layerKey] = parsed.document;
    state.ruleExamples[layerKey] = parsed.document.rules;
    if (options.showStatus) {
      setRuleEditorStatus(layerKey, `В наборе ${parsed.document.rules.length} правил.`);
    }
    return true;
  }

  if (options.showStatus) {
    setRuleEditorStatus(layerKey, parsed.error, 'error');
  }
  return false;
}

function parseRuleDocument(layerKey) {
  const stored = state.ruleDocuments[layerKey];
  if (!stored) {
    return {
      ok: true,
      document: defaultRuleDocument(layerKey)
    };
  }

  try {
    const parsed = cloneJson(stored);
    const document = Array.isArray(parsed)
      ? { ...defaultRuleDocument(layerKey), rules: parsed }
      : parsed;
    return {
      ok: true,
      document: normalizeRuleDocument(document, layerKey)
    };
  } catch (error) {
    return {
      ok: false,
      error: `Набор правил некорректен: ${error.message}`
    };
  }
}

function writeRuleDocument(layerKey, ruleDocument) {
  const normalized = normalizeRuleDocument(cloneJson(ruleDocument), layerKey);
  state.ruleDocuments[layerKey] = normalized;
  syncRulesFromDocument(layerKey);
  renderRulesPreviews();
  renderConversionConfigSyncView();
}

function normalizeRuleDocument(document, layerKey) {
  const normalized = document && typeof document === 'object' && !Array.isArray(document)
    ? document
    : defaultRuleDocument(layerKey);
  normalized.version = String(normalized.version ?? '1').trim() || '1';
  normalized.layer = normalized.layer || layerKey;
  normalized.source = normalized.source && typeof normalized.source === 'object' && !Array.isArray(normalized.source)
    ? normalized.source
    : {};
  normalized.source.entityClasses = Array.isArray(normalized.source.entityClasses)
    ? normalized.source.entityClasses
    : [];
  normalized.source.fields = normalized.source.fields && typeof normalized.source.fields === 'object' && !Array.isArray(normalized.source.fields)
    ? normalized.source.fields
    : {};
  normalized.runtimePolicy = normalized.runtimePolicy && typeof normalized.runtimePolicy === 'object' && !Array.isArray(normalized.runtimePolicy)
    ? normalized.runtimePolicy
    : defaultRuleRuntimePolicy();
  normalized.rules = Array.isArray(normalized.rules)
    ? normalized.rules.map((rule) => normalizeBindingRuleTarget(rule, layerKey))
        .filter((rule) => !isLegacySeededRule(rule, layerKey))
    : [];
  normalizeRuleDocumentRuleIds(normalized, layerKey);
  normalized.templateDeletionPlans = Array.isArray(normalized.templateDeletionPlans)
    ? normalized.templateDeletionPlans.map((plan) => normalizeTemplateDeletionPlan(plan, layerKey))
    : [];
  normalized.templateApplications = Array.isArray(normalized.templateApplications)
    ? normalized.templateApplications.map((snapshot) => normalizeTemplateApplicationSnapshot(snapshot, layerKey))
        .filter((snapshot) => snapshot.application_id)
    : [];
  return normalized;
}

function normalizeRuleDocumentRuleIds(document, layerKey) {
  const used = new Set();
  const renamed = new Map();
  for (const [index, rule] of (document.rules ?? []).entries()) {
    const previousId = String(rule?.rule_id ?? '').trim();
    const fallbackId = deterministicRuleIdForRule(layerKey, rule, index);
    const desiredId = previousId && !isGenericRuleId(previousId)
      ? previousId
      : fallbackId;
    const nextId = uniqueRuleIdFromUsed(desiredId, used);
    used.add(nextId);
    if (nextId !== previousId) {
      if (!renamed.has(previousId)) {
        renamed.set(previousId, []);
      }
      renamed.get(previousId).push(nextId);
      rule.rule_id = nextId;
    }
  }

  for (const rule of document.rules ?? []) {
    for (const relation of rule.managed_relations ?? []) {
      const targetRuleId = String(relation?.target_rule_id ?? '').trim();
      const replacements = renamed.get(targetRuleId) ?? [];
      if (replacements.length === 1) {
        relation.target_rule_id = replacements[0];
      }
    }
  }
}

function deterministicRuleIdForRule(layerKey, rule, index = 0) {
  return normalizeRuleId([
    layerKey,
    ruleSourceClassCode(rule),
    ruleTargetClassCode(rule),
    rule?.target?.card_id || rule?.target?.idempotency_key || rule?.target?.attribute_mappings?.Code || rule?.name || index
  ].filter(Boolean).join('-'));
}

function uniqueRuleIdFromUsed(value, used) {
  const base = normalizeRuleId(value);
  if (!used.has(base)) {
    return base;
  }

  for (let suffix = 2; suffix < 10000; suffix += 1) {
    const candidate = `${base}-${suffix}`;
    if (!used.has(candidate)) {
      return candidate;
    }
  }

  throw new Error(`Не удалось сформировать уникальный rule_id для ${base}.`);
}

function isLegacySeededRule(rule, layerKey) {
  const ruleId = String(rule?.rule_id ?? '').trim();
  if (!LEGACY_SEEDED_RULE_IDS.has(ruleId)) {
    return false;
  }

  return !rule.layer || rule.layer === layerKey;
}

function normalizeTemplateDeletionPlan(plan, layerKey) {
  const normalized = plan && typeof plan === 'object' && !Array.isArray(plan)
    ? plan
    : {};
  normalized.action_id = normalized.action_id || normalizeRuleId(`${layerKey}-${normalized.template_id || ''}-${normalized.created_at || ''}`);
  normalized.action = normalized.action || 'delete_generated_rules_and_objects';
  normalized.status = normalized.status || 'pending_manual_apply';
  normalized.delete_relations = normalized.delete_relations !== false;
  normalized.layer = normalized.layer || layerKey;
  normalized.template_id = normalized.template_id || '';
  normalized.template_name = normalized.template_name || normalized.template_id;
  normalized.reason = normalized.reason || '';
  normalized.created_at = normalized.created_at || '';
  normalized.targets = Array.isArray(normalized.targets)
    ? normalized.targets.map((target) => ({
        rule_id: String(target?.rule_id ?? ''),
        source_class_code: String(target?.source_class_code ?? ''),
        target_class_code: String(target?.target_class_code ?? target?.class_code ?? ''),
        card_id: String(target?.card_id ?? ''),
        idempotency_key: String(target?.idempotency_key ?? ''),
        card_description: String(target?.card_description ?? '')
      }))
    : [];
  return normalized;
}

function normalizeTemplateApplicationSnapshot(snapshot, layerKey) {
  const normalized = snapshot && typeof snapshot === 'object' && !Array.isArray(snapshot)
    ? snapshot
    : {};
  normalized.application_id = String(normalized.application_id ?? '').trim();
  normalized.layer = normalized.layer || layerKey;
  normalized.applied_at = String(normalized.applied_at ?? '').trim();
  normalized.reconcile = normalized.reconcile && typeof normalized.reconcile === 'object' && !Array.isArray(normalized.reconcile)
    ? normalized.reconcile
    : {};
  normalized.templates = Array.isArray(normalized.templates)
    ? normalized.templates.map((template) => ({
        template_id: String(template?.template_id ?? '').trim(),
        template_name: String(template?.template_name ?? template?.template_id ?? '').trim(),
        template_version: Number(template?.template_version || 0),
        content_hash: String(template?.content_hash ?? '').trim(),
        candidate_count: Number(template?.candidate_count || 0),
        candidates: Array.isArray(template?.candidates)
          ? template.candidates.map((candidate) => String(candidate ?? '').trim()).filter(Boolean)
          : [],
        generated_rules: Array.isArray(template?.generated_rules)
          ? template.generated_rules.map((rule) => ({
              managed_key: String(rule?.managed_key ?? '').trim(),
              rule_id: String(rule?.rule_id ?? '').trim(),
              artifact_fingerprint: String(rule?.artifact_fingerprint ?? '').trim(),
              source_class_code: String(rule?.source_class_code ?? '').trim(),
              target_class_code: String(rule?.target_class_code ?? '').trim()
            })).filter((rule) => rule.managed_key)
          : []
      })).filter((template) => template.template_id)
    : [];
  return normalized;
}

function normalizeBindingRuleTarget(rule, layerKey = '') {
  const normalized = rule && typeof rule === 'object' && !Array.isArray(rule)
    ? rule
    : {};
  normalized.layer = normalized.layer || layerKey;
  normalized.managed_relations = Array.isArray(normalized.managed_relations)
    ? normalized.managed_relations.map((relation) => normalizeRuleManagedRelation(relation, normalized, normalized.layer || layerKey))
        .filter((relation) => relation.managed_key)
    : [];
  normalized.source = normalized.source && typeof normalized.source === 'object' && !Array.isArray(normalized.source)
    ? normalized.source
    : {};
  normalized.when = normalized.when && typeof normalized.when === 'object' && !Array.isArray(normalized.when)
    ? normalized.when
    : {};
  normalized.target = normalized.target && typeof normalized.target === 'object' && !Array.isArray(normalized.target)
    ? normalized.target
    : {};
  normalized.target.attribute_mappings = normalized.target.attribute_mappings
    && typeof normalized.target.attribute_mappings === 'object'
    && !Array.isArray(normalized.target.attribute_mappings)
    ? normalized.target.attribute_mappings
    : {};
  normalized.target.initial_user_values = normalized.target.initial_user_values
    && typeof normalized.target.initial_user_values === 'object'
    && !Array.isArray(normalized.target.initial_user_values)
    ? normalized.target.initial_user_values
    : {};
  normalized.target.card_id = String(normalized.target.card_id ?? normalized.target.cardId ?? '').trim();
  if (normalized.target.card_id) {
    normalized.target.create_instance = false;
    normalized.target.idempotency_key = normalized.target.idempotency_key
      || `cmdbuild:${normalized.target.class_code || ''}:${normalized.target.card_id}`;
    normalized.target.user_responsibility_attributes = [];
    return normalized;
  }

  normalized.target.create_instance = true;

  const populationSourceKeyEntry = Object.entries(normalized.target.attribute_mappings)
    .find(([targetAttribute]) => canonicalToken(targetAttribute) === canonicalToken(POPULATION_SOURCE_KEY_ATTRIBUTE));
  const hasPopulationSourceKeyMapping = Boolean(populationSourceKeyEntry);
  const populationSourceKeyExpression = populationSourceKeyEntry?.[1] ?? '';
  const mappedKeyField = keyFieldFromMappings(normalized.target.attribute_mappings);
  const existingIdempotencyKey = normalized.target.idempotency_key || '';
  const keyField = mappedKeyField
    || normalized.source.key_attribute
    || normalized.when.fieldExists
    || sourceAttributeFromDirectExpression(existingIdempotencyKey)
    || firstSourceAttributeFromTemplate(existingIdempotencyKey);

  if (keyField) {
    normalized.source.key_attribute = keyField;
    normalized.when.fieldExists = keyField;
    if (!existingIdempotencyKey) {
      normalized.target.idempotency_key = `\${source.${keyField}}`;
    }

    if (!hasPopulationSourceKeyMapping) {
      normalized.target.attribute_mappings[POPULATION_SOURCE_KEY_ATTRIBUTE] = normalized.target.idempotency_key
        || populationSourceKeyExpression
        || `\${source.${keyField}}`;
    }
  }

  const effectiveLayerKey = normalized.layer || layerKey;
  const userAttributes = allowedUserResponsibilityAttributes(effectiveLayerKey);
  if (userAttributes.length > 0) {
    const userAttributeTokens = new Set(userAttributes.map(canonicalToken));
    for (const targetAttribute of Object.keys(normalized.target.attribute_mappings)) {
      if (userAttributeTokens.has(canonicalToken(targetAttribute))) {
        delete normalized.target.attribute_mappings[targetAttribute];
      }
    }

    normalized.target.user_responsibility_attributes = userAttributes;
  }

  return normalized;
}

function defaultRuleDocument(layerKey) {
  return {
    version: '1',
    layer: layerKey,
    source: {
      entityClasses: [],
      fields: {}
    },
    runtimePolicy: defaultRuleRuntimePolicy(),
    rules: [],
    templateDeletionPlans: [],
    templateApplications: []
  };
}

function defaultRuleRuntimePolicy() {
  return {
    create: 'Выполнить правила привязки и создать недостающие управляемые карточки/связи для подходящих карточек-источников.',
    update: 'Повторно выполнить те же правила, объединить управляемые атрибуты правила и сохранить пользовательские атрибуты/внешние значения.',
    delete: 'Удалить сгенерированные связи источника и сверить производные структуры Zabbix; удаление карточки агрегатора остается за заказчиком.'
  };
}

function setRuleEditorStatus(layerKey, message, type = '') {
  state.ruleEditorStatus[layerKey] = { message, type };
  renderRuleEditorStatus(layerKey);
}

function renderRuleEditorStatus(layerKey, parsed = null) {
  const config = ruleEditorConfig(layerKey);
  const status = state.ruleEditorStatus[layerKey] ?? { message: '', type: '' };
  const rulesInfo = parsed?.ok
    ? `В наборе ${parsed.document.rules.length} правил.`
    : '';
  config.status.textContent = status.message || rulesInfo;
  config.status.classList.toggle('error', status.type === 'error' || parsed?.ok === false);
}

function ruleSelectOptions(rules, options = {}) {
  const filterTemplateRules = options.filterTemplateRules === true;
  if (rules.length === 0) {
    return [{ value: '', label: 'В наборе нет правил', disabled: true }];
  }

  const visibleRules = rules
    .map((rule, index) => ({ rule, index }))
    .filter(({ rule }) => !filterTemplateRules || !isGeneratedTemplateRule(rule));
  if (visibleRules.length === 0) {
    return [{ value: '', label: 'Все правила скрыты фильтром шаблонов', disabled: true }];
  }

  return [
    { value: '', label: 'Выберите правило' },
    ...visibleRules.map(({ rule, index }) => ({
      value: String(index),
      label: `${index + 1}. ${rule.name || rule.rule_id || ruleSourceClassCode(rule) || 'rule'}`
    }))
  ];
}

function templateTargetClassOptions(layerKey) {
  return targetClassOptions(layerKey, '', { includeInstances: false });
}

function targetClassOptions(layerKey, suggestedCode = '', options = {}) {
  const includeInstances = options.includeInstances !== false;
  const filterTemplateTargets = options.filterTemplateTargets === true;
  const layer = layerKey === 'service' ? 'Service' : 'Suppression';
  const hierarchyClasses = schemaClassesForLayer(layer);
  const classes = sortSchemaClassesByInheritance(
    hierarchyClasses.filter((item) => !item.isSuperclass && item.origin !== 'model_root_superclass'),
    hierarchyClasses);
  const instancesByClass = includeInstances
    ? targetInstanceOptionsByClass(layerKey, { filterTemplateTargets })
    : new Map();
  const instanceCount = [...instancesByClass.values()].reduce((sum, items) => sum + items.length, 0);
  if (classes.length === 0 && instanceCount === 0) {
    return [{ value: '', label: 'Целевые классы не загружены', disabled: true }];
  }

  const selectOptions = [];
  const renderedClassTokens = new Set();
  for (const item of classes) {
    const classToken = canonicalToken(item.code);
    const classLabel = schemaClassOptionLabel(item);
    renderedClassTokens.add(classToken);
    selectOptions.push({
      value: item.code,
      label: `Класс: ${classLabel}`
    });
    selectOptions.push(...(instancesByClass.get(classToken) ?? []).map((card) => ({
      value: targetInstanceOptionValue(item.code, card.id),
      label: `Класс: ${classLabel} -> экземпляр: ${targetCardDisplayLabel({ ...card, classCode: item.code }, item.code)}`
    })));
  }

  for (const [classToken, cards] of instancesByClass.entries()) {
    if (renderedClassTokens.has(classToken)) {
      continue;
    }

    for (const card of cards) {
      const classLabel = targetFallbackClassLabel(card.classCode);
      selectOptions.push({
        value: targetInstanceOptionValue(card.classCode, card.id),
        label: `Класс: ${classLabel} -> экземпляр: ${targetCardDisplayLabel(card, card.classCode)}`
      });
    }
  }

  return [
    {
      value: '',
      label: includeInstances
        ? (suggestedCode
          ? `Выберите целевой класс\\экземпляр класса, например ${suggestedCode}`
          : 'Выберите целевой класс\\экземпляр класса')
        : 'Выберите целевой класс'
    },
    ...selectOptions
  ];
}

function targetInstanceOptionsByClass(layerKey, options = {}) {
  const layer = layerKey === 'service' ? 'Service' : 'Suppression';
  const classOrder = schemaClassOrderMap(layer);
  const result = new Map();
  const hiddenTemplateTargets = options.filterTemplateTargets === true
    ? generatedTemplateTargetRefs(layerKey)
    : null;
  const classItems = state.cmdbClassInstances
    .filter((item) => String(item.layer).toLowerCase() === layer.toLowerCase())
    .sort((left, right) => compareTargetInstanceClasses(left, right, classOrder));
  for (const classItem of classItems) {
    result.set(canonicalToken(classItem.classCode), (classItem.cards ?? [])
      .slice()
      .filter((card) => !isTemplateGeneratedTargetCard(hiddenTemplateTargets, classItem.classCode, card))
      .sort((left, right) =>
        targetCardDisplayLabel(left, classItem.classCode)
          .localeCompare(targetCardDisplayLabel(right, classItem.classCode), undefined, { sensitivity: 'base' }))
      .map((card) => ({ ...card, classCode: classItem.classCode })));
  }

  addRuleTargetInstanceOptions(result, layerKey, { hiddenTemplateTargets });
  return result;
}

function addRuleTargetInstanceOptions(result, layerKey, options = {}) {
  for (const rule of state.ruleExamples[layerKey] ?? []) {
    if (options.hiddenTemplateTargets && isGeneratedTemplateRule(rule)) {
      continue;
    }

    const targetClass = ruleTargetClassCode(rule);
    const cardId = String(rule?.target?.card_id ?? '').trim();
    if (!targetClass || !cardId) {
      continue;
    }

    const key = canonicalToken(targetClass);
    const cards = result.get(key) ?? [];
    if (!cards.some((card) => String(card.id) === cardId)) {
      cards.push(ruleTargetInstanceCard(rule, targetClass, cardId));
    }

    result.set(key, cards.sort((left, right) =>
      targetCardDisplayLabel(left, left.classCode)
        .localeCompare(targetCardDisplayLabel(right, right.classCode), undefined, { sensitivity: 'base' })));
  }
}

function generatedTemplateTargetRefs(layerKey) {
  const refs = {
    cardKeys: new Set(),
    populationRuleIds: new Set(),
    populationKeys: new Set()
  };
  for (const rule of state.ruleExamples[layerKey] ?? []) {
    if (!isGeneratedTemplateRule(rule)) {
      continue;
    }

    const classToken = canonicalToken(ruleTargetClassCode(rule));
    const ruleId = String(rule.rule_id ?? '').trim();
    const cardId = String(rule?.target?.card_id ?? '').trim();
    const idempotencyKey = String(rule?.target?.idempotency_key ?? '').trim();
    const populationSourceKey = String(rule?.target?.attribute_mappings?.[POPULATION_SOURCE_KEY_ATTRIBUTE] ?? '').trim();
    if (ruleId) {
      refs.populationRuleIds.add(canonicalToken(ruleId));
    }
    if (classToken && cardId) {
      refs.cardKeys.add(`${classToken}\u0000${cardId}`);
    }
    if (classToken && idempotencyKey) {
      refs.populationKeys.add(`${classToken}\u0000${idempotencyKey}`);
    }
    if (classToken && populationSourceKey) {
      refs.populationKeys.add(`${classToken}\u0000${populationSourceKey}`);
    }
  }

  return refs;
}

function isTemplateGeneratedTargetCard(refs, classCode, card) {
  if (!refs) {
    return false;
  }

  const classToken = canonicalToken(classCode || card?.classCode);
  const cardId = String(card?.id ?? '').trim();
  if (classToken && cardId && refs.cardKeys.has(`${classToken}\u0000${cardId}`)) {
    return true;
  }

  const populationRuleId = String(cardAttributeValue(card, 'population_rule_id') ?? '').trim();
  if (populationRuleId && refs.populationRuleIds.has(canonicalToken(populationRuleId))) {
    return true;
  }

  const populationKey = String(cardAttributeValue(card, POPULATION_SOURCE_KEY_ATTRIBUTE) ?? '').trim();
  return Boolean(classToken && populationKey && refs.populationKeys.has(`${classToken}\u0000${populationKey}`));
}

function compareTargetInstanceClasses(left, right, classOrder = new Map()) {
  const leftOrder = classOrder.get(canonicalToken(left.classCode)) ?? Number.MAX_SAFE_INTEGER;
  const rightOrder = classOrder.get(canonicalToken(right.classCode)) ?? Number.MAX_SAFE_INTEGER;
  return (leftOrder - rightOrder)
    || String(left.classDescription || left.className || left.classCode)
    .localeCompare(String(right.classDescription || right.className || right.classCode), undefined, { sensitivity: 'base' })
    || String(left.classCode).localeCompare(String(right.classCode), undefined, { sensitivity: 'base' });
}

function schemaClassesForLayer(layer) {
  return state.classes.filter((item) =>
    item.layer === layer || item.origin === 'model_root_superclass');
}

function schemaClassOrderMap(layer) {
  return schemaClassOrderMapFrom(schemaClassesForLayer(layer));
}

function schemaClassOptionLabel(item) {
  const path = item.hierarchyPath || schemaClassDisplayName(item);
  return `${path} (${item.code})`;
}

function targetFallbackClassLabel(classCode) {
  const layerClass = state.classes.find((item) => canonicalToken(item.code) === canonicalToken(classCode));
  if (layerClass) {
    return schemaClassOptionLabel(layerClass);
  }

  const instanceClass = state.cmdbClassInstances.find((item) =>
    canonicalToken(item.classCode) === canonicalToken(classCode));
  return `${instanceClass?.classDescription || instanceClass?.className || classCode} (${classCode})`;
}

function choiceFieldFromTarget(target) {
  if (!(target instanceof Element)) {
    return null;
  }

  const field = target.closest('select, input[list]');
  if (!field || field.disabled || field.readOnly) {
    return null;
  }

  return field;
}

function ensureWideChoiceMenu() {
  let menu = document.querySelector('#wideChoiceMenu');
  if (menu) {
    return menu;
  }

  menu = document.createElement('div');
  menu.id = 'wideChoiceMenu';
  menu.className = 'wide-choice-menu hidden';
  menu.setAttribute('role', 'listbox');
  menu.addEventListener('mousedown', (event) => {
    event.preventDefault();
  });
  menu.addEventListener('mousemove', (event) => {
    const item = event.target.closest('[data-choice-index]');
    if (item) {
      setWideChoiceHighlight(Number(item.dataset.choiceIndex));
    }
  });
  menu.addEventListener('click', (event) => {
    const item = event.target.closest('[data-choice-index]');
    if (item) {
      selectWideChoiceItem(Number(item.dataset.choiceIndex));
    }
  });
  document.body.append(menu);
  return menu;
}

function toggleWideChoiceMenu(field) {
  if (isWideChoiceMenuOpenFor(field)) {
    hideWideChoiceMenu();
    return;
  }

  showWideChoiceMenu(field);
}

function showWideChoiceMenu(field) {
  const model = wideChoiceMenuModel(field);
  if (!model.hasChoices) {
    hideWideChoiceMenu();
    return;
  }

  const selectedIndex = model.items.findIndex((item) => item.selected);
  wideChoiceMenuState.field = field;
  wideChoiceMenuState.items = model.items;
  wideChoiceMenuState.highlightedIndex = selectedIndex >= 0 ? selectedIndex : (model.items.length > 0 ? 0 : -1);

  const menu = ensureWideChoiceMenu();
  const body = model.items.length > 0
    ? model.items.map((item, index) => wideChoiceOptionTemplate(item, index)).join('')
    : '<div class="wide-choice-empty">Нет вариантов по текущему вводу.</div>';
  const tail = model.truncated
    ? `<div class="wide-choice-empty">Показаны первые ${WIDE_CHOICE_MENU_MAX_ITEMS}; уточните ввод для сокращения списка.</div>`
    : '';
  menu.innerHTML = body + tail;
  menu.classList.remove('hidden');
  positionWideChoiceMenu(field);
  setWideChoiceHighlight(wideChoiceMenuState.highlightedIndex);
}

function hideWideChoiceMenu() {
  const menu = document.querySelector('#wideChoiceMenu');
  menu?.classList.add('hidden');
  wideChoiceMenuState.field = null;
  wideChoiceMenuState.items = [];
  wideChoiceMenuState.highlightedIndex = -1;
}

function isWideChoiceMenuOpenFor(field) {
  const menu = document.querySelector('#wideChoiceMenu');
  return Boolean(menu && !menu.classList.contains('hidden') && wideChoiceMenuState.field === field);
}

function wideChoiceMenuModel(field) {
  const allItems = field.matches('select')
    ? selectChoiceItems(field)
    : datalistChoiceItems(field);
  if (field.matches('select')) {
    return {
      hasChoices: allItems.length > 0,
      items: allItems,
      truncated: false
    };
  }

  const searchText = normalizeChoiceSearch(field.value);
  const filteredItems = searchText
    ? allItems.filter((item) => choiceItemMatches(item, searchText))
    : allItems;

  return {
    hasChoices: allItems.length > 0,
    items: filteredItems.slice(0, WIDE_CHOICE_MENU_MAX_ITEMS),
    truncated: filteredItems.length > WIDE_CHOICE_MENU_MAX_ITEMS
  };
}

function selectChoiceItems(field) {
  return [...field.options]
    .filter((option) => !option.disabled)
    .map((option) => {
      const label = cleanChoiceText(option.textContent || option.label || option.value);
      return {
        value: option.value,
        label: label || option.value,
        meta: option.value && option.value !== label ? option.value : '',
        selected: option.selected
      };
    });
}

function datalistChoiceItems(field) {
  const listId = field.getAttribute('list');
  const datalist = listId ? document.getElementById(listId) : null;
  if (!datalist) {
    return [];
  }

  const seen = new Set();
  return [...datalist.options].map((option) => {
    const label = cleanChoiceText(option.getAttribute('label') || option.textContent || option.value);
    return {
      value: option.value,
      label: label || option.value,
      meta: label && label !== option.value ? option.value : '',
      selected: option.value === field.value
    };
  }).filter((item) => {
    const key = `${item.value}\u0000${item.label}`;
    if (!item.value || seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

function choiceItemMatches(item, searchText) {
  return normalizeChoiceSearch(`${item.label} ${item.meta} ${item.value}`).includes(searchText);
}

function normalizeChoiceSearch(value) {
  return String(value ?? '').trim().toLocaleLowerCase();
}

function cleanChoiceText(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function wideChoiceOptionTemplate(item, index) {
  const activeClass = index === wideChoiceMenuState.highlightedIndex ? ' active' : '';
  const meta = item.meta ? `<span>${escapeHtml(item.meta)}</span>` : '';
  return `
    <div class="wide-choice-option${activeClass}" data-choice-index="${index}" role="option" aria-selected="${item.selected ? 'true' : 'false'}">
      <strong>${escapeHtml(item.label || item.value)}</strong>
      ${meta}
    </div>
  `;
}

function setWideChoiceHighlight(index) {
  if (!Number.isInteger(index) || index < 0 || index >= wideChoiceMenuState.items.length) {
    wideChoiceMenuState.highlightedIndex = -1;
    return;
  }

  wideChoiceMenuState.highlightedIndex = index;
  const menu = document.querySelector('#wideChoiceMenu');
  if (!menu) {
    return;
  }

  menu.querySelectorAll('[data-choice-index]').forEach((item) => {
    const active = Number(item.dataset.choiceIndex) === index;
    item.classList.toggle('active', active);
    if (active) {
      item.scrollIntoView({ block: 'nearest' });
    }
  });
}

function selectWideChoiceItem(index) {
  const field = wideChoiceMenuState.field;
  const item = wideChoiceMenuState.items[index];
  if (!field || !item) {
    return;
  }

  wideChoiceMenuState.selecting = true;
  try {
    field.value = item.value;
    field.title = [item.label, item.meta].filter(Boolean).join(' · ');
    if (field.matches('select')) {
      field.dispatchEvent(new Event('change', { bubbles: true }));
    } else {
      field.dispatchEvent(new Event('input', { bubbles: true }));
      field.dispatchEvent(new Event('change', { bubbles: true }));
    }
  } finally {
    wideChoiceMenuState.selecting = false;
    hideWideChoiceMenu();
  }

  if (field.isConnected) {
    field.focus({ preventScroll: true });
  }
}

function handleWideChoiceKeydown(event) {
  const field = choiceFieldFromTarget(event.target);
  if (!field) {
    if (event.key === 'Escape') {
      hideWideChoiceMenu();
    }
    return;
  }

  const menuOpen = isWideChoiceMenuOpenFor(field);
  if (event.key === 'Escape' && menuOpen) {
    event.preventDefault();
    hideWideChoiceMenu();
    return;
  }

  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    if (!menuOpen) {
      showWideChoiceMenu(field);
      return;
    }

    const direction = event.key === 'ArrowDown' ? 1 : -1;
    const itemCount = wideChoiceMenuState.items.length;
    if (itemCount > 0) {
      const nextIndex = (wideChoiceMenuState.highlightedIndex + direction + itemCount) % itemCount;
      setWideChoiceHighlight(nextIndex);
    }
    return;
  }

  if ((event.key === 'Enter' || event.key === ' ') && field.matches('select')) {
    event.preventDefault();
    if (!menuOpen) {
      showWideChoiceMenu(field);
      return;
    }
  }

  if (event.key === 'Enter' && menuOpen && wideChoiceMenuState.highlightedIndex >= 0) {
    event.preventDefault();
    selectWideChoiceItem(wideChoiceMenuState.highlightedIndex);
  }
}

function positionWideChoiceMenu(field = wideChoiceMenuState.field) {
  const menu = document.querySelector('#wideChoiceMenu');
  if (!menu || menu.classList.contains('hidden')) {
    return;
  }

  if (!field || !field.isConnected) {
    hideWideChoiceMenu();
    return;
  }

  const rect = field.getBoundingClientRect();
  const viewportWidth = window.innerWidth || document.documentElement.clientWidth;
  const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
  const margin = 12;
  const availableWidth = Math.max(240, viewportWidth - margin * 2);
  const width = Math.min(
    availableWidth,
    Math.max(rect.width, Math.min(WIDE_CHOICE_MENU_MIN_WIDTH, availableWidth)),
    WIDE_CHOICE_MENU_MAX_WIDTH
  );
  const left = Math.min(Math.max(margin, rect.left), viewportWidth - width - margin);
  const spaceBelow = viewportHeight - rect.bottom - margin;
  const spaceAbove = rect.top - margin;
  const openAbove = spaceBelow < 180 && spaceAbove > spaceBelow;
  const availableHeight = Math.max(80, (openAbove ? spaceAbove : spaceBelow) - 4);
  const maxHeight = Math.min(360, availableHeight);

  menu.style.width = `${Math.round(width)}px`;
  menu.style.maxHeight = `${Math.round(maxHeight)}px`;
  menu.style.left = `${Math.round(left)}px`;
  const menuHeight = Math.min(menu.scrollHeight, maxHeight);
  const top = openAbove
    ? Math.max(margin, rect.top - menuHeight - 4)
    : rect.bottom + 4;
  menu.style.top = `${Math.round(top)}px`;
}

function setSelectOptions(select, options, selectedValue = '') {
  if (!select) {
    return;
  }

  select.innerHTML = options.map((option) => `
    <option value="${escapeHtml(option.value)}" title="${escapeHtml(option.label)}" ${option.disabled ? 'disabled' : ''}>${escapeHtml(option.label)}</option>
  `).join('');

  if (options.some((option) => option.value === selectedValue && !option.disabled)) {
    select.value = selectedValue;
    select.title = options.find((option) => option.value === selectedValue)?.label ?? selectedValue;
    return;
  }

  const firstEnabled = options.find((option) => !option.disabled);
  select.value = firstEnabled?.value ?? '';
  select.title = firstEnabled?.label ?? '';
}

function ruleEditorConfig(layerKey) {
  const prefix = layerKey === 'service' ? 'serviceRule' : 'suppressionRule';
  return {
    action: document.querySelector(`#${prefix}Action`),
    filterTemplateRules: document.querySelector(`#${prefix}FilterTemplateRules`),
    selectField: document.querySelector(`#${prefix}Select`)?.closest('[data-rule-select-field]'),
    select: document.querySelector(`#${prefix}Select`),
    name: document.querySelector(`#${prefix}Name`),
    sourceClass: document.querySelector(`#${prefix}SourceClass`),
    priority: document.querySelector(`#${prefix}Priority`),
    targetClass: document.querySelector(`#${prefix}TargetClass`),
    selectionFilterList: document.querySelector(`#${prefix}SelectionFilterList`),
    attributeList: document.querySelector(`#${prefix}AttributeList`),
    status: document.querySelector(`#${prefix}Status`),
    applyButton: document.querySelector(`#${prefix}ApplyButton`),
    fieldOptions: document.querySelector(`#${prefix}SourceFieldOptions`),
    targetFieldOptions: document.querySelector(`#${prefix}TargetFieldOptions`)
  };
}

function classDescriptionsByCode() {
  const descriptions = new Map();
  for (const item of state.cmdbClasses) {
    descriptions.set(item.code, item.description || item.name || item.code);
  }

  for (const item of state.classes) {
    descriptions.set(item.code, item.displayName || item.code);
  }

  return descriptions;
}

function renderTargetSchemaCards(layerKey, layer, highlight, searchQuery = '') {
  const classes = state.classes.filter((item) =>
    item.layer === layer || item.origin === 'model_root_superclass');
  if (classes.length === 0) {
    return '<div class="empty-state">Нет целевых классов для показа.</div>';
  }

  const targetDomains = state.domains
    .concat(state.suggestedDomains)
    .filter((domain) => domain.layer === layer);
  const domainsByClass = domainsBySource(targetDomains);
  const instancesByClass = targetInstancesByClass(layer, layerKey);
  const visibility = targetVisibilityForPreview(classes, instancesByClass, highlight, searchQuery);
  if (visibility.visibleClassCodes.size === 0) {
    return '<div class="empty-state">Нет целевых классов или экземпляров по текущему выделению или поиску.</div>';
  }

  return renderPreviewClassTree(classes, domainsByClass, {
    layerKey,
    highlight,
    targetInstancesByClass: instancesByClass,
    targetSearchQuery: normalizePreviewSearchQuery(searchQuery),
    visibleClassCodes: visibility.visibleClassCodes,
    directClassCodes: visibility.directClassCodes,
    contextClassCodes: visibility.contextClassCodes
  });
}

function renderPreviewClassTree(classes, domainsByClass, options = {}) {
  const classOrder = schemaClassOrderMapFrom(classes);
  const visibleClassCodes = options.visibleClassCodes ?? new Set(classes.map((item) => canonicalToken(item.code)));
  const visibleClasses = classes.filter((item) => visibleClassCodes.has(canonicalToken(item.code)));
  const childrenByParent = new Map();
  for (const item of visibleClasses) {
    if (!item.parentClassCode) {
      continue;
    }

    const children = childrenByParent.get(item.parentClassCode) ?? [];
    children.push(item);
    childrenByParent.set(item.parentClassCode, children);
  }

  const classCodes = new Set(visibleClasses.map((item) => item.code));
  const roots = visibleClasses
    .filter((item) => !item.parentClassCode || !classCodes.has(item.parentClassCode))
    .sort((left, right) => compareSchemaClassesByHierarchy(left, right, classOrder));

  return roots.map((item) => renderPreviewClassCard(item, childrenByParent, domainsByClass, classOrder, options)).join('');
}

function renderPreviewClassCard(item, childrenByParent, domainsByClass, classOrder = new Map(), options = {}) {
  const children = (childrenByParent.get(item.code) ?? [])
    .sort((left, right) => compareSchemaClassesByHierarchy(left, right, classOrder));
  const domains = domainsByClass.get(item.code) ?? [];
  const contextOnly = options.contextClassCodes?.has(canonicalToken(item.code))
    && !options.directClassCodes?.has(canonicalToken(item.code));
  const instances = contextOnly
    ? []
    : targetInstancesForPreview(
      item,
      options.targetInstancesByClass?.get(canonicalToken(item.code)) ?? [],
      options.highlight,
      options.targetSearchQuery);
  const shouldOpen = Boolean(contextOnly || options.highlight?.active || options.targetSearchQuery);
  return `
    <details
      class="preview-card ${contextOnly ? 'preview-context' : previewNodeClass('target-class', options.highlight, { targetClass: item.code })}"
      data-preview-node="target-class"
      data-layer="${escapeHtml(options.layerKey || '')}"
      data-target-class="${escapeHtml(item.code)}"
      ${shouldOpen ? 'open' : ''}>
      <summary>
        <strong>${escapeHtml(item.displayName || item.code)}</strong>
        <span>${escapeHtml(item.code)}</span>
      </summary>
      ${item.parentClassCode ? `<p class="preview-meta">наследует ${escapeHtml(item.parentClassCode)}</p>` : ''}
      ${contextOnly ? '' : renderPreviewAttributes(item.attributes ?? [])}
      ${contextOnly ? '' : renderTargetInstanceList(options.layerKey, item.code, instances, options.highlight)}
      ${!contextOnly && domains.length > 0 ? `
        <div class="preview-domain-list">
          ${domains.map((domain) => `
            <div class="preview-domain-row">
              <strong>${escapeHtml(domain.displayName || domain.code)}</strong>
              <span>${escapeHtml(domain.sourceClassCode)} -> ${escapeHtml(domain.targetClassCode)}</span>
            </div>
          `).join('')}
        </div>
      ` : ''}
      ${children.length > 0 ? `<div class="preview-child-list">${children.map((child) => renderPreviewClassCard(child, childrenByParent, domainsByClass, classOrder, options)).join('')}</div>` : ''}
    </details>
  `;
}

function targetVisibilityForPreview(classes, instancesByClass, highlight, searchQuery) {
  const query = normalizePreviewSearchQuery(searchQuery);
  const byCode = new Map(classes.map((item) => [item.code, item]));
  const directClassCodes = new Set();
  const visibleClassCodes = new Set();
  const contextClassCodes = new Set();

  for (const item of classes) {
    const token = canonicalToken(item.code);
    const instances = instancesByClass.get(token) ?? [];
    const linkedClass = !highlight.active
      || highlight.targetClasses.has(token)
      || instances.some((card) => highlight.targetInstances.has(previewTargetInstanceKey(item.code, card.id)));
    const classMatches = targetClassMatchesPreviewSearch(item, instances, query);
    if (linkedClass && (!query || classMatches)) {
      directClassCodes.add(token);
      visibleClassCodes.add(token);
    }
  }

  for (const item of classes) {
    if (!directClassCodes.has(canonicalToken(item.code))) {
      continue;
    }

    let parent = schemaParentClassCode(item);
    while (parent && byCode.has(parent)) {
      const parentToken = canonicalToken(parent);
      if (!visibleClassCodes.has(parentToken)) {
        visibleClassCodes.add(parentToken);
        contextClassCodes.add(parentToken);
      }

      parent = schemaParentClassCode(byCode.get(parent));
    }
  }

  return { visibleClassCodes, directClassCodes, contextClassCodes };
}

function targetClassMatchesPreviewSearch(item, instances, query) {
  if (!query) {
    return true;
  }

  return previewTextMatches([
    item.code,
    item.displayName,
    item.description,
    item.name,
    item.parentClassCode,
    item.hierarchyPath
  ], query)
    || (item.attributes ?? []).some((attribute) => previewAttributeMatches(attribute, query))
    || instances.some((card) => targetInstanceMatchesPreviewSearch(card, query));
}

function targetInstancesForPreview(item, instances, highlight, query) {
  const classMatches = targetClassMatchesPreviewSearch(item, [], query);
  let visibleInstances = instances;
  if (highlight.active) {
    visibleInstances = visibleInstances.filter((card) =>
      highlight.targetInstances.has(previewTargetInstanceKey(item.code, card.id)));
  }

  if (query && !classMatches) {
    visibleInstances = visibleInstances.filter((card) => targetInstanceMatchesPreviewSearch(card, query));
  }

  return visibleInstances;
}

function targetInstanceMatchesPreviewSearch(card, query) {
  return previewTextMatches([
    card.id,
    card.description,
    card.classCode,
    cardAttributeValue(card, 'name'),
    ...(card.attributes ?? []).flatMap((attribute) => [
      attribute.code,
      attribute.description,
      attribute.name,
      attribute.value
    ])
  ], query);
}

function targetInstancesByClass(layer, layerKey) {
  const result = new Map();
  for (const classItem of state.cmdbClassInstances.filter((item) =>
    String(item.layer).toLowerCase() === String(layer).toLowerCase())) {
    const key = canonicalToken(classItem.classCode);
    const cards = result.get(key) ?? [];
    cards.push(...(classItem.cards ?? []).map((card) => ({ ...card, classCode: classItem.classCode })));
    result.set(key, cards);
  }

  for (const rule of state.ruleExamples[layerKey] ?? []) {
    const targetClass = ruleTargetClassCode(rule);
    const cardId = String(rule.target?.card_id ?? '').trim();
    if (!targetClass || !cardId) {
      continue;
    }

    const key = canonicalToken(targetClass);
    const cards = result.get(key) ?? [];
    if (!cards.some((card) => String(card.id) === cardId)) {
      cards.push(ruleTargetInstanceCard(rule, targetClass, cardId));
    }

    result.set(key, cards);
  }

  for (const [key, cards] of result.entries()) {
    result.set(key, cards.sort((left, right) =>
      targetCardDisplayLabel(left, left.classCode)
        .localeCompare(targetCardDisplayLabel(right, right.classCode), undefined, { sensitivity: 'base' })));
  }

  return result;
}

function renderTargetInstanceList(layerKey, targetClass, instances, highlight) {
  if (!instances.length) {
    return '';
  }

  return `
    <div class="preview-instance-list">
      <h3>Экземпляры (${instances.length})</h3>
      ${instances.map((card) => {
        const cardId = String(card.id ?? '').trim();
        return `
          <div
            class="preview-instance-row ${previewNodeClass('target-instance', highlight, {
              targetClass,
              targetCardId: cardId
            })}"
            data-preview-node="target-instance"
            data-layer="${escapeHtml(layerKey || '')}"
            data-target-class="${escapeHtml(targetClass)}"
            data-target-card-id="${escapeHtml(cardId)}">
            <strong>${escapeHtml(targetCardDisplayLabel(card, targetClass))}</strong>
            <span>карточка #${escapeHtml(cardId)}</span>
          </div>
        `;
      }).join('')}
    </div>
  `;
}

function sortSchemaClassesByInheritance(classes, hierarchyClasses = classes) {
  const selectableCodes = new Set(classes.map((item) => item.code).filter(Boolean));
  const byCode = new Map();
  for (const item of [...hierarchyClasses, ...classes]) {
    if (item?.code && !byCode.has(item.code)) {
      byCode.set(item.code, item);
    }
  }

  const childrenByParent = new Map();
  const roots = [];
  for (const item of byCode.values()) {
    const parent = schemaParentClassCode(item);
    if (parent && byCode.has(parent)) {
      const children = childrenByParent.get(parent) ?? [];
      children.push(item);
      childrenByParent.set(parent, children);
    } else {
      roots.push(item);
    }
  }

  const result = [];
  const visit = (item, parentLabels) => {
    const label = schemaClassDisplayName(item);
    const path = parentLabels.concat(label);
    if (selectableCodes.has(item.code)) {
      result.push({
        ...item,
        hierarchyLabel: `${path.join(' / ')} (${item.code})`,
        hierarchyPath: path.join(' / '),
        hierarchyDepth: path.length - 1
      });
    }

    (childrenByParent.get(item.code) ?? [])
      .sort(compareSchemaClasses)
      .forEach((child) => visit(child, path));
  };

  roots
    .sort(compareSchemaClasses)
    .forEach((item) => visit(item, []));

  return result;
}

function schemaClassOrderMapFrom(hierarchyClasses) {
  return new Map(sortSchemaClassesByInheritance(hierarchyClasses, hierarchyClasses)
    .map((item, index) => [canonicalToken(item.code), index]));
}

function schemaParentClassCode(item) {
  return String(item?.parentClassCode || item?.parent || '').trim();
}

function schemaClassDisplayName(item) {
  return String(item?.displayName || item?.description || item?.name || item?.code || '').trim();
}

function compareSchemaClasses(left, right) {
  return schemaClassDisplayName(left).localeCompare(schemaClassDisplayName(right), undefined, {
    sensitivity: 'base'
  }) || String(left.code).localeCompare(String(right.code), undefined, { sensitivity: 'base' });
}

function compareSchemaClassesByHierarchy(left, right, classOrder = new Map()) {
  const leftOrder = classOrder.get(canonicalToken(left.code)) ?? Number.MAX_SAFE_INTEGER;
  const rightOrder = classOrder.get(canonicalToken(right.code)) ?? Number.MAX_SAFE_INTEGER;
  return (leftOrder - rightOrder) || compareSchemaClasses(left, right);
}

function rememberOpenRows() {
  document.querySelectorAll('details[data-class-code]').forEach((details) => {
    updateOpenSet(state.openClassRows, details.dataset.classCode, details.open);
  });
  document.querySelectorAll('details[data-domain-code]').forEach((details) => {
    updateOpenSet(state.openDomainRows, details.dataset.domainCode, details.open);
  });
}

function updateOpenSet(set, key, isOpen) {
  if (!key) {
    return;
  }

  if (isOpen) {
    set.add(key);
    return;
  }

  set.delete(key);
}

function addCustomEntity() {
  const layer = state.activeLayer;
  const codeInput = document.querySelector('#entityCodeInput');
  const displayInput = document.querySelector('#entityDisplayInput');
  const purposeInput = document.querySelector('#entityPurposeInput');
  const code = normalizeEntityCode(codeInput.value);

  if (!code) {
    state.error = 'Код сущности обязателен.';
    render();
    return false;
  }

  const duplicate = state.customEntities.some((entity) =>
    entity.layer === layer && normalizeEntityCode(entity.code) === code);
  if (duplicate) {
    state.error = `Сущность ${code} уже существует в слое ${layer === 'Service' ? 'сервиса' : 'подавления'}.`;
    render();
    return false;
  }

  state.customEntities.push({
    code,
    layer,
    displayName: displayInput.value.trim(),
    purpose: purposeInput.value.trim(),
    suggestDomains: true
  });
  state.error = '';
  codeInput.value = '';
  displayInput.value = '';
  purposeInput.value = '';
  return true;
}

function renderCustomEntityList() {
  const container = document.querySelector('#customEntityList');
  const entities = state.customEntities
    .map((entity, index) => ({ entity, index }))
    .filter((item) => item.entity.layer === state.activeLayer);

  if (entities.length === 0) {
    container.innerHTML = '';
    return;
  }

  container.innerHTML = entities.map(({ entity, index }) => `
    <div class="entity-chip">
      <span class="badge ${layerClass(entity.layer)}">${entity.layer}</span>
      <strong>${escapeHtml(entity.code)}</strong>
      <span>${escapeHtml(entity.displayName || entity.purpose || '')}</span>
      <button type="button" class="icon-button" data-remove-entity="${index}" title="Удалить сущность">×</button>
    </div>
  `).join('');
}

function byActiveLayer(items) {
  return items.filter((item) => item.layer === state.activeLayer || item.origin === 'model_root_superclass');
}

function normalizeLanguage(language) {
  return String(language).toLowerCase() === 'en' ? 'En' : 'Ru';
}

function normalizeEntityCode(value) {
  return String(value ?? '').trim().replaceAll(/[^A-Za-z0-9]/g, '');
}

function layerClass(layer) {
  return String(layer ?? '').toLowerCase();
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function canonicalToken(value) {
  return String(value ?? '').trim().replaceAll(/[^A-Za-z0-9]/g, '').toLowerCase();
}

function regexLiteralValue(pattern) {
  return String(pattern ?? '')
    .trim()
    .replace(/^\(\?i\)/i, '')
    .replace(/^\^/, '')
    .replace(/\$$/, '')
    .replace(/\\(.)/g, '$1');
}

function escapeRegex(value) {
  return String(value ?? '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function normalizeRuleId(value) {
  return String(value ?? 'rule')
    .trim()
    .toLowerCase()
    .replaceAll(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'rule';
}

function clampNumber(value, fallback, min, max) {
  const number = Number(value);
  if (!Number.isFinite(number)) {
    return fallback;
  }

  return Math.min(max, Math.max(min, Math.trunc(number)));
}

function cloneJson(value) {
  return JSON.parse(JSON.stringify(value));
}

function openDataCacheDb() {
  if (typeof indexedDB === 'undefined') {
    return Promise.reject(new Error('IndexedDB недоступен в текущем браузере.'));
  }

  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATA_CACHE_DB, DATA_CACHE_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(DATA_CACHE_STORE)) {
        db.createObjectStore(DATA_CACHE_STORE, { keyPath: 'key' });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Не удалось открыть локальный кэш.'));
  });
}

async function readDataCache(key) {
  const db = await openDataCacheDb();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction(DATA_CACHE_STORE, 'readonly');
    const request = transaction.objectStore(DATA_CACHE_STORE).get(key);
    request.onsuccess = () => resolve(request.result ?? null);
    request.onerror = () => reject(request.error ?? new Error('Не удалось прочитать локальный кэш.'));
    transaction.oncomplete = () => db.close();
    transaction.onerror = () => {
      db.close();
      reject(transaction.error ?? new Error('Не удалось прочитать локальный кэш.'));
    };
  });
}

async function writeDataCache(key, payload) {
  const db = await openDataCacheDb();
  const record = {
    key,
    updatedAt: new Date().toISOString(),
    payload
  };

  return new Promise((resolve, reject) => {
    const transaction = db.transaction(DATA_CACHE_STORE, 'readwrite');
    transaction.objectStore(DATA_CACHE_STORE).put(record);
    transaction.oncomplete = () => {
      db.close();
      resolve(record);
    };
    transaction.onerror = () => {
      db.close();
      reject(transaction.error ?? new Error('Не удалось записать локальный кэш.'));
    };
  });
}
