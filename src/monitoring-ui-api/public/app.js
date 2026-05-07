const DATA_CACHE_DB = 'cmdb2monitoring-service-suppression';
const DATA_CACHE_STORE = 'dataSourceCache';
const DATA_CACHE_VERSION = 1;
const CACHE_KEYS = {
  cmdbuild: 'cmdbuild.catalogs.v3',
  zabbix: 'zabbix.check',
  conversionConfig: 'conversion.config'
};
const POPULATION_SOURCE_KEY_ATTRIBUTE = 'population_source_key';
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
const WIDE_CHOICE_MENU_MIN_WIDTH = 520;
const WIDE_CHOICE_MENU_MAX_WIDTH = 760;
const WIDE_CHOICE_MENU_MAX_ITEMS = 120;

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
  sourceLinks: [],
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
  ruleEditorMappings: {
    service: {},
    suppression: {}
  },
  ruleEditorUserAttributes: {
    service: [],
    suppression: []
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
  syncingConversionConfigs: false,
  loadingConversionConfigCache: false,
  syncConversionConfigMessage: '',
  syncConversionConfigError: '',
  conversionConfigCacheUpdatedAt: '',
  openClassRows: new Set(),
  openDomainRows: new Set(),
  activeLayer: 'Service',
  loading: false,
  error: ''
};

document.querySelectorAll('.nav-item').forEach((button) => {
  button.addEventListener('click', () => {
    void activateView(button.dataset.view);
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
    state.sourceLinks = [];
    state.cmdbClassInstances = [];
    state.cmdbCacheUpdatedAt = '';
    state.conversionConfigCacheUpdatedAt = '';
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
  renderRuleEditors();
  renderConversionConfigSyncView();
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

document.querySelector('#syncConversionConfigButton').addEventListener('click', () => {
  void syncConversionConfigs();
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
    return;
  }

  const addButton = event.target.closest('[data-add-source-link]');
  const removeButton = event.target.closest('[data-remove-source-link]');
  if (!addButton && !removeButton) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  rememberOpenRows();
  const row = event.target.closest('.source-link-editor');
  const managedClassCode = (addButton ?? removeButton).dataset.managedClass;
  const input = row?.querySelector('[data-source-link-input]');
  state.openClassRows.add(managedClassCode);
  let changed = false;
  if (addButton) {
    changed = addSourceLink(managedClassCode, input?.value ?? '');
    if (input) {
      input.value = '';
    }
  } else {
    changed = removeSourceLink(managedClassCode, removeButton.dataset.customerClass);
  }

  if (changed) {
    await loadPreview({
      preserveClassCodes: [managedClassCode],
      renderLoading: false
    });
    return;
  }

  render();
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
      return;
    }

    if (event.target.matches('[data-rule-attribute-source], [data-rule-attribute-target]')) {
      ensureRuleAttributeDraftRow(panel.dataset.ruleEditorLayer);
      renderRuleAttributeRowType(panel.dataset.ruleEditorLayer, event.target.closest('[data-rule-attribute-row]'));
    }
  });
});

document.querySelectorAll('[data-rule-apply]').forEach((button) => {
  button.addEventListener('click', () => {
    const panel = button.closest('[data-rule-editor-layer]');
    if (panel) {
      applyRuleEditorChange(panel.dataset.ruleEditorLayer);
    }
  });
});

document.querySelectorAll('[data-template-editor-layer]').forEach((panel) => {
  panel.addEventListener('change', (event) => {
    handleTemplateEditorChange(panel.dataset.templateEditorLayer, event.target);
  });
  panel.addEventListener('input', (event) => {
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

await loadInitialConfig();
await loadCmdbSourceCache({ silent: true });
await loadZabbixSourceCache({ silent: true });
await loadPreview({ refreshCmdbDomains: false });
seedRules();
await loadConversionConfigCache({ silent: true });
render();

async function activateView(view) {
  document.querySelectorAll('.nav-item').forEach((button) => {
    button.classList.toggle('active', button.dataset.view === view);
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
    state.modelRoots = preview.modelRoots ?? [];
    applyPreviewModelRoots();
    applyModelRootInputs();
  } catch (error) {
    state.classes = [];
    state.domains = [];
    state.suggestedDomains = [];
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
    sourceLinks: state.sourceLinks
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

async function syncConversionConfigs() {
  state.syncingConversionConfigs = true;
  state.syncConversionConfigMessage = 'Обновление конфигураций конвертации...';
  state.syncConversionConfigError = '';
  renderConversionConfigSyncView();

  try {
    const serviceOk = syncRulesFromDocument('service');
    const suppressionOk = syncRulesFromDocument('suppression');
    if (!serviceOk || !suppressionOk) {
      throw new Error('Одна или несколько конфигураций конвертации некорректны.');
    }

    renderRulesPreviews();
    renderRuleEditors();
    const cacheRecord = await writeDataCache(conversionConfigCacheKey(), {
      prefix: state.prefix,
      ruleDocuments: state.ruleDocuments,
      templateDocuments: state.templateDocuments
    });
    state.conversionConfigCacheUpdatedAt = cacheRecord.updatedAt;
    const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates.length;
    const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates.length;
    state.syncConversionConfigMessage = `Конфигурации конвертации обновлены: ${state.ruleExamples.service.length} service rules, ${state.ruleExamples.suppression.length} suppression rules, ${serviceTemplates + suppressionTemplates} templates.`;
    state.syncConversionConfigError = '';
  } catch (error) {
    state.syncConversionConfigMessage = '';
    state.syncConversionConfigError = error.message;
  } finally {
    state.syncingConversionConfigs = false;
    render();
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
      throw new Error('Локальный кэш конфигураций конвертации для текущего prefix не найден.');
    }

    const payload = cacheRecord.payload ?? {};
    const documents = payload.ruleDocuments ?? {};
    state.ruleDocuments.service = documents.service ?? state.ruleDocuments.service;
    state.ruleDocuments.suppression = documents.suppression ?? state.ruleDocuments.suppression;
    const templateDocuments = payload.templateDocuments ?? {};
    state.templateDocuments.service = normalizeTemplateDocument(templateDocuments.service ?? state.templateDocuments.service, 'service');
    state.templateDocuments.suppression = normalizeTemplateDocument(templateDocuments.suppression ?? state.templateDocuments.suppression, 'suppression');
    syncRulesFromDocument('service');
    syncRulesFromDocument('suppression');
    state.conversionConfigCacheUpdatedAt = cacheRecord.updatedAt ?? '';
    if (!silent) {
      const serviceTemplates = normalizeTemplateDocument(state.templateDocuments.service, 'service').templates.length;
      const suppressionTemplates = normalizeTemplateDocument(state.templateDocuments.suppression, 'suppression').templates.length;
      state.syncConversionConfigMessage = `Локальный кэш конфигураций конвертации загружен: ${state.ruleExamples.service.length} service rules, ${state.ruleExamples.suppression.length} suppression rules, ${serviceTemplates + suppressionTemplates} templates.`;
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
  document.querySelector('#maxTraversalDepthSelect').value = String(state.maxTraversalDepth);
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
  renderDataSourceSyncView();
  renderZabbixSyncView();
  renderConversionConfigSyncView();
  renderTemplateApplyView();
  renderTemplateAuditView();
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
    return (state.rootClassesByLayer[layer] ?? [])
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
    !item.parentClassCode || !classCodes.has(item.parentClassCode));
  container.innerHTML = roots.map((item) =>
    renderClassRow(item, childrenByParent, domainsByClass)).join('');
}

function renderClassRow(item, childrenByParent = new Map(), domainsByClass = new Map()) {
  const isOpen = item.isSuperclass || state.openClassRows.has(item.code);
  const children = childrenByParent.get(item.code) ?? [];
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
      ${renderSourceLinkEditor(item)}
      ${renderClassDomains(classDomains)}
      ${renderAttributeTable(item.attributes ?? [], item.isSuperclass ? 'Superclass attributes' : 'Local class attributes')}
      ${children.length > 0 ? `<div class="child-list">${children.map((child) => renderClassRow(child, childrenByParent, domainsByClass)).join('')}</div>` : ''}
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

function renderSourceLinkEditor(item) {
  if (item.isSuperclass) {
    return '';
  }

  const linkedClasses = sourceLinkValues(item.code);
  const sourceClasses = availableSourceClasses();
  const selectableClassCount = state.cmdbClasses.filter((classItem) => classItem.prototype !== true).length;
  const hiddenClassCount = Math.max(0, selectableClassCount - sourceClasses.length);
  const help = state.cmdbClassError
    ? state.cmdbClassError
    : `${sourceClasses.length} existing CMDB source classes available; ${hiddenClassCount} aggregation classes hidden.`;

  return `
    <div class="source-link-editor">
      <label class="text-field">
        <span>Add customer CMDB class for population link</span>
        <input
          data-source-link-input
          list="cmdbClassOptions"
          placeholder="Select existing CMDB class"
          autocomplete="off">
      </label>
      <button type="button" class="secondary-button" data-add-source-link data-managed-class="${escapeHtml(item.code)}">Add link</button>
      <span>${escapeHtml(help)}</span>
      <div class="source-link-list">
        ${linkedClasses.length === 0
          ? '<span>No customer classes linked.</span>'
          : linkedClasses.map((customerClassCode) => `
            <span class="source-link-chip">
              <strong>${escapeHtml(customerClassCode)}</strong>
              <button type="button" class="icon-button" data-remove-source-link data-managed-class="${escapeHtml(item.code)}" data-customer-class="${escapeHtml(customerClassCode)}" title="Remove source link">×</button>
            </span>
          `).join('')}
      </div>
    </div>
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
  const byCode = new Map(hierarchyClasses.map((item) => [item.code, item]));
  const childrenByParent = new Map();
  const roots = [];

  for (const item of hierarchyClasses) {
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
  setSourceLamp('#conversionLoadedLamp', {
    loaded: Boolean(state.conversionConfigCacheUpdatedAt),
    loading: state.syncingConversionConfigs || state.loadingConversionConfigCache,
    error: Boolean(state.syncConversionConfigError),
    loadedText: 'Конвертация загружена',
    loadingText: 'Конвертация загружается',
    errorText: 'Конвертация ошибка',
    emptyText: 'Конвертация не загружена',
    updatedAt: state.conversionConfigCacheUpdatedAt
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

function renderConversionConfigSyncView() {
  renderTopSourceStatus();
  const serviceRuleCount = document.querySelector('#conversionServiceRuleCount');
  const suppressionRuleCount = document.querySelector('#conversionSuppressionRuleCount');
  const traversalDepth = document.querySelector('#conversionTraversalDepth');
  const updatedAt = document.querySelector('#conversionConfigLastUpdatedAt');
  const status = document.querySelector('#syncConversionConfigStatus');
  const button = document.querySelector('#syncConversionConfigButton');
  const cacheButton = document.querySelector('#loadCachedConversionConfigButton');
  if (!serviceRuleCount || !suppressionRuleCount || !traversalDepth || !updatedAt || !status || !button || !cacheButton) {
    return;
  }

  serviceRuleCount.textContent = String(state.ruleExamples.service.length);
  suppressionRuleCount.textContent = String(state.ruleExamples.suppression.length);
  traversalDepth.textContent = String(state.maxTraversalDepth);
  updatedAt.textContent = formatCacheTimestamp(state.conversionConfigCacheUpdatedAt);
  button.disabled = state.syncingConversionConfigs || state.loadingConversionConfigCache;
  button.textContent = state.syncingConversionConfigs
    ? 'Синхронизация...'
    : 'Провести синхронизацию';
  cacheButton.disabled = state.syncingConversionConfigs || state.loadingConversionConfigCache;
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

  const sourceClasses = availableSourceClassSchemas();
  sourceStatus.textContent = state.cmdbClassSchemaError || `${sourceClasses.length} source classes available.`;
  sourceStatus.classList.toggle('error', Boolean(state.cmdbClassSchemaError));
  sourceList.innerHTML = renderSourceSchemaCards(sourceClasses);
  rulesList.innerHTML = renderRuleGroups(state.ruleExamples[layerKey] ?? []);
  targetList.innerHTML = renderTargetSchemaCards(layer);
}

function availableSourceClassSchemas() {
  const schemasByCode = new Map(state.cmdbClassSchemas.map((item) => [item.code, item]));
  return availableSourceClasses().map((item) => {
    const schema = schemasByCode.get(item.code) ?? item;
    return {
      ...schema,
      hierarchyLabel: item.hierarchyLabel,
      attributes: schema.attributes ?? []
    };
  });
}

function renderSourceSchemaCards(classes) {
  if (classes.length === 0) {
    return '<div class="empty-state">No source classes to show.</div>';
  }

  return classes.map((item) => `
    <details class="preview-card">
      <summary>
        <strong>${escapeHtml(item.description || item.name || item.code)}</strong>
        <span>${escapeHtml(item.hierarchyLabel || item.code)}</span>
      </summary>
      ${item.parent ? `<p class="preview-meta">inherits ${escapeHtml(item.parent)}</p>` : ''}
      ${renderPreviewAttributes(item.attributes ?? [])}
    </details>
  `).join('');
}

function renderPreviewAttributes(attributes) {
  if (attributes.length === 0) {
    return '<div class="empty-state">No attributes loaded.</div>';
  }

  return `
    <div class="preview-attribute-list">
      ${attributes.map((attribute) => `
        <div class="preview-attribute-row">
          <strong>${escapeHtml(attribute.description || attribute.displayName || attribute.name || attribute.code)}</strong>
          <span>${escapeHtml(attribute.code)} · ${escapeHtml(formatCatalogAttributeType(attribute))}${attribute.required ? ' · required' : ''}</span>
        </div>
      `).join('')}
    </div>
  `;
}

function formatCatalogAttributeType(attribute) {
  return attribute.lookupTypeCode
    ? `${attribute.type}: ${attribute.lookupTypeCode}`
    : (attribute.type || 'unknown');
}

function renderRuleGroups(rules) {
  if (rules.length === 0) {
    return '<div class="empty-state">No conversion rules to show.</div>';
  }

  const descriptions = classDescriptionsByCode();
  const groups = new Map();
  for (const rule of rules) {
    const sourceCode = ruleSourceClassCode(rule);
    const groupKey = (descriptions.get(sourceCode) ?? sourceCode) || 'Unknown source class';
    const items = groups.get(groupKey) ?? [];
    items.push(rule);
    groups.set(groupKey, items);
  }

  return [...groups.entries()]
    .sort(([left], [right]) => left.localeCompare(right, undefined, { sensitivity: 'base' }))
    .map(([description, items]) => `
      <section class="preview-card rule-group">
        <h3>${escapeHtml(description)}</h3>
        ${items.map((rule) => renderRuleSummary(rule, descriptions)).join('')}
      </section>
    `).join('');
}

function renderRuleSummary(rule, descriptions) {
  const sourceCode = ruleSourceClassCode(rule);
  const targetCode = ruleTargetClassCode(rule);
  const filterText = ruleFilterDescriptions(rule).join('; ');
  const mappingCount = Object.keys(rule.target?.attribute_mappings ?? {}).length;
  const userAttributeCount = ruleTargetUserAttributes(rule).length;
  const ruleKind = rule.generated_from_template
    ? `generated from ${rule.generated_from_template}`
    : 'binding rule';
  return `
    <div class="rule-summary">
      <span class="structure-mark">${escapeHtml(ruleKind)}</span>
      <strong>${escapeHtml(sourceCode)} -> ${escapeHtml(targetCode)}</strong>
      <span>${escapeHtml(descriptions.get(targetCode) ?? targetCode)}</span>
      <span>filter ${escapeHtml(filterText || 'none')}</span>
      <span>priority ${escapeHtml(rule.priority ?? 100)} · create target instance · key ${escapeHtml(rule.source?.key_attribute ?? rule.when?.fieldExists ?? '')} · idempotency ${escapeHtml(rule.target?.idempotency_key ?? '')} · mappings ${mappingCount} · user attr ${userAttributeCount}</span>
    </div>
  `;
}

function ruleSourceClassCode(rule) {
  return String(rule?.source?.class_code || classCodeFromWhen(rule?.when) || '').trim();
}

function ruleTargetClassCode(rule) {
  return String(rule?.target?.class_code || '').trim();
}

function ruleFilterDescriptions(rule) {
  const sourceFilters = (rule?.source?.filters ?? [])
    .map((filter) => `${filter.attribute} ${filter.operator} ${filter.value}`);
  const regexFilters = [
    ...(rule?.when?.allRegex ?? []),
    ...(rule?.when?.anyRegex ?? [])
  ]
    .filter((matcher) => !['classname', 'eventtype'].includes(canonicalToken(matcher.field)))
    .map((matcher) => `${matcher.field} matches ${matcher.pattern}`);
  return sourceFilters.concat(regexFilters);
}

function ruleTargetUserAttributes(rule) {
  const allowedAttributes = allowedUserResponsibilityAttributes(rule?.layer);
  if (allowedAttributes.length > 0) {
    return allowedAttributes;
  }

  return Array.isArray(rule?.target?.user_responsibility_attributes)
    ? rule.target.user_responsibility_attributes
    : [];
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
    config.filterField,
    config.filterRegex,
    config.priority,
    config.targetClass
  ].forEach((element) => {
    element.disabled = editDisabled;
  });

  config.applyButton.textContent = action === 'delete' ? 'Удалить правило' : 'Применить';
  renderRuleSourceFieldOptions(layerKey);
  renderRuleTargetFieldOptions(layerKey);
  renderRuleAttributeList(layerKey);
  config.attributeList.querySelectorAll('input').forEach((element) => {
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
    renderRuleAttributeRowTypes(layerKey);
    return;
  }

  if (target.matches('[data-rule-target-class]')) {
    renderRuleTargetFieldOptions(layerKey);
    renderRuleAttributeList(layerKey);
  }
}

function applyRuleEditorChange(layerKey) {
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

    const rule = buildBindingRule(layerKey, values, existingRule);
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
  const filterField = config.filterField.value.trim();
  const filterRegex = config.filterRegex.value.trim();
  const targetClass = config.targetClass.value.trim();
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

  const attributes = readRuleAttributeRows(layerKey);
  const mappings = attributes.mappings;
  const userAttributes = attributes.userAttributes;

  const mappedTargets = new Set(Object.keys(mappings).map(canonicalToken));
  const overlap = userAttributes.find((attribute) => mappedTargets.has(canonicalToken(attribute)));
  if (overlap) {
    throw new Error(`Атрибут ${overlap} уже заполняется маппингом и не может быть в зоне ответственности пользователя.`);
  }

  const keyField = keyFieldFromMappings(mappings);
  if (!keyField) {
    throw new Error(`Добавьте маппинг ${POPULATION_SOURCE_KEY_ATTRIBUTE} <- source attribute: этот ключ записывается в целевой объект и используется для поиска без дублей.`);
  }

  return {
    sourceClass,
    keyField,
    filterField,
    filterRegex,
    targetClass,
    priority,
    name,
    mappings,
    userAttributes
  };
}

function buildBindingRule(layerKey, values, existingRule = null) {
  const fallbackName = [
    layerKey,
    values.sourceClass,
    values.targetClass,
    values.keyField
  ].join('-');
  const name = values.name || existingRule?.name || existingRule?.rule_id || normalizeRuleId(fallbackName);
  const ruleId = existingRule?.rule_id || normalizeRuleId(name);
  const allRegex = [
    {
      field: 'className',
      pattern: `(?i)^${escapeRegex(values.sourceClass)}$`
    }
  ];

  if (values.filterField && values.filterRegex) {
    allRegex.push({
      field: values.filterField,
      pattern: values.filterRegex
    });
  }

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
      fieldExists: values.keyField
    },
    target: {
      class_code: values.targetClass,
      create_instance: true,
      idempotency_key: `\${source.${values.keyField}}`,
      attribute_mappings: values.mappings,
      user_responsibility_attributes: values.userAttributes
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
  const fields = new Set([values.keyField, values.filterField]);
  for (const field of sourceFieldsFromMappings(values.mappings)) {
    fields.add(field);
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

  const filter = primaryRuleFilter(rule);
  config.name.value = rule.name || rule.rule_id || '';
  config.sourceClass.value = ruleSourceClassCode(rule);
  config.filterField.value = filter.field;
  config.filterRegex.value = filter.pattern;
  config.priority.value = String(rule.priority ?? 100);
  config.targetClass.value = ruleTargetClassCode(rule);
  state.ruleEditorMappings[layerKey] = { ...(rule.target?.attribute_mappings ?? {}) };
  state.ruleEditorUserAttributes[layerKey] = [...ruleTargetUserAttributes(rule)];
  renderRuleSourceFieldOptions(layerKey);
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
  config.filterField.value = '';
  config.filterRegex.value = '';
  config.priority.value = '';
  config.targetClass.value = '';
  state.ruleEditorMappings[layerKey] = {};
  state.ruleEditorUserAttributes[layerKey] = allowedUserResponsibilityAttributes(layerKey);
  applyRuleEditorSuggestions(layerKey, suggestion);
  renderRuleAttributeList(layerKey);
}

function applyRuleEditorSuggestions(layerKey, rule) {
  const config = ruleEditorConfig(layerKey);
  const filter = rule ? primaryRuleFilter(rule) : { field: '', pattern: '' };
  config.name.placeholder = rule?.name || 'Название правила';
  config.sourceClass.placeholder = ruleSourceClassCode(rule) || 'Класс заказчика';
  config.filterField.placeholder = filter.field || 'Атрибут фильтра';
  config.filterRegex.placeholder = filter.pattern || '(?i)^active$';
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

function primaryRuleFilter(rule) {
  const sourceFilter = rule?.source?.filters?.[0];
  if (sourceFilter?.attribute) {
    return {
      field: sourceFilter.attribute,
      pattern: sourceFilter.operator === 'equals'
        ? `(?i)^${escapeRegex(String(sourceFilter.value))}$`
        : String(sourceFilter.value ?? '')
    };
  }

  const keyField = canonicalToken(rule?.source?.key_attribute || rule?.when?.fieldExists || '');
  const matcher = [
    ...(rule?.when?.allRegex ?? []),
    ...(rule?.when?.anyRegex ?? [])
  ].find((item) => {
    const field = canonicalToken(item.field);
    return field !== 'classname' && field !== 'eventtype' && field !== keyField;
  });

  return {
    field: matcher?.field || '',
    pattern: matcher?.pattern || ''
  };
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

function renderRuleTargetFieldOptions(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.targetFieldOptions) {
    return;
  }

  const attributes = targetClassAttributes(config.targetClass.value.trim());
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

  const rows = ruleAttributeRowsFromState(layerKey);
  rows.push({ sourceAttribute: '', targetAttribute: '' });
  config.attributeList.innerHTML = `
    <div class="rule-attribute-header" role="row">
      <span role="columnheader">target</span>
      <span role="columnheader">source</span>
      <span role="columnheader">Тип и контроль</span>
    </div>
    ${rows.map((row) => ruleAttributeRowTemplate(layerKey, row)).join('')}
  `;
}

function ruleAttributeRowsFromState(layerKey) {
  const allowedUserAttributes = allowedUserResponsibilityAttributes(layerKey);
  const allowedUserTokens = new Set(allowedUserAttributes.map(canonicalToken));
  const userAttributes = new Map(allowedUserAttributes.map((attribute) => [canonicalToken(attribute), attribute]));
  const rows = [];

  for (const [targetAttribute, sourceExpression] of Object.entries(state.ruleEditorMappings[layerKey] ?? {})) {
    if (allowedUserTokens.has(canonicalToken(targetAttribute))) {
      continue;
    }

    rows.push({
      sourceAttribute: sourceAttributeInputValue(sourceExpression),
      targetAttribute
    });
  }

  for (const targetAttribute of state.ruleEditorUserAttributes[layerKey] ?? []) {
    const allowedAttribute = allowedUserAttributeCode(layerKey, targetAttribute);
    if (allowedAttribute) {
      userAttributes.set(canonicalToken(allowedAttribute), allowedAttribute);
    }
  }

  for (const targetAttribute of userAttributes.values()) {
    rows.push({
      sourceAttribute: '',
      targetAttribute
    });
  }

  return rows.sort((left, right) =>
    String(left.targetAttribute).localeCompare(String(right.targetAttribute), undefined, { sensitivity: 'base' }));
}

function ruleAttributeRowTemplate(layerKey, row) {
  const prefix = layerKey === 'service' ? 'serviceRule' : 'suppressionRule';
  return `
    <div class="rule-attribute-row" data-rule-attribute-row>
      <label class="text-field rule-attribute-cell">
        <input data-rule-attribute-target list="${prefix}TargetFieldOptions" value="${escapeHtml(row.targetAttribute)}" aria-label="target" autocomplete="off">
      </label>
      <label class="text-field rule-attribute-cell">
        <input data-rule-attribute-source list="${prefix}SourceFieldOptions" value="${escapeHtml(row.sourceAttribute)}" aria-label="source" autocomplete="off">
      </label>
      <div class="rule-attribute-type" data-rule-attribute-type>
        ${ruleAttributeTypeTemplate(layerKey, row)}
      </div>
    </div>
  `;
}

function renderRuleAttributeRowTypes(layerKey) {
  const config = ruleEditorConfig(layerKey);
  config.attributeList?.querySelectorAll('[data-rule-attribute-row]').forEach((row) => {
    renderRuleAttributeRowType(layerKey, row);
  });
}

function renderRuleAttributeRowType(layerKey, row) {
  if (!row) {
    return;
  }

  const target = row.querySelector('[data-rule-attribute-type]');
  if (!target) {
    return;
  }

  target.innerHTML = ruleAttributeTypeTemplate(layerKey, ruleAttributeDomRowValues(row));
}

function ruleAttributeTypeTemplate(layerKey, row) {
  const checks = ruleAttributeChecks(layerKey, row);
  if (checks.length === 0) {
    return '<span class="rule-attribute-check muted">Новая строка</span>';
  }

  return checks.map((check) => `
    <span class="rule-attribute-check ${escapeHtml(check.level || 'info')}">${escapeHtml(check.text)}</span>
  `).join('');
}

function ruleAttributeChecks(layerKey, row) {
  const checks = [];
  const targetAttribute = String(row.targetAttribute ?? '').trim();
  const sourceAttribute = String(row.sourceAttribute ?? '').trim();
  if (!targetAttribute && !sourceAttribute) {
    return checks;
  }

  const targetAttributeInfo = ruleTargetAttributeInfo(layerKey, targetAttribute);
  const sourceFieldInfo = ruleSourceFieldInfo(layerKey, sourceAttribute);
  const targetType = targetAttributeInfo ? formatRuleAttributeCatalogType(targetAttributeInfo) : '';
  const sourceType = sourceFieldInfo ? formatRuleSourceFieldType(sourceFieldInfo) : '';

  if (targetAttribute) {
    if (targetAttributeInfo) {
      checks.push({
        level: 'ok',
        text: sourceFieldInfo ? `${targetType} <- ${sourceType}` : targetType
      });
      if (targetAttributeInfo.required) {
        checks.push({ level: 'warn', text: 'required' });
      }
      if (targetAttributeInfo.validationRules) {
        checks.push({ level: 'warn', text: 'JS validation' });
      }
    } else {
      checks.push({ level: 'error', text: `Атрибут ${targetAttribute} не найден в целевом классе` });
    }
  } else if (sourceAttribute) {
    checks.push({ level: 'error', text: 'Укажите target' });
  }

  if (sourceAttribute) {
    if (sourceFieldInfo) {
      const compatibility = ruleAttributeTypeCompatibility(targetAttributeInfo, sourceFieldInfo);
      if (compatibility) {
        checks.push(compatibility);
      }
    } else if (isComplexSourceExpression(sourceAttribute)) {
      checks.push({ level: 'warn', text: 'выражение без проверки типа' });
    } else {
      checks.push({ level: 'error', text: `Атрибут ${sourceAttribute} не найден в классе-источнике` });
    }
  } else if (targetAttributeInfo) {
    checks.push({
      level: allowedUserAttributeCode(layerKey, targetAttribute) ? 'ok' : 'warn',
      text: allowedUserAttributeCode(layerKey, targetAttribute) ? 'заполняет пользователь' : 'требуется source'
    });
  }

  return checks;
}

function ruleAttributeTypeCompatibility(targetAttribute, sourceField) {
  if (!targetAttribute || !sourceField) {
    return null;
  }

  const targetKind = normalizedRuleFieldKind(targetAttribute);
  const sourceKind = normalizedRuleFieldKind(sourceField.fieldRule ?? {});
  const targetLookup = lookupTypeCode(targetAttribute);
  const sourceLookup = sourceField.fieldRule?.lookupType || sourceField.fieldRule?.resolve?.lookupType || '';

  if (targetKind === 'lookup' && sourceKind === 'lookup' && targetLookup && sourceLookup
    && canonicalToken(targetLookup) !== canonicalToken(sourceLookup)) {
    return { level: 'warn', text: `lookup type проверить: ${targetLookup} <- ${sourceLookup}` };
  }

  if (targetKind === 'boolean' && !['boolean', 'string', 'unknown'].includes(sourceKind)) {
    return { level: 'warn', text: `bool <- ${sourceKind}: проверить преобразование` };
  }

  if (['integer', 'decimal', 'double', 'number'].includes(targetKind)
    && !['integer', 'decimal', 'double', 'number', 'string', 'unknown'].includes(sourceKind)) {
    return { level: 'warn', text: `${targetKind} <- ${sourceKind}: проверить преобразование` };
  }

  return { level: 'ok', text: 'тип проверен' };
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

function ruleTargetAttributeInfo(layerKey, attributeCodeValue) {
  const code = String(attributeCodeValue ?? '').trim();
  if (!code) {
    return null;
  }

  const config = ruleEditorConfig(layerKey);
  return targetClassAttributes(config.targetClass.value.trim()).find((attribute) =>
    canonicalToken(attributeCode(attribute)) === canonicalToken(code)) ?? null;
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

function isComplexSourceExpression(value) {
  const text = String(value ?? '').trim();
  return Boolean(text.startsWith('${') && !sourceAttributeFromDirectExpression(text));
}

function formatRuleAttributeCatalogType(attribute) {
  const type = formatCatalogAttributeType(attribute);
  return attribute?.validationRules ? `${type} · JS validation` : type;
}

function formatRuleSourceFieldType(option) {
  const fieldRule = option?.fieldRule ?? {};
  const type = fieldRule.lookupType
    ? `${fieldRule.type || 'lookup'}: ${fieldRule.lookupType}`
    : (fieldRule.type || 'unknown');
  return fieldRule.resolve?.mode && fieldRule.resolve.mode !== 'none'
    ? `${type} · ${fieldRule.resolve.mode}`
    : type;
}

function sourceAttributeInputValue(expression) {
  return sourceAttributeFromDirectExpression(expression) || String(expression ?? '');
}

function ensureRuleAttributeDraftRow(layerKey) {
  const config = ruleEditorConfig(layerKey);
  if (!config.attributeList) {
    return;
  }

  const rows = [...config.attributeList.querySelectorAll('[data-rule-attribute-row]')];
  const lastRow = rows.at(-1);
  if (!lastRow || ruleAttributeDomRowValues(lastRow).sourceAttribute || ruleAttributeDomRowValues(lastRow).targetAttribute) {
    config.attributeList.insertAdjacentHTML('beforeend', ruleAttributeRowTemplate(layerKey, {
      sourceAttribute: '',
      targetAttribute: ''
    }));
  }
}

function readRuleAttributeRows(layerKey) {
  const config = ruleEditorConfig(layerKey);
  const mappings = {};
  const userAttributes = new Set(allowedUserResponsibilityAttributes(layerKey));
  const seenTargets = new Set();

  for (const row of config.attributeList.querySelectorAll('[data-rule-attribute-row]')) {
    const { sourceAttribute, targetAttribute } = ruleAttributeDomRowValues(row);
    if (!sourceAttribute && !targetAttribute) {
      continue;
    }

    if (sourceAttribute && !targetAttribute) {
      throw new Error('Для заполненного source укажите target.');
    }

    const targetAttributeInfo = ruleTargetAttributeInfo(layerKey, targetAttribute);
    if (!targetAttributeInfo) {
      throw new Error(`Атрибут target ${targetAttribute} не найден в выбранном целевом классе.`);
    }

    const normalizedTargetAttribute = attributeCode(targetAttributeInfo);
    const targetKey = canonicalToken(normalizedTargetAttribute);
    if (seenTargets.has(targetKey)) {
      throw new Error(`Атрибут target ${normalizedTargetAttribute} указан несколько раз.`);
    }

    seenTargets.add(targetKey);
    const allowedUserAttribute = allowedUserAttributeCode(layerKey, normalizedTargetAttribute);
    if (sourceAttribute) {
      if (allowedUserAttribute) {
        throw new Error(`Атрибут ${allowedUserAttribute} находится в зоне ответственности пользователя и не должен заполняться из source.`);
      }

      const sourceLookupValue = sourceFieldLookupValue(sourceAttribute);
      if (sourceLookupValue && !ruleSourceFieldInfo(layerKey, sourceAttribute)) {
        throw new Error(`Атрибут source ${sourceLookupValue} не найден в выбранном классе-источнике.`);
      }

      mappings[normalizedTargetAttribute] = sourceAttributeToExpression(sourceAttribute);
    } else {
      if (!allowedUserAttribute) {
        throw new Error(`Атрибут ${normalizedTargetAttribute} нельзя отдать пользователю. Разрешены: ${allowedUserResponsibilityAttributes(layerKey).join(', ')}.`);
      }
      userAttributes.add(allowedUserAttribute);
    }
  }

  state.ruleEditorMappings[layerKey] = mappings;
  state.ruleEditorUserAttributes[layerKey] = [...userAttributes];
  return { mappings, userAttributes: [...userAttributes] };
}

function ruleAttributeDomRowValues(row) {
  return {
    sourceAttribute: row.querySelector('[data-rule-attribute-source]')?.value.trim() ?? '',
    targetAttribute: row.querySelector('[data-rule-attribute-target]')?.value.trim() ?? ''
  };
}

function sourceAttributeToExpression(sourceAttribute) {
  const value = String(sourceAttribute ?? '').trim();
  return value.startsWith('${') ? value : `\${source.${value}}`;
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

function allowedUserAttributeCode(layerKey, attribute) {
  const token = canonicalToken(attribute);
  return allowedUserResponsibilityAttributes(layerKey).find((item) => canonicalToken(item) === token) || '';
}

function handleTemplateEditorChange(layerKey, target) {
  if (target.matches('[data-template-select]')) {
    state.templateEditorSelected[layerKey] = target.value;
    loadSelectedTemplateIntoEditor(layerKey);
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
  config.filterField.value = template.filter?.field || '';
  config.filterRegex.value = template.filter?.regex || '';
  config.priority.value = String(template.priority ?? 100);
  setSelectOptions(config.targetClass, targetClassOptions(layerKey), template.target?.class_code || '');
  config.targetName.value = template.target?.name_template || '';
  config.targetDescription.value = template.target?.description_template || '';
  config.sourceKey.value = template.target?.population_source_key_template || '';
  renderTemplateVariableList(layerKey, template.variables ?? []);
  setTemplateEditorStatus(layerKey, '');
}

function resetTemplateEditorForCreate(layerKey) {
  const config = templateEditorConfig(layerKey);
  config.select.value = '';
  config.id.value = '';
  config.name.value = '';
  config.sourceRegex.value = '';
  config.filterField.value = '';
  config.filterRegex.value = '';
  config.priority.value = '';
  setSelectOptions(config.targetClass, targetClassOptions(layerKey), '');
  config.targetName.value = '';
  config.targetDescription.value = '';
  config.sourceKey.value = '';
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
    filter: {
      field: config.filterField.value.trim(),
      regex: config.filterRegex.value.trim()
    },
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
    const index = document.templates.findIndex((item) => item.template_id === template.template_id);
    if (index >= 0) {
      document.templates[index] = template;
    } else {
      document.templates.push(template);
    }

    document.templates.sort((left, right) =>
      String(left.name || left.template_id).localeCompare(String(right.name || right.template_id), undefined, { sensitivity: 'base' }));
    state.templateDocuments[layerKey] = document;
    state.templateEditorSelected[layerKey] = template.template_id;
    setTemplateEditorStatus(layerKey, `Сохранен шаблон ${template.name}.`);
    renderTemplateEditor(layerKey);
    renderTemplateApplyView();
    renderTemplateAuditView();
    renderConversionConfigSyncView();
  } catch (error) {
    setTemplateEditorStatus(layerKey, error.message, 'error');
  }
}

function deleteTemplateEditorSelection(layerKey) {
  const selectedId = state.templateEditorSelected[layerKey];
  if (!selectedId) {
    setTemplateEditorStatus(layerKey, 'Выберите шаблон для удаления.', 'error');
    return;
  }

  const document = normalizeTemplateDocument(state.templateDocuments[layerKey], layerKey);
  document.templates = document.templates.filter((template) => template.template_id !== selectedId);
  state.templateDocuments[layerKey] = document;
  state.templateEditorSelected[layerKey] = '';
  resetTemplateEditorForCreate(layerKey);
  setTemplateEditorStatus(layerKey, `Удален шаблон ${selectedId}.`);
  renderTemplateEditor(layerKey);
  renderTemplateApplyView();
  renderTemplateAuditView();
  renderConversionConfigSyncView();
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
  const filter = primaryRuleFilter(rule);
  return {
    keyField: rule.source?.key_attribute || rule.when?.fieldExists || '',
    filterField: filter.field,
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
      <span>candidates ${escapeHtml(audit.candidates)} · missing ${escapeHtml(audit.missingRules)} · stale ${escapeHtml(audit.staleRules)}</span>
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

  return {
    templates: plan.templates.length,
    candidates: plan.candidateCount,
    generatedRules: generatedRuleIds.size,
    missingRules: [...expectedRuleIds].filter((ruleId) => !generatedRuleIds.has(ruleId)).length,
    staleRules: staleRules.length,
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
  const keyField = firstSourceAttributeFromTemplate(keyExpression)
    || firstSourceAttributeFromTemplate(template.target?.population_source_key_template)
    || template.filter?.field
    || 'id';
  const ruleName = renderTemplateString(template.name || template.template_id, context);
  const ruleId = normalizeRuleId(`${template.template_id}-${candidate.code}`);
  const allRegex = [
    {
      field: 'className',
      pattern: `(?i)^${escapeRegex(candidate.code)}$`
    }
  ];
  if (template.filter?.field && template.filter?.regex) {
    allRegex.push({
      field: template.filter.field,
      pattern: template.filter.regex
    });
  }

  return normalizeBindingRuleTarget({
    rule_id: ruleId,
    name: ruleName,
    layer: layerKey,
    priority: template.priority ?? 100,
    generated_from_template: template.template_id,
    template_version: template.version ?? 1,
    source: {
      class_code: candidate.code,
      key_attribute: keyField
    },
    when: {
      allRegex,
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
      user_responsibility_attributes: allowedUserResponsibilityAttributes(layerKey)
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
  return String(template ?? '')
    .replace(/\$\{class\.([A-Za-z_][A-Za-z0-9_]*)\}/g, (_, key) => String(context.class?.[key] ?? ''))
    .replace(/\$\{vars\.([A-Za-z_][A-Za-z0-9_]*)\}/g, (_, key) => String(context.vars?.[key] ?? ''));
}

function firstSourceAttributeFromTemplate(template) {
  const match = String(template ?? '').match(/\$\{source\.([A-Za-z_][A-Za-z0-9_]*)\}/);
  return match?.[1] || '';
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
  normalized.filter = normalized.filter && typeof normalized.filter === 'object' && !Array.isArray(normalized.filter)
    ? normalized.filter
    : {};
  normalized.priority = Number(normalized.priority || 100);
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
    filterField: document.querySelector(`#${prefix}FilterField`),
    filterRegex: document.querySelector(`#${prefix}FilterRegex`),
    priority: document.querySelector(`#${prefix}Priority`),
    targetClass: document.querySelector(`#${prefix}TargetClass`),
    targetName: document.querySelector(`#${prefix}TargetName`),
    targetDescription: document.querySelector(`#${prefix}TargetDescription`),
    sourceKey: document.querySelector(`#${prefix}SourceKey`),
    variableList: document.querySelector(`#${prefix}VariableList`),
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

  return [...targets.values()].sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'base' }));
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
  normalized.target.create_instance = true;
  normalized.target.attribute_mappings = normalized.target.attribute_mappings
    && typeof normalized.target.attribute_mappings === 'object'
    && !Array.isArray(normalized.target.attribute_mappings)
    ? normalized.target.attribute_mappings
    : {};

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
    layer: layerKey,
    source: {
      entityClasses: [],
      fields: {}
    },
    runtimePolicy: defaultRuleRuntimePolicy(),
    rules: []
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
  const classes = state.classes
    .filter((item) => item.layer === layer && !item.isSuperclass && item.origin !== 'model_root_superclass')
    .sort(compareSchemaClasses);
  if (classes.length === 0) {
    return [{ value: '', label: 'Целевые классы не загружены', disabled: true }];
  }

  return [
    {
      value: '',
      label: suggestedCode
        ? `Выберите целевой класс\\экземпляр класса, например ${suggestedCode}`
        : 'Выберите целевой класс\\экземпляр класса'
    },
    ...classes.map((item) => ({
      value: item.code,
      label: `${item.displayName || item.code} (${item.code})`
    }))
  ];
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
    filterField: document.querySelector(`#${prefix}FilterField`),
    filterRegex: document.querySelector(`#${prefix}FilterRegex`),
    priority: document.querySelector(`#${prefix}Priority`),
    targetClass: document.querySelector(`#${prefix}TargetClass`),
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

function renderTargetSchemaCards(layer) {
  const classes = state.classes.filter((item) =>
    item.layer === layer || item.origin === 'model_root_superclass');
  if (classes.length === 0) {
    return '<div class="empty-state">No target classes to show.</div>';
  }

  const targetDomains = state.domains
    .concat(state.suggestedDomains)
    .filter((domain) => domain.layer === layer);
  const domainsByClass = domainsBySource(targetDomains);
  return renderPreviewClassTree(classes, domainsByClass);
}

function renderPreviewClassTree(classes, domainsByClass) {
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
  const roots = classes
    .filter((item) => !item.parentClassCode || !classCodes.has(item.parentClassCode))
    .sort(compareSchemaClasses);

  return roots.map((item) => renderPreviewClassCard(item, childrenByParent, domainsByClass)).join('');
}

function renderPreviewClassCard(item, childrenByParent, domainsByClass) {
  const children = (childrenByParent.get(item.code) ?? []).sort(compareSchemaClasses);
  const domains = domainsByClass.get(item.code) ?? [];
  return `
    <details class="preview-card" ${item.isSuperclass ? 'open' : ''}>
      <summary>
        <strong>${escapeHtml(item.displayName || item.code)}</strong>
        <span>${escapeHtml(item.code)}</span>
      </summary>
      ${item.parentClassCode ? `<p class="preview-meta">inherits ${escapeHtml(item.parentClassCode)}</p>` : ''}
      ${renderPreviewAttributes(item.attributes ?? [])}
      ${domains.length > 0 ? `
        <div class="preview-domain-list">
          ${domains.map((domain) => `
            <div class="preview-domain-row">
              <strong>${escapeHtml(domain.displayName || domain.code)}</strong>
              <span>${escapeHtml(domain.sourceClassCode)} -> ${escapeHtml(domain.targetClassCode)}</span>
            </div>
          `).join('')}
        </div>
      ` : ''}
      ${children.length > 0 ? `<div class="preview-child-list">${children.map((child) => renderPreviewClassCard(child, childrenByParent, domainsByClass)).join('')}</div>` : ''}
    </details>
  `;
}

function compareSchemaClasses(left, right) {
  return String(left.displayName || left.code).localeCompare(String(right.displayName || right.code), undefined, {
    sensitivity: 'base'
  }) || left.code.localeCompare(right.code, undefined, { sensitivity: 'base' });
}

function addSourceLink(managedClassCode, customerClassCode) {
  const managed = String(managedClassCode ?? '').trim();
  const customer = String(customerClassCode ?? '').trim();
  if (!managed || !customer) {
    return false;
  }

  const parentByCode = new Map(state.cmdbClasses.map((item) => [item.code, item.parent || '']));
  if (isAggregationClassCode(customer, parentByCode, aggregationClassCodes())) {
    state.error = `Class ${customer} is part of the monitoring aggregation model and cannot be used as a customer source class.`;
    return false;
  }

  const exists = state.sourceLinks.some((link) =>
    link.managedClassCode === managed && link.customerClassCode === customer);
  if (exists) {
    return false;
  }

  state.sourceLinks.push({
    managedClassCode: managed,
    customerClassCode: customer
  });
  state.error = '';
  return true;
}

function removeSourceLink(managedClassCode, customerClassCode) {
  const managed = String(managedClassCode ?? '').trim();
  const customer = String(customerClassCode ?? '').trim();
  const before = state.sourceLinks.length;
  state.sourceLinks = state.sourceLinks.filter((link) =>
    link.managedClassCode !== managed || link.customerClassCode !== customer);
  return state.sourceLinks.length !== before;
}

function sourceLinkValues(managedClassCode) {
  return state.sourceLinks
    .filter((link) => link.managedClassCode === managedClassCode)
    .map((link) => link.customerClassCode)
    .sort((left, right) => left.localeCompare(right));
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

function seedRules() {
  const serviceSourceClass = 'CustomerWorkplace';
  const suppressionSourceClass = 'CustomerSubnet';
  const serviceBindingRule = {
    rule_id: 'service-workplace-group-by-branch',
    name: 'Bind active workplace branch to service group',
    layer: 'service',
    priority: 100,
    source: {
      class_code: serviceSourceClass,
      key_attribute: 'branch_code'
    },
    when: {
      allRegex: [
        {
          field: 'className',
          pattern: `(?i)^${serviceSourceClass}$`
        },
        {
          field: 'status',
          pattern: '(?i)^active$'
        }
      ],
      fieldExists: 'branch_code'
    },
    target: {
      class_code: `${state.prefix}ServiceWorkplaceGroup`,
      create_instance: true,
      idempotency_key: '${source.branch_code}',
      attribute_mappings: {
        name: '${source.branch_name}',
        population_source_key: '${source.branch_code}',
        is_active: '${source.is_active}'
      },
      user_responsibility_attributes: allowedUserResponsibilityAttributes('service')
    }
  };

  const suppressionBindingRule = {
    rule_id: 'suppression-network-zone-by-subnet',
    name: 'Bind monitored subnet to suppression network zone',
    layer: 'suppression',
    priority: 100,
    source: {
      class_code: suppressionSourceClass,
      key_attribute: 'subnet_id'
    },
    when: {
      allRegex: [
        {
          field: 'className',
          pattern: `(?i)^${suppressionSourceClass}$`
        },
        {
          field: 'monitoring_enabled',
          pattern: '(?i)^true$'
        }
      ],
      fieldExists: 'subnet_id'
    },
    target: {
      class_code: `${state.prefix}SuppressionNetworkAccessZone`,
      create_instance: true,
      idempotency_key: '${source.subnet_id}',
      attribute_mappings: {
        name: '${source.name}',
        population_source_key: '${source.subnet_id}',
        is_active: '${source.is_active}'
      },
      user_responsibility_attributes: allowedUserResponsibilityAttributes('suppression')
    }
  };

  const serviceDocument = {
    layer: 'service',
    source: {
      entityClasses: [serviceSourceClass],
      fields: {
        branch_code: sourceFieldDefinition(serviceSourceClass, 'branch_code'),
        branch_name: sourceFieldDefinition(serviceSourceClass, 'branch_name'),
        status: sourceFieldDefinition(serviceSourceClass, 'status'),
        is_active: sourceFieldDefinition(serviceSourceClass, 'is_active')
      }
    },
    runtimePolicy: defaultRuleRuntimePolicy(),
    rules: [serviceBindingRule]
  };

  const suppressionDocument = {
    layer: 'suppression',
    source: {
      entityClasses: [suppressionSourceClass],
      fields: {
        subnet_id: sourceFieldDefinition(suppressionSourceClass, 'subnet_id'),
        name: sourceFieldDefinition(suppressionSourceClass, 'name'),
        monitoring_enabled: sourceFieldDefinition(suppressionSourceClass, 'monitoring_enabled'),
        is_active: sourceFieldDefinition(suppressionSourceClass, 'is_active')
      }
    },
    runtimePolicy: defaultRuleRuntimePolicy(),
    rules: [suppressionBindingRule]
  };

  writeRuleDocument('service', serviceDocument);
  writeRuleDocument('suppression', suppressionDocument);
  state.ruleEditorSuggestions.service = serviceBindingRule;
  state.ruleEditorSuggestions.suppression = suppressionBindingRule;
  resetRuleEditorForCreate('service');
  resetRuleEditorForCreate('suppression');
  renderRuleEditors();
  renderRulesPreviews();
  renderConversionConfigSyncView();
}
