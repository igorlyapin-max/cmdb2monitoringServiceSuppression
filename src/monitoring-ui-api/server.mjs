import http from 'node:http';
import https from 'node:https';
import { AsyncLocalStorage } from 'node:async_hooks';
import { appendFile, mkdir, readFile, rename, stat, writeFile } from 'node:fs/promises';
import { createHash, randomUUID, timingSafeEqual } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(root, '..', '..');
const publicRoot = path.join(root, 'public');
const baseConfig = JSON.parse(await readFile(path.join(root, 'config', 'appsettings.json'), 'utf8'));
const config = await resolveSecretReferences(applyRuntimeServerOverrides(baseConfig), 'monitoring-ui-api');
let conversionConfigStoreWriteChain = Promise.resolve();
let conversionConfigPostgresPoolPromise = null;
const requestContext = new AsyncLocalStorage();
const metricCounters = new Map();
const rateLimitCounters = new Map();

const mimeTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8']
]);

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url ?? '/', `http://${request.headers.host ?? 'localhost'}`);
  const context = createRequestContext(request, response);
  await requestContext.run(context, async () => {
    const started = process.hrtime.bigint();
    try {
      applySecurityHeaders(response);

      if (!hostAllowed(request.headers.host)) {
        return sendJson(response, 400, { error: 'host_not_allowed' });
      }

      if (url.pathname === readinessConfig().route) {
        return sendJson(response, 200, readinessPayload());
      }

      if (url.pathname === metricsConfig().route) {
        if (!metricsAccessAllowed(request)) {
          return sendJson(response, 401, { error: 'metrics_unauthorized' });
        }

        return sendText(response, 200, renderMetrics(), 'text/plain; version=0.0.4; charset=utf-8');
      }

      if (isRateLimited(request, url)) {
        incrementMetric('http_rate_limited_requests_total', {
          method: request.method ?? 'GET',
          path: url.pathname
        });
        return sendJson(response, 429, { error: 'rate limit exceeded' });
      }

    if (url.pathname === '/health') {
      return sendJson(response, 200, { service: 'monitoring-ui-api', status: 'ok' });
    }

    if (url.pathname === '/api/config') {
      return sendJson(response, 200, {
        roles: config.auth.roles,
        cmdbuildSchema: config.cmdbuildSchema,
        cmdbuild: publicCmdbuildConfig(),
        webhooks: publicWebhooksConfig(),
        kafka: config.kafka ?? {},
        readiness: config.readiness ?? {},
        conversionConfig: publicConversionConfig()
      });
    }

    if (url.pathname === '/api/conversion-config/storage' && request.method === 'GET') {
      return sendJson(response, 200, await readConversionConfigStoreCurrent());
    }

    if (url.pathname === '/api/conversion-config/storage' && request.method === 'PUT') {
      const body = await readJsonBody(request);
      const result = await saveConversionConfigStoreAuthoring(body, {
        actor: 'legacy-api',
        changeType: 'legacy_storage_write',
        reason: 'compatibility /api/conversion-config/storage'
      });
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/conversion-config/deploy' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const result = await deployConversionConfigStoreToRuntime(body, {
        actor: 'legacy-api',
        changeType: 'legacy_deploy',
        reason: 'compatibility /api/conversion-config/deploy'
      });
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/conversion-config-store/current' && request.method === 'GET') {
      return sendJson(response, 200, await readConversionConfigStoreCurrent());
    }

    if (url.pathname === '/api/conversion-config-store/save-authoring' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const result = await saveConversionConfigStoreAuthoring(body, {
        actor: stringValue(body?.actor) || 'monitoring-ui-api',
        changeType: stringValue(body?.changeType) || 'authoring_change',
        reason: stringValue(body?.reason) || 'operator save-authoring'
      });
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/conversion-config-store/deploy' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const result = await deployConversionConfigStoreToRuntime(body, {
        actor: stringValue(body?.actor) || 'monitoring-ui-api',
        changeType: stringValue(body?.changeType) || 'authoring_deploy',
        reason: stringValue(body?.reason) || 'operator deploy'
      });
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/conversion-config-store/audit' && request.method === 'GET') {
      const limit = optionalNumber(url.searchParams.get('limit')) ?? 100;
      return sendJson(response, 200, await readConversionConfigStoreAudit(limit));
    }

    if (url.pathname === '/api/materializer/status' && request.method === 'GET') {
      if (!config.backend.modelMaterializerStatusUrl) {
        return sendJson(response, 500, { error: 'backend.modelMaterializerStatusUrl is not configured' });
      }

      return proxyJson(response, config.backend.modelMaterializerStatusUrl);
    }

    if (url.pathname === '/api/materializer/retry' && request.method === 'POST') {
      if (!config.backend.modelMaterializerProcessUrl) {
        return sendJson(response, 500, { error: 'backend.modelMaterializerProcessUrl is not configured' });
      }

      const body = await readJsonBody(request);
      const materializerRequest = objectValue(body?.request ?? body);
      return proxyJson(response, config.backend.modelMaterializerProcessUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(materializerRequest)
      });
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
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
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
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: JSON.stringify(body)
      });
    }

    if (url.pathname === '/api/zabbix/apply-current/scope-preview' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const layer = normalizeRuntimeLayer(body?.layer);
      if (layer !== 'service' && layer !== 'suppression') {
        return sendJson(response, 400, { error: 'layer must be service or suppression' });
      }

      const backendBody = zabbixApplyCurrentBackendBody(body, layer, { dryRun: true });
      const backendInit = {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: JSON.stringify(backendBody)
      };
      const previewUrl = config.backend.rulesApplyCurrentScopePreviewUrl
        || appendPath(config.backend.rulesApplyCurrentUrl, 'scope-preview');
      return proxyJson(response, previewUrl, backendInit);
    }

    if (url.pathname === '/api/zabbix/apply-current' && request.method === 'POST') {
      const body = await readJsonBody(request);
      const layer = normalizeRuntimeLayer(body?.layer);
      if (layer !== 'service' && layer !== 'suppression') {
        return sendJson(response, 400, { error: 'layer must be service or suppression' });
      }

      const directApplyUrl = stringValue(config.backend.zabbixCommandApplyUrl);
      const targets = directApplyUrl ? ['zabbix-direct'] : ['zabbix'];
      const operationId = stringValue(body?.operationId) || randomUUID();
      const backendBody = zabbixApplyCurrentBackendBody(body, layer, { operationId, targets });
      const backendInit = {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: JSON.stringify(backendBody)
      };

      if (body?.detached === true) {
        runDetachedJsonRequest(config.backend.rulesApplyCurrentUrl, backendInit, operationId);
        return sendJson(response, 202, {
          operationId,
          status: 'accepted',
          detached: true,
          dryRun: Boolean(body?.dryRun),
          topics: targets
        });
      }

      return proxyJson(response, config.backend.rulesApplyCurrentUrl, backendInit);
    }

    const zabbixApplyProgressMatch = url.pathname.match(/^\/api\/zabbix\/apply-current\/progress\/([^/]+)$/);
    if (zabbixApplyProgressMatch && request.method === 'GET') {
      return proxyJson(response, appendPath(
        config.backend.rulesApplyCurrentUrl,
        'progress',
        decodeURIComponent(zabbixApplyProgressMatch[1])));
    }

    const zabbixApplyCancelMatch = url.pathname.match(/^\/api\/zabbix\/apply-current\/cancel\/([^/]+)$/);
    if (zabbixApplyCancelMatch && request.method === 'POST') {
      return proxyJson(response, appendPath(
        config.backend.rulesApplyCurrentUrl,
        'cancel',
        decodeURIComponent(zabbixApplyCancelMatch[1])), {
        method: 'POST',
        headers: {
          accept: 'application/json'
        }
      });
    }

    if (url.pathname === '/api/zabbix/apply/status' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixApplyStatusUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixApplyStatusUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/runtime-storage/status' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixRuntimeStorageStatusUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixRuntimeStorageStatusUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/redis/check' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixRedisCheckUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixRedisCheckUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/rules/redis/check' && request.method === 'GET') {
      const targetUrl = config.backend.cmdbConfigBuilderRedisCheckUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.cmdbConfigBuilderRedisCheckUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/runtime-storage/migration/dry-run' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixRuntimeStorageMigrationDryRunUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixRuntimeStorageMigrationDryRunUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/zabbix/runtime-storage/migration/apply' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixRuntimeStorageMigrationApplyUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixRuntimeStorageMigrationApplyUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/zabbix/runtime-storage/dirty-scopes' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixDirtyScopesUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixDirtyScopesUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/runtime-storage/dirty-scopes' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixDirtyScopesUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixDirtyScopesUrl is not configured' });
      }

      const body = await readJsonBody(request);
      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(body ?? {})
      });
    }

    const dirtyScopeClearMatch = url.pathname.match(/^\/api\/zabbix\/runtime-storage\/dirty-scopes\/([^/]+)$/);
    if (dirtyScopeClearMatch && request.method === 'DELETE') {
      if (!config.backend.zabbixDirtyScopesUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixDirtyScopesUrl is not configured' });
      }

      const targetUrl = appendPath(
        config.backend.zabbixDirtyScopesUrl,
        decodeURIComponent(dirtyScopeClearMatch[1]));
      return proxyJson(response, targetUrl, {
        method: 'DELETE',
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/zabbix/monitoring-coverage/snapshot' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixMonitoringCoverageSnapshotUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixMonitoringCoverageSnapshotUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/zabbix/monitoring-coverage/snapshots' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixMonitoringCoverageSnapshotUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixMonitoringCoverageSnapshotUrl is not configured' });
      }

      const historyUrl = new URL(targetUrl);
      historyUrl.pathname = historyUrl.pathname.replace(/\/snapshot\/?$/, '/snapshots');
      historyUrl.search = url.search;
      return proxyJson(response, historyUrl, {
        headers: { accept: 'application/json' }
      });
    }

    if (url.pathname === '/api/zabbix/apply-state/stale-report' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixApplyStateStaleReportUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixApplyStateStaleReportUrl is not configured' });
      }

      const body = await readJsonBody(request);
      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(body ?? {})
      });
    }

    if (url.pathname === '/api/zabbix/apply-state/cleanup' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixApplyStateCleanupUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixApplyStateCleanupUrl is not configured' });
      }

      const body = await readJsonBody(request);
      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(body ?? {})
      });
    }

    if (url.pathname === '/api/zabbix/apply-state/delete-zabbix-services' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixApplyStateDeleteServicesUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixApplyStateDeleteServicesUrl is not configured' });
      }

      const body = await readJsonBody(request);
      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(body ?? {})
      });
    }

    if (url.pathname === '/api/zabbix/trigger-dependencies/status' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixTriggerDependenciesStatusUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixTriggerDependenciesStatusUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/sla/status' && request.method === 'GET') {
      const targetUrl = config.backend.zabbixSlaStatusUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixSlaStatusUrl is not configured' });
      }

      return proxyJson(response, targetUrl);
    }

    if (url.pathname === '/api/zabbix/sla/service/dry-run' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixSlaDryRunUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixSlaDryRunUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: '{}'
      });
    }

    if (url.pathname === '/api/zabbix/sla/service/apply' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixSlaApplyUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixSlaApplyUrl is not configured' });
      }

      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: '{}'
      });
    }

    if (url.pathname === '/api/admin/zabbixconfig2api/settings' && request.method === 'GET') {
      const result = await readZabbixconfig2apiSettings();
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/admin/zabbixconfig2api/settings' && request.method === 'PUT') {
      const body = await readJsonBody(request);
      const result = await updateZabbixconfig2apiSettings(body);
      return sendJson(response, result.statusCode, result.body);
    }

    if (url.pathname === '/api/zabbix/trigger-dependencies/dry-run' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixTriggerDependenciesDryRunUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixTriggerDependenciesDryRunUrl is not configured' });
      }

      const body = await readJsonBody(request);
      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(body ?? {})
      });
    }

    if (url.pathname === '/api/zabbix/trigger-dependencies/apply' && request.method === 'POST') {
      const targetUrl = config.backend.zabbixTriggerDependenciesApplyUrl;
      if (!targetUrl) {
        return sendJson(response, 500, { error: 'backend.zabbixTriggerDependenciesApplyUrl is not configured' });
      }

      const body = await readJsonBody(request);
      return proxyJson(response, targetUrl, {
        method: 'POST',
        headers: {
          accept: 'application/json',
          'content-type': 'application/json'
        },
        body: JSON.stringify(body ?? {})
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
      return proxyJson(response, backendUrl, {
        headers: cmdbuildBackendAuthHeaders(request)
      });
    }

    if (url.pathname === '/api/cmdbuild/classes/schema' && request.method === 'GET') {
      return proxyJson(response, config.backend.cmdbuildClassSchemasUrl, {
        headers: cmdbuildBackendAuthHeaders(request)
      });
    }

    if (url.pathname === '/api/cmdbuild/classes/instances' && request.method === 'GET') {
      const backendUrl = new URL(config.backend.cmdbuildClassInstancesUrl);
      for (const key of ['prefix', 'serviceModelRoot', 'suppressionModelRoot']) {
        if (url.searchParams.has(key)) {
          backendUrl.searchParams.set(key, url.searchParams.get(key));
        }
      }
      return proxyJson(response, backendUrl, {
        headers: cmdbuildBackendAuthHeaders(request)
      });
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
      return proxyJson(response, backendUrl, {
        headers: cmdbuildBackendAuthHeaders(request)
      });
    }

    if (cardCreateMatch && request.method === 'POST') {
      const classCode = decodeURIComponent(cardCreateMatch[1]);
      const backendUrl = new URL(`${config.backend.cmdbuildClassesUrl}/${encodeURIComponent(classCode)}/cards`);
      const body = await readJsonBody(request);
      return proxyJson(response, backendUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: JSON.stringify(body)
      });
    }

    const cardUpdateMatch = url.pathname.match(/^\/api\/cmdbuild\/classes\/([^/]+)\/cards\/([^/]+)$/);
    if (cardUpdateMatch && request.method === 'PUT') {
      const classCode = decodeURIComponent(cardUpdateMatch[1]);
      const cardId = decodeURIComponent(cardUpdateMatch[2]);
      const backendUrl = new URL(`${config.backend.cmdbuildClassesUrl}/${encodeURIComponent(classCode)}/cards/${encodeURIComponent(cardId)}`);
      const body = await readJsonBody(request);
      return proxyJson(response, backendUrl, {
        method: 'PUT',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: JSON.stringify(body)
      });
    }

    if (cardUpdateMatch && request.method === 'DELETE') {
      const classCode = decodeURIComponent(cardUpdateMatch[1]);
      const cardId = decodeURIComponent(cardUpdateMatch[2]);
      const backendUrl = new URL(`${config.backend.cmdbuildClassesUrl}/${encodeURIComponent(classCode)}/cards/${encodeURIComponent(cardId)}`);
      return proxyJson(response, backendUrl, {
        method: 'DELETE',
        headers: cmdbuildBackendAuthHeaders(request)
      });
    }

    if (url.pathname === '/api/cmdbuild/domains' && request.method === 'GET') {
      const backendUrl = new URL(config.backend.cmdbuildDomainsUrl);
      if (url.searchParams.has('prefix')) {
        backendUrl.searchParams.set('prefix', url.searchParams.get('prefix'));
      }
      return proxyJson(response, backendUrl, {
        headers: cmdbuildBackendAuthHeaders(request)
      });
    }

    if (url.pathname === '/api/cmdbuild/domains/relations' && request.method === 'GET') {
      const backendUrl = appendPath(config.backend.cmdbuildDomainsUrl, 'relations');
      if (url.searchParams.has('prefix')) {
        backendUrl.searchParams.set('prefix', url.searchParams.get('prefix'));
      }
      return proxyJson(response, backendUrl, {
        headers: cmdbuildBackendAuthHeaders(request)
      });
    }

    const domainRelationCreateMatch = url.pathname.match(/^\/api\/cmdbuild\/domains\/([^/]+)\/relations$/);
    if (domainRelationCreateMatch && request.method === 'POST') {
      const domainCode = decodeURIComponent(domainRelationCreateMatch[1]);
      const backendUrl = appendPath(config.backend.cmdbuildDomainsUrl, domainCode, 'relations');
      const body = await readJsonBody(request);
      return proxyJson(response, backendUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        },
        body: JSON.stringify(body)
      });
    }

    const domainRelationDeleteMatch = url.pathname.match(/^\/api\/cmdbuild\/domains\/([^/]+)\/relations\/([^/]+)$/);
    if (domainRelationDeleteMatch && request.method === 'DELETE') {
      const domainCode = decodeURIComponent(domainRelationDeleteMatch[1]);
      const relationId = decodeURIComponent(domainRelationDeleteMatch[2]);
      const backendUrl = appendPath(config.backend.cmdbuildDomainsUrl, domainCode, 'relations', relationId);
      return proxyJson(response, backendUrl, {
        method: 'DELETE',
        headers: {
          accept: 'application/json',
          ...cmdbuildBackendAuthHeaders(request)
        }
      });
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
    } finally {
      recordHttpMetric(request, url, response, started);
    }
  });
});

const longRunningRequestTimeoutMs = Number.parseInt(
  process.env.MONITORING_UI_LONG_REQUEST_TIMEOUT_MS ?? '',
  10);
const effectiveLongRunningRequestTimeoutMs = Number.isInteger(longRunningRequestTimeoutMs) && longRunningRequestTimeoutMs > 0
  ? longRunningRequestTimeoutMs
  : 30 * 60 * 1000;
server.requestTimeout = effectiveLongRunningRequestTimeoutMs;
server.timeout = effectiveLongRunningRequestTimeoutMs;
server.headersTimeout = Math.min(60 * 1000, effectiveLongRunningRequestTimeoutMs);
server.keepAliveTimeout = 75 * 1000;

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
  const rewrittenConfig = rewriteKnownInternalUrls(configValue);
  const conversionConfig = {
    ...(rewrittenConfig.conversionConfig ?? {})
  };
  setIfPresent(conversionConfig, 'storeBackend', process.env.MONITORING_UI_CONVERSION_CONFIG_STORE_BACKEND);
  const postgres = {
    ...(conversionConfig.postgres ?? {})
  };
  setIfPresent(postgres, 'connectionString', process.env.MONITORING_UI_CONVERSION_CONFIG_POSTGRES_CONNECTION_STRING);
  setIfPresent(postgres, 'schema', process.env.MONITORING_UI_CONVERSION_CONFIG_POSTGRES_SCHEMA);
  setIfPresent(postgres, 'lockKey', process.env.MONITORING_UI_CONVERSION_CONFIG_POSTGRES_LOCK_KEY);
  if (Object.keys(postgres).length > 0) {
    conversionConfig.postgres = postgres;
  }
  const hostValidation = {
    ...(configValue.hostValidation ?? {})
  };
  setBooleanIfPresent(hostValidation, 'enabled', process.env.MONITORING_UI_HOST_VALIDATION_ENABLED);
  const allowedHosts = envIndexedValues('MONITORING_UI_ALLOWED_HOST_');
  const trustedProxies = {
    ...(configValue.trustedProxies ?? {})
  };
  setBooleanIfPresent(trustedProxies, 'enabled', process.env.MONITORING_UI_TRUSTED_PROXIES_ENABLED);
  const trustedProxyNetworks = envIndexedValues('MONITORING_UI_TRUSTED_PROXY_');
  const rateLimiting = {
    ...(configValue.rateLimiting ?? {})
  };
  setBooleanIfPresent(rateLimiting, 'trustForwardedFor', process.env.MONITORING_UI_RATE_LIMIT_TRUST_FORWARDED_FOR);
  const metrics = {
    ...(configValue.metrics ?? {})
  };
  setIfPresent(metrics, 'bearerToken', process.env.MONITORING_UI_METRICS_BEARER_TOKEN);
  setIfPresent(metrics, 'bearerTokenSecret', process.env.MONITORING_UI_METRICS_BEARER_TOKEN_SECRET);
  setBooleanIfPresent(metrics, 'requireBearerToken', process.env.MONITORING_UI_METRICS_REQUIRE_BEARER_TOKEN);
  const metricsAllowedNetworks = envIndexedValues('MONITORING_UI_METRICS_ALLOWED_NETWORK_');
  if (metricsAllowedNetworks.length > 0) {
    metrics.allowedNetworks = metricsAllowedNetworks;
  }
  const securityHeaders = {
    ...(configValue.securityHeaders ?? {})
  };
  setBooleanIfPresent(securityHeaders, 'hstsEnabled', process.env.MONITORING_UI_HSTS_ENABLED);
  return {
    ...rewrittenConfig,
    server: {
      ...serverConfig,
      host,
      port: Number.isInteger(port) && port > 0 ? port : serverConfig.port
    },
    allowedHosts: allowedHosts.length > 0 ? allowedHosts : configValue.allowedHosts,
    hostValidation,
    trustedProxies: {
      ...trustedProxies,
      networks: trustedProxyNetworks.length > 0 ? trustedProxyNetworks : trustedProxies.networks
    },
    rateLimiting,
    metrics,
    securityHeaders,
    conversionConfig
  };
}

function rewriteKnownInternalUrls(value) {
  const replacements = new Map([
    ['http://127.0.0.1:5180', process.env.MONITORING_UI_WEBHOOKS_BASE_URL],
    ['http://127.0.0.1:5181', process.env.MONITORING_UI_CMDBAGGREGATION_BASE_URL],
    ['http://127.0.0.1:5182', process.env.MONITORING_UI_CMDBCONFIGBUILDER_BASE_URL],
    ['http://127.0.0.1:5183', process.env.MONITORING_UI_ZABBIXCONFIG_BASE_URL],
    ['http://127.0.0.1:5184', process.env.MONITORING_UI_MATERIALIZER_BASE_URL]
  ]);

  return rewriteValue(value);

  function rewriteValue(item) {
    if (Array.isArray(item)) {
      return item.map(rewriteValue);
    }
    if (item && typeof item === 'object') {
      return Object.fromEntries(Object.entries(item).map(([key, child]) => [key, rewriteValue(child)]));
    }
    if (typeof item !== 'string') {
      return item;
    }

    for (const [from, to] of replacements.entries()) {
      if (to && item.startsWith(from)) {
        return `${to.replace(/\/+$/, '')}${item.slice(from.length)}`;
      }
    }
    return item;
  }
}

function setIfPresent(target, key, value) {
  if (value !== undefined && value !== null && value !== '') {
    target[key] = value;
  }
}

function setBooleanIfPresent(target, key, value) {
  if (value !== undefined && value !== null && value !== '') {
    target[key] = ['1', 'true', 'yes', 'on'].includes(String(value).trim().toLowerCase());
  }
}

function envIndexedValues(prefix) {
  return Object.entries(process.env)
    .filter(([key, value]) => key.startsWith(prefix) && stringValue(value))
    .sort(([left], [right]) => envIndex(left, prefix) - envIndex(right, prefix) || left.localeCompare(right))
    .map(([, value]) => stringValue(value));
}

function envIndex(key, prefix) {
  const parsed = Number.parseInt(key.slice(prefix.length), 10);
  return Number.isInteger(parsed) ? parsed : Number.MAX_SAFE_INTEGER;
}

function zabbixApplyCurrentBackendBody(body, layer, overrides = {}) {
  const directApplyUrl = stringValue(config.backend.zabbixCommandApplyUrl);
  const targets = Array.isArray(overrides.targets)
    ? overrides.targets
    : (directApplyUrl ? ['zabbix-direct'] : ['zabbix']);
  const dryRun = overrides.dryRun === undefined ? Boolean(body?.dryRun) : Boolean(overrides.dryRun);
  const publishMode = stringValue(body?.publishMode || body?.zabbixPublishMode) || 'changes';
  return {
    operationId: stringValue(overrides.operationId ?? body?.operationId) || randomUUID(),
    layers: [layer],
    targets,
    cmdbuildPrefix: stringValue(body?.cmdbuildPrefix || body?.prefix || config.cmdbuildSchema?.defaultPrefix),
    serviceModelRoot: stringValue(body?.serviceModelRoot),
    suppressionModelRoot: stringValue(body?.suppressionModelRoot),
    zabbixCommandApplyUrl: directApplyUrl,
    zabbixPublishMode: dryRun ? 'changes' : publishMode,
    buildMode: dryRun ? 'graph-overlay' : normalizeZabbixBuildMode(body?.buildMode || body?.zabbixBuildMode),
    topologyReadMode: dryRun ? 'rules' : normalizeZabbixTopologyReadMode(body?.topologyReadMode || body?.zabbixTopologyReadMode),
    zabbixScopeKeys: Array.isArray(body?.scopeKeys) ? body.scopeKeys.map((item) => stringValue(item)).filter(Boolean) : [],
    zabbixScopeDepth: Number.isInteger(body?.scopeDepth) ? body.scopeDepth : 0,
    requireZabbixScopeMatch: body?.requireScopeMatch === undefined
      ? Boolean(body?.requireZabbixScopeMatch)
      : Boolean(body.requireScopeMatch),
    dryRun,
    sourceClasses: Array.isArray(body?.sourceClasses) ? body.sourceClasses : [],
    maxCardsPerClass: Number.isInteger(body?.maxCardsPerClass) ? body.maxCardsPerClass : 0,
    eventType: stringValue(body?.eventType) || 'UPDATE'
  };
}

function normalizeZabbixBuildMode(value) {
  const normalized = stringValue(value).toLowerCase();
  return normalized === 'graph'
    || normalized === 'graph-overlay'
    || normalized === 'topology'
    || normalized === 'topology-only'
    ? 'graph-overlay'
    : 'membership';
}

function normalizeZabbixTopologyReadMode(value) {
  const normalized = stringValue(value).replaceAll('_', '-').toLowerCase();
  return ['rules', 'rule', 'scoped', 'scope', 'runtime-rules'].includes(normalized)
    ? 'rules'
    : ['full', 'cmdbuild', 'cmdbuild-full', 'legacy-full'].includes(normalized)
      ? 'full'
      : 'auto';
}

function createRequestContext(request, response) {
  const headerName = correlationConfig().headerName;
  const provided = stringValue(request.headers[headerName.toLowerCase()]);
  const correlationId = provided || randomUUID().replaceAll('-', '');
  response.setHeader(headerName, correlationId);
  return { correlationId };
}

function correlationConfig() {
  const configured = objectValue(config.correlation);
  return {
    enabled: configured.enabled !== false,
    headerName: stringValue(configured.headerName) || 'X-Correlation-Id'
  };
}

function metricsConfig() {
  const configured = objectValue(config.metrics);
  return {
    enabled: configured.enabled !== false,
    route: stringValue(configured.route) || '/metrics',
    requireBearerToken: configured.requireBearerToken === true,
    bearerToken: stringValue(configured.bearerToken),
    allowedNetworks: Array.isArray(configured.allowedNetworks)
      ? configured.allowedNetworks.map(stringValue).filter(Boolean)
      : []
  };
}

function readinessConfig() {
  const configured = objectValue(config.readiness);
  return {
    route: stringValue(configured.route) || '/ready'
  };
}

function hostValidationConfig() {
  const configured = objectValue(config.hostValidation);
  const rootAllowedHosts = Array.isArray(config.allowedHosts)
    ? config.allowedHosts.map(stringValue).filter(Boolean)
    : stringValue(config.allowedHosts).split(/[;,]/).map(stringValue).filter(Boolean);
  return {
    enabled: configured.enabled !== false,
    allowedHosts: rootAllowedHosts.length > 0
      ? rootAllowedHosts
      : (Array.isArray(configured.allowedHosts)
          ? configured.allowedHosts.map(stringValue).filter(Boolean)
          : ['localhost', '127.0.0.1', '::1'])
  };
}

function trustedProxiesConfig() {
  const configured = objectValue(config.trustedProxies);
  return {
    enabled: configured.enabled !== false,
    networks: Array.isArray(configured.networks)
      ? configured.networks.map(stringValue).filter(Boolean)
      : ['127.0.0.1', '::1']
  };
}

function rateLimitingConfig() {
  const configured = objectValue(config.rateLimiting);
  return {
    enabled: configured.enabled === true,
    permitLimit: positiveInteger(configured.permitLimit, 600),
    windowSeconds: positiveInteger(configured.windowSeconds, 60),
    trustForwardedFor: configured.trustForwardedFor !== false,
    excludedPathPrefixes: Array.isArray(configured.excludedPathPrefixes)
      ? configured.excludedPathPrefixes.map(stringValue).filter(Boolean)
      : ['/health', '/ready', '/metrics']
  };
}

function securityHeadersConfig() {
  const configured = objectValue(config.securityHeaders);
  return {
    enabled: configured.enabled !== false,
    hstsEnabled: configured.hstsEnabled === true,
    hstsMaxAgeSeconds: positiveInteger(configured.hstsMaxAgeSeconds, 31536000),
    contentSecurityPolicy: stringValue(configured.contentSecurityPolicy),
    frameOptions: stringValue(configured.frameOptions) || 'DENY',
    referrerPolicy: stringValue(configured.referrerPolicy) || 'no-referrer',
    permissionsPolicy: stringValue(configured.permissionsPolicy) || 'geolocation=(), microphone=(), camera=()'
  };
}

function applySecurityHeaders(response) {
  const headers = securityHeadersConfig();
  if (!headers.enabled) {
    return;
  }

  response.setHeader('X-Content-Type-Options', 'nosniff');
  response.setHeader('X-Frame-Options', headers.frameOptions);
  response.setHeader('Referrer-Policy', headers.referrerPolicy);
  response.setHeader('Permissions-Policy', headers.permissionsPolicy);
  if (headers.contentSecurityPolicy) {
    response.setHeader('Content-Security-Policy', headers.contentSecurityPolicy);
  }
  if (headers.hstsEnabled) {
    response.setHeader('Strict-Transport-Security', `max-age=${headers.hstsMaxAgeSeconds}; includeSubDomains`);
  }
}

function isRateLimited(request, url) {
  const settings = rateLimitingConfig();
  if (!settings.enabled || settings.excludedPathPrefixes.some((prefix) => url.pathname.startsWith(prefix))) {
    return false;
  }

  const remote = clientAddress(request, settings.trustForwardedFor);
  const key = `${remote}:${request.method ?? 'GET'}:${url.pathname}`;
  const now = Date.now();
  const current = rateLimitCounters.get(key);
  if (!current || now - current.windowStartedAt >= settings.windowSeconds * 1000) {
    rateLimitCounters.set(key, { windowStartedAt: now, count: 1 });
    return false;
  }

  current.count += 1;
  return current.count > settings.permitLimit;
}

function metricsAccessAllowed(request) {
  const metrics = metricsConfig();
  const tokenConfigured = Boolean(metrics.bearerToken);
  const networksConfigured = metrics.allowedNetworks.length > 0;
  if (!metrics.requireBearerToken && !tokenConfigured && !networksConfigured) {
    return true;
  }

  if (tokenConfigured && bearerTokenValid(request, metrics.bearerToken)) {
    return true;
  }

  return networksConfigured && addressInNetworks(remoteAddress(request), metrics.allowedNetworks);
}

function bearerTokenValid(request, expectedToken) {
  const authorization = stringValue(request.headers.authorization);
  const prefix = 'Bearer ';
  if (!authorization.toLowerCase().startsWith(prefix.toLowerCase())) {
    return false;
  }

  const provided = Buffer.from(authorization.slice(prefix.length).trim(), 'utf8');
  const expected = Buffer.from(expectedToken, 'utf8');
  return provided.length === expected.length && timingSafeEqual(provided, expected);
}

function readinessPayload() {
  return {
    service: 'monitoring-ui-api',
    status: 'ready',
    backendHealthChecks: Array.isArray(config.healthChecks) ? config.healthChecks.length : 0,
    conversionConfig: publicConversionConfig()
  };
}

function hostAllowed(hostHeader) {
  const settings = hostValidationConfig();
  if (!settings.enabled || settings.allowedHosts.includes('*')) {
    return true;
  }

  const host = normalizeHost(hostNameFromHeader(hostHeader));
  return Boolean(host)
    && settings.allowedHosts.some((allowed) => normalizeHost(allowed) === host);
}

function hostNameFromHeader(hostHeader) {
  const value = stringValue(hostHeader);
  if (value.startsWith('[')) {
    const closing = value.indexOf(']');
    return closing > 0 ? value.slice(1, closing) : value;
  }

  return value.split(':')[0];
}

function normalizeHost(host) {
  return stringValue(host).replace(/^\[/, '').replace(/\]$/, '').replace(/\.$/, '').toLowerCase();
}

function clientAddress(request, trustForwardedFor) {
  const remote = remoteAddress(request);
  const trustedProxies = trustedProxiesConfig();
  if (trustForwardedFor && (!trustedProxies.enabled || addressInNetworks(remote, trustedProxies.networks))) {
    const forwarded = stringValue(request.headers['x-forwarded-for']).split(',')[0].trim();
    return forwarded || remote || 'unknown';
  }

  return remote || 'unknown';
}

function remoteAddress(request) {
  return normalizeIpAddress(request.socket.remoteAddress || '');
}

function normalizeIpAddress(address) {
  const value = stringValue(address);
  return value.startsWith('::ffff:') ? value.slice('::ffff:'.length) : value;
}

function addressInNetworks(address, networks) {
  const normalized = normalizeIpAddress(address);
  return networks.some((network) => networkContains(network, normalized));
}

function networkContains(network, address) {
  const configured = stringValue(network);
  if (!configured || !address) {
    return false;
  }
  if (configured === '*') {
    return true;
  }

  const [base, prefixText] = configured.split('/');
  if (prefixText === undefined) {
    return normalizeIpAddress(base) === normalizeIpAddress(address);
  }

  const prefix = Number.parseInt(prefixText, 10);
  const addressValue = ipv4ToInt(address);
  const baseValue = ipv4ToInt(base);
  if (!Number.isInteger(prefix) || prefix < 0 || prefix > 32 || addressValue == null || baseValue == null) {
    return false;
  }

  const mask = prefix === 0 ? 0 : (0xFFFFFFFF << (32 - prefix)) >>> 0;
  return (addressValue & mask) === (baseValue & mask);
}

function ipv4ToInt(address) {
  const parts = normalizeIpAddress(address).split('.');
  if (parts.length !== 4) {
    return null;
  }

  let value = 0;
  for (const part of parts) {
    const octet = Number.parseInt(part, 10);
    if (!Number.isInteger(octet) || octet < 0 || octet > 255) {
      return null;
    }
    value = ((value << 8) | octet) >>> 0;
  }
  return value >>> 0;
}

function recordHttpMetric(request, url, response, started) {
  if (!metricsConfig().enabled) {
    return;
  }

  const elapsedSeconds = Number(process.hrtime.bigint() - started) / 1_000_000_000;
  const labels = {
    method: request.method ?? 'GET',
    path: url.pathname,
    status: String(response.statusCode || 0)
  };
  incrementMetric('http_requests_total', labels);
  incrementMetric('http_request_duration_seconds_count', labels);
  incrementMetric('http_request_duration_seconds_sum', labels, elapsedSeconds);
}

function incrementMetric(name, labels = {}, value = 1) {
  if (!metricsConfig().enabled) {
    return;
  }

  const key = metricKey(name, labels);
  metricCounters.set(key, (metricCounters.get(key) ?? 0) + value);
}

function metricKey(name, labels) {
  const renderedLabels = Object.entries(labels)
    .filter(([key]) => key)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${sanitizeMetricName(key)}="${escapeMetricLabel(stringValue(value))}"`)
    .join(',');
  return `${sanitizeMetricName(name)}${renderedLabels ? `{${renderedLabels}}` : ''}`;
}

function renderMetrics() {
  if (!metricsConfig().enabled) {
    return '';
  }

  return [...metricCounters.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${key} ${Number(value).toString()}`)
    .join('\n') + '\n';
}

function sanitizeMetricName(value) {
  return stringValue(value).replaceAll(/[^a-zA-Z0-9_:]/g, '_');
}

function escapeMetricLabel(value) {
  return stringValue(value)
    .replaceAll('\\', '\\\\')
    .replaceAll('\n', '\\n')
    .replaceAll('"', '\\"');
}

function positiveInteger(value, fallback) {
  const parsed = Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function currentCorrelationId() {
  return correlationConfig().enabled
    ? stringValue(requestContext.getStore()?.correlationId)
    : '';
}

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, { 'content-type': 'application/json; charset=utf-8' });
  response.end(JSON.stringify(body));
}

function sendText(response, statusCode, body, contentType) {
  response.writeHead(statusCode, { 'content-type': contentType });
  response.end(body);
}

async function proxyJson(response, targetUrl, init = undefined) {
  const backendInit = withCorrelationHeader(init ?? {
    headers: {
      accept: 'application/json'
    }
  });
  const backendResponse = await fetch(targetUrl, backendInit);
  const text = await backendResponse.text();

  response.writeHead(backendResponse.status, {
    'content-type': backendResponse.headers.get('content-type') ?? 'application/json; charset=utf-8'
  });
  response.end(text);
}

function withCorrelationHeader(init = {}) {
  const correlationId = currentCorrelationId();
  if (!correlationId) {
    return init;
  }

  const headerName = correlationConfig().headerName;
  return {
    ...init,
    headers: {
      ...(init.headers ?? {}),
      [headerName]: correlationId
    }
  };
}

function runDetachedJsonRequest(targetUrl, init, operationId) {
  void (async () => {
    try {
      const backendResponse = await httpRequestBuffer(targetUrl, init, effectiveLongRunningRequestTimeoutMs);
      if (backendResponse.statusCode < 200 || backendResponse.statusCode >= 300) {
        console.error(`detached request ${operationId} failed: HTTP ${backendResponse.statusCode}`);
      }
    } catch (error) {
      console.error(`detached request ${operationId} failed:`, error);
    }
  })();
}

function httpRequestBuffer(targetUrl, init = {}, timeoutMs = effectiveLongRunningRequestTimeoutMs) {
  return new Promise((resolve, reject) => {
    const effectiveInit = withCorrelationHeader(init);
    const parsedUrl = new URL(targetUrl);
    const transport = parsedUrl.protocol === 'https:' ? https : http;
    const request = transport.request(parsedUrl, {
      method: effectiveInit.method ?? 'GET',
      headers: effectiveInit.headers ?? {},
      timeout: timeoutMs
    }, (backendResponse) => {
      const chunks = [];
      backendResponse.on('data', (chunk) => {
        chunks.push(chunk);
      });
      backendResponse.on('end', () => {
        resolve({
          statusCode: backendResponse.statusCode ?? 0,
          headers: backendResponse.headers,
          body: Buffer.concat(chunks)
        });
      });
    });

    request.setTimeout(timeoutMs, () => {
      request.destroy(new Error(`request timed out after ${timeoutMs} ms`));
    });
    request.on('error', reject);
    if (effectiveInit.body != null) {
      request.write(effectiveInit.body);
    }
    request.end();
  });
}

function publicConversionConfig() {
  const storage = conversionConfigStorage();
  const runtime = conversionConfigRuntimeRulesFile();
  const store = conversionConfigStoreSettings();
  return {
    storeBackend: store.backend,
    storageFolder: storage.configuredFolder,
    resolvedStorageFolder: storage.folder,
    files: storage.files,
    runtimeRulesFile: runtime.configuredFile,
    resolvedRuntimeRulesFile: runtime.file
  };
}

function conversionConfigStorePublicInfo() {
  const storage = conversionConfigStorage();
  const store = conversionConfigStoreSettings();
  const postgres = store.postgres;
  return {
    api: 'conversion-config-store',
    backend: store.backend,
    lock: store.backend === 'postgresql' ? 'postgres_advisory_xact_lock' : 'process',
    auditFile: store.backend === 'folder' ? path.join(storage.folder, storage.files.audit) : '',
    migrationTarget: store.backend === 'folder' ? 'postgresql' : '',
    postgres: store.backend === 'postgresql'
      ? {
          schema: postgres.schema,
          connectionConfigured: Boolean(postgres.connectionString),
          connectionEndpoint: redactedConnectionEndpoint(postgres.connectionString),
          folderExportEnabled: postgres.exportFolder
        }
      : null
  };
}

async function withConversionConfigStoreWriteLock(operation) {
  const previous = conversionConfigStoreWriteChain;
  let release = () => {};
  conversionConfigStoreWriteChain = new Promise((resolve) => {
    release = resolve;
  });
  try {
    await previous.catch(() => {});
    return await operation();
  } finally {
    release();
  }
}

function publicCmdbuildConfig() {
  const cmdbuild = cmdbuildConfiguration();
  return {
    authMode: cmdbuild.apiToken ? 'Token' : (cmdbuild.authMode || 'Login'),
    username: cmdbuild.username,
    baseUrlConfigured: Boolean(cmdbuild.baseUrl),
    usernameConfigured: Boolean(cmdbuild.username),
    apiTokenConfigured: Boolean(cmdbuild.apiToken)
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

function cmdbuildBackendAuthHeaders(request) {
  return {
    ...cmdbuildConfiguredBackendAuthHeaders(),
    ...cmdbuildRequestBackendAuthHeaders(request)
  };
}

function cmdbuildConfiguredBackendAuthHeaders() {
  const cmdbuild = cmdbuildConfiguration();
  const headers = {};
  if (cmdbuild.baseUrl) {
    headers['x-cmdb2monitoring-cmdbuild-base-url'] = cmdbuild.baseUrl;
  }
  if (cmdbuild.requestTimeoutMs) {
    headers['x-cmdb2monitoring-cmdbuild-timeout-ms'] = String(cmdbuild.requestTimeoutMs);
  }
  if (cmdbuild.apiToken) {
    headers['x-cmdb2monitoring-cmdbuild-auth-mode'] = 'Token';
    headers['x-cmdb2monitoring-cmdbuild-api-token'] = cmdbuild.apiToken;
    return headers;
  }
  if (cmdbuild.username || cmdbuild.password) {
    headers['x-cmdb2monitoring-cmdbuild-auth-mode'] = 'Login';
    if (cmdbuild.username) {
      headers['x-cmdb2monitoring-cmdbuild-username'] = cmdbuild.username;
    }
    if (cmdbuild.password) {
      headers['x-cmdb2monitoring-cmdbuild-password'] = cmdbuild.password;
    }
  }
  return headers;
}

function cmdbuildRequestBackendAuthHeaders(request) {
  const headers = {};
  copyRequestHeader(request, headers, 'x-cmdb2monitoring-cmdbuild-auth-mode');
  copyRequestHeader(request, headers, 'x-cmdb2monitoring-cmdbuild-base-url');
  copyRequestHeader(request, headers, 'x-cmdb2monitoring-cmdbuild-username');
  copyRequestHeader(request, headers, 'x-cmdb2monitoring-cmdbuild-password');
  copyRequestHeader(request, headers, 'x-cmdb2monitoring-cmdbuild-api-token');
  copyRequestHeader(request, headers, 'x-cmdb2monitoring-cmdbuild-timeout-ms');
  return headers;
}

function copyRequestHeader(request, target, name) {
  const value = stringValue(request.headers[name]);
  if (value) {
    target[name] = value;
  }
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
      manifest: String(conversionConfig.manifestFile ?? 'manifest.json'),
      audit: String(conversionConfig.auditFile ?? 'audit.jsonl')
    }
  };
}

function conversionConfigStoreSettings() {
  const conversionConfig = config.conversionConfig ?? {};
  const backend = normalizeConversionConfigStoreBackend(
    conversionConfig.storeBackend
      ?? conversionConfig.storageBackend
      ?? conversionConfig.backend
      ?? 'folder');
  const postgres = conversionConfig.postgres ?? conversionConfig.Postgres ?? {};
  const schema = normalizePostgresIdentifier(
    postgres.schema
      ?? postgres.Schema
      ?? conversionConfig.postgresSchema
      ?? 'monitoring_ui');
  const lockKey = optionalNumber(
    postgres.lockKey
      ?? postgres.LockKey
      ?? conversionConfig.postgresLockKey) ?? 2024031901;
  return {
    backend,
    postgres: {
      connectionString: stringValue(
        postgres.connectionString
          ?? postgres.ConnectionString
          ?? conversionConfig.postgresConnectionString),
      schema,
      lockKey,
      exportFolder: postgres.exportFolder === undefined
        ? true
        : postgres.exportFolder !== false
    }
  };
}

function normalizeConversionConfigStoreBackend(value) {
  const normalized = stringValue(value || 'folder').toLowerCase();
  if (['postgres', 'postgresql', 'pg'].includes(normalized)) {
    return 'postgresql';
  }
  return 'folder';
}

function normalizePostgresIdentifier(value) {
  const text = stringValue(value) || 'monitoring_ui';
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(text)) {
    throw new Error(`conversionConfig.postgres.schema must be a PostgreSQL identifier, got '${text}'.`);
  }
  return text;
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

function zabbixconfig2apiConfigFile() {
  const section = config.managedMicroservices?.zabbixconfig2api ?? {};
  const configuredFile = String(section.configFile ?? 'src/zabbixconfig2api/appsettings.json');
  const file = path.isAbsolute(configuredFile)
    ? configuredFile
    : path.resolve(projectRoot, configuredFile);
  return {
    configuredFile,
    file,
    reloadApplierId: stringValue(section.reloadApplierId) || 'zabbixconfig2api'
  };
}

async function readZabbixconfig2apiSettings() {
  try {
    const fileInfo = zabbixconfig2apiConfigFile();
    const document = await readJsonFileIfExists(fileInfo.file);
    if (!document) {
      return {
        statusCode: 404,
        body: {
          success: false,
          error: `zabbixconfig2api config file not found: ${fileInfo.configuredFile}`,
          configFile: fileInfo.configuredFile,
          resolvedConfigFile: fileInfo.file
        }
      };
    }

    return {
      statusCode: 200,
      body: zabbixconfig2apiSettingsResponse(document, fileInfo)
    };
  } catch (error) {
    return {
      statusCode: 500,
      body: {
        success: false,
        error: error.message
      }
    };
  }
}

async function updateZabbixconfig2apiSettings(body) {
  let fileInfo = null;
  try {
    fileInfo = zabbixconfig2apiConfigFile();
    const document = await readJsonFileIfExists(fileInfo.file);
    if (!document) {
      return {
        statusCode: 404,
        body: {
          success: false,
          error: `zabbixconfig2api config file not found: ${fileInfo.configuredFile}`,
          configFile: fileInfo.configuredFile,
          resolvedConfigFile: fileInfo.file
        }
      };
    }

    const nextDocument = applyZabbixconfig2apiSettingsPatch(document, body ?? {});
    await writeJsonFile(fileInfo.file, nextDocument);
    const reload = await reloadApplierConfiguration(fileInfo.reloadApplierId);
    const responseBody = {
      ...zabbixconfig2apiSettingsResponse(nextDocument, fileInfo),
      reload: reload.body
    };
    if (reload.statusCode >= 400) {
      return {
        statusCode: reload.statusCode,
        body: {
          ...responseBody,
          success: false,
          error: reload.body?.error || 'zabbixconfig2api_reload_failed'
        }
      };
    }

    return {
      statusCode: 200,
      body: responseBody
    };
  } catch (error) {
    return {
      statusCode: 400,
      body: {
        success: false,
        error: error.message,
        configFile: fileInfo?.configuredFile ?? '',
        resolvedConfigFile: fileInfo?.file ?? ''
      }
    };
  }
}

function zabbixconfig2apiSettingsResponse(document, fileInfo) {
  return {
    success: true,
    service: 'zabbixconfig2api',
    configFile: fileInfo.configuredFile,
    resolvedConfigFile: fileInfo.file,
    reloadApplierId: fileInfo.reloadApplierId,
    settings: normalizeZabbixconfig2apiSettings(document)
  };
}

function normalizeZabbixconfig2apiSettings(document) {
  const dependencies = objectValue(document.ZabbixTriggerDependencies);
  const zabbix = objectValue(document.Zabbix);
  const sla = objectValue(document.ZabbixSla);
  const redis = objectValue(document.Redis);
  const durableStore = objectValue(document.DurableStore);
  const coverage = objectValue(document.MonitoringCoverageAudit);
  return {
    redis: {
      enabled: booleanValue(redis.Enabled, false),
      endpoint: redactedConnectionEndpoint(redis.ConnectionString),
      connectionConfigured: Boolean(stringValue(redis.ConnectionString)),
      keyPrefix: stringValue(redis.KeyPrefix) || 'cmdb2m:test',
      instanceId: stringValue(redis.InstanceId),
      operationTtlSeconds: integerValue(redis.OperationTtlSeconds, 86400),
      lockTtlSeconds: integerValue(redis.LockTtlSeconds, 300),
      lockExtendSeconds: integerValue(redis.LockExtendSeconds, 120),
      cacheDefaultTtlSeconds: integerValue(redis.CacheDefaultTtlSeconds, 300),
      failureMode: stringValue(redis.FailureMode) || 'fallback'
    },
    durableStore: {
      provider: stringValue(durableStore.Provider) || 'sqlite',
      endpoint: redactedConnectionEndpoint(durableStore.ConnectionString),
      connectionConfigured: Boolean(stringValue(durableStore.ConnectionString)),
      migrationsEnabled: booleanValue(durableStore.MigrationsEnabled, true)
    },
    monitoringCoverageAudit: {
      enabled: booleanValue(coverage.Enabled, true),
      snapshotRetentionDays: integerValue(coverage.SnapshotRetentionDays, 180),
      triggerMode: stringValue(coverage.TriggerMode) || 'manual',
      defaultExpectedPolicy: stringValue(coverage.DefaultExpectedPolicy) || 'rules_matched',
      hostIdAttribute: stringValue(coverage.HostIdAttribute) || 'zabbix_main_hostid',
      allowOperationalDelta: booleanValue(coverage.AllowOperationalDelta, true),
      maxOperationalDeltaMinutes: integerValue(coverage.MaxOperationalDeltaMinutes, 30),
      autoSnapshotAfterFullGraphApply: booleanValue(coverage.AutoSnapshotAfterFullGraphApply, false),
      autoSnapshotAfterScopedReconcile: booleanValue(coverage.AutoSnapshotAfterScopedReconcile, false),
      scheduledSnapshotCronConfigured: Boolean(stringValue(coverage.ScheduledSnapshotCron))
    },
    zabbixTriggerDependencies: {
      transitiveGroupDependencyDepth: integerValue(dependencies.TransitiveGroupDependencyDepth, 2),
      triggerGetBatchSize: integerValue(dependencies.TriggerGetBatchSize, 25),
      maxSourceHostsPerAggregate: integerValue(dependencies.MaxSourceHostsPerAggregate, 1000),
      maxAggregateFormulaLength: integerValue(dependencies.MaxAggregateFormulaLength, 65000),
      maxDependenciesPerRun: integerValue(dependencies.MaxDependenciesPerRun, 10000),
      sampleLimit: integerValue(dependencies.SampleLimit, 100),
      aggregateStateTriggerIncludeTags: normalizeTagSelectors(dependencies.AggregateStateTriggerIncludeTags),
      aggregateStateTriggerExcludeTags: normalizeTagSelectors(dependencies.AggregateStateTriggerExcludeTags),
      aggregateStateTriggerIncludeNameRegex: stringValue(dependencies.AggregateStateTriggerIncludeNameRegex),
      aggregateStateTriggerExcludeNameRegex: stringValue(dependencies.AggregateStateTriggerExcludeNameRegex),
      aggregateStateTriggerMinPriority: integerValue(dependencies.AggregateStateTriggerMinPriority, 0)
    },
    zabbix: {
      requestTimeoutMs: integerValue(zabbix.RequestTimeoutMs, 60000)
    },
    zabbixSla: {
      enabled: booleanValue(sla.Enabled, true),
      defaultPolicyKey: stringValue(sla.DefaultPolicyKey),
      downtimePublicationHorizonMonths: integerValue(sla.DowntimePublicationHorizonMonths, 6),
      managedExcludedDowntimePrefix: stringValue(sla.ManagedExcludedDowntimePrefix) || 'CMDB2M REG:',
      cmdbuildPrefix: stringValue(sla.CmdbuildPrefix) || 'C2M_',
      serviceRootPath: stringValue(sla.ServiceRootPath),
      defaultReportingPeriod: stringValue(sla.DefaultReportingPeriod) || 'monthly',
      defaultTimezone: stringValue(sla.DefaultTimezone) || 'Europe/Moscow',
      sampleLimit: integerValue(sla.SampleLimit, 100)
    }
  };
}

function applyZabbixconfig2apiSettingsPatch(document, body) {
  const next = JSON.parse(JSON.stringify(document ?? {}));
  if (body.redis) {
    const source = objectValue(body.redis);
    const target = ensureObjectSection(next, 'Redis');
    if ('enabled' in source) {
      target.Enabled = booleanValue(source.enabled, false);
    }
    if ('keyPrefix' in source) {
      const keyPrefix = stringValue(source.keyPrefix);
      if (!keyPrefix) {
        throw new Error('Redis:KeyPrefix is required.');
      }
      target.KeyPrefix = keyPrefix;
    }
    if ('operationTtlSeconds' in source) {
      target.OperationTtlSeconds = validatedInteger(source.operationTtlSeconds, 'Redis:OperationTtlSeconds', 1, 604800);
    }
    if ('lockTtlSeconds' in source) {
      target.LockTtlSeconds = validatedInteger(source.lockTtlSeconds, 'Redis:LockTtlSeconds', 1, 86400);
    }
    if ('lockExtendSeconds' in source) {
      target.LockExtendSeconds = validatedInteger(source.lockExtendSeconds, 'Redis:LockExtendSeconds', 1, 86400);
    }
    if ('cacheDefaultTtlSeconds' in source) {
      target.CacheDefaultTtlSeconds = validatedInteger(source.cacheDefaultTtlSeconds, 'Redis:CacheDefaultTtlSeconds', 1, 86400);
    }
    if ('failureMode' in source) {
      target.FailureMode = validatedChoice(source.failureMode, 'Redis:FailureMode', ['fallback', 'fail']);
    }
  }

  if (body.durableStore) {
    const source = objectValue(body.durableStore);
    const target = ensureObjectSection(next, 'DurableStore');
    if ('provider' in source) {
      target.Provider = validatedChoice(source.provider, 'DurableStore:Provider', ['file', 'sqlite']);
    }
    if ('migrationsEnabled' in source) {
      target.MigrationsEnabled = booleanValue(source.migrationsEnabled, true);
    }
  }

  if (body.monitoringCoverageAudit) {
    const source = objectValue(body.monitoringCoverageAudit);
    const target = ensureObjectSection(next, 'MonitoringCoverageAudit');
    if ('enabled' in source) {
      target.Enabled = booleanValue(source.enabled, true);
    }
    if ('snapshotRetentionDays' in source) {
      target.SnapshotRetentionDays = validatedInteger(source.snapshotRetentionDays, 'MonitoringCoverageAudit:SnapshotRetentionDays', 1, 3650);
    }
    if ('triggerMode' in source) {
      target.TriggerMode = validatedChoice(source.triggerMode, 'MonitoringCoverageAudit:TriggerMode', ['manual', 'scheduled', 'manual_and_scheduled']);
    }
    if ('defaultExpectedPolicy' in source) {
      target.DefaultExpectedPolicy = validatedChoice(source.defaultExpectedPolicy, 'MonitoringCoverageAudit:DefaultExpectedPolicy', ['rules_matched', 'class_policy', 'explicit_attribute', 'manual_scope']);
    }
    if ('hostIdAttribute' in source) {
      const hostIdAttribute = stringValue(source.hostIdAttribute);
      if (!hostIdAttribute) {
        throw new Error('MonitoringCoverageAudit:HostIdAttribute is required.');
      }
      target.HostIdAttribute = hostIdAttribute;
    }
    if ('allowOperationalDelta' in source) {
      target.AllowOperationalDelta = booleanValue(source.allowOperationalDelta, true);
    }
    if ('maxOperationalDeltaMinutes' in source) {
      target.MaxOperationalDeltaMinutes = validatedInteger(source.maxOperationalDeltaMinutes, 'MonitoringCoverageAudit:MaxOperationalDeltaMinutes', 0, 1440);
    }
    if ('autoSnapshotAfterFullGraphApply' in source) {
      target.AutoSnapshotAfterFullGraphApply = booleanValue(source.autoSnapshotAfterFullGraphApply, false);
    }
    if ('autoSnapshotAfterScopedReconcile' in source) {
      target.AutoSnapshotAfterScopedReconcile = booleanValue(source.autoSnapshotAfterScopedReconcile, false);
    }
  }

  if (body.zabbixTriggerDependencies) {
    const source = objectValue(body.zabbixTriggerDependencies);
    const target = ensureObjectSection(next, 'ZabbixTriggerDependencies');
    if ('transitiveGroupDependencyDepth' in source) {
      target.TransitiveGroupDependencyDepth = validatedInteger(source.transitiveGroupDependencyDepth, 'ZabbixTriggerDependencies:TransitiveGroupDependencyDepth', 1, 3);
    }
    if ('triggerGetBatchSize' in source) {
      target.TriggerGetBatchSize = validatedInteger(source.triggerGetBatchSize, 'ZabbixTriggerDependencies:TriggerGetBatchSize', 1, 100);
    }
    if ('maxSourceHostsPerAggregate' in source) {
      target.MaxSourceHostsPerAggregate = validatedInteger(source.maxSourceHostsPerAggregate, 'ZabbixTriggerDependencies:MaxSourceHostsPerAggregate', 1, 100000);
    }
    if ('maxAggregateFormulaLength' in source) {
      target.MaxAggregateFormulaLength = validatedInteger(source.maxAggregateFormulaLength, 'ZabbixTriggerDependencies:MaxAggregateFormulaLength', 1000, 1000000);
    }
    if ('maxDependenciesPerRun' in source) {
      target.MaxDependenciesPerRun = validatedInteger(source.maxDependenciesPerRun, 'ZabbixTriggerDependencies:MaxDependenciesPerRun', 1, 1000000);
    }
    if ('sampleLimit' in source) {
      target.SampleLimit = validatedInteger(source.sampleLimit, 'ZabbixTriggerDependencies:SampleLimit', 1, 10000);
    }
    if ('aggregateStateTriggerIncludeTags' in source) {
      target.AggregateStateTriggerIncludeTags = normalizeTagSelectors(source.aggregateStateTriggerIncludeTags)
        .map((item) => ({ Tag: item.tag, Value: item.value }));
    }
    if ('aggregateStateTriggerExcludeTags' in source) {
      target.AggregateStateTriggerExcludeTags = normalizeTagSelectors(source.aggregateStateTriggerExcludeTags)
        .map((item) => ({ Tag: item.tag, Value: item.value }));
    }
    if ('aggregateStateTriggerIncludeNameRegex' in source) {
      target.AggregateStateTriggerIncludeNameRegex = stringValue(source.aggregateStateTriggerIncludeNameRegex);
    }
    if ('aggregateStateTriggerExcludeNameRegex' in source) {
      target.AggregateStateTriggerExcludeNameRegex = stringValue(source.aggregateStateTriggerExcludeNameRegex);
    }
    if ('aggregateStateTriggerMinPriority' in source) {
      target.AggregateStateTriggerMinPriority = validatedInteger(source.aggregateStateTriggerMinPriority, 'ZabbixTriggerDependencies:AggregateStateTriggerMinPriority', 0, 5);
    }
  }

  if (body.zabbix) {
    const source = objectValue(body.zabbix);
    const target = ensureObjectSection(next, 'Zabbix');
    if ('requestTimeoutMs' in source) {
      target.RequestTimeoutMs = validatedInteger(source.requestTimeoutMs, 'Zabbix:RequestTimeoutMs', 1, 600000);
    }
  }

  if (body.zabbixSla) {
    const source = objectValue(body.zabbixSla);
    const target = ensureObjectSection(next, 'ZabbixSla');
    if ('enabled' in source) {
      target.Enabled = booleanValue(source.enabled, true);
    }
    if ('defaultPolicyKey' in source) {
      target.DefaultPolicyKey = stringValue(source.defaultPolicyKey);
    }
    if ('downtimePublicationHorizonMonths' in source) {
      target.DowntimePublicationHorizonMonths = validatedInteger(source.downtimePublicationHorizonMonths, 'ZabbixSla:DowntimePublicationHorizonMonths', 1, 24);
    }
    if ('managedExcludedDowntimePrefix' in source) {
      const prefix = stringValue(source.managedExcludedDowntimePrefix);
      if (!prefix) {
        throw new Error('ZabbixSla:ManagedExcludedDowntimePrefix is required.');
      }
      target.ManagedExcludedDowntimePrefix = prefix;
    }
    if ('sampleLimit' in source) {
      target.SampleLimit = validatedInteger(source.sampleLimit, 'ZabbixSla:SampleLimit', 1, 10000);
    }
  }

  return next;
}

function ensureObjectSection(document, sectionName) {
  if (!document[sectionName] || typeof document[sectionName] !== 'object' || Array.isArray(document[sectionName])) {
    document[sectionName] = {};
  }

  return document[sectionName];
}

function objectValue(value) {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value
    : {};
}

function integerValue(value, fallback) {
  const parsed = Number(value);
  return Number.isInteger(parsed) ? parsed : fallback;
}

function validatedInteger(value, name, min, max) {
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < min || parsed > max) {
    throw new Error(`${name} must be an integer from ${min} to ${max}.`);
  }

  return parsed;
}

function validatedChoice(value, name, allowed) {
  const text = stringValue(value);
  if (!allowed.includes(text)) {
    throw new Error(`${name} must be one of: ${allowed.join(', ')}.`);
  }

  return text;
}

function booleanValue(value, fallback) {
  if (typeof value === 'boolean') {
    return value;
  }
  const normalized = stringValue(value).toLowerCase();
  if (['true', '1', 'yes', 'on', 'enabled', 'включено'].includes(normalized)) {
    return true;
  }
  if (['false', '0', 'no', 'off', 'disabled', 'выключено'].includes(normalized)) {
    return false;
  }

  return fallback;
}

function normalizeTagSelectors(value) {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .map((item) => ({
      tag: stringValue(item?.tag ?? item?.Tag),
      value: stringValue(item?.value ?? item?.Value)
    }))
    .filter((item) => item.tag);
}

function redactedConnectionEndpoint(connectionString) {
  const text = stringValue(connectionString);
  if (!text) {
    return '';
  }

  return text
    .split(';')
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const index = part.indexOf('=');
      if (index <= 0) {
        return part.includes('@') ? '***' : part;
      }

      const key = part.slice(0, index).trim();
      const value = part.slice(index + 1).trim();
      return ['password', 'pwd', 'user id', 'userid', 'username', 'uid', 'token', 'access key'].includes(key.toLowerCase())
        ? `${key}=***`
        : `${key}=${value}`;
    })
    .join(';');
}

async function readConversionConfigStoreCurrent() {
  const payload = conversionConfigStoreSettings().backend === 'postgresql'
    ? await readConversionConfigPostgres()
    : await readConversionConfigStorage();
  return {
    ...payload,
    store: conversionConfigStorePublicInfo()
  };
}

async function saveConversionConfigStoreAuthoring(body, context = {}) {
  if (conversionConfigStoreSettings().backend === 'postgresql') {
    return writeConversionConfigPostgres(body, context);
  }
  return withConversionConfigStoreWriteLock(() => writeConversionConfigStorageUnlocked(body, context));
}

async function deployConversionConfigStoreToRuntime(body, context = {}) {
  if (conversionConfigStoreSettings().backend === 'postgresql') {
    return deployConversionConfigToRuntimePostgres(body, context);
  }
  return withConversionConfigStoreWriteLock(() => deployConversionConfigToRuntimeUnlocked(body, context));
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

async function deployConversionConfigToRuntimeUnlocked(body, context = {}) {
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

  const storageResult = await writeConversionConfigStorageUnlocked(body, context);
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
      store: storageResult.body.store,
      version: storageResult.body.version,
      etag: storageResult.body.etag,
      runtimeRules: runtimePublicInfo(runtime, document, runtimeBuild),
      validation: validation.payload ?? null,
      rulesStatus: rulesStatus?.ok ? rulesStatus.payload : null,
      rulesStatusError: rulesStatus && !rulesStatus.ok ? rulesStatus.error : ''
    }
  };
}

async function deployConversionConfigToRuntimePostgres(body, context = {}) {
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

  const storageResult = await writeConversionConfigPostgres(body, context);
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
      store: storageResult.body.store,
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

async function writeConversionConfigStorageUnlocked(body, context = {}) {
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
    writer: stringValue(context.actor) || 'monitoring-ui-api',
    files: storage.files
  };

  await exportConversionConfigPayloadToFolder(storage, nextPayload, manifest);
  await appendConversionConfigAudit({
    actor: manifest.writer,
    changeType: stringValue(context.changeType) || 'authoring_change',
    reason: stringValue(context.reason),
    previousVersion: current.version,
    previousEtag: current.etag,
    version: nextVersion,
    etag,
    savedAt,
    storageFolder: storage.configuredFolder
  });

  return {
    statusCode: 200,
    body: {
      success: true,
      storage: publicConversionConfig(),
      store: conversionConfigStorePublicInfo(),
      version: nextVersion,
      etag,
      savedAt,
      prefix: manifest.prefix
    }
  };
}

async function exportConversionConfigPayloadToFolder(storage, payload, manifest) {
  await mkdir(storage.folder, { recursive: true });
  await Promise.all([
    writeJsonFile(path.join(storage.folder, storage.files.serviceRules), payload.ruleDocuments.service),
    writeJsonFile(path.join(storage.folder, storage.files.suppressionRules), payload.ruleDocuments.suppression),
    writeJsonFile(path.join(storage.folder, storage.files.serviceTemplates), payload.templateDocuments.service),
    writeJsonFile(path.join(storage.folder, storage.files.suppressionTemplates), payload.templateDocuments.suppression),
    writeJsonFile(path.join(storage.folder, storage.files.sharedTemplates), payload.templateDocuments.shared)
  ]);
  await writeJsonFile(path.join(storage.folder, storage.files.manifest), manifest);
}

async function readConversionConfigPostgres() {
  return withConversionConfigPostgresClient(async (client) => {
    await ensureConversionConfigPostgresSchema(client);
    const current = await readConversionConfigPostgresCurrent(client);
    if (current) {
      return current;
    }

    const folderPayload = await readConversionConfigStorage();
    return {
      ...folderPayload,
      migration: {
        importRequired: folderPayload.exists,
        sourceBackend: 'folder',
        message: folderPayload.exists
          ? 'PostgreSQL conversion-config-store is empty; the next save imports the current folder state as the previous version.'
          : ''
      }
    };
  });
}

async function writeConversionConfigPostgres(body, context = {}) {
  const storage = conversionConfigStorage();
  const store = conversionConfigStoreSettings();
  const savedAt = new Date().toISOString();
  const ruleDocuments = body?.ruleDocuments ?? {};
  const templateDocuments = body?.templateDocuments ?? {};
  let exportPayload = null;
  let exportManifest = null;
  const result = await withConversionConfigPostgresClient(async (client) => {
    await ensureConversionConfigPostgresSchema(client);
    await client.query('BEGIN');
    try {
      await client.query('SELECT pg_advisory_xact_lock($1)', [store.postgres.lockKey]);
      const current = await readConversionConfigPostgresCurrent(client) ?? await readConversionConfigStorage();
      const conflict = storageWriteConflict(body, current);
      if (conflict) {
        await client.query('ROLLBACK');
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
      const writer = stringValue(context.actor) || 'monitoring-ui-api';
      const changeType = stringValue(context.changeType) || 'authoring_change';
      const reason = stringValue(context.reason);
      const manifest = {
        schemaVersion: 1,
        version: nextVersion,
        etag,
        savedAt,
        prefix: nextPayload.prefix,
        writer,
        files: storage.files,
        storeBackend: 'postgresql',
        postgresSchema: store.postgres.schema,
        exportedToFolder: store.postgres.exportFolder
      };
      const tables = conversionConfigPostgresTables();
      await client.query(
        `INSERT INTO ${tables.documents}
          (version, etag, prefix, rule_documents, template_documents, manifest, saved_at, writer, change_type, reason)
         VALUES ($1, $2, $3, $4::jsonb, $5::jsonb, $6::jsonb, $7, $8, $9, $10)`,
        [
          nextVersion,
          etag,
          nextPayload.prefix,
          JSON.stringify(nextPayload.ruleDocuments),
          JSON.stringify(nextPayload.templateDocuments),
          JSON.stringify(manifest),
          savedAt,
          writer,
          changeType,
          reason
        ]);
      await insertConversionConfigPostgresAudit(client, {
        actor: writer,
        changeType,
        reason,
        previousVersion: current.version,
        previousEtag: current.etag,
        version: nextVersion,
        etag,
        savedAt,
        storageFolder: storage.configuredFolder,
        storeBackend: 'postgresql'
      });
      await client.query('COMMIT');
      exportPayload = nextPayload;
      exportManifest = manifest;
      return {
        statusCode: 200,
        body: {
          success: true,
          storage: publicConversionConfig(),
          store: conversionConfigStorePublicInfo(),
          version: nextVersion,
          etag,
          savedAt,
          prefix: manifest.prefix,
          folderExported: false
        }
      };
    } catch (error) {
      await client.query('ROLLBACK').catch(() => {});
      throw error;
    }
  });

  if (result.statusCode !== 200 || !store.postgres.exportFolder) {
    return result;
  }

  try {
    await exportConversionConfigPayloadToFolder(storage, exportPayload, exportManifest);
    return {
      ...result,
      body: {
        ...result.body,
        folderExported: true
      }
    };
  } catch (error) {
    return {
      statusCode: 500,
      body: {
        success: false,
        error: 'conversion_config_folder_export_failed',
        message: error.message,
        committed: true,
        version: result.body.version,
        etag: result.body.etag,
        savedAt: result.body.savedAt,
        store: conversionConfigStorePublicInfo()
      }
    };
  }
}

async function readConversionConfigPostgresAudit(limit = 100) {
  return withConversionConfigPostgresClient(async (client) => {
    await ensureConversionConfigPostgresSchema(client);
    const parsedLimit = Math.max(1, Math.min(1000, Number(limit) || 100));
    const tables = conversionConfigPostgresTables();
    const result = await client.query(
      `SELECT event_id, saved_at, actor, change_type, reason, previous_version,
              previous_etag, version, etag, storage_folder, payload
         FROM ${tables.audit}
        ORDER BY saved_at DESC
        LIMIT $1`,
      [parsedLimit]);
    return {
      success: true,
      store: conversionConfigStorePublicInfo(),
      entries: result.rows.map((row) => ({
        schemaVersion: 1,
        eventId: stringValue(row.event_id),
        savedAt: isoTimestamp(row.saved_at),
        actor: stringValue(row.actor),
        changeType: stringValue(row.change_type),
        reason: stringValue(row.reason),
        previousVersion: row.previous_version == null ? 0 : Number(row.previous_version),
        previousEtag: stringValue(row.previous_etag),
        version: row.version == null ? 0 : Number(row.version),
        etag: stringValue(row.etag),
        storageFolder: stringValue(row.storage_folder),
        ...(jsonValue(row.payload, {}) ?? {})
      }))
    };
  });
}

async function readConversionConfigPostgresCurrent(client) {
  const tables = conversionConfigPostgresTables();
  const result = await client.query(
    `SELECT version, etag, prefix, rule_documents, template_documents, manifest, saved_at
       FROM ${tables.documents}
      ORDER BY version DESC
      LIMIT 1`);
  const row = result.rows[0];
  if (!row) {
    return null;
  }

  const ruleDocuments = jsonValue(row.rule_documents, {});
  const templateDocuments = jsonValue(row.template_documents, {});
  const manifest = jsonValue(row.manifest, {});
  return {
    success: true,
    exists: true,
    storage: publicConversionConfig(),
    version: Number(row.version) || storageVersion(manifest),
    etag: stringValue(row.etag) || storageEtag(manifest, {
      prefix: row.prefix ?? '',
      ruleDocuments,
      templateDocuments
    }),
    savedAt: isoTimestamp(row.saved_at) || stringValue(manifest?.savedAt),
    prefix: stringValue(row.prefix ?? manifest?.prefix),
    ruleDocuments: {
      service: ruleDocuments?.service ?? null,
      suppression: ruleDocuments?.suppression ?? null
    },
    templateDocuments: {
      service: templateDocuments?.service ?? null,
      suppression: templateDocuments?.suppression ?? null,
      shared: templateDocuments?.shared ?? null
    }
  };
}

async function insertConversionConfigPostgresAudit(client, entry) {
  const tables = conversionConfigPostgresTables();
  const eventId = randomUUID();
  await client.query(
    `INSERT INTO ${tables.audit}
      (event_id, saved_at, actor, change_type, reason, previous_version,
       previous_etag, version, etag, storage_folder, payload)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11::jsonb)`,
    [
      eventId,
      entry.savedAt ?? new Date().toISOString(),
      stringValue(entry.actor) || 'conversion-config-store',
      stringValue(entry.changeType) || 'authoring_change',
      stringValue(entry.reason),
      optionalNumber(entry.previousVersion),
      stringValue(entry.previousEtag),
      optionalNumber(entry.version),
      stringValue(entry.etag),
      stringValue(entry.storageFolder),
      JSON.stringify({
        storeBackend: stringValue(entry.storeBackend) || 'postgresql'
      })
    ]);
}

async function withConversionConfigPostgresClient(operation) {
  const pool = await conversionConfigPostgresPool();
  const client = await pool.connect();
  try {
    return await operation(client);
  } finally {
    client.release();
  }
}

async function conversionConfigPostgresPool() {
  if (conversionConfigPostgresPoolPromise) {
    return conversionConfigPostgresPoolPromise;
  }

  const store = conversionConfigStoreSettings();
  if (!store.postgres.connectionString) {
    throw new Error('conversionConfig.postgres.connectionString is required when conversionConfig.storeBackend=postgresql.');
  }

  conversionConfigPostgresPoolPromise = import('pg')
    .catch((error) => {
      throw new Error(`PostgreSQL conversion-config-store requires the 'pg' npm package. Run 'npm --prefix src/monitoring-ui-api install'. ${error.message}`);
    })
    .then((pgModule) => {
      const Pool = pgModule.Pool ?? pgModule.default?.Pool;
      if (!Pool) {
        throw new Error("PostgreSQL conversion-config-store could not load 'pg.Pool'.");
      }
      return new Pool({
        connectionString: store.postgres.connectionString,
        application_name: 'monitoring-ui-api conversion-config-store'
      });
    });
  return conversionConfigPostgresPoolPromise;
}

async function ensureConversionConfigPostgresSchema(client) {
  const tables = conversionConfigPostgresTables();
  await client.query(`CREATE SCHEMA IF NOT EXISTS ${tables.schema}`);
  await client.query(`
    CREATE TABLE IF NOT EXISTS ${tables.documents} (
      version integer PRIMARY KEY,
      etag text NOT NULL,
      prefix text NOT NULL DEFAULT '',
      rule_documents jsonb NOT NULL,
      template_documents jsonb NOT NULL,
      manifest jsonb NOT NULL,
      saved_at timestamptz NOT NULL,
      writer text NOT NULL,
      change_type text NOT NULL DEFAULT '',
      reason text NOT NULL DEFAULT ''
    )`);
  await client.query(`
    CREATE TABLE IF NOT EXISTS ${tables.materializationJobs} (
      job_id text PRIMARY KEY,
      idempotency_key text NOT NULL UNIQUE,
      status text NOT NULL,
      request_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
      result_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
      attempts integer NOT NULL DEFAULT 0,
      locked_by text NOT NULL DEFAULT '',
      locked_at timestamptz NULL,
      created_at timestamptz NOT NULL DEFAULT now(),
      updated_at timestamptz NOT NULL DEFAULT now()
    )`);
  await client.query(`
    CREATE TABLE IF NOT EXISTS ${tables.materializedDimensions} (
      layer text NOT NULL,
      template_id text NOT NULL,
      dimension_key text NOT NULL,
      dimension_value text NOT NULL DEFAULT '',
      source_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
      config_version integer NULL,
      first_seen_at timestamptz NOT NULL DEFAULT now(),
      last_seen_at timestamptz NOT NULL DEFAULT now(),
      PRIMARY KEY (layer, template_id, dimension_key)
    )`);
  await client.query(`
    CREATE TABLE IF NOT EXISTS ${tables.locks} (
      lock_name text PRIMARY KEY,
      owner text NOT NULL,
      lock_reason text NOT NULL DEFAULT '',
      locked_at timestamptz NOT NULL DEFAULT now(),
      expires_at timestamptz NULL
    )`);
  await client.query(`
    CREATE TABLE IF NOT EXISTS ${tables.audit} (
      event_id text PRIMARY KEY,
      saved_at timestamptz NOT NULL,
      actor text NOT NULL,
      change_type text NOT NULL,
      reason text NOT NULL DEFAULT '',
      previous_version integer NULL,
      previous_etag text NOT NULL DEFAULT '',
      version integer NULL,
      etag text NOT NULL DEFAULT '',
      storage_folder text NOT NULL DEFAULT '',
      payload jsonb NOT NULL DEFAULT '{}'::jsonb
    )`);
  await client.query(`CREATE INDEX IF NOT EXISTS conversion_config_audit_saved_at_idx ON ${tables.audit} (saved_at DESC)`);
  await client.query(`CREATE INDEX IF NOT EXISTS conversion_config_materialization_jobs_status_idx ON ${tables.materializationJobs} (status, updated_at DESC)`);
  await client.query(`CREATE INDEX IF NOT EXISTS conversion_config_materialized_dimensions_version_idx ON ${tables.materializedDimensions} (config_version)`);
}

function conversionConfigPostgresTables() {
  const schema = quotePostgresIdentifier(conversionConfigStoreSettings().postgres.schema);
  return {
    schema,
    documents: `${schema}.conversion_config_documents`,
    materializationJobs: `${schema}.conversion_config_materialization_jobs`,
    materializedDimensions: `${schema}.conversion_config_materialized_dimensions`,
    locks: `${schema}.conversion_config_locks`,
    audit: `${schema}.conversion_config_audit`
  };
}

function quotePostgresIdentifier(value) {
  return `"${normalizePostgresIdentifier(value).replaceAll('"', '""')}"`;
}

async function appendConversionConfigAudit(entry) {
  const storage = conversionConfigStorage();
  const auditPath = path.join(storage.folder, storage.files.audit);
  await mkdir(path.dirname(auditPath), { recursive: true });
  const payload = {
    schemaVersion: 1,
    eventId: randomUUID(),
    savedAt: new Date().toISOString(),
    ...entry
  };
  await appendFile(auditPath, `${JSON.stringify(payload)}\n`, 'utf8');
}

async function readConversionConfigStoreAudit(limit = 100) {
  if (conversionConfigStoreSettings().backend === 'postgresql') {
    return readConversionConfigPostgresAudit(limit);
  }

  const storage = conversionConfigStorage();
  const auditPath = path.join(storage.folder, storage.files.audit);
  try {
    const text = await readFile(auditPath, 'utf8');
    const parsedLimit = Math.max(1, Math.min(1000, Number(limit) || 100));
    const entries = text
      .split('\n')
      .map((line) => line.trim())
      .filter(Boolean)
      .map((line) => {
        try {
          return JSON.parse(line);
        } catch (error) {
          return {
            schemaVersion: 1,
            eventId: '',
            savedAt: '',
            actor: 'conversion-config-store',
            changeType: 'audit_parse_error',
            reason: error.message,
            raw: line.slice(0, 500)
          };
        }
      });
    return {
      success: true,
      store: conversionConfigStorePublicInfo(),
      entries: entries.slice(-parsedLimit).reverse()
    };
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return {
        success: true,
        store: conversionConfigStorePublicInfo(),
        entries: []
      };
    }
    throw error;
  }
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
    return `Stored conversion config is v${current.version}, but editor is based on v${expectedVersion}. Reload conversion config before saving.`;
  }

  if (expectedEtag && current.etag && expectedEtag !== current.etag) {
    return `Stored conversion config etag changed from ${expectedEtag} to ${current.etag}. Reload conversion config before saving.`;
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

function jsonValue(value, fallback = null) {
  if (value === undefined || value === null) {
    return fallback;
  }
  if (typeof value === 'string') {
    try {
      return JSON.parse(value);
    } catch {
      return fallback;
    }
  }
  return value;
}

function isoTimestamp(value) {
  if (!value) {
    return '';
  }
  if (value instanceof Date) {
    return value.toISOString();
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? stringValue(value) : date.toISOString();
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
        requiredFields: new Set(readinessWebhookPayloadFields()),
        payloadFields: new Set(readinessWebhookPayloadFields())
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

  const readinessFields = readinessWebhookPayloadFields();
  return {
    code,
    displayName: stringValue(item?.displayName ?? item?.name ?? item?.description) || code,
    requiredFields: [...new Set([
      ...stringArray(item?.requiredFields ?? item?.required_fields),
      ...readinessFields
    ])],
    payloadFields: [...new Set([
      ...stringArray(item?.payloadFields ?? item?.payload_fields),
      ...readinessFields
    ])]
  };
}

function readinessWebhookPayloadFields() {
  const hostIdAttribute = stringValue(config.readiness?.zabbixHostIdAttribute) || 'zabbix_main_hostid';
  return [hostIdAttribute]
    .map(stringValue)
    .filter((field) => isSafeCmdbuildAttributeName(field) && !isWebhookSystemPayloadField(field));
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
  const payloadFields = new Set([
    ...(sourceClass.payloadFields ?? []),
    ...readinessWebhookPayloadFields()
  ]);
  for (const field of payloadFields) {
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
    authMode: stringValue(section.authMode ?? section.AuthMode),
    username: stringValue(section.username ?? section.Username),
    password: stringValue(section.password ?? section.Password),
    apiToken: stringValue(section.apiToken ?? section.ApiToken),
    requestTimeoutMs: Number(section.timeoutMs ?? section.requestTimeoutMs ?? section.RequestTimeoutMs ?? 0)
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
  if (config.appliers?.reloadEnabled === false) {
    return {
      statusCode: 503,
      body: {
        success: false,
        error: 'applier_reload_disabled'
      }
    };
  }

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
