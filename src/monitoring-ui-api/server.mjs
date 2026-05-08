import http from 'node:http';
import { mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(root, '..', '..');
const publicRoot = path.join(root, 'public');
const baseConfig = JSON.parse(await readFile(path.join(root, 'config', 'appsettings.json'), 'utf8'));
const config = await resolveSecretReferences(baseConfig, 'monitoring-ui-api');

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
        webhooks: config.webhooks ?? {},
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
      return sendJson(response, 200, await writeConversionConfigStorage(body));
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

    if (url.pathname === '/api/zabbix/check' && request.method === 'GET') {
      return proxyJson(response, config.backend.zabbixCheckUrl);
    }

    if (url.pathname === '/api/webhooks/check' && request.method === 'GET') {
      const result = await checkConfiguredWebhooks();
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
      'content-type': mimeTypes.get(path.extname(filePath)) ?? 'application/octet-stream'
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

  const [manifest, serviceRules, suppressionRules, serviceTemplates, suppressionTemplates] = await Promise.all([
    readJsonFileIfExists(manifestPath),
    readJsonFileIfExists(serviceRulesPath),
    readJsonFileIfExists(suppressionRulesPath),
    readJsonFileIfExists(serviceTemplatesPath),
    readJsonFileIfExists(suppressionTemplatesPath)
  ]);

  const exists = Boolean(serviceRules || suppressionRules || serviceTemplates || suppressionTemplates);
  return {
    success: true,
    exists,
    storage: publicConversionConfig(),
    savedAt: manifest?.savedAt ?? await latestMtime([
      serviceRulesPath,
      suppressionRulesPath,
      serviceTemplatesPath,
      suppressionTemplatesPath
    ]),
    prefix: manifest?.prefix ?? '',
    ruleDocuments: {
      service: serviceRules,
      suppression: suppressionRules
    },
    templateDocuments: {
      service: serviceTemplates,
      suppression: suppressionTemplates
    }
  };
}

async function deployConversionConfigToRuntime(body) {
  const runtime = conversionConfigRuntimeRulesFile();
  const document = buildRuntimeConversionRulesDocument(body);
  const validation = await validateRuntimeConversionRules(document);
  if (!validation.ok) {
    return {
      statusCode: 400,
      body: {
        success: false,
        error: validation.error,
        validation: validation.payload ?? null,
        runtimeRules: runtimePublicInfo(runtime, document)
      }
    };
  }

  const storageResult = await writeConversionConfigStorage(body);
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
      savedAt: storageResult.savedAt,
      storage: storageResult.storage,
      runtimeRules: runtimePublicInfo(runtime, document),
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
  return {
    version: uniqueVersions.length === 1
      ? uniqueVersions[0]
      : uniqueVersions.join('+') || '1',
    source,
    rules: documents.flatMap((document) => document.rules)
  };
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

function runtimePublicInfo(runtime, document) {
  const rules = Array.isArray(document.rules) ? document.rules : [];
  return {
    configuredFile: runtime.configuredFile,
    filePath: runtime.file,
    version: document.version,
    ruleCount: rules.length,
    serviceRuleCount: rules.filter((rule) => stringValue(rule.layer).toLowerCase() === 'service').length,
    suppressionRuleCount: rules.filter((rule) => stringValue(rule.layer).toLowerCase() === 'suppression').length
  };
}

async function writeConversionConfigStorage(body) {
  const storage = conversionConfigStorage();
  await mkdir(storage.folder, { recursive: true });

  const savedAt = new Date().toISOString();
  const ruleDocuments = body?.ruleDocuments ?? {};
  const templateDocuments = body?.templateDocuments ?? {};
  const manifest = {
    schemaVersion: 1,
    savedAt,
    prefix: String(body?.prefix ?? ''),
    files: storage.files
  };

  await Promise.all([
    writeJsonFile(path.join(storage.folder, storage.files.serviceRules), ruleDocuments.service ?? null),
    writeJsonFile(path.join(storage.folder, storage.files.suppressionRules), ruleDocuments.suppression ?? null),
    writeJsonFile(path.join(storage.folder, storage.files.serviceTemplates), templateDocuments.service ?? null),
    writeJsonFile(path.join(storage.folder, storage.files.suppressionTemplates), templateDocuments.suppression ?? null),
    writeJsonFile(path.join(storage.folder, storage.files.manifest), manifest)
  ]);

  return {
    success: true,
    storage: publicConversionConfig(),
    savedAt,
    prefix: manifest.prefix
  };
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
  await writeFile(filePath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
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

    body.endpoint ??= webhooks.targetUrl ?? '';
    body.route ??= webhooks.route ?? '';
    body.rawTopic ??= webhooks.rawTopic ?? '';
    body.identifier ??= webhooks.managedIdentifier ?? '';
    body.events = onlineEvents.length > 0 ? onlineEvents : configuredEvents;
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
