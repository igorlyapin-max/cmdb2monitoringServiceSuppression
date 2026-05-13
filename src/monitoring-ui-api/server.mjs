import http from 'node:http';
import { mkdir, readFile, rename, stat, writeFile } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(root, '..', '..');
const publicRoot = path.join(root, 'public');
const baseConfig = JSON.parse(await readFile(path.join(root, 'config', 'appsettings.json'), 'utf8'));
const config = applyRuntimeServerOverrides(await resolveSecretReferences(baseConfig, 'monitoring-ui-api'));

const mimeTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8']
]);

const server = http.createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? '/', `http://${request.headers.host ?? 'localhost'}`);

    if (url.pathname === '/health') {
      return sendJson(response, 200, { service: 'monitoring-ui-api', status: 'ok' });
    }

    if (url.pathname === '/api/config') {
      return sendJson(response, 200, {
        roles: config.auth.roles,
        cmdbuildSchema: config.cmdbuildSchema,
        webhooks: publicWebhooksConfig(),
        kafka: config.kafka ?? {},
        readiness: config.readiness ?? {},
        conversionConfig: publicConversionConfig()
      });
    }

    if (url.pathname === '/api/conversion-config/storage' && request.method === 'GET') {
      return sendJson(response, 200, await readConversionConfigStorage());
    }

    if (url.pathname === '/api/conversion-config/storage' && request.method === 'PUT') {
      const body = await readJsonBody(request);
      const result = await writeConversionConfigStorage(body);
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/conversion-config/deploy' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const result = await deployConversionConfigToRuntime(body);
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/health/services' && request.method === 'GET') {
      return sendJson(response, 200, await checkConfiguredHealthServices());
    }

    const applierReloadMatch = url.pathname.match(/^\/api\/appliers\/([^/]+)\/configuration\/reload$/);
    if (applierReloadMatch && request.method === 'POST') {
      const result = await reloadApplierConfiguration(decodeURIComponent(applierReloadMatch[1]));
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/schema/preview' && request.method === 'GET') {
      const backendUrl = new URL(config.backend.schemaPreviewUrl);
      backendUrl.searchParams.set('prefix', url.searchParams.get('prefix') ?? config.cmdbuildSchema.defaultPrefix);
      backendUrl.searchParams.set('language', url.searchParams.get('language') ?? config.cmdbuildSchema.defaultLanguage);
      if (url.searchParams.has('serviceModelRoot')) {
        backendUrl.searchParams.set('serviceModelRoot', url.searchParams.get('serviceModelRoot'));
      }
      if (url.searchParams.has('suppressionModelRoot')) {
        backendUrl.searchParams.set('suppressionModelRoot', url.searchParams.get('suppressionModelRoot'));
      }
      return proxyJson(response, backendUrl);
    }

    if (url.pathname === '/api/schema/preview' && request.method === 'POST') {
      const body = await readJsonBody(request);
      return proxyJson(response, config.backend.schemaPreviewUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json'
        },
        body: JSON.stringify(body)
      });
    }

    if (url.pathname === '/api/schema/apply' && request.method === 'POST') {
      const body = await readJsonBody(request);
      return proxyJson(response, config.backend.schemaApplyUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json'
        },
        body: JSON.stringify(body)
      });
    }

    if (url.pathname === '/api/rules/apply-current' && request.method === 'POST') {
      const body = await readJsonBody(request);
      return proxyJson(response, config.backend.rulesApplyCurrentUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json'
        },
        body: JSON.stringify(body)
      });
    }

    if (url.pathname === '/api/zabbix/apply-current' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const layer = normalizeRuntimeLayer(body?.layer);
      if (layer !== 'service' && layer !== 'suppression') {
        return sendJson(response, 400, { error: 'layer must be service or suppression' });
      }

      return proxyJson(response, config.backend.rulesApplyCurrentUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json'
        },
        body: JSON.stringify({
          operationId: stringValue(body?.operationId),
          layers: [layer],
          targets: ['zabbix'],
          dryRun: Boolean(body?.dryRun),
          sourceClasses: Array.isArray(body?.sourceClasses) ? body.sourceClasses : [],
          maxCardsPerClass: Number.isInteger(body?.maxCardsPerClass) ? body.maxCardsPerClass : 0,
          eventType: stringValue(body?.eventType) || 'UPDATE'
        })
      });
    }

    const zabbixApplyProgressMatch = url.pathname.match(/^\/api\/zabbix\/apply-current\/progress\/([^/]+)$/);
    if (zabbixApplyProgressMatch && request.method === 'GET') {
      return proxyJson(response, appendPath(
        config.backend.rulesApplyCurrentUrl,
        'progress',
        decodeURIComponent(zabbixApplyProgressMatch[1])));
    }

    if (url.pathname === '/api/zabbix/apply/status' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixApplyStatusUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixApplyStatusUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/trigger-dependencies/status' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixTriggerDependenciesStatusUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixTriggerDependenciesStatusUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/trigger-dependencies/dry-run' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixTriggerDependenciesDryRunUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixTriggerDependenciesDryRunUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/zabbix/trigger-dependencies/apply' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixTriggerDependenciesApplyUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixTriggerDependenciesApplyUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/cmdbuild/classes' && request.method === 'GET') {
      const backendUrl = new URL(config.backend.cmdbuildClassesUrl);
      if (url.searchParams.has('rootPath')) {
        backendUrl.searchParams.set('rootPath', url.searchParams.get('rootPath'));
      }
      if (url.searchParams.has('prefix')) {
        backendUrl.searchParams.set('prefix', url.searchParams.get('prefix'));
      }
      if (url.searchParams.has('layer')) {
        backendUrl.searchParams.set('layer', url.searchParams.get('layer'));
      }
      if (url.searchParams.has('managedOnly')) {
        backendUrl.searchParams.set('managedOnly', url.searchParams.get('managedOnly'));
      }
      if (url.searchParams.has('includePrototypes')) {
        backendUrl.searchParams.set('includePrototypes', url.searchParams.get('includePrototypes'));
      }
      return proxyJson(response, backendUrl);
    }

    if (url.pathname === '/api/cmdbuild/classes/schema' && request.method === 'GET') {
      return proxyJson(response, config.backend.cmdbuildClassSchemasUrl);
    }

    if (url.pathname === '/api/cmdbuild/classes/instances' && request.method === 'GET') {
      const backendUrl = new URL(config.backend.cmdbuildClassInstancesUrl);
      for (const key of ['prefix', 'serviceModelRoot', 'suppressionModelRoot']) {
        if (url.searchParams.has(key)) {
          backendUrl.searchParams.set(key, url.searchParams.get(key));
        }
      }
      return proxyJson(response, backendUrl);
    }

    const cardCreateMatch = url.pathname.match(/^\/api\/cmdbuild\/classes\/([^/]+)\/cards$/);
    if (cardCreateMatch && request.method === 'GET') {
      const classCode = decodeURIComponent(cardCreateMatch[1]);
      const backendUrl = new URL(`${config.backend.cmdbuildClassesUrl}/${encodeURIComponent(classCode)}/cards`);
      for (const key of ['layer']) {
        if (url.searchParams.has(key)) {
          backendUrl.searchParams.set(key, url.searchParams.get(key));
        }
      }
      return proxyJson(response, backendUrl);
    }

    if (cardCreateMatch && request.method === 'POST') {
      const classCode = decodeURIComponent(cardCreateMatch[1]);
      const backendUrl = new URL(`${config.backend.cmdbuildClassesUrl}/${encodeURIComponent(classCode)}/cards`);
      const body = await readJsonBody(request);
      return proxyJson(response, backendUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json'
        },
        body: JSON.stringify(body)
      });
    }

    if (url.pathname === '/api/cmdbuild/domains' && request.method === 'GET') {
      const backendUrl = new URL(config.backend.cmdbuildDomainsUrl);
      if (url.searchParams.has('prefix')) {
        backendUrl.searchParams.set('prefix', url.searchParams.get('prefix'));
      }
      return proxyJson(response, backendUrl);
    }

    if (url.pathname === '/api/cmdbuild/domains/relations' && request.method === 'GET') {
      const backendUrl = appendPath(config.backend.cmdbuildDomainsUrl, 'relations');
      if (url.searchParams.has('prefix')) {
        backendUrl.searchParams.set('prefix', url.searchParams.get('prefix'));
      }
      return proxyJson(response, backendUrl);
    }

    if (url.pathname === '/api/zabbix/check' && request.method === 'GET') {
      return proxyJson(response, config.backend.zabbixCheckUrl);
    }

    if (url.pathname === '/api/webhooks/check' && request.method === 'GET') {
      const result = await checkConfiguredWebhooks();
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/webhooks/publish' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const result = await publishManagedWebhooksToCmdbuild(body);
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/kafka/topics' && request.method === 'GET') {
      return proxyJson(response, config.backend.kafkaTopicsUrl);
    }

    const kafkaEventsMatch = url.pathname.match(/^\/api\/kafka\/topics\/([^/]+)\/events$/);
    if (kafkaEventsMatch && request.method === 'GET') {
      const backendUrl = appendPath(config.backend.kafkaTopicsUrl, decodeURIComponent(kafkaEventsMatch[1]), 'events');
      backendUrl.searchParams.set('limit', url.searchParams.get('limit') ?? String(config.kafka?.defaultEventLimit ?? 5));
      return proxyJson(response, backendUrl);
    }

    const filePath = url.pathname === '/'
      ? path.join(publicRoot, 'index.html')
      : path.join(publicRoot, path.normalize(url.pathname));

    if (!filePath.startsWith(publicRoot)) {
      return sendJson(response, 403, { error: 'forbidden' });
    }

    const body = await readFile(filePath);
    response.writeHead(200, {
      'content-type': mimeTypes.get(path.extname(filePath)) ?? 'application/octet-stream',
      'cache-control': 'no-store'
    });
    response.end(body);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return sendJson(response, 404, { error: 'not_found' });
    }

    console.error(error);
    return sendJson(response, 500, { error: 'internal_error' });
  }
});

server.listen(config.server.port, config.server.host, () => {
  console.log(`monitoring-ui-api listening on http://${config.server.host}:${config.server.port}`);
});

function applyRuntimeServerOverrides(configValue) {
  const serverConfig = configValue.server ?? {};
  const host = process.env.MONITORING_UI_HOST ?? process.env.HOST ?? serverConfig.host;
  const portRaw = process.env.MONITORING_UI_PORT ?? process.env.PORT;
  const port = portRaw == null || portRaw === ''
    ? serverConfig.port
    : Number.parseInt(portRaw, 10);
  return {
    ...configValue,
    server: {
      ...serverConfig,
      host,
      port: Number.isInteger(port) && port > 0 ? port : serverConfig.port
    }
  };
}

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, { 'content-type': 'application/json; charset=utf-8' });
  response.end(JSON.stringify(body));
}

async function proxyJson(response, targetUrl, init = undefined) {
  const backendResponse = await fetch(targetUrl, init ?? {
    headers: {
      accept: 'application/json'
    }
  });
  const text = await backendResponse.text();

  response.writeHead(backendResponse.status, {
    'content-type': backendResponse.headers.get('content-type') ?? 'application/json; charset=utf-8'
  });
  response.end(text);
}

function publicConversionConfig() {
  const storage = conversionConfigStorage();
  const runtime = conversionConfigRuntimeRulesFile();
  return {
    storageFolder: storage.configuredFolder,
    resolvedStorageFolder: storage.folder,
    files: storage.files,
    runtimeRulesFile: runtime.configuredFile,
    resolvedRuntimeRulesFile: runtime.file
  };
}

function publicWebhooksConfig() {
  const {
    targetBearerToken,
    targetBearerTokenSecret,
    ...publicConfig
  } = config.webhooks ?? {};
  return publicConfig;
}

function conversionConfigStorage() {
  const conversionConfig = config.conversionConfig ?? {};
  const configuredFolder = String(conversionConfig.storageFolder ?? 'state/conversion-config');
  const folder = path.isAbsolute(configuredFolder)
    ? configuredFolder
    : path.resolve(projectRoot, configuredFolder);
  return {
    configuredFolder,
    folder,
    files: {
      serviceRules: String(conversionConfig.serviceRulesFile ?? 'service-rules.json'),
      suppressionRules: String(conversionConfig.suppressionRulesFile ?? 'suppression-rules.json'),
      serviceTemplates: String(conversionConfig.serviceTemplatesFile ?? 'service-templates.json'),
      suppressionTemplates: String(conversionConfig.suppressionTemplatesFile ?? 'suppression-templates.json'),
      sharedTemplates: String(conversionConfig.sharedTemplatesFile ?? 'shared-templates.json'),
      manifest: String(conversionConfig.manifestFile ?? 'manifest.json')
    }
  };
}

function conversionConfigRuntimeRulesFile() {
  const conversionConfig = config.conversionConfig ?? {};
  const configuredFile = String(conversionConfig.runtimeRulesFile ?? 'rules/conversion-rules.runtime.json');
  const file = path.isAbsolute(configuredFile)
    ? configuredFile
    : path.resolve(projectRoot, configuredFile);
  return {
    configuredFile,
    file
  };
}

async function readConversionConfigStorage() {
  const storage = conversionConfigStorage();
  const manifestPath = path.join(storage.folder, storage.files.manifest);
  const serviceRulesPath = path.join(storage.folder, storage.files.serviceRules);
  const suppressionRulesPath = path.join(storage.folder, storage.files.suppressionRules);
  const serviceTemplatesPath = path.join(storage.folder, storage.files.serviceTemplates);
  const suppressionTemplatesPath = path.join(storage.folder, storage.files.suppressionTemplates);
  const sharedTemplatesPath = path.join(storage.folder, storage.files.sharedTemplates);

  const [manifest, serviceRules, suppressionRules, serviceTemplates, suppressionTemplates, sharedTemplates] = await Promise.all([
    readJsonFileIfExists(manifestPath),
    readJsonFileIfExists(serviceRulesPath),
    readJsonFileIfExists(suppressionRulesPath),
    readJsonFileIfExists(serviceTemplatesPath),
    readJsonFileIfExists(suppressionTemplatesPath),
    readJsonFileIfExists(sharedTemplatesPath)
  ]);

  const exists = Boolean(serviceRules || suppressionRules || serviceTemplates || suppressionTemplates || sharedTemplates);
  const prefix = manifest?.prefix ?? '';
  const version = storageVersion(manifest);
  const etag = storageEtag(manifest, {
    prefix,
    ruleDocuments: {
      service: serviceRules,
      suppression: suppressionRules
    },
    templateDocuments: {
      service: serviceTemplates,
      suppression: suppressionTemplates,
      shared: sharedTemplates
    }
  });
  return {
    success: true,
    exists,
    storage: publicConversionConfig(),
    version,
    etag,
    savedAt: manifest?.savedAt ?? await latestMtime([
      serviceRulesPath,
      suppressionRulesPath,
      serviceTemplatesPath,
      suppressionTemplatesPath,
      sharedTemplatesPath
    ]),
    prefix,
    ruleDocuments: {
      service: serviceRules,
      suppression: suppressionRules
    },
    templateDocuments: {
      service: serviceTemplates,
      suppression: suppressionTemplates,
      shared: sharedTemplates
    }
  };
}

async function deployConversionConfigToRuntime(body) {
  const runtime = conversionConfigRuntimeRulesFile();
  const runtimeBuild = buildRuntimeConversionRulesDocument(body);
  const document = runtimeBuild.document;
  const validation = await validateRuntimeConversionRules(document);
  if (!validation.ok) {
    return {
      statusCode: 400,
      body: {
        success: false,
        error: validation.error,
        validation: validation.payload ?? null,
        runtimeRules: runtimePublicInfo(runtime, document, runtimeBuild)
      }
    };
  }

  const storageResult = await writeConversionConfigStorage(body);
  if (storageResult.statusCode !== 200) {
    return storageResult;
  }
  await mkdir(path.dirname(runtime.file), { recursive: true });
  await writeJsonFile(runtime.file, document);

  const rulesStatusUrl = configuredRulesStatusUrl();
  const rulesStatus = rulesStatusUrl
    ? await fetchServiceJson(rulesStatusUrl, Number(config.appliers?.reloadTimeoutMs ?? 5000))
    : null;

  return {
    statusCode: 200,
    body: {
      success: true,
      savedAt: storageResult.body.savedAt,
      storage: storageResult.body.storage,
      version: storageResult.body.version,
      etag: storageResult.body.etag,
      runtimeRules: runtimePublicInfo(runtime, document, runtimeBuild),
      validation: validation.payload ?? null,
      rulesStatus: rulesStatus?.ok ? rulesStatus.payload : null,
      rulesStatusError: rulesStatus && !rulesStatus.ok ? rulesStatus.error : ''
    }
  };
}

function buildRuntimeConversionRulesDocument(body) {
  const documents = [
    normalizeRuntimeRuleDocument(body?.ruleDocuments?.service, 'service'),
    normalizeRuntimeRuleDocument(body?.ruleDocuments?.suppression, 'suppression')
  ];
  const versions = documents
    .map((document) => document.version)
    .filter(Boolean);
  const uniqueVersions = [...new Set(versions)];
  const source = mergeRuntimeSources(documents);
  const runtimeRules = ensureGlobalRuntimeRuleIds(documents.flatMap((document) => document.rules));
  return {
    document: {
      version: uniqueVersions.length === 1
        ? uniqueVersions[0]
        : uniqueVersions.join('+') || '1',
      source,
      rules: runtimeRules.rules
    },
    ruleIdAliases: runtimeRules.aliases
  };
}

function ensureGlobalRuntimeRuleIds(rules) {
  const groups = new Map();
  for (const [index, rule] of rules.entries()) {
    const ruleId = stringValue(rule?.rule_id);
    if (!ruleId) {
      continue;
    }

    const refs = groups.get(ruleId) ?? [];
    refs.push({
      index,
      layer: normalizeRuntimeLayer(rule?.layer)
    });
    groups.set(ruleId, refs);
  }

  const crossLayerDuplicateIds = new Set();
  const sameLayerDuplicateIds = new Set();
  for (const [ruleId, refs] of groups.entries()) {
    if (refs.length <= 1) {
      continue;
    }

    const layers = new Set(refs.map((ref) => ref.layer));
    if (layers.size === refs.length) {
      crossLayerDuplicateIds.add(ruleId);
    } else {
      sameLayerDuplicateIds.add(ruleId);
    }
  }

  const reservedIds = new Set();
  for (const rule of rules) {
    const ruleId = stringValue(rule?.rule_id);
    if (!ruleId || crossLayerDuplicateIds.has(ruleId)) {
      continue;
    }

    reservedIds.add(ruleId);
  }

  const aliases = [];
  const normalizedRules = rules.map((rule, index) => {
    const ruleId = stringValue(rule?.rule_id);
    if (!ruleId || !crossLayerDuplicateIds.has(ruleId) || sameLayerDuplicateIds.has(ruleId)) {
      return rule;
    }

    const layer = normalizeRuntimeLayer(rule?.layer);
    const runtimeRuleId = uniqueRuntimeRuleId(`${layer}-${ruleId}`, reservedIds, rule, index);
    reservedIds.add(runtimeRuleId);
    aliases.push({
      layer,
      originalRuleId: ruleId,
      runtimeRuleId
    });
    return {
      ...rule,
      rule_id: runtimeRuleId
    };
  });

  return {
    rules: normalizedRules,
    aliases
  };
}

function normalizeRuntimeLayer(layer) {
  const normalized = stringValue(layer).toLowerCase();
  if (normalized === 'service' || normalized === 'suppression') {
    return normalized;
  }

  return 'rule';
}

function uniqueRuntimeRuleId(baseValue, reservedIds, rule, index) {
  const base = normalizeRuntimeRuleId(baseValue);
  if (!reservedIds.has(base)) {
    return base;
  }

  const hash = createHash('sha1')
    .update(stableJson({
      index,
      rule_id: rule?.rule_id ?? '',
      layer: rule?.layer ?? '',
      name: rule?.name ?? '',
      source: rule?.source ?? {},
      target: rule?.target ?? {}
    }))
    .digest('hex')
    .slice(0, 8);
  for (let suffix = 0; ; suffix += 1) {
    const candidate = normalizeRuntimeRuleId(`${base}-${hash}${suffix > 0 ? `-${suffix}` : ''}`);
    if (!reservedIds.has(candidate)) {
      return candidate;
    }
  }
}

function normalizeRuntimeRuleId(value) {
  return stringValue(value || 'rule')
    .toLowerCase()
    .replaceAll(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'rule';
}

function normalizeRuntimeRuleDocument(document, fallbackLayer) {
  const source = document?.source && typeof document.source === 'object' && !Array.isArray(document.source)
    ? document.source
    : {};
  const rules = Array.isArray(document?.rules)
    ? document.rules.map((rule) => normalizeRuntimeRule(rule, fallbackLayer))
    : [];
  return {
    version: stringValue(document?.version || '1') || '1',
    source: normalizeRuntimeSource(source),
    rules
  };
}

function normalizeRuntimeRule(rule, fallbackLayer) {
  const source = rule?.source && typeof rule.source === 'object' && !Array.isArray(rule.source)
    ? rule.source
    : {};
  const when = rule?.when && typeof rule.when === 'object' && !Array.isArray(rule.when)
    ? rule.when
    : {};
  const target = rule?.target && typeof rule.target === 'object' && !Array.isArray(rule.target)
    ? rule.target
    : {};

  return {
    ...rule,
    rule_id: stringValue(rule?.rule_id),
    name: stringValue(rule?.name),
    layer: stringValue(rule?.layer || fallbackLayer),
    source: {
      ...source,
      class_code: stringValue(source.class_code),
      key_attribute: source.key_attribute == null ? undefined : stringValue(source.key_attribute),
      conditions: normalizeRuntimeConditions(source.conditions),
      filters: normalizeRuntimeConditions(source.filters)
    },
    when: {
      ...when,
      allRegex: normalizeRuntimeRegexList(when.allRegex),
      anyRegex: normalizeRuntimeRegexList(when.anyRegex),
      noneRegex: normalizeRuntimeRegexList(when.noneRegex),
      fieldExists: when.fieldExists == null ? undefined : stringValue(when.fieldExists)
    },
    target: {
      ...target,
      class_code: stringValue(target.class_code),
      idempotency_key: stringValue(target.idempotency_key),
      card_id: stringValue(target.card_id),
      create_instance: stringValue(target.card_id) ? false : target.create_instance !== false,
      card_description: stringValue(target.card_description),
      attribute_mappings: normalizeStringDictionary(target.attribute_mappings),
      initial_user_values: normalizeStringDictionary(target.initial_user_values),
      user_responsibility_attributes: Array.isArray(target.user_responsibility_attributes)
        ? target.user_responsibility_attributes.map(stringValue).filter(Boolean)
        : []
    },
    relations: Array.isArray(rule?.relations)
      ? rule.relations.map(normalizeRuntimeRelation)
      : []
  };
}

function normalizeRuntimeRelation(relation) {
  return {
    ...relation,
    domain_code: stringValue(relation?.domain_code),
    target_class_code: stringValue(relation?.target_class_code),
    target_lookup: stringValue(relation?.target_lookup),
    attribute_mappings: normalizeStringDictionary(relation?.attribute_mappings)
  };
}

function normalizeRuntimeConditions(conditions) {
  return Array.isArray(conditions)
    ? conditions.map((condition) => ({
        ...condition,
        attribute: stringValue(condition?.attribute),
        operator: stringValue(condition?.operator),
        value: stringValue(condition?.value)
      }))
    : [];
}

function normalizeRuntimeRegexList(items) {
  return Array.isArray(items)
    ? items.map((item) => ({
        ...item,
        field: stringValue(item?.field),
        pattern: stringValue(item?.pattern)
      }))
    : [];
}

function normalizeRuntimeSource(source) {
  return {
    ...source,
    entityClasses: Array.isArray(source.entityClasses)
      ? source.entityClasses.map(stringValue).filter(Boolean)
      : [],
    fields: source.fields && typeof source.fields === 'object' && !Array.isArray(source.fields)
      ? source.fields
      : {}
  };
}

function mergeRuntimeSources(documents) {
  const entityClasses = new Set();
  const fields = {};
  for (const document of documents) {
    for (const classCode of document.source.entityClasses) {
      entityClasses.add(classCode);
    }
    for (const [key, value] of Object.entries(document.source.fields ?? {})) {
      fields[key] = value;
    }
  }

  return {
    entityClasses: [...entityClasses],
    fields
  };
}

function normalizeStringDictionary(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return {};
  }

  return Object.fromEntries(
    Object.entries(value).map(([key, item]) => [String(key), stringValue(item)])
  );
}

async function validateRuntimeConversionRules(document) {
  const targetUrl = config.backend?.rulesValidateUrl;
  if (!targetUrl) {
    return { ok: true, payload: null };
  }

  try {
    const response = await fetch(targetUrl, {
      method: 'POST',
      headers: {
        accept: 'application/json',
        'content-type': 'application/json'
      },
      body: JSON.stringify(document)
    });
    const text = await response.text();
    const payload = parseJsonOrNull(text);
    return response.ok
      ? { ok: true, payload }
      : {
          ok: false,
          payload,
          error: (payload?.errors?.join?.('; ') ?? payload?.detail ?? payload?.error ?? text) || `rules validation failed: ${response.status}`
        };
  } catch (error) {
    return {
      ok: false,
      payload: null,
      error: error.message
    };
  }
}

function configuredRulesStatusUrl() {
  const checks = Array.isArray(config.healthChecks) ? config.healthChecks : [];
  return checks.find((item) => item.id === 'cmdbconfigbuilder' && item.rulesStatusUrl)?.rulesStatusUrl ?? '';
}

function runtimePublicInfo(runtime, document, runtimeBuild = null) {
  const rules = Array.isArray(document.rules) ? document.rules : [];
  const aliases = Array.isArray(runtimeBuild?.ruleIdAliases) ? runtimeBuild.ruleIdAliases : [];
  return {
    configuredFile: runtime.configuredFile,
    filePath: runtime.file,
    version: document.version,
    ruleCount: rules.length,
    serviceRuleCount: rules.filter((rule) => stringValue(rule.layer).toLowerCase() === 'service').length,
    suppressionRuleCount: rules.filter((rule) => stringValue(rule.layer).toLowerCase() === 'suppression').length,
    ruleIdAliasCount: aliases.length,
    ruleIdAliases: aliases.slice(0, 20)
  };
}

async function writeConversionConfigStorage(body) {
  const storage = conversionConfigStorage();
  await mkdir(storage.folder, { recursive: true });

  const savedAt = new Date().toISOString();
  const ruleDocuments = body?.ruleDocuments ?? {};
  const templateDocuments = body?.templateDocuments ?? {};
  const current = await readConversionConfigStorage();
  const conflict = storageWriteConflict(body, current);
  if (conflict) {
    return {
      statusCode: 409,
      body: {
        success: false,
        error: 'conversion_config_conflict',
        message: conflict,
        currentVersion: current.version,
        currentEtag: current.etag,
        currentSavedAt: current.savedAt
      }
    };
  }

  const nextVersion = current.exists
    ? Math.max(1, Number(current.version) || 0) + 1
    : 1;
  const nextPayload = {
    prefix: String(body?.prefix ?? ''),
    ruleDocuments: {
      service: ruleDocuments.service ?? null,
      suppression: ruleDocuments.suppression ?? null
    },
    templateDocuments: {
      service: templateDocuments.service ?? null,
      suppression: templateDocuments.suppression ?? null,
      shared: templateDocuments.shared ?? null
    }
  };
  const etag = computeStorageEtag(nextPayload);
  const manifest = {
    schemaVersion: 1,
    version: nextVersion,
    etag,
    savedAt,
    prefix: nextPayload.prefix,
    writer: 'monitoring-ui-api',
    files: storage.files
  };

  await Promise.all([
    writeJsonFile(path.join(storage.folder, storage.files.serviceRules), nextPayload.ruleDocuments.service),
    writeJsonFile(path.join(storage.folder, storage.files.suppressionRules), nextPayload.ruleDocuments.suppression),
    writeJsonFile(path.join(storage.folder, storage.files.serviceTemplates), nextPayload.templateDocuments.service),
    writeJsonFile(path.join(storage.folder, storage.files.suppressionTemplates), nextPayload.templateDocuments.suppression),
    writeJsonFile(path.join(storage.folder, storage.files.sharedTemplates), nextPayload.templateDocuments.shared)
  ]);
  await writeJsonFile(path.join(storage.folder, storage.files.manifest), manifest);

  return {
    statusCode: 200,
    body: {
      success: true,
      storage: publicConversionConfig(),
      version: nextVersion,
      etag,
      savedAt,
      prefix: manifest.prefix
    }
  };
}

function storageWriteConflict(body, current) {
  if (!current.exists) {
    return '';
  }

  const expectedVersion = optionalNumber(body?.baseVersion ?? body?.version);
  const expectedEtag = stringValue(body?.baseEtag ?? body?.etag);
  if (expectedVersion == null && !expectedEtag) {
    return '';
  }

  if (expectedVersion != null && expectedVersion !== current.version) {
    return `Stored conversion config is v${current.version}, but editor is based on v${expectedVersion}. Reload folder before saving.`;
  }

  if (expectedEtag && current.etag && expectedEtag !== current.etag) {
    return `Stored conversion config etag changed from ${expectedEtag} to ${current.etag}. Reload folder before saving.`;
  }

  return '';
}

function storageVersion(manifest) {
  const parsed = Number(manifest?.version);
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : 0;
}

function storageEtag(manifest, payload) {
  return stringValue(manifest?.etag) || computeStorageEtag(payload);
}

function computeStorageEtag(payload) {
  return createHash('sha256')
    .update(stableJson({
      prefix: payload?.prefix ?? '',
      ruleDocuments: payload?.ruleDocuments ?? {},
      templateDocuments: payload?.templateDocuments ?? {}
    }))
    .digest('hex');
}

function stableJson(value) {
  if (Array.isArray(value)) {
    return `[${value.map(stableJson).join(',')}]`;
  }

  if (value && typeof value === 'object') {
    return `{${Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`)
      .join(',')}}`;
  }

  return JSON.stringify(value);
}

function optionalNumber(value) {
  if (value === undefined || value === null || value === '') {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.floor(parsed) : null;
}

async function readJsonFileIfExists(filePath) {
  try {
    const text = await readFile(filePath, 'utf8');
    return JSON.parse(text);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return null;
    }
    throw error;
  }
}

async function writeJsonFile(filePath, value) {
  await mkdir(path.dirname(filePath), { recursive: true });
  const tempPath = `${filePath}.${process.pid}.${Date.now()}.${randomUUID()}.tmp`;
  await writeFile(tempPath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
  await rename(tempPath, filePath);
}

async function latestMtime(filePaths) {
  const stats = await Promise.all(filePaths.map(async (filePath) => {
    try {
      return await stat(filePath);
    } catch (error) {
      if (error?.code === 'ENOENT') {
        return null;
      }
      throw error;
    }
  }));
  const lastModifiedMs = stats.reduce((latest, item) => Math.max(latest, item?.mtimeMs ?? 0), 0);
  return lastModifiedMs > 0 ? new Date(lastModifiedMs).toISOString() : '';
}

async function checkConfiguredHealthServices() {
  const checks = Array.isArray(config.healthChecks) ? config.healthChecks : [];
  const checkedAt = new Date().toISOString();
  const services = await Promise.all(checks.map((check) => checkHealthService(check)));
  return { checkedAt, services };
}

async function checkConfiguredWebhooks() {
  const backendUrl = config.backend.webhooksCheckUrl;
  const webhooks = config.webhooks ?? {};
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), Number(webhooks.timeoutMs ?? 2500));

  try {
    const backendResponse = await fetch(backendUrl, {
      headers: {
        accept: 'application/json'
      },
      signal: controller.signal
    });
    const text = await backendResponse.text();
    const payload = parseJsonOrNull(text);
    const body = payload && typeof payload === 'object' && !Array.isArray(payload)
      ? { ...payload }
      : { raw: text };
    const configuredEvents = Array.isArray(webhooks.events) ? webhooks.events : [];
    const onlineEvents = firstArray(
      body.events,
      body.Events,
      body.webhooks,
      body.Webhooks,
      body.config?.events,
      body.Config?.Events);
    const cmdbuildInventory = await readCmdbuildManagedWebhookInventory();
    const cmdbuildEvents = Array.isArray(cmdbuildInventory?.events) ? cmdbuildInventory.events : [];
    const effectiveEvents = dedupeWebhookEvents([...cmdbuildEvents, ...onlineEvents]);

    body.endpoint ??= webhooks.targetUrl ?? '';
    body.route ??= webhooks.route ?? '';
    body.rawTopic ??= webhooks.rawTopic ?? '';
    body.identifier ??= webhooks.managedIdentifier ?? '';
    body.events = effectiveEvents.length > 0 ? effectiveEvents : configuredEvents;
    body.cmdbuild = cmdbuildInventory
      ? {
          success: cmdbuildInventory.success,
          total: cmdbuildInventory.total,
          managed: cmdbuildInventory.events.length,
          error: cmdbuildInventory.error ?? ''
        }
      : { success: false, total: 0, managed: 0, error: 'cmdbuild_webhook_inventory_not_configured' };
    body.config = {
      ...(body.config && typeof body.config === 'object' && !Array.isArray(body.config) ? body.config : {}),
      managedIdentifier: webhooks.managedIdentifier ?? '',
      targetUrl: webhooks.targetUrl ?? '',
      route: webhooks.route ?? '',
      rawTopic: webhooks.rawTopic ?? '',
      events: body.events
    };

    return {
      statusCode: backendResponse.status,
      body
    };
  } catch (error) {
    return {
      statusCode: 502,
      body: {
        success: false,
        error: error.name === 'AbortError' ? 'webhooks check timeout' : error.message,
        endpoint: webhooks.targetUrl ?? '',
        route: webhooks.route ?? '',
        rawTopic: webhooks.rawTopic ?? '',
        identifier: webhooks.managedIdentifier ?? '',
        events: Array.isArray(webhooks.events) ? webhooks.events : []
      }
    };
  } finally {
    clearTimeout(timeout);
  }
}

const WEBHOOK_EVENTS = [
  { eventType: 'CREATE', suffix: 'create', cmdbuildEvent: 'card_create_after' },
  { eventType: 'UPDATE', suffix: 'update', cmdbuildEvent: 'card_update_after' },
  { eventType: 'DELETE', suffix: 'delete', cmdbuildEvent: 'card_delete_after' }
];

async function publishManagedWebhooksToCmdbuild(body) {
  const cmdbuild = cmdbuildConfiguration();
  if (!cmdbuild.baseUrl) {
    return {
      statusCode: 500,
      body: {
        success: false,
        error: 'cmdbuild_base_url_not_configured'
      }
    };
  }

  const targetUrl = stringValue(config.webhooks?.targetUrl);
  if (!targetUrl) {
    return {
      statusCode: 500,
      body: {
        success: false,
        error: 'webhooks_target_url_not_configured'
      }
    };
  }

  const sourceClasses = managedWebhookSourceClasses(body);
  if (sourceClasses.length === 0) {
    return {
      statusCode: 400,
      body: {
        success: false,
        error: 'webhook_source_classes_empty'
      }
    };
  }

  const existingInventory = await readCmdbuildWebhookList();
  if (!existingInventory.success) {
    return {
      statusCode: existingInventory.status || 502,
      body: {
        success: false,
        error: existingInventory.error || 'cmdbuild_webhook_inventory_failed'
      }
    };
  }

  const existingCodes = new Set(existingInventory.items.map((item) => stringValue(item.code ?? item._id)));
  const results = [];
  const errors = [];
  for (const sourceClass of sourceClasses) {
    for (const event of WEBHOOK_EVENTS) {
      const payload = buildCmdbuildWebhookPayload(sourceClass, event);
      const exists = existingCodes.has(payload.code);
      const url = exists
        ? cmdbuildUrl('etl', 'webhook', payload.code)
        : cmdbuildUrl('etl', 'webhook');
      const result = await fetchCmdbuildJson(url, {
        method: exists ? 'PUT' : 'POST',
        body: JSON.stringify(payload)
      });
      const item = {
        code: payload.code,
        classCode: sourceClass.code,
        eventType: event.eventType,
        action: exists ? 'updated' : 'created',
        status: result.status
      };
      if (result.ok) {
        results.push(item);
      } else {
        errors.push({
          ...item,
          error: result.error
        });
      }
    }
  }

  const inventory = await readCmdbuildManagedWebhookInventory();
  return {
    statusCode: errors.length > 0 ? 502 : 200,
    body: {
      success: errors.length === 0,
      publishedAt: new Date().toISOString(),
      sourceClassCount: sourceClasses.length,
      created: results.filter((item) => item.action === 'created').length,
      updated: results.filter((item) => item.action === 'updated').length,
      failed: errors.length,
      results,
      errors,
      events: inventory?.events ?? [],
      cmdbuild: inventory
        ? {
            success: inventory.success,
            total: inventory.total,
            managed: inventory.events.length,
            error: inventory.error ?? ''
          }
        : null
    }
  };
}

async function readCmdbuildManagedWebhookInventory() {
  const inventory = await readCmdbuildWebhookList();
  if (!inventory.configured) {
    return null;
  }
  if (!inventory.success) {
    return {
      success: false,
      total: 0,
      events: [],
      error: inventory.error
    };
  }

  const events = inventory.items
    .filter(isManagedCmdbuildWebhook)
    .map(normalizeCmdbuildWebhookEvent)
    .filter(Boolean);
  return {
    success: true,
    total: inventory.items.length,
    events
  };
}

async function readCmdbuildWebhookList() {
  if (!cmdbuildConfiguration().baseUrl) {
    return {
      configured: false,
      success: false,
      status: 0,
      items: [],
      error: 'cmdbuild_base_url_not_configured'
    };
  }

  const url = cmdbuildUrl('etl', 'webhook');
  url.searchParams.set('detailed', 'true');
  const result = await fetchCmdbuildJson(url);
  const data = Array.isArray(result.payload?.data)
    ? result.payload.data
    : (Array.isArray(result.payload) ? result.payload : []);
  return {
    configured: true,
    success: result.ok,
    status: result.status,
    items: result.ok ? data : [],
    error: result.ok ? '' : result.error
  };
}

function managedWebhookSourceClasses(body) {
  const explicit = Array.isArray(body?.sourceClasses) ? body.sourceClasses : [];
  const fromExplicit = explicit
    .map((item) => normalizeManagedWebhookSourceClass(item))
    .filter(Boolean);
  if (fromExplicit.length > 0) {
    return fromExplicit;
  }

  const byCode = new Map();
  for (const layerKey of ['service', 'suppression']) {
    const document = normalizeRuntimeRuleDocument(body?.ruleDocuments?.[layerKey], layerKey);
    for (const rule of document.rules) {
      if (rule?.enabled === false || !rule?.source?.class_code) {
        continue;
      }
      const classCode = rule.source.class_code;
      const key = classCode.toLowerCase();
      const current = byCode.get(key) ?? {
        code: classCode,
        displayName: classCode,
        requiredFields: new Set(),
        payloadFields: new Set()
      };
      for (const field of sourceFieldsForWebhookRule(rule)) {
        current.requiredFields.add(field);
        if (isSafeCmdbuildAttributeName(field)) {
          current.payloadFields.add(field);
        }
      }
      byCode.set(key, current);
    }
  }

  return [...byCode.values()].map((item) => ({
    ...item,
    requiredFields: [...item.requiredFields],
    payloadFields: [...item.payloadFields]
  }));
}

function normalizeManagedWebhookSourceClass(item) {
  const code = stringValue(item?.code ?? item?.classCode ?? item?.sourceClass ?? item?.source_class);
  if (!code) {
    return null;
  }

  return {
    code,
    displayName: stringValue(item?.displayName ?? item?.name ?? item?.description) || code,
    requiredFields: stringArray(item?.requiredFields ?? item?.required_fields),
    payloadFields: stringArray(item?.payloadFields ?? item?.payload_fields)
  };
}

function sourceFieldsForWebhookRule(rule) {
  const fields = new Set([
    rule?.source?.key_attribute,
    rule?.when?.fieldExists
  ]);
  for (const condition of [...(rule?.source?.conditions ?? []), ...(rule?.source?.filters ?? [])]) {
    fields.add(condition?.attribute);
  }
  for (const matcher of [
    ...(rule?.when?.allRegex ?? []),
    ...(rule?.when?.anyRegex ?? []),
    ...(rule?.when?.noneRegex ?? [])
  ]) {
    if (!isWebhookSystemField(matcher?.field)) {
      fields.add(matcher?.field);
    }
  }
  for (const value of [
    rule?.target?.idempotency_key,
    rule?.target?.card_id,
    rule?.target?.card_description,
    ...Object.values(rule?.target?.attribute_mappings ?? {}),
    ...Object.values(rule?.target?.initial_user_values ?? {})
  ]) {
    for (const field of sourceTemplateFields(value)) {
      fields.add(field);
    }
  }
  return [...fields].map(stringValue).filter(Boolean);
}

function sourceTemplateFields(template) {
  const fields = [];
  const text = stringValue(template);
  if (!text) {
    return fields;
  }

  for (const match of text.matchAll(/\$\{\s*source\.([A-Za-z_][A-Za-z0-9_]*)\s*\}/gi)) {
    fields.push(match[1]);
  }
  return fields;
}

function isWebhookSystemField(field) {
  return ['classname', 'classcode', 'class-code', 'eventtype', 'event-type'].includes(stringValue(field).toLowerCase());
}

function buildCmdbuildWebhookPayload(sourceClass, event) {
  const webhooks = config.webhooks ?? {};
  const identifier = stringValue(webhooks.managedIdentifier);
  const targetTopic = stringValue(webhooks.rawTopic);
  const body = {
    source: 'cmdbuild',
    managedIdentifier: identifier,
    targetTopic,
    className: sourceClass.code,
    class_code: sourceClass.code,
    eventType: event.eventType.toLowerCase(),
    cmdbuildEvent: event.cmdbuildEvent,
    id: '{card:Id}',
    card_id: '{card:Id}',
    code: '{card:Code}',
    description: '{card:Description}'
  };
  for (const field of sourceClass.payloadFields ?? []) {
    const name = stringValue(field);
    if (!isSafeCmdbuildAttributeName(name) || isWebhookSystemPayloadField(name) || Object.hasOwn(body, name)) {
      continue;
    }
    body[name] = `{card:${name}}`;
  }

  const headers = webhookTargetHeaders();
  return {
    code: managedWebhookCode(sourceClass.code, event.suffix),
    description: `${identifier || webhookCodePrefix()} ${sourceClass.displayName || sourceClass.code} ${event.suffix}`,
    event: event.cmdbuildEvent,
    target: sourceClass.code,
    method: 'post',
    url: stringValue(webhooks.targetUrl),
    headers,
    body,
    language: stringValue(config.cmdbuildSchema?.defaultLanguage || 'ru') || 'ru',
    active: true
  };
}

function webhookTargetHeaders() {
  const token = stringValue(config.webhooks?.targetBearerToken);
  return token
    ? { Authorization: `Bearer ${token}` }
    : {};
}

function isSafeCmdbuildAttributeName(value) {
  return /^[A-Za-z_][A-Za-z0-9_]*$/.test(stringValue(value));
}

function isWebhookSystemPayloadField(value) {
  return new Set([
    'source',
    'managedidentifier',
    'identifier',
    'targettopic',
    'rawtopic',
    'classname',
    'classcode',
    'class_code',
    'eventtype',
    'event_type',
    'cmdbuildevent',
    'id',
    '_id',
    'cardid',
    'card_id'
  ]).has(stringValue(value).toLowerCase());
}

function isManagedCmdbuildWebhook(webhook) {
  const body = webhookBodyObject(webhook);
  const configuredTargetUrl = stringValue(config.webhooks?.targetUrl);
  const webhookUrl = stringValue(webhook?.url);
  if (configuredTargetUrl && webhookUrl && webhookUrl !== configuredTargetUrl) {
    return false;
  }

  const identifier = stringValue(config.webhooks?.managedIdentifier);
  const bodyIdentifier = stringValue(body.managedIdentifier ?? body.identifier);
  const code = stringValue(webhook?.code ?? webhook?._id).toLowerCase();
  return Boolean(
    (identifier && bodyIdentifier === identifier)
    || code.startsWith(`${webhookCodePrefix().toLowerCase()}-`));
}

function normalizeCmdbuildWebhookEvent(webhook) {
  const eventType = cmdbuildWebhookEventType(webhook?.event);
  if (!eventType) {
    return null;
  }

  const body = webhookBodyObject(webhook);
  const classCode = stringValue(
    body.className
    ?? body.class_code
    ?? body.classCode
    ?? webhook?.target);
  if (!classCode) {
    return null;
  }

  const bodyFields = Object.keys(body)
    .filter((field) => !isWebhookSystemPayloadField(field))
    .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'base' }));
  return {
    eventType,
    classCode,
    identifier: stringValue(config.webhooks?.managedIdentifier),
    code: stringValue(webhook?.code ?? webhook?._id),
    targetTopic: stringValue(body.targetTopic ?? body.rawTopic ?? config.webhooks?.rawTopic),
    url: stringValue(webhook?.url),
    bodyFields
  };
}

function cmdbuildWebhookEventType(event) {
  const value = stringValue(event).toLowerCase();
  if (value === 'card_create_after') {
    return 'CREATE';
  }
  if (value === 'card_update_after') {
    return 'UPDATE';
  }
  if (value === 'card_delete_after') {
    return 'DELETE';
  }
  return '';
}

function webhookBodyObject(webhook) {
  const body = webhook?.body;
  return body && typeof body === 'object' && !Array.isArray(body) ? body : {};
}

function dedupeWebhookEvents(events) {
  const byKey = new Map();
  for (const event of events) {
    const eventType = stringValue(event?.eventType ?? event?.type ?? event?.EventType ?? event?.Type).toUpperCase();
    if (!eventType) {
      continue;
    }
    const classCodes = webhookEventClassCodesForServer(event);
    const classKey = classCodes.length > 0
      ? classCodes.map((item) => item.toLowerCase()).sort().join(',')
      : '*';
    const identifier = stringValue(event?.identifier ?? event?.Identifier ?? config.webhooks?.managedIdentifier);
    const key = `${identifier}|${eventType}|${classKey}`;
    if (!byKey.has(key)) {
      byKey.set(key, event);
    }
  }
  return [...byKey.values()];
}

function webhookEventClassCodesForServer(event) {
  const values = [
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
  return values.map(stringValue).filter(Boolean);
}

async function fetchCmdbuildJson(targetUrl, init = {}) {
  const timeoutMs = Number(config.cmdbuild?.timeoutMs ?? 10000);
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(targetUrl, {
      ...init,
      headers: {
        accept: 'application/json',
        ...(init.body ? { 'content-type': 'application/json' } : {}),
        ...cmdbuildAuthHeaders(),
        ...(init.headers ?? {})
      },
      signal: controller.signal
    });
    const text = await response.text();
    const payload = parseJsonOrNull(text);
    return response.ok
      ? { ok: true, status: response.status, payload, text }
      : {
          ok: false,
          status: response.status,
          payload,
          text,
          error: payload?.messages?.[0]?.message ?? payload?.detail ?? payload?.error ?? text
        };
  } catch (error) {
    return {
      ok: false,
      status: 502,
      payload: null,
      text: '',
      error: error.name === 'AbortError' ? 'cmdbuild request timeout' : error.message
    };
  } finally {
    clearTimeout(timeout);
  }
}

function cmdbuildAuthHeaders() {
  const cmdbuild = cmdbuildConfiguration();
  if (cmdbuild.apiToken) {
    return { authorization: `Bearer ${cmdbuild.apiToken}` };
  }
  if (cmdbuild.username || cmdbuild.password) {
    return {
      authorization: `Basic ${Buffer.from(`${cmdbuild.username}:${cmdbuild.password}`).toString('base64')}`
    };
  }
  return {};
}

function cmdbuildUrl(...segments) {
  return appendPath(cmdbuildConfiguration().baseUrl, ...segments);
}

function cmdbuildConfiguration() {
  const section = config.cmdbuild ?? config.Cmdbuild ?? {};
  return {
    baseUrl: stringValue(section.baseUrl ?? section.BaseUrl),
    username: stringValue(section.username ?? section.Username),
    password: stringValue(section.password ?? section.Password),
    apiToken: stringValue(section.apiToken ?? section.ApiToken)
  };
}

function managedWebhookCode(classCode, eventSuffix) {
  return `${webhookCodePrefix()}-${webhookCodeSegment(classCode)}-${eventSuffix}`;
}

function webhookCodePrefix() {
  return stringValue(config.webhooks?.codePrefix ?? config.webhooks?.managedIdentifier ?? 'cmdbwebhooks2kafka')
    .toLowerCase()
    .replaceAll(/[^a-z0-9_]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'cmdbwebhooks2kafka';
}

function webhookCodeSegment(value) {
  const normalized = stringValue(value)
    .toLowerCase()
    .replaceAll(/[^a-z0-9_]+/g, '-')
    .replace(/^-+|-+$/g, '');
  if (normalized) {
    return normalized;
  }

  return `class-${createHash('sha256').update(stringValue(value)).digest('hex').slice(0, 8)}`;
}

function stringArray(value) {
  return Array.isArray(value)
    ? [...new Set(value.map(stringValue).filter(Boolean))]
    : [];
}

async function checkHealthService(check) {
  const startedAt = Date.now();
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), Number(check.timeoutMs ?? 2500));
  try {
    const healthResponse = await fetch(check.url, {
      headers: {
        accept: 'application/json'
      },
      signal: controller.signal
    });
    const text = await healthResponse.text();
    const payload = parseJsonOrNull(text);
    const payloadStatus = String(payload?.status ?? payload?.Status ?? '').toLowerCase();
    const ok = healthResponse.ok && (!payloadStatus || payloadStatus === 'ok' || payloadStatus === 'healthy');
    const enrichedPayload = payload && typeof payload === 'object' && !Array.isArray(payload)
      ? { ...payload }
      : {};
    if (check.rulesStatusUrl) {
      const rulesStatus = await fetchServiceJson(check.rulesStatusUrl, Number(check.timeoutMs ?? 2500));
      enrichedPayload.conversionRules = rulesStatus.ok
        ? rulesStatus.payload
        : {
            success: false,
            error: rulesStatus.error,
            status: rulesStatus.status
          };
    }
    return {
      id: check.id ?? '',
      name: check.name ?? check.id ?? check.url,
      url: check.url,
      canReloadConfiguration: Boolean(check.reloadUrl),
      status: ok ? 'ok' : 'error',
      httpStatus: healthResponse.status,
      latencyMs: Date.now() - startedAt,
      payload: payload && typeof payload === 'object' && !Array.isArray(payload)
        ? enrichedPayload
        : payload,
      error: ok ? '' : (payload?.error ?? payload?.Error ?? text)
    };
  } catch (error) {
    return {
      id: check.id ?? '',
      name: check.name ?? check.id ?? check.url,
      url: check.url,
      canReloadConfiguration: Boolean(check.reloadUrl),
      status: 'error',
      httpStatus: 0,
      latencyMs: Date.now() - startedAt,
      payload: null,
      error: error.name === 'AbortError' ? 'healthcheck timeout' : error.message
    };
  } finally {
    clearTimeout(timeout);
  }
}

async function fetchServiceJson(targetUrl, timeoutMs) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(targetUrl, {
      headers: {
        accept: 'application/json'
      },
      signal: controller.signal
    });
    const text = await response.text();
    const payload = parseJsonOrNull(text);
    return response.ok
      ? { ok: true, status: response.status, payload }
      : {
          ok: false,
          status: response.status,
          error: payload?.detail ?? payload?.error ?? text
        };
  } catch (error) {
    return {
      ok: false,
      status: 0,
      error: error.name === 'AbortError' ? 'request timeout' : error.message
    };
  } finally {
    clearTimeout(timeout);
  }
}

async function reloadApplierConfiguration(applierId) {
  const check = (Array.isArray(config.healthChecks) ? config.healthChecks : [])
    .find((item) => item.id === applierId && item.reloadUrl);
  if (!check) {
    return {
      statusCode: 404,
      body: {
        success: false,
        error: 'applier_reload_target_not_found'
      }
    };
  }

  const token = String(config.appliers?.reloadBearerToken ?? '').trim();
  if (!token) {
    return {
      statusCode: 500,
      body: {
        success: false,
        error: 'applier_reload_bearer_token_not_configured'
      }
    };
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), Number(config.appliers?.reloadTimeoutMs ?? 5000));
  try {
    const reloadResponse = await fetch(check.reloadUrl, {
      method: 'POST',
      headers: {
        accept: 'application/json',
        authorization: `Bearer ${token}`
      },
      signal: controller.signal
    });
    const text = await reloadResponse.text();
    const payload = parseJsonOrNull(text);
    if (!reloadResponse.ok) {
      return {
        statusCode: reloadResponse.status === 401 ? 401 : 502,
        body: {
          success: false,
          applierId,
          name: check.name ?? applierId,
          status: reloadResponse.status,
          error: payload?.detail ?? payload?.error ?? text
        }
      };
    }

    return {
      statusCode: 200,
      body: {
        success: true,
        applierId,
        name: check.name ?? applierId,
        service: payload?.service ?? payload?.Service ?? '',
        version: payload?.version ?? payload?.Version ?? '',
        payload,
        configurationVersion: payload?.configurationVersion ?? payload?.ConfigurationVersion ?? null,
        configurationReloadedAt: payload?.configurationReloadedAt ?? payload?.ConfigurationReloadedAt ?? ''
      }
    };
  } catch (error) {
    return {
      statusCode: 502,
      body: {
        success: false,
        applierId,
        name: check.name ?? applierId,
        error: error.name === 'AbortError' ? 'applier reload timeout' : error.message
      }
    };
  } finally {
    clearTimeout(timeout);
  }
}

function parseJsonOrNull(text) {
  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function firstArray(...values) {
  return values.find(Array.isArray) ?? [];
}

function appendPath(baseUrl, ...segments) {
  const url = new URL(baseUrl);
  const basePath = url.pathname.replace(/\/+$/, '');
  url.pathname = [
    basePath,
    ...segments.map((segment) => encodeURIComponent(segment))
  ].join('/');
  return url;
}

async function resolveSecretReferences(rawConfig, serviceName) {
  applyPamCompatibilityEnvironment(rawConfig);
  applySecretCompanionReferences(rawConfig);

  const references = [];
  collectSecretReferences(rawConfig, [], references);
  if (references.length === 0) {
    return rawConfig;
  }

  const provider = stringValue(readPath(rawConfig, ['secrets', 'provider']) ?? readPath(rawConfig, ['Secrets', 'Provider']) ?? 'None');
  if (provider.toLowerCase() !== 'indeedpamaapm') {
    throw new Error(`Configuration contains secret:// references, but secrets.provider is '${provider}'.`);
  }

  for (const reference of references) {
    setPath(rawConfig, reference.path, await readIndeedPamAapmSecret(rawConfig, serviceName, reference.secretId));
  }

  return rawConfig;
}

function collectSecretReferences(value, pathParts, references) {
  if (!value || typeof value !== 'object') {
    return;
  }

  for (const [key, child] of Object.entries(value)) {
    const nextPath = pathParts.concat(key);
    if (pathParts.length === 0 && key.toLowerCase() === 'secrets') {
      continue;
    }

    const secretId = parseSecretId(child);
    if (secretId) {
      references.push({ path: nextPath, secretId });
      continue;
    }

    collectSecretReferences(child, nextPath, references);
  }
}

function applySecretCompanionReferences(value) {
  if (!value || typeof value !== 'object') {
    return;
  }

  for (const [key, child] of Object.entries(value)) {
    if (child && typeof child === 'object') {
      applySecretCompanionReferences(child);
    }

    if (typeof child !== 'string'
      || !stringValue(child)
      || !key.toLowerCase().endsWith('secret')
      || key.length <= 'Secret'.length) {
      continue;
    }

    const targetKey = key.slice(0, -'Secret'.length);
    if (Object.hasOwn(value, targetKey)) {
      value[targetKey] = ensureSecretReference(child);
    }
  }
}

function applyPamCompatibilityEnvironment(rawConfig) {
  const secrets = ensureObject(rawConfig, 'secrets');
  const pam = ensureObject(secrets, 'indeedPamAapm');
  setIfMissing(pam, 'baseUrl', process.env.PAMURL);
  setIfMissing(pam, 'applicationUsername', process.env.PAMUSERNAME);
  setIfMissing(pam, 'applicationPassword', process.env.PAMPASSWORD);
  setIfMissing(pam, 'applicationToken', process.env.PAMTOKEN);
  setIfMissing(pam, 'defaultAccountPath', process.env.PAMDEFAULTACCOUNTPATH);

  const hasPamCompatibility = Boolean(process.env.PAMURL || process.env.PAMTOKEN || (process.env.PAMUSERNAME && process.env.PAMPASSWORD));
  if (hasPamCompatibility && stringValue(secrets.provider || 'None').toLowerCase() === 'none') {
    secrets.provider = 'IndeedPamAapm';
  }
}

async function readIndeedPamAapmSecret(rawConfig, serviceName, secretId) {
  const pam = readPath(rawConfig, ['secrets', 'indeedPamAapm']) ?? {};
  const references = readPath(rawConfig, ['secrets', 'references']) ?? {};
  const reference = references[secretId] ?? {};
  const baseUrl = requiredConfigValue(pam.baseUrl, 'secrets.indeedPamAapm.baseUrl');
  const endpointPath = stringValue(pam.passwordEndpointPath) || '/sc_aapm_ui/rest/aapm/password';
  const parsed = parsePamAccount(secretId);
  const accountPath = stringValue(reference.accountPath)
    || parsed.accountPath
    || stringValue(pam.defaultAccountPath);
  const accountName = stringValue(reference.accountName) || parsed.accountName;
  if (!accountPath) {
    throw new Error(`Required PAM account path is missing for secret '${secretId}'.`);
  }
  if (!accountName) {
    throw new Error(`Required PAM account name is missing for secret '${secretId}'.`);
  }

  const applicationCredentials = await readPamApplicationCredentials(pam);
  const query = new URLSearchParams();
  addQuery(query, 'token', applicationCredentials.token);
  addQuery(query, 'sapmaccountpath', accountPath);
  addQuery(query, 'sapmaccountname', accountName);
  addQuery(query, 'responsetype', stringValue(reference.responseType) || stringValue(pam.responseType) || 'json');
  addQuery(query, 'passwordexpirationinminute', stringValue(reference.passwordExpirationInMinute) || stringValue(pam.passwordExpirationInMinute));
  addQuery(query, 'passwordchangerequired', boolText(reference.passwordChangeRequired) || boolText(pam.passwordChangeRequired));
  addQuery(
    query,
    'comment',
    (stringValue(reference.comment) || stringValue(pam.comment) || `cmdb2monitoring ${serviceName} ${secretId}`)
      .replaceAll('{service}', serviceName)
      .replaceAll('{secretId}', secretId));
  addQuery(query, 'tenantid', stringValue(reference.tenantId) || stringValue(pam.tenantId));
  addQuery(query, 'pin', stringValue(reference.pin) || stringValue(pam.pin));

  const url = new URL(endpointPath.replace(/^\/+/, ''), `${baseUrl.replace(/\/+$/, '')}/`);
  url.search = query.toString();
  const headers = {};
  if (applicationCredentials.username && applicationCredentials.password) {
    headers.authorization = `Basic ${Buffer.from(`${applicationCredentials.username}:${applicationCredentials.password}`, 'utf8').toString('base64')}`;
    if (pam.sendApplicationCredentialsInQuery === true) {
      url.searchParams.set('username', applicationCredentials.username);
      url.searchParams.set('password', applicationCredentials.password);
    }
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), Number(pam.timeoutMs ?? 10000));
  try {
    const response = await fetch(url, { headers, signal: controller.signal });
    const body = await response.text();
    if (!response.ok) {
      throw new Error(`PAM secret '${secretId}' request failed with HTTP ${response.status}.`);
    }

    const secret = extractSecretValue(
      body,
      stringValue(reference.responseType) || stringValue(pam.responseType) || 'json',
      stringValue(reference.valueJsonPath) || stringValue(pam.valueJsonPath) || 'password');
    if (!secret) {
      throw new Error(`PAM secret '${secretId}' returned an empty value.`);
    }

    return secret;
  } finally {
    clearTimeout(timeout);
  }
}

async function readPamApplicationCredentials(pam) {
  const token = stringValue(pam.applicationToken);
  if (token) {
    return { token };
  }

  const tokenFile = stringValue(pam.applicationTokenFile);
  if (tokenFile) {
    return { token: (await readFile(tokenFile, 'utf8')).trim() };
  }

  const username = stringValue(pam.applicationUsername);
  const password = stringValue(pam.applicationPassword);
  if (username && password) {
    return { username, password };
  }

  throw new Error('PAM credentials are not configured. Set applicationToken, applicationTokenFile, or applicationUsername/applicationPassword.');
}

function extractSecretValue(body, responseType, valueJsonPath) {
  if (responseType.toLowerCase() !== 'json') {
    return body.trim();
  }

  const parsed = JSON.parse(body);
  if (typeof parsed === 'string') {
    return parsed.trim();
  }

  for (const pathName of [valueJsonPath, 'password', 'value', 'secret', 'Password']) {
    const value = readJsonPath(parsed, pathName);
    if (value !== undefined && value !== null && stringValue(value)) {
      return stringValue(value);
    }
  }

  return '';
}

function readJsonPath(value, pathName) {
  return String(pathName ?? '')
    .split(/[.:]/)
    .filter(Boolean)
    .reduce((current, key) => (current && typeof current === 'object' ? current[key] : undefined), value);
}

function parsePamAccount(secretId) {
  const dot = secretId.lastIndexOf('.');
  if (dot > 0 && dot < secretId.length - 1) {
    return {
      accountPath: secretId.slice(0, dot),
      accountName: secretId.slice(dot + 1)
    };
  }

  const slash = secretId.lastIndexOf('/');
  if (slash > 0 && slash < secretId.length - 1) {
    return {
      accountPath: secretId.slice(0, slash),
      accountName: secretId.slice(slash + 1)
    };
  }

  return {
    accountPath: '',
    accountName: ''
  };
}

function parseSecretId(value) {
  const text = stringValue(value);
  if (text.toLowerCase().startsWith('secret://')) {
    return text.slice('secret://'.length).trim();
  }
  if (text.toLowerCase().startsWith('aapm://')) {
    return text.slice('aapm://'.length).trim();
  }
  return '';
}

function ensureSecretReference(value) {
  const text = stringValue(value);
  return parseSecretId(text) ? text : `secret://${text}`;
}

function readPath(value, pathParts) {
  let current = value;
  for (const part of pathParts) {
    if (!current || typeof current !== 'object') {
      return undefined;
    }

    const key = Object.keys(current).find((candidate) => candidate.toLowerCase() === part.toLowerCase());
    current = key ? current[key] : undefined;
  }

  return current;
}

function setPath(value, pathParts, nextValue) {
  let current = value;
  for (const part of pathParts.slice(0, -1)) {
    current = current[part];
  }

  current[pathParts[pathParts.length - 1]] = nextValue;
}

function ensureObject(value, key) {
  const existingKey = Object.keys(value).find((candidate) => candidate.toLowerCase() === key.toLowerCase());
  if (existingKey && value[existingKey] && typeof value[existingKey] === 'object') {
    return value[existingKey];
  }

  value[key] = {};
  return value[key];
}

function setIfMissing(value, key, nextValue) {
  if (nextValue && !stringValue(value[key])) {
    value[key] = nextValue;
  }
}

function addQuery(query, key, value) {
  if (value !== undefined && value !== null && String(value).trim()) {
    query.set(key, String(value).trim());
  }
}

function boolText(value) {
  return typeof value === 'boolean' ? String(value) : stringValue(value);
}

function requiredConfigValue(value, pathName) {
  const text = stringValue(value);
  if (!text) {
    throw new Error(`Required configuration value is missing: ${pathName}.`);
  }

  return text;
}

function stringValue(value) {
  return String(value ?? '').trim();
}

async function readJsonBody(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }

  const text = Buffer.concat(chunks).toString('utf8');
  return text ? JSON.parse(text) : {};
}
