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
  'description',
  'is_critical',
  'aggregation_type',
  'threshold',
  'n'
];
const SUPPRESSION_USER_RESPONSIBILITY_ATTRIBUTES = [
  'description',
  'is_critical'
];
const TEMPLATE_DELETE_MODES = {
  detachRulesKeepObjects: 'detach_rules_keep_objects',
  deleteRulesAndObjects: 'delete_rules_and_objects'
};
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
    suppression: null
  },
  ruleEditorSuggestions: {
    service: null,
    suppression: null
  },
  ruleEditorSelectionFilters: {
    service: [],
    suppression: []
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
  deployingConversionRules: false,
  webhooksCheck: null,
  webhooksConfig: {},
  webhooksCheckError: '',
  webhooksDeployMessage: '',
  webhooksDeployError: '',
  webhookRuleCoverage: null,
  webhookRuleCoverageError: '',
  webhooksCacheUpdatedAt: '',
  zabbixHostIdAttribute: 'zabbix_hostid',
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
    const group = button.closest('.nav-group');
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

document.querySelector('#deployConversionRulesButton').addEventListener('click', async () => {
  await deployConversionRulesToServer();
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
  panel.addEventListener('change', (event) => {
    handleTemplateEditorChange(panel.dataset.templateEditorLayer, event.target);
  });
  panel.addEventListener('input', (event) => {
    if (event.target.matches('[data-template-source-regex]')) {
      renderTemplateSourceFieldOptions(panel.dataset.templateEditorLayer);
      return;
    }

    if (event.target.matches('[data-selection-filter-field], [data-selection-filter-regex]')) {
      ensureSelectionFilterDraftRow(panel.dataset.templateEditorLayer, 'template');
      state.templateEditorSelectionFilters[panel.dataset.templateEditorLayer] = selectionFilterRowsFromDom(
        templateEditorConfig(panel.dataset.templateEditorLayer).selectionFilterList);
      return;
    }

    if (event.target.matches('[data-template-variable-name], [data-template-variable-value]')) {
      ensureTemplateVariableDraftRow(panel.dataset.templateEditorLayer);
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
  applyTemplatesToRuleDocuments();
});

document.addEventListener('click', (event) => {
  const button = event.target.closest('[data-detach-template-rule]');
  if (!button) {
    return;
  }

  detachGeneratedRuleFromTemplate(button.dataset.layer, button.dataset.ruleId);
});

await loadInitialConfig();
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

  document.querySelector(`#${view}View`).classList.remove('hidden');
  if (view === 'serviceTemplates') {
    renderTemplateEditor('service');
  } else if (view === 'suppressionTemplates') {
    renderTemplateEditor('suppression');
  } else if (view === 'templateApply') {
    renderTemplateApplyView();
  } else if (view === 'templateAudit') {
    renderTemplateAuditView();
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
      throw new Error(`config request failed: ${response.status}`);
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
      throw new Error(text || `schema preview failed: ${response.status}`);
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
    customEntities: state.customEntities
  };
}

async function applySelectedSchema() {
  rememberOpenRows();
  syncModelRootsFromInputs();
  const selection = selectedApplyObjects();
  if (selection.classes.length === 0 && selection.domains.length === 0) {
    state.applyMessage = '';
    state.applyError = 'Select at least one class or domain.';
    render();
    return;
  }

  state.applying = true;
  state.applyMessage = 'Sending selected schema objects to CMDBuild...';
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
      const detail = payload.detail || payload.error || `schema apply failed: ${response.status}`;
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
      throw new Error(payload.detail || payload.error || `healthcheck failed: ${response.status}`);
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
      throw new Error(payload.detail || payload.error || `configuration reload failed: ${response.status}`);
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
      throw new Error(payload.detail || payload.error || `kafka topics request failed: ${response.status}`);
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
    state.kafkaEventsError = state.kafkaTopics.length === 0 ? 'Нет доступных managed Kafka topics.' : '';
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
      throw new Error(payload.detail || payload.error || `kafka events request failed: ${response.status}`);
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
    state.syncMessage = `CMDBuild синхронизирован: ${state.cmdbClasses.length} classes, ${cmdbAttributeCount()} attributes, ${state.cmdbSourceDomains.length} domains, ${cmdbInstanceCount()} instances.`;
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
      throw new Error('Локальный кэш CMDBuild для текущего prefix не найден.');
    }

    applyCmdbSourceCache(cacheRecord);
    if (!silent) {
      state.syncMessage = `Локальный кэш CMDBuild загружен: ${state.cmdbClasses.length} classes, ${cmdbAttributeCount()} attributes, ${state.cmdbSourceDomains.length} domains, ${cmdbInstanceCount()} instances.`;
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
      throw new Error(payload.detail || payload.error || `zabbix check failed: ${response.status}`);
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
  state.webhooksDeployError = '';
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
      throw new Error(payload.detail || payload.error || `webhooks check failed: ${response.status}`);
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

async function deployConversionRulesToServer() {
  state.deployingConversionRules = true;
  state.webhooksDeployMessage = 'Отправка текущих правил конвертации на сервер...';
  state.webhooksDeployError = '';
  renderWebhooksSyncView();

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
    const result = await response.json().catch(() => ({}));
    if (!response.ok || result.success === false) {
      throw new Error(result.error ?? `conversion rules deploy failed: ${response.status}`);
    }

    state.conversionConfigStorage = result.storage ?? state.conversionConfigStorage;
    state.conversionConfigStorageUpdatedAt = result.savedAt ?? '';
    await writeConversionConfigCacheSnapshot(payload);
    const runtime = result.runtimeRules ?? {};
    const rulesStatus = result.rulesStatus ?? null;
    const serverText = rulesStatus
      ? ` Микросервис видит v${rulesStatus.version ?? '-'} (${rulesStatus.ruleCount ?? 0} rules).`
      : (result.rulesStatusError ? ` Статус микросервиса не прочитан: ${result.rulesStatusError}.` : '');
    state.webhooksDeployMessage = `Отправлено: v${runtime.version ?? '-'}, ${runtime.ruleCount ?? 0} rules -> ${runtime.configuredFile ?? runtime.filePath ?? '-'}.${serverText}`;
    state.webhooksDeployError = '';
    await checkHealthServices();
  } catch (error) {
    state.webhooksDeployMessage = '';
    state.webhooksDeployError = error.message;
  } finally {
    state.deployingConversionRules = false;
    render();
  }
}

async function checkWebhooksAgainstConversionRules() {
  state.checkingWebhookRuleCoverage = true;
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
      throw new Error(payload.detail || payload.error || `webhooks check failed: ${response.status}`);
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
    render();
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
  state.syncConversionConfigMessage = 'Сохранение конфигураций конвертации в папку...';
  state.syncConversionConfigError = '';
  renderConversionConfigSyncView();

  try {
    const payload = currentConversionConfigPayload();
    const response = await fetch('/api/conversion-config/storage', {
      method: 'PUT',
      headers: {
        'content-type': 'application/json',
        accept: 'application/json'
      },
      body: JSON.stringify(payload)
    });
    const result = await response.json();
    if (!response.ok || result.success === false) {
      throw new Error(result.error ?? `conversion config storage write failed: ${response.status}`);
    }

    renderRulesPreviews();
    renderRuleEditors();
    state.conversionConfigStorage = result.storage ?? state.conversionConfigStorage;
    state.conversionConfigStorageUpdatedAt = result.savedAt ?? '';
    const cacheMessage = await writeConversionConfigCacheSnapshot(payload);
    state.syncConversionConfigMessage = `${conversionConfigStatsMessage('Сохранено')} Папка: ${conversionConfigFolderLabel()}.${cacheMessage}`;
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
  state.syncConversionConfigMessage = 'Загрузка конфигураций конвертации из папки...';
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
      throw new Error(payload.error ?? `conversion config storage read failed: ${response.status}`);
    }
    if (!payload.exists) {
      throw new Error(`В папке ${payload.storage?.resolvedStorageFolder ?? payload.storage?.storageFolder ?? ''} нет сохраненных конфигураций конвертации.`);
    }

    applyConversionConfigPayload(payload);
    const cacheMessage = await writeConversionConfigCacheSnapshot(currentConversionConfigPayload());
    state.conversionConfigStorageUpdatedAt = payload.savedAt ?? '';
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

  return {
    prefix: state.prefix,
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
  syncRulesFromDocument('service');
  syncRulesFromDocument('suppression');
  renderRulesPreviews();
  renderRuleEditors();
}

function conversionConfigStatsMessage(prefix) {
  const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates.length;
  const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates.length;
  return `${prefix}: ${state.ruleExamples.service.length} service rules, ${state.ruleExamples.suppression.length} suppression rules, ${serviceTemplates + suppressionTemplates} templates.`;
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

function applyGeneralSettingsPayload(payload) {
  state.maxTraversalDepth = clampNumber(Number(payload?.maxTraversalDepth), 2, 2, 5);
  document.querySelector('#maxTraversalDepthSelect').value = String(state.maxTraversalDepth);
  renderRuleEditors();
  renderConversionConfigSyncView();
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

function loadGeneralSettings() {
  try {
    const raw = localStorage.getItem(GENERAL_SETTINGS_STORAGE_KEY);
    if (!raw) {
      throw new Error('сохраненные настройки не найдены');
    }

    const payload = JSON.parse(raw);
    applyGeneralSettingsPayload(payload.settings ?? {});
    const savedAt = payload.savedAt ? ` (${formatCacheTimestamp(payload.savedAt)})` : '';
    state.generalSettingsMessage = `Настройки загружены${savedAt}.`;
    state.generalSettingsError = '';
  } catch (error) {
    state.generalSettingsMessage = '';
    state.generalSettingsError = `Настройки не загружены: ${error.message}`;
  }

  renderGeneralSettingsView();
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
      throw new Error('Локальный кэш конфигураций конвертации для текущего prefix не найден.');
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
  document.querySelector('#schemaTitle').textContent = `${state.activeLayer} schema`;
  document.querySelector('#schemaLead').textContent = state.activeLayer === 'Service'
    ? 'Classes and domains for the Zabbix service tree.'
    : 'Classes and domains for Zabbix trigger dependency suppression.';
  document.querySelector('#entityCodeInput').placeholder = state.activeLayer === 'Service'
    ? 'ApplicationCluster'
    : 'FirewallGroup';
  document.querySelector('#entityDisplayInput').placeholder = state.activeLayer === 'Service'
    ? 'Application cluster'
    : 'Firewall group';
  document.querySelector('#addEntityButton').textContent = `Add ${state.activeLayer.toLowerCase()} entity`;
  document.querySelector('#serviceModelRootInput').placeholder = defaultModelRoot(state.language);
  document.querySelector('#suppressionModelRootInput').placeholder = defaultModelRoot(state.language);
  renderGeneralSettingsView();
  const selected = selectedApplyObjects();
  const sendButton = document.querySelector('#sendSelectedButton');
  sendButton.disabled = state.loading || state.applying;
  sendButton.textContent = state.applying
    ? 'Sending...'
    : `Send selected to CMDBuild (${selected.classes.length + selected.domains.length})`;
  readySchemaPanelTitle.textContent = `Ready classes/domains (${readyClasses.length}/${readyDomains.length})`;
  plannedSchemaPanelTitle.textContent = `Planned classes/domains (${plannedClasses.length}/${plannedDomains.length})`;

  const status = document.querySelector('#schemaStatus');
  const rootError = state.rootClassErrors[state.activeLayer] ?? '';
  const catalogError = state.cmdbDomainError;
  status.textContent = state.loading
    ? 'Loading schema preview...'
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
}

function renderGeneralSettingsView() {
  const maxDepth = document.querySelector('#maxTraversalDepthSelect');
  const zabbixAttribute = document.querySelector('#zabbixHostIdAttributeInput');
  const conversionFolder = document.querySelector('#conversionConfigFolderInput');
  const status = document.querySelector('#generalSettingsStatus');
  if (!maxDepth || !zabbixAttribute || !conversionFolder || !status) {
    return;
  }

  maxDepth.value = String(state.maxTraversalDepth);
  zabbixAttribute.value = state.zabbixHostIdAttribute;
  conversionFolder.value = conversionConfigFolderLabel();
  status.textContent = state.generalSettingsError || state.generalSettingsMessage;
  status.classList.toggle('error', Boolean(state.generalSettingsError));
}

function applyResultMessage(payload) {
  return `CMDBuild apply finished: ${payload.created ?? 0} created, ${payload.updated ?? 0} updated, ${payload.skipped ?? 0} skipped, ${payload.failed ?? 0} failed.`;
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
    schemaStatusLabel: ready ? 'Ready domain' : 'Planned domain'
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
    <label class="apply-checkbox" title="Send to CMDBuild">
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

  return {
    classes: classes
      .filter((item) => isApplySelected('class', item.code, item.schemaStatus !== 'ready_to_work'))
      .map((item) => item.code),
    domains: domains
      .filter((item) => isApplySelected('domain', item.code, item.schemaStatus !== 'ready_to_work'))
      .map((item) => item.code),
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
      throw new Error(text || `CMDBuild root classes request failed: ${response.status}`);
    }

    const catalog = await response.json();
    const classes = catalog.classes ?? [];
    const error = catalog.rootFound === false
      ? `CMDBuild root ${rootPath} was not found.`
      : '';
    return { layer, classes, error };
  } catch (error) {
    return { layer, classes: [], error: `CMDBuild root ${rootPath} is unavailable: ${error.message}` };
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

function normalizeRootPath(rootPath) {
  const normalized = String(rootPath ?? '').trim();
  if (!normalized) {
    return '';
  }

  return normalized.startsWith('/') ? normalized : `/${normalized}`;
}

function renderClassTree(container, classes, domainsByClass) {
  if (classes.length === 0 && !state.loading) {
    container.innerHTML = '<div class="empty-state">No items to show.</div>';
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
          <span class="structure-mark">${item.isSuperclass ? 'superclass' : 'class'}</span>
          ${item.schemaStatusLabel ? `<span class="schema-status-mark ${escapeHtml(item.schemaStatus)}">${escapeHtml(item.schemaStatusLabel)}</span>` : ''}
          ${item.origin === 'existing_managed_descendant' ? '<span class="source-link-mark">managed descendant</span>' : ''}
          ${item.managedByBuilder ? '<span class="source-link-mark">managed_by_builder</span>' : ''}
          ${item.autoPopulationEnabled ? '<span class="source-link-mark">auto_population_enabled</span>' : ''}
        </span>
        <span class="row-title">${escapeHtml(item.code)}</span>
        <span class="row-meta">${escapeHtml(item.displayName ?? '')}</span>
        <span class="row-count">${classAttributeLabel(item, classDomains.length)}</span>
      </summary>
      <p class="help-text">${escapeHtml(item.help ?? '')}</p>
      ${item.parentClassCode ? `<p class="help-text">inherits ${escapeHtml(item.parentClassCode)}</p>` : ''}
      ${item.modelRoot ? `<p class="help-text">model root ${escapeHtml(item.modelRoot)}</p>` : ''}
      ${renderClassDomains(classDomains)}
      ${renderAttributeTable(item.attributes ?? [], item.isSuperclass ? 'Superclass attributes' : 'Local class attributes')}
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
      <h3>Domains (${domains.length})</h3>
      <div class="nested-domain-list">
        ${domains.map((domain) => renderDomainRow(domain, domain.suggested)).join('')}
      </div>
    </div>
  `;
}

function renderAttributeTable(attributes, label) {
  if (attributes.length === 0) {
    return '<div class="empty-state">No attributes planned.</div>';
  }

  return `
    <div class="attribute-grid" role="table" aria-label="${escapeHtml(label)}">
      <div class="attribute-head" role="row">
        <span role="columnheader">Code</span>
        <span role="columnheader">Display</span>
        <span role="columnheader">Type</span>
        <span role="columnheader">Req</span>
        <span role="columnheader">Help</span>
      </div>
      ${attributes.map((attribute) => `
        <div class="attribute-row" role="row">
          <span role="cell">${escapeHtml(attribute.code)}</span>
          <span role="cell">${escapeHtml(attribute.displayName)}</span>
          <span role="cell">${escapeHtml(formatAttributeType(attribute))}</span>
          <span role="cell">${attribute.required ? 'yes' : 'no'}</span>
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
    ? `${type} · JS validation`
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
          ${suggested ? '<span class="suggestion-mark">suggested</span>' : ''}
          ${item.isSourceLink ? '<span class="source-link-mark">source link</span>' : ''}
          ${item.schemaStatusLabel ? `<span class="schema-status-mark ${escapeHtml(item.schemaStatus)}">${escapeHtml(item.schemaStatusLabel)}</span>` : ''}
        </span>
        <span class="row-title">${escapeHtml(item.code)}</span>
        <span class="row-meta row-route">${escapeHtml(item.sourceClassCode)} -> ${escapeHtml(item.targetClassCode)}</span>
        <span class="row-meta row-relation">${escapeHtml(item.relationType)} · delete relation on card delete: ${item.deleteRelationOnCardDelete}</span>
        <span class="row-count">${item.attributes?.length ?? 0} attr</span>
      </summary>
      <p class="help-text">${escapeHtml(item.help ?? '')}</p>
      ${suggested ? `<p class="help-text">${escapeHtml(item.reason)}</p>` : ''}
      ${renderAttributeTable(item.attributes ?? [], 'Domain attributes')}
    </details>
  `;
}

function classAttributeLabel(item, domainCount = 0) {
  const count = item.attributes?.length ?? 0;
  const attributeLabel = item.isSuperclass ? `${count} common` : `${count} local`;
  return `${attributeLabel} · ${domainCount} dom`;
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
      throw new Error(text || `CMDBuild classes request failed: ${response.status}`);
    }

    const catalog = await response.json();
    state.cmdbClasses = catalog.classes ?? [];
    state.cmdbClassError = '';
  } catch (error) {
    state.cmdbClasses = [];
    state.cmdbClassError = `CMDB class catalog is unavailable: ${error.message}`;
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
      throw new Error(text || `CMDBuild class schema request failed: ${response.status}`);
    }

    const catalog = await response.json();
    state.cmdbClassSchemas = catalog.classes ?? [];
    state.cmdbClassSchemaError = '';
  } catch (error) {
    state.cmdbClassSchemas = [];
    state.cmdbClassSchemaError = `CMDB class schema is unavailable: ${error.message}`;
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
    state.cmdbDomainError = `CMDB domain catalog is unavailable: ${error.message}`;
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
      throw new Error(text || `CMDBuild class instances request failed: ${response.status}`);
    }

    const catalog = await response.json();
    state.cmdbClassInstances = catalog.classes ?? [];
    state.cmdbClassInstanceError = '';
  } catch (error) {
    state.cmdbClassInstances = [];
    state.cmdbClassInstanceError = `CMDB class instances are unavailable: ${error.message}`;
  }
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
    throw new Error(text || `CMDBuild domains request failed: ${response.status}`);
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
  button.textContent = state.checkingHealth ? 'Проверка...' : 'Обновить healthcheck';
  status.textContent = state.applierReloadError
    || state.healthCheckError
    || state.applierReloadMessage
    || (state.checkingHealth
      ? 'Проверка healthcheck микросервисов...'
      : (services.length > 0 ? `Healthcheck: ${okCount} ok, ${failedCount} error.` : 'Healthcheck еще не выполнен.'));
  status.classList.toggle('error', Boolean(state.applierReloadError || state.healthCheckError || failedCount > 0));

  list.innerHTML = services.length > 0
    ? services.map(renderHealthServiceCard).join('')
    : '<div class="empty-state">Нет данных healthcheck.</div>';
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
    payloadService ? `service ${payloadService}` : ''
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
        <span class="health-status">${ok ? 'ok' : 'error'}</span>
        ${reloadButton}
      </div>
      ${versionDetailsHtml}
      <p class="health-meta">${escapeHtml(details || '-')}</p>
      ${ok ? '' : `<p class="health-error">${escapeHtml(service.error || 'healthcheck failed')}</p>`}
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
    `service v${info.serviceVersion} (${info.serviceRuleCount})`,
    `suppression v${info.suppressionVersion} (${info.suppressionRuleCount})`
  ].join(' · ');
}

function formatServiceConversionRulesVersion(status) {
  if (status.error) {
    return status.error;
  }

  const version = status.version ? `v${status.version}` : 'version -';
  const counts = [
    `${Number.isFinite(status.ruleCount) ? status.ruleCount : 0} rules`,
    `${Number.isFinite(status.serviceRuleCount) ? status.serviceRuleCount : 0} service`,
    `${Number.isFinite(status.suppressionRuleCount) ? status.suppressionRuleCount : 0} suppression`
  ].join(', ');
  const updatedAt = status.loadedAtUtc || status.fileLastModifiedAtUtc
    ? ` · ${formatCacheTimestamp(status.loadedAtUtc || status.fileLastModifiedAtUtc)}`
    : '';
  return `${version} (${counts})${updatedAt}`;
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

  const serviceVersion = String(serviceRules.version ?? '').trim();
  const uiVersions = [uiRules.serviceVersion, uiRules.suppressionVersion].filter(Boolean);
  const versionMatches = Boolean(serviceVersion) && uiVersions.every((version) => version === serviceVersion);
  const countMatches = Number(serviceRules.ruleCount) === uiRules.totalRuleCount;
  if (versionMatches && countMatches) {
    return {
      matches: true,
      text: 'совпадает'
    };
  }

  const parts = [];
  if (!versionMatches) {
    parts.push(`UI ${uiVersions.map((version) => `v${version}`).join('/')} vs service v${serviceVersion || '-'}`);
  }
  if (!countMatches) {
    parts.push(`UI ${uiRules.totalRuleCount} rules vs service ${Number(serviceRules.ruleCount) || 0}`);
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
    : '<option value="">Нет managed topics</option>';
  select.disabled = state.loadingKafkaTopics || state.kafkaTopics.length === 0;

  const currentTopic = state.kafkaTopics.find((topic) => topic.name === state.kafkaSelectedTopic);
  const summary = currentTopic
    ? `${state.kafkaEvents.length} events loaded from ${currentTopic.name}.`
    : 'Выберите Kafka topic.';
  status.textContent = state.kafkaTopicsError
    || state.kafkaEventsError
    || (state.loadingKafkaTopics
      ? 'Загрузка списка managed Kafka topics...'
      : (state.loadingKafkaEvents ? 'Загрузка последних событий...' : summary));
  status.classList.toggle('error', Boolean(state.kafkaTopicsError || state.kafkaEventsError));

  topicList.innerHTML = state.kafkaTopics.length > 0
    ? state.kafkaTopics.map(renderKafkaTopicCard).join('')
    : '<div class="empty-state">Managed Kafka topics не загружены.</div>';
  eventList.innerHTML = state.kafkaEvents.length > 0
    ? state.kafkaEvents.map(renderKafkaEventCard).join('')
    : '<div class="empty-state">События не загружены.</div>';
}

function renderKafkaTopicCard(topic) {
  const selected = topic.name === state.kafkaSelectedTopic;
  const existsClass = topic.exists === true ? 'ok' : (topic.exists === false ? 'error' : '');
  const existsText = topic.exists === true ? 'exists' : (topic.exists === false ? 'missing' : 'not checked');
  const partitions = topic.partitionCount == null ? '-' : `${topic.partitionCount} partitions`;
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
        <span>partition ${escapeHtml(event.partition)} · offset ${escapeHtml(event.offset)} · key ${escapeHtml(event.key || '-')}</span>
      </div>
      <pre class="event-value">${escapeHtml(value || '-')}</pre>
    </article>
  `;
}

function kafkaTopicLabel(topic) {
  return `${topic.name}${topic.exists === false ? ' (missing)' : ''}`;
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
  const deployButton = document.querySelector('#deployConversionRulesButton');
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
    || !deployButton
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
  const busy = state.checkingWebhooks || state.checkingWebhookRuleCoverage || state.loadingWebhooksCache || state.deployingConversionRules;
  button.disabled = busy;
  button.textContent = state.checkingWebhooks
    ? 'Синхронизация...'
    : 'Провести синхронизацию';
  deployButton.disabled = busy;
  deployButton.textContent = state.deployingConversionRules
    ? 'Отправка...'
    : 'Отправить изменения на сервер';
  ruleCheckButton.disabled = busy;
  ruleCheckButton.textContent = state.checkingWebhookRuleCoverage
    ? 'Проверка...'
    : 'Проверить правила онлайн';
  cacheButton.disabled = busy;
  cacheButton.textContent = state.loadingWebhooksCache
    ? 'Загрузка...'
    : 'Загрузить локальный кэш';
  status.textContent = state.webhooksDeployError
    || state.webhooksCheckError
    || state.webhooksDeployMessage
    || check?.summary
    || check?.Summary
    || (success && managedIdentifier
      ? `Загружены наши webhooks по identifier ${managedIdentifier}: CREATE ${counts.CREATE ?? 0}, UPDATE ${counts.UPDATE ?? 0}, DELETE ${counts.DELETE ?? 0}. Чужие webhooks CMDBuild не учитываются.`
      : '')
    || check?.service
    || check?.Service
    || '';
  status.classList.toggle('error', Boolean(state.webhooksDeployError || state.webhooksCheckError || check?.error || check?.Error));
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
    return 'Онлайн-проверка не выполнялась. Кнопка проверяет live endpoint webhooks, а не локальный кэш.';
  }
  if (coverage.sourceClassCount === 0) {
    return `Онлайн-проверка ${formatCacheTimestamp(coverage.checkedAt)}: в текущих правилах нет source-классов для проверки.`;
  }
  if (coverage.missing.length === 0) {
    const globalText = coverage.hasGlobalWebhooks
      ? ` Общие webhooks покрывают события: ${coverage.globalEvents.join(', ')}.`
      : '';
    return `Онлайн-проверка ${formatCacheTimestamp(coverage.checkedAt)}: все ${coverage.sourceClassCount} source-классов правил покрыты managed webhooks.${globalText}`;
  }

  return `Онлайн-проверка ${formatCacheTimestamp(coverage.checkedAt)}: ${coverage.missing.length} из ${coverage.sourceClassCount} source-классов правил не имеют полного набора managed webhooks.`;
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
          <span>layers ${escapeHtml(item.layers.join(', '))} · rules ${item.ruleIds.length} · missing ${escapeHtml(item.missingEvents.join(', '))}</span>
        </div>
      `).join('')}
    </div>
  `;
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
    if (missingEvents.length > 0) {
      missing.push({
        ...sourceClass,
        missingEvents
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
        ruleIds: []
      };
      current.layers.add(layerKey === 'service' ? 'service' : 'suppression');
      current.ruleIds.push(rule.rule_id ?? rule.name ?? '');
      byCode.set(key, current);
    }
  }

  const sourceOrder = sourceClassOrderMap();
  return [...byCode.values()]
    .map((item) => ({
      ...item,
      layers: [...item.layers],
      ruleIds: item.ruleIds.filter(Boolean)
    }))
    .sort((left, right) => compareSourceClassCodesByHierarchy(left.code, right.code, sourceOrder));
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
    if (classCodes.length === 0) {
      globalEvents.add(eventType);
      continue;
    }

    classSpecificWebhookCount += classCodes.length;
    for (const classCode of classCodes) {
      const key = canonicalToken(classCode);
      const eventSet = byClass.get(key) ?? new Set();
      eventSet.add(eventType);
      byClass.set(key, eventSet);
    }
  }

  return { globalEvents, byClass, classSpecificWebhookCount };
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

function renderConversionConfigSyncView() {
  renderTopSourceStatus();
  const serviceRuleCount = document.querySelector('#conversionServiceRuleCount');
  const suppressionRuleCount = document.querySelector('#conversionSuppressionRuleCount');
  const traversalDepth = document.querySelector('#conversionTraversalDepth');
  const folder = document.querySelector('#conversionConfigFolder');
  const storageUpdatedAt = document.querySelector('#conversionConfigStorageUpdatedAt');
  const updatedAt = document.querySelector('#conversionConfigLastUpdatedAt');
  const status = document.querySelector('#syncConversionConfigStatus');
  const button = document.querySelector('#syncConversionConfigButton');
  const storedButton = document.querySelector('#loadStoredConversionConfigButton');
  const cacheButton = document.querySelector('#loadCachedConversionConfigButton');
  if (!serviceRuleCount || !suppressionRuleCount || !traversalDepth || !folder || !storageUpdatedAt || !updatedAt || !status || !button || !storedButton || !cacheButton) {
    return;
  }

  serviceRuleCount.textContent = String(state.ruleExamples.service.length);
  suppressionRuleCount.textContent = String(state.ruleExamples.suppression.length);
  traversalDepth.textContent = String(state.maxTraversalDepth);
  folder.textContent = conversionConfigFolderLabel() || '-';
  storageUpdatedAt.textContent = formatCacheTimestamp(state.conversionConfigStorageUpdatedAt);
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
  state.cmdbClassInstances = Array.isArray(payload.classInstances) ? payload.classInstances : [];
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
  sourceStatus.textContent = state.cmdbClassSchemaError || `${sourceClasses.length} source classes available.`;
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
      hierarchyLabel: schema?.hierarchyLabel || `${code} (referenced by rule)`,
      hierarchyPath: schema?.hierarchyPath || code,
      attributes: schema?.attributes?.length
        ? schema.attributes
        : sourceFieldsForRulePreview(rule).map((field) => ({
          code: field,
          name: field,
          description: field,
          type: 'rule field'
        }))
    };
    classes.push(fallback);
    byCode.set(token, fallback);
  }

  return sortClassesByInheritance(classes, state.cmdbClasses);
}

function renderSourceSchemaCards(layerKey, classes, highlight, searchQuery = '') {
  if (classes.length === 0) {
    return '<div class="empty-state">No source classes to show.</div>';
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
      ${item.parent ? `<p class="preview-meta">inherits ${escapeHtml(item.parent)}</p>` : ''}
      ${renderPreviewAttributes(model.attributes, {
        kind: 'source',
        layerKey,
        sourceClass: item.code,
        highlight
      })}
    </details>
  `;
  }).filter(Boolean);

  return cards.join('') || '<div class="empty-state">Нет source classes по текущему выделению или поиску.</div>';
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
    return '<div class="empty-state">No attributes loaded.</div>';
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
      <span>${escapeHtml(code)} · ${escapeHtml(formatCatalogAttributeType(attribute))}${attribute.required ? ' · required' : ''}</span>
    </div>
  `;
}

function formatCatalogAttributeType(attribute) {
  return attribute.lookupTypeCode
    ? `${attribute.type}: ${attribute.lookupTypeCode}`
    : (attribute.type || 'unknown');
}

function renderRuleGroups(layerKey, rules, highlight, searchQuery = '') {
  if (rules.length === 0) {
    return '<div class="empty-state">No conversion rules to show.</div>';
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
      description: (descriptions.get(sourceCode) ?? sourceCode) || 'Unknown source class',
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
    ? `target card #${targetCardId}`
    : 'create target instance';
  const ruleKind = rule.generated_from_template
    ? `generated from ${rule.generated_from_template}`
    : 'binding rule';
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
      <span>filter ${escapeHtml(filterText || 'none')}</span>
      <span>priority ${escapeHtml(rule.priority ?? 100)} · ${escapeHtml(targetLabel)} · key ${escapeHtml(rule.source?.key_attribute ?? rule.when?.fieldExists ?? '')} · idempotency ${escapeHtml(rule.target?.idempotency_key ?? '')} · mappings ${mappingCount} · target attr ${initialValueCount}</span>
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

function targetInstanceBySelection(selection) {
  const classItem = state.cmdbClassInstances.find((item) =>
    canonicalToken(item.classCode) === canonicalToken(selection.classCode));
  const card = classItem?.cards?.find((item) => String(item.id) === String(selection.cardId)) ?? null;
  return card ? { ...card, classCode: classItem.classCode } : null;
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
  setSelectOptions(config.select, ruleSelectOptions(rules), selectedRule);
  config.select.disabled = action === 'add' || rules.length === 0;
  config.selectField.classList.toggle('hidden', action === 'add');

  const selectedTarget = config.targetClass.value;
  const suggestedTarget = action === 'add'
    ? ruleTargetClassCode(state.ruleEditorSuggestions[layerKey])
    : '';
  setSelectOptions(config.targetClass, targetClassOptions(layerKey, suggestedTarget), selectedTarget);

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
    config.targetClass.value = targetInstanceOptionValue(values.targetClass, targetCard.id);
    state.ruleEditorTargetValues[layerKey] = {};
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
    throw new Error(`Класс ${sourceClass} входит в monitoring aggregation model и не может быть source.`);
  }

  if (!targetClass) {
    throw new Error('Целевой класс\\экземпляр класса обязателен.');
  }

  if (!Number.isFinite(priority) || priority < 1) {
    throw new Error('Priority должен быть положительным целым числом; 1 самый высокий.');
  }

  const selectionFilters = readSelectionFilterRows(config.selectionFilterList, {
    layerKey,
    kind: 'rule',
    sourceClass
  });
  const keyField = sourceKeyFieldFromSelection(selectionFilters);
  const targetValues = targetSelection.kind === 'class'
    ? readRuleTargetObjectValues(layerKey, sourceClass)
    : {};
  const selectedCard = targetSelection.kind === 'instance'
    ? targetInstanceBySelection(targetSelection)
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
      initialValues: {}
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
  return {
    name,
    ruleId: existingRule?.rule_id || normalizeRuleId(name)
  };
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
  state.ruleEditorTargetValues[layerKey] = { ...(rule.target?.initial_user_values ?? {}) };
  renderRuleSourceFieldOptions(layerKey);
  renderRuleSelectionFilterList(layerKey);
  renderRuleTargetFieldOptions(layerKey);
  renderRuleAttributeList(layerKey);
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
  if (parsed.ok && !config.select.value && parsed.document.rules.length > 0) {
    config.select.value = '0';
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
      <span role="columnheader">mode</span>
      <span role="columnheader">attribute</span>
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

  const sourceClasses = candidates.length > 0 ? candidates : availableSourceClasses();
  const options = sourceClasses.flatMap((item) => sourceFieldOptionsForClass(item.code));
  return uniqueSourceFieldOptions(options)
    .sort((left, right) => left.label.localeCompare(right.label, undefined, { sensitivity: 'base' }));
}

function selectionFilterRowTemplate(row, fieldOptions) {
  const mode = row.mode === 'exclude' ? 'exclude' : 'include';
  return `
    <div class="selection-filter-row" data-selection-filter-row>
      <select data-selection-filter-mode aria-label="mode">
        <option value="include" ${mode === 'include' ? 'selected' : ''}>Включить</option>
        <option value="exclude" ${mode === 'exclude' ? 'selected' : ''}>Исключить</option>
      </select>
      <select data-selection-filter-field aria-label="attribute">
        ${selectionFilterFieldOptionsTemplate(fieldOptions, row.field || '')}
      </select>
      <input data-selection-filter-regex value="${escapeHtml(row.regex || '')}" placeholder="(?i)^active$" aria-label="regex" autocomplete="off">
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

  if (selection.kind === 'instance') {
    const card = targetInstanceBySelection(selection);
    config.attributeList.innerHTML = `
      <div class="empty-state">Выбран существующий объект ${escapeHtml(targetCardDisplayLabel(card, selection.classCode))}; атрибуты целевого объекта уже заполнены в CMDBuild.</div>
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
      <span role="columnheader">attribute</span>
      <span role="columnheader">value</span>
      <span role="columnheader">help</span>
    </div>
    ${rows.map((row) => ruleAttributeRowTemplate(row)).join('')}
    ${missingCodes.length > 0 ? targetMissingAttributeNoteTemplate(missingCodes) : ''}
  `;
}

function ruleTargetObjectAttributeRows(layerKey, classCode) {
  const values = state.ruleEditorTargetValues[layerKey] ?? {};
  return targetObjectEditableAttributes(layerKey, classCode).map((attribute) => {
    const code = attributeCode(attribute);
    return {
      attribute,
      code,
      value: Object.hasOwn(values, code) ? values[code] : defaultRuleTargetObjectValue(layerKey, code)
    };
  });
}

function targetObjectEditableAttributes(layerKey, classCode) {
  const allowedCodes = targetObjectEditableAttributeCodes(layerKey);
  const attributes = targetClassAttributes(classCode);
  const attributeByExactCode = new Map(attributes
    .map((attribute) => [attributeCode(attribute), attribute]));
  const attributeByToken = new Map(attributes
    .map((attribute) => [canonicalToken(attributeCode(attribute)), attribute]));
  return allowedCodes
    .map((code) => attributeByExactCode.get(code) ?? attributeByToken.get(canonicalToken(code)))
    .filter(Boolean);
}

function missingTargetObjectEditableAttributeCodes(layerKey, classCode) {
  const attributes = targetClassAttributes(classCode);
  const exactCodes = new Set(attributes.map((attribute) => attributeCode(attribute)));
  const attributeTokens = new Set(attributes.map((attribute) => canonicalToken(attributeCode(attribute))));
  return targetObjectEditableAttributeCodes(layerKey)
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

function targetObjectEditableAttributeCodes(layerKey) {
  const normalizedLayer = String(layerKey ?? '').toLowerCase();
  if (normalizedLayer === 'service') {
    return [...TARGET_CARD_IDENTITY_ATTRIBUTES, ...SERVICE_USER_RESPONSIBILITY_ATTRIBUTES];
  }

  if (normalizedLayer === 'suppression') {
    return [...TARGET_CARD_IDENTITY_ATTRIBUTES, ...SUPPRESSION_USER_RESPONSIBILITY_ATTRIBUTES];
  }

  return [...TARGET_CARD_IDENTITY_ATTRIBUTES];
}

function defaultRuleTargetObjectValue(layerKey, code) {
  const token = canonicalToken(code);
  if (token === 'iscritical') {
    return 'false';
  }

  if (String(layerKey).toLowerCase() === 'service' && token === 'aggregationtype') {
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

function ruleTargetValueControlTemplate(attribute, value) {
  const code = attributeCode(attribute);
  const kind = normalizedRuleFieldKind(attribute);
  if (kind === 'boolean') {
    const normalizedValue = String(value ?? '').toLowerCase();
    return `
      <label class="select-field rule-attribute-cell">
        <select data-rule-target-value data-target-attribute="${escapeHtml(code)}">
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
        <select data-rule-target-value data-target-attribute="${escapeHtml(code)}">
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
        <textarea data-rule-target-value data-target-attribute="${escapeHtml(code)}" rows="2">${escapeHtml(value ?? '')}</textarea>
      </label>
    `;
  }

  const inputType = kind === 'integer' ? 'number' : 'text';
  const step = kind === 'integer' ? ' step="1"' : '';
  return `
    <label class="text-field rule-attribute-cell">
      <input data-rule-target-value data-target-attribute="${escapeHtml(code)}" type="${inputType}"${step} value="${escapeHtml(value ?? '')}" autocomplete="off">
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
  const attributes = targetObjectEditableAttributes(layerKey, selection.classCode);
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

  validateRuleTargetObjectValues(layerKey, values, attributes, selection.classCode);
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

function validateRuleTargetObjectValues(layerKey, values, attributes = [], classCode = '') {
  const availableTokens = new Set(attributes.map((attribute) => canonicalToken(attributeCode(attribute))));
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

  if (String(layerKey).toLowerCase() !== 'service') {
    return;
  }

  if (!availableTokens.has('aggregationtype')) {
    return;
  }

  const aggregationType = String(values.aggregation_type ?? 'all').trim();
  const hasThreshold = availableTokens.has('threshold') && values.threshold !== undefined && values.threshold !== '';
  const hasN = availableTokens.has('n') && values.n !== undefined && values.n !== '';
  if ((aggregationType === 'all' || aggregationType === 'any') && (hasThreshold || hasN)) {
    throw new Error('Для aggregation_type all/any поля threshold и n должны быть пустыми.');
  }

  if (aggregationType === 'threshold' && !availableTokens.has('threshold')) {
    throw new Error('Для aggregation_type threshold в целевом классе должен быть атрибут threshold.');
  }

  if (aggregationType === 'threshold' && (!hasThreshold || values.threshold < 0 || values.threshold > 100 || hasN)) {
    throw new Error('Для aggregation_type threshold заполните threshold от 0 до 100 и оставьте n пустым.');
  }

  if (aggregationType === 'n_of_m' && !availableTokens.has('n')) {
    throw new Error('Для aggregation_type n_of_m в целевом классе должен быть атрибут n.');
  }

  if (aggregationType === 'n_of_m' && (!hasN || values.n < 1 || hasThreshold)) {
    throw new Error('Для aggregation_type n_of_m заполните n >= 1 и оставьте threshold пустым.');
  }
}

function normalizedRuleFieldKind(attributeOrField) {
  if (!attributeOrField) {
    return 'unknown';
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
    ? ' · JS validation'
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

  return attributeOrField?.type || 'unknown';
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

function handleTemplateEditorChange(layerKey, target) {
  if (target.matches('[data-template-select]')) {
    state.templateEditorSelected[layerKey] = target.value;
    loadSelectedTemplateIntoEditor(layerKey);
    return;
  }

  if (target.matches('[data-selection-filter-mode], [data-selection-filter-field]')) {
    ensureSelectionFilterDraftRow(layerKey, 'template');
    state.templateEditorSelectionFilters[layerKey] = selectionFilterRowsFromDom(templateEditorConfig(layerKey).selectionFilterList);
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
  setSelectOptions(config.targetClass, targetClassOptions(layerKey), config.targetClass.value);
  if (selectedId && !document.templates.some((template) => template.template_id === selectedId)) {
    state.templateEditorSelected[layerKey] = '';
  }

  if (!config.id.value && !config.name.value && !config.sourceRegex.value) {
    resetTemplateEditorForCreate(layerKey);
  }

  renderTemplateSourceFieldOptions(layerKey);
  renderTemplateSelectionFilterList(layerKey);
  renderTemplateEditorStatus(layerKey);
}

function templateSelectOptions(templates) {
  return [
    { value: '', label: 'Новый шаблон' },
    ...templates.map((template, index) => ({
      value: template.template_id,
      label: `${index + 1}. ${template.name || template.template_id}`
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
  config.name.value = template.name || '';
  config.sourceRegex.value = template.source_class_regex || '';
  state.templateEditorSelectionFilters[layerKey] = selectionFiltersFromTemplate(template);
  config.priority.value = String(template.priority ?? 100);
  setSelectOptions(config.targetClass, targetClassOptions(layerKey), template.target?.class_code || '');
  config.targetName.value = template.target?.name_template || '';
  config.targetDescription.value = template.target?.description_template || '';
  config.sourceKey.value = template.target?.population_source_key_template || '';
  if (config.deleteMode) {
    config.deleteMode.value = TEMPLATE_DELETE_MODES.detachRulesKeepObjects;
  }
  renderTemplateVariableList(layerKey, template.variables ?? []);
  renderTemplateSourceFieldOptions(layerKey);
  renderTemplateSelectionFilterList(layerKey);
  setTemplateEditorStatus(layerKey, '');
}

function resetTemplateEditorForCreate(layerKey) {
  const config = templateEditorConfig(layerKey);
  config.select.value = '';
  config.id.value = '';
  config.name.value = '';
  config.sourceRegex.value = '';
  state.templateEditorSelectionFilters[layerKey] = [];
  config.priority.value = '';
  setSelectOptions(config.targetClass, targetClassOptions(layerKey), '');
  config.targetName.value = '';
  config.targetDescription.value = '';
  config.sourceKey.value = '';
  if (config.deleteMode) {
    config.deleteMode.value = TEMPLATE_DELETE_MODES.detachRulesKeepObjects;
  }
  renderTemplateSourceFieldOptions(layerKey);
  renderTemplateSelectionFilterList(layerKey);
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
    throw new Error('Целевой класс\\экземпляр класса обязателен.');
  }

  if (!Number.isFinite(priority) || priority < 1) {
    throw new Error('Priority должен быть положительным целым числом; 1 самый высокий.');
  }

  if (config.sourceRegex.value.trim()) {
    assertValidRegexPattern(config.sourceRegex.value.trim());
  }

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
    filter: selectionFiltersToTemplateFilter(selectionFilters),
    priority,
    target: {
      class_code: config.targetClass.value,
      name_template: config.targetName.value.trim() || '${class.description}',
      description_template: config.targetDescription.value.trim() || 'Автоматически создано для ${class.description}',
      population_source_key_template: config.sourceKey.value.trim() || '${source.id}'
    },
    variables
  };
}

function saveTemplateEditorChange(layerKey) {
  try {
    const template = readTemplateEditorValues(layerKey);
    const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
    const selectedId = state.templateEditorSelected[layerKey];
    const index = document.templates.findIndex((item) =>
      item.template_id === (selectedId || template.template_id) || item.template_id === template.template_id);
    const previousTemplate = index >= 0 ? cloneJson(document.templates[index]) : null;
    const changeMode = templateChangeMode(previousTemplate, template);
    template.version = nextTemplateVersion(previousTemplate, changeMode);
    template.lifecycle = templateLifecycleMetadata(template, changeMode);

    let generatedRemoval = { removedRules: 0, targets: 0 };
    if (changeMode === 'delete_create') {
      generatedRemoval = removeGeneratedRulesForTemplate(
        layerKey,
        previousTemplate.template_id,
        previousTemplate,
        'template_regex_changed');
    }

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
    const lifecycleMessage = changeMode === 'delete_create'
      ? ` Regex изменен: удалено старых правил ${generatedRemoval.removedRules}, целей в плане удаления ${generatedRemoval.targets}.`
      : changeMode === 'variables_modified'
        ? ' Переменные изменены как модификация шаблона.'
        : '';
    setTemplateEditorStatus(layerKey, `Сохранен шаблон ${template.name}.${lifecycleMessage}`);
    renderTemplateEditor(layerKey);
    renderTemplateApplyView();
    renderTemplateAuditView();
    renderConversionConfigSyncView();
  } catch (error) {
    setTemplateEditorStatus(layerKey, error.message, 'error');
  }
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

  if (templateRegexFingerprint(previousTemplate) !== templateRegexFingerprint(nextTemplate)) {
    return 'delete_create';
  }

  if (templateVariablesFingerprint(previousTemplate) !== templateVariablesFingerprint(nextTemplate)) {
    return 'variables_modified';
  }

  return 'modified';
}

function nextTemplateVersion(previousTemplate, changeMode) {
  const previousVersion = Number(previousTemplate?.version || 1);
  return ['delete_create', 'variables_modified', 'modified'].includes(changeMode)
    ? previousVersion + 1
    : 1;
}

function templateLifecycleMetadata(template, changeMode) {
  return {
    change_mode: changeMode,
    updated_at: new Date().toISOString(),
    source_regex_fingerprint: stableHash(template.source_class_regex || ''),
    regex_fingerprint: templateRegexFingerprint(template),
    variables_fingerprint: templateVariablesFingerprint(template),
    full_fingerprint: templateFingerprint(template)
  };
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

function applyTemplatesToRuleDocuments() {
  try {
    const servicePlan = templateMaterializationPlan('service');
    const suppressionPlan = templateMaterializationPlan('suppression');
    const serviceResult = materializeTemplatesForLayer('service', servicePlan);
    const suppressionResult = materializeTemplatesForLayer('suppression', suppressionPlan);
    state.templateApplyMessage = `Сгенерировано правил: service ${serviceResult.generatedRules.length}, suppression ${suppressionResult.generatedRules.length}.`;
    state.templateApplyError = '';
    renderRulesPreviews();
    renderRuleEditors();
    renderTemplateApplyView();
    renderTemplateAuditView();
    renderConversionConfigSyncView();
  } catch (error) {
    state.templateApplyMessage = '';
    state.templateApplyError = error.message;
    renderTemplateApplyView();
  }
}

function materializeTemplatesForLayer(layerKey, plan) {
  const parsed = parseRuleDocument(layerKey);
  if (!parsed.ok) {
    throw new Error(parsed.error);
  }

  const document = parsed.document;
  document.rules = document.rules
    .filter((rule) => !rule.generated_from_template)
    .concat(plan.generatedRules);
  for (const rule of plan.generatedRules) {
    const fields = sourceFieldsForRule(ruleValuesFromRule(rule))
      .concat(sourceFieldsFromMappings(rule.target?.initial_user_values ?? {}));
    ensureRuleDocumentSource(document, ruleSourceClassCode(rule), fields);
  }

  writeRuleDocument(layerKey, document);
  return plan;
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
  const candidateCount = document.querySelector('#templateApplyCandidateCount');
  const status = document.querySelector('#templateApplyStatus');
  const list = document.querySelector('#templateApplyPlanList');
  if (!serviceCount || !suppressionCount || !ruleCount || !candidateCount || !status || !list) {
    return;
  }

  const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates;
  const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates;
  serviceCount.textContent = String(serviceTemplates.length);
  suppressionCount.textContent = String(suppressionTemplates.length);
  ruleCount.textContent = String(plan.generatedRules.length + suppressionPlan.generatedRules.length);
  candidateCount.textContent = String(plan.candidateCount + suppressionPlan.candidateCount);
  status.textContent = state.templateApplyError || state.templateApplyMessage || '';
  status.classList.toggle('error', Boolean(state.templateApplyError));
  list.innerHTML = renderTemplatePlanCards('service', plan).concat(renderTemplatePlanCards('suppression', suppressionPlan)).join('')
    || '<div class="empty-state">Шаблоны не настроены или нет подходящих source classes.</div>';
}

function renderTemplateAuditView() {
  const list = document.querySelector('#templateAuditList');
  if (!list) {
    return;
  }

  const serviceAudit = templateAuditForLayer('service');
  const suppressionAudit = templateAuditForLayer('suppression');
  list.innerHTML = renderTemplateAuditCard('service', serviceAudit) + renderTemplateAuditCard('suppression', suppressionAudit);
}

function renderTemplatePlanCards(layerKey, plan) {
  return plan.templates.map((item) => `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey)} template</span>
      <strong>${escapeHtml(item.template.name || item.template.template_id)}</strong>
      <span>source candidates ${item.candidates.length} · generated rules ${item.rules.length}</span>
      <span>target ${escapeHtml(item.template.target?.class_code || '')} · source regex ${escapeHtml(item.template.source_class_regex || 'all')}</span>
    </div>
  `);
}

function renderTemplateAuditCard(layerKey, audit) {
  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(layerKey)} audit</span>
      <strong>${escapeHtml(audit.templates)} templates · ${escapeHtml(audit.generatedRules)} generated rules</strong>
      <span>candidates ${escapeHtml(audit.candidates)} · missing ${escapeHtml(audit.missingRules)} · stale ${escapeHtml(audit.staleRules)} · detached ${escapeHtml(audit.detachedRules)} · deletion plans ${escapeHtml(audit.deletionPlans)}</span>
      <span>${escapeHtml(audit.errors.join('; ') || 'Ошибок шаблонов нет.')}</span>
    </div>
  `;
}

function templateAuditForLayer(layerKey) {
  const plan = templateMaterializationPlan(layerKey, { safe: true });
  const document = parseRuleDocument(layerKey).document;
  const generatedRuleIds = new Set((document.rules ?? [])
    .filter((rule) => rule.generated_from_template)
    .map((rule) => rule.rule_id));
  const expectedRuleIds = new Set(plan.generatedRules.map((rule) => rule.rule_id));
  const currentTemplateIds = new Set(normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey).templates
    .map((template) => template.template_id));
  const staleRules = (document.rules ?? []).filter((rule) =>
    rule.generated_from_template && !currentTemplateIds.has(rule.generated_from_template));
  const detachedRules = (document.rules ?? []).filter((rule) => rule.detached_from_template
    || rule.template_generation?.status === 'detached');
  const deletionPlans = (document.templateDeletionPlans ?? []).filter((plan) => plan.status !== 'done');

  return {
    templates: plan.templates.length,
    candidates: plan.candidateCount,
    generatedRules: generatedRuleIds.size,
    missingRules: [...expectedRuleIds].filter((ruleId) => !generatedRuleIds.has(ruleId)).length,
    staleRules: staleRules.length,
    detachedRules: detachedRules.length,
    deletionPlans: deletionPlans.length,
    errors: plan.errors
  };
}

function templateMaterializationPlan(layerKey, options = {}) {
  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  const errors = [];
  const templates = [];
  const generatedRules = [];
  let candidateCount = 0;
  for (const template of document.templates.filter((item) => item.enabled !== false)) {
    try {
      const candidates = templateCandidateClasses(template);
      const rules = candidates.map((candidate) => ruleFromTemplate(layerKey, template, candidate));
      candidateCount += candidates.length;
      generatedRules.push(...rules);
      templates.push({ template, candidates, rules });
    } catch (error) {
      errors.push(`${template.template_id}: ${error.message}`);
      if (!options.safe) {
        throw error;
      }
    }
  }

  return { templates, generatedRules, candidateCount, errors };
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
  const text = String(pattern ?? '').trim();
  if (text.startsWith('(?i)')) {
    return new RegExp(text.slice(4), 'i');
  }

  return new RegExp(text);
}

function ruleFromTemplate(layerKey, template, candidate) {
  const context = templateContext(template, candidate);
  const keyExpression = renderTemplateString(template.target?.population_source_key_template || '${source.id}', context);
  const selectionFilters = selectionFiltersFromTemplate(template);
  const keyField = firstSourceAttributeFromTemplate(keyExpression)
    || firstSourceAttributeFromTemplate(template.target?.population_source_key_template)
    || selectionFilters.find((filter) => filter.field)?.field
    || 'id';
  const ruleName = renderTemplateString(template.name || template.template_id, context);
  const ruleId = normalizeRuleId(`${template.template_id}-${candidate.code}`);
  const generatedAt = new Date().toISOString();
  const fullFingerprint = templateFingerprint(template);
  const variablesFingerprint = templateVariablesFingerprint(template);
  const allRegex = [
    {
      field: 'className',
      pattern: `(?i)^${escapeRegex(candidate.code)}$`
    }
  ].concat(selectionFiltersToRegexMatchers(selectionFilters, 'include'));
  const noneRegex = selectionFiltersToRegexMatchers(selectionFilters, 'exclude');

  return normalizeBindingRuleTarget({
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
      template_source_regex: template.source_class_regex || '',
      template_fingerprint: fullFingerprint,
      variables_fingerprint: variablesFingerprint,
      generated_at: generatedAt,
      candidate_class_code: candidate.code || '',
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
        name: renderTemplateString(template.target?.name_template || '${class.description}', context),
        [POPULATION_SOURCE_KEY_ATTRIBUTE]: keyExpression
      },
      initial_user_values: {
        description: renderTemplateString(template.target?.description_template || '', context)
      },
      user_responsibility_attributes: allowedUserResponsibilityAttributes(layerKey),
      created_by_template: {
        template_id: template.template_id,
        template_name: template.name || template.template_id,
        template_version: template.version ?? 1,
        template_fingerprint: fullFingerprint,
        generated_at: generatedAt,
        candidate_class_code: candidate.code || ''
      }
    }
  }, layerKey);
}

function templateContext(template, candidate) {
  const context = {
    class: {
      code: candidate.code || '',
      name: candidate.name || candidate.code || '',
      description: classDisplayName(candidate),
      hierarchyPath: candidate.hierarchyPath || ''
    },
    vars: {}
  };

  for (let pass = 0; pass < 4; pass += 1) {
    for (const variable of template.variables ?? []) {
      context.vars[variable.name] = renderTemplateString(variable.value, context);
    }
  }

  return context;
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
  const match = String(expression ?? '').match(/^(class|vars|source)\.([A-Za-z_][A-Za-z0-9_]*)$/);
  if (!match) {
    return { value: '', resolved: false, unresolved: false };
  }

  const [, scope, key] = match;
  if (scope === 'source') {
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
  return stableHash({
    template_id: template?.template_id || '',
    layer: template?.layer || '',
    source_class_regex: template?.source_class_regex || '',
    filter: template?.filter ?? {},
    priority: template?.priority ?? 100,
    target: template?.target ?? {},
    variables: template?.variables ?? []
  });
}

function templateVariablesFingerprint(template) {
  return stableHash(template?.variables ?? []);
}

function templateRegexFingerprint(template) {
  return stableHash({
    source_class_regex: template?.source_class_regex || '',
    filter: selectionFiltersToTemplateFilter(selectionFiltersFromTemplate(template))
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
  return normalized;
}

function normalizeTemplate(template, layerKey) {
  const normalized = template && typeof template === 'object' && !Array.isArray(template)
    ? template
    : {};
  normalized.template_id = normalizeRuleId(normalized.template_id || normalized.name || '');
  normalized.name = normalized.name || normalized.template_id;
  normalized.layer = normalized.layer || layerKey;
  normalized.enabled = normalized.enabled !== false;
  normalized.source_class_regex = normalized.source_class_regex || '';
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
  normalized.target.population_source_key_template = normalized.target.population_source_key_template || '${source.id}';
  normalized.variables = Array.isArray(normalized.variables)
    ? normalized.variables
        .filter((variable) => variable?.name)
        .map((variable) => ({ name: String(variable.name), value: String(variable.value ?? '') }))
    : [];
  return normalized;
}

function defaultTemplateDocument(layerKey) {
  return {
    layer: layerKey,
    templates: []
  };
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
    targetName: document.querySelector(`#${prefix}TargetName`),
    targetDescription: document.querySelector(`#${prefix}TargetDescription`),
    sourceKey: document.querySelector(`#${prefix}SourceKey`),
    deleteMode: document.querySelector(`#${prefix}DeleteMode`),
    selectionFilterList: document.querySelector(`#${prefix}SelectionFilterList`),
    variableList: document.querySelector(`#${prefix}VariableList`),
    fieldOptions: document.querySelector(`#${prefix}SourceFieldOptions`),
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
  const targetClass = referenceTargetClass(attribute, currentClass);
  if (!targetClass || depth > state.maxTraversalDepth) {
    return [];
  }

  const visitKey = `${targetClass}:${attributeCode(attribute)}:${depth}`;
  if (seen.has(visitKey)) {
    return [];
  }

  const nextSeen = new Set(seen);
  nextSeen.add(visitKey);
  const path = [...prefix, attribute];
  const options = [];
  for (const targetAttribute of sourceDirectAttributes(targetClass)) {
    if (!isReadableSourceAttribute(targetAttribute)) {
      continue;
    }

    if (isReferenceSourceAttribute(targetAttribute)) {
      options.push(...sourceReferenceLeafFieldOptions(rootClass, targetAttribute, path, depth + 1, nextSeen, targetClass));
      continue;
    }

    const leafPath = [...path, targetAttribute];
    const fieldKey = fieldKeyForCmdbPath(leafPath);
    options.push({
      value: fieldKey,
      label: `${leafPath.map(attributeCode).join(' -> ')}${targetAttribute.type ? ` / ${targetAttribute.type}` : ''}`,
      meta: `path ${[rootClass, ...leafPath.map(attributeCode)].join('.')}`,
      fieldRule: sourceFieldRuleForCmdbPath(rootClass, leafPath)
    });
  }

  return options;
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
        label: `domain ${sourceClassDisplayName(targetClass)} -> ${attributeCode(attribute)}${attribute.type ? ` / ${attribute.type}` : ''}`,
        meta: `path ${rootClass}.{domain:${targetClass}}.${attributeCode(attribute)}`,
        fieldRule: sourceFieldRuleForDomainPath(rootClass, targetClass, leafPath)
      });
    }
  }

  return options;
}

function sourceDomainReferenceLeafFieldOptions(rootClass, domainTargetClass, attribute, prefix = [], depth = 1, seen = new Set(), currentClass = domainTargetClass) {
  const targetClass = referenceTargetClass(attribute, currentClass);
  if (!targetClass || depth > state.maxTraversalDepth) {
    return [];
  }

  const visitKey = `${domainTargetClass}:${targetClass}:${attributeCode(attribute)}:${depth}`;
  if (seen.has(visitKey)) {
    return [];
  }

  const nextSeen = new Set(seen);
  nextSeen.add(visitKey);
  const path = [...prefix, attribute];
  const options = [];
  for (const targetAttribute of sourceDirectAttributes(targetClass)) {
    if (!isReadableSourceAttribute(targetAttribute)) {
      continue;
    }

    if (isReferenceSourceAttribute(targetAttribute)) {
      options.push(...sourceDomainReferenceLeafFieldOptions(rootClass, domainTargetClass, targetAttribute, path, depth + 1, nextSeen, targetClass));
      continue;
    }

    const leafPath = [...path, targetAttribute];
    options.push({
      value: fieldKeyForDomainPath(domainTargetClass, leafPath),
      label: `domain ${sourceClassDisplayName(domainTargetClass)} -> ${leafPath.map(attributeCode).join(' -> ')}${targetAttribute.type ? ` / ${targetAttribute.type}` : ''}`,
      meta: `path ${rootClass}.{domain:${domainTargetClass}}.${leafPath.map(attributeCode).join('.')}`,
      fieldRule: sourceFieldRuleForDomainPath(rootClass, domainTargetClass, leafPath)
    });
  }

  return options;
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
    type: leaf.type || '',
    required: false,
    resolve: {
      mode: 'cmdbPath',
      valueMode: isLookupSourceAttribute(leaf) ? 'code' : 'leaf',
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
    type: leaf.type || '',
    required: false,
    resolve: {
      mode: 'cmdbPath',
      valueMode: isLookupSourceAttribute(leaf) ? 'code' : 'leaf',
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
  const instanceClass = state.cmdbClassInstances.find((item) =>
    canonicalToken(item.classCode) === canonicalToken(classCode));
  if (instanceClass?.attributes?.length > 0) {
    return [...instanceClass.attributes].sort((left, right) =>
      String(left.displayName || left.description || left.code || left.name)
        .localeCompare(String(right.displayName || right.description || right.code || right.name), undefined, {
          sensitivity: 'base'
        }));
  }

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
  normalized.templateDeletionPlans = Array.isArray(normalized.templateDeletionPlans)
    ? normalized.templateDeletionPlans.map((plan) => normalizeTemplateDeletionPlan(plan, layerKey))
    : [];
  return normalized;
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

function normalizeBindingRuleTarget(rule, layerKey = '') {
  const normalized = rule && typeof rule === 'object' && !Array.isArray(rule)
    ? rule
    : {};
  normalized.layer = normalized.layer || layerKey;
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
    templateDeletionPlans: []
  };
}

function defaultRuleRuntimePolicy() {
  return {
    create: 'Evaluate binding rules and create missing managed cards/relations for matching source cards.',
    update: 'Re-evaluate the same rules, merge rule-owned managed attributes, and preserve user responsibility attributes/external values.',
    delete: 'Remove generated source relations and reconcile derived Zabbix structures; the customer owns aggregator card deletion.'
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

function ruleSelectOptions(rules) {
  if (rules.length === 0) {
    return [{ value: '', label: 'В наборе нет правил', disabled: true }];
  }

  return [
    { value: '', label: 'Выберите правило' },
    ...rules.map((rule, index) => ({
      value: String(index),
      label: `${index + 1}. ${rule.name || rule.rule_id || ruleSourceClassCode(rule) || 'rule'}`
    }))
  ];
}

function targetClassOptions(layerKey, suggestedCode = '') {
  const layer = layerKey === 'service' ? 'Service' : 'Suppression';
  const hierarchyClasses = schemaClassesForLayer(layer);
  const classes = sortSchemaClassesByInheritance(
    hierarchyClasses.filter((item) => !item.isSuperclass && item.origin !== 'model_root_superclass'),
    hierarchyClasses);
  const instancesByClass = targetInstanceOptionsByClass(layerKey);
  const instanceCount = [...instancesByClass.values()].reduce((sum, items) => sum + items.length, 0);
  if (classes.length === 0 && instanceCount === 0) {
    return [{ value: '', label: 'Целевые классы не загружены', disabled: true }];
  }

  const options = [];
  const renderedClassTokens = new Set();
  for (const item of classes) {
    const classToken = canonicalToken(item.code);
    const classLabel = schemaClassOptionLabel(item);
    renderedClassTokens.add(classToken);
    options.push({
      value: item.code,
      label: `Класс: ${classLabel}`
    });
    options.push(...(instancesByClass.get(classToken) ?? []).map((card) => ({
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
      options.push({
        value: targetInstanceOptionValue(card.classCode, card.id),
        label: `Класс: ${classLabel} -> экземпляр: ${targetCardDisplayLabel(card, card.classCode)}`
      });
    }
  }

  return [
    {
      value: '',
      label: suggestedCode
        ? `Выберите целевой класс\\экземпляр класса, например ${suggestedCode}`
        : 'Выберите целевой класс\\экземпляр класса'
    },
    ...options
  ];
}

function targetInstanceOptionsByClass(layerKey) {
  const layer = layerKey === 'service' ? 'Service' : 'Suppression';
  const classOrder = schemaClassOrderMap(layer);
  const result = new Map();
  const classItems = state.cmdbClassInstances
    .filter((item) => String(item.layer).toLowerCase() === layer.toLowerCase())
    .sort((left, right) => compareTargetInstanceClasses(left, right, classOrder));
  for (const classItem of classItems) {
    result.set(canonicalToken(classItem.classCode), (classItem.cards ?? [])
      .slice()
      .sort((left, right) =>
        targetCardDisplayLabel(left, classItem.classCode)
          .localeCompare(targetCardDisplayLabel(right, classItem.classCode), undefined, { sensitivity: 'base' }))
      .map((card) => ({ ...card, classCode: classItem.classCode })));
  }

  return result;
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
    return '<div class="empty-state">No target classes to show.</div>';
  }

  const targetDomains = state.domains
    .concat(state.suggestedDomains)
    .filter((domain) => domain.layer === layer);
  const domainsByClass = domainsBySource(targetDomains);
  const instancesByClass = targetInstancesByClass(layer, layerKey);
  const visibility = targetVisibilityForPreview(classes, instancesByClass, highlight, searchQuery);
  if (visibility.visibleClassCodes.size === 0) {
    return '<div class="empty-state">Нет target classes или instances по текущему выделению или поиску.</div>';
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
      ${item.parentClassCode ? `<p class="preview-meta">inherits ${escapeHtml(item.parentClassCode)}</p>` : ''}
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
      cards.push({
        id: cardId,
        classCode: targetClass,
        description: rule.target?.card_description
          || rule.target?.initial_user_values?.description
          || rule.target?.attribute_mappings?.name
          || `${targetClass} #${cardId}`,
        attributes: []
      });
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
      <h3>Instances (${instances.length})</h3>
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
            <span>card #${escapeHtml(cardId)}</span>
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
    state.error = 'Entity code is required.';
    render();
    return false;
  }

  const duplicate = state.customEntities.some((entity) =>
    entity.layer === layer && normalizeEntityCode(entity.code) === code);
  if (duplicate) {
    state.error = `Entity ${code} already exists in ${layer}.`;
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
      <button type="button" class="icon-button" data-remove-entity="${index}" title="Remove entity">×</button>
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
