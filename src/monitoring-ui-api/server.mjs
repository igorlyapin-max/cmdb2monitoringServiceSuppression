import http from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const publicRoot = path.join(root, 'public');
const baseConfig = JSON.parse(await readFile(path.join(root, 'config', 'appsettings.json'), 'utf8'));
const config = baseConfig;

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
        cmdbuildSchema: config.cmdbuildSchema
      });
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

async function readJsonBody(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }

  const text = Buffer.concat(chunks).toString('utf8');
  return text ? JSON.parse(text) : {};
}
