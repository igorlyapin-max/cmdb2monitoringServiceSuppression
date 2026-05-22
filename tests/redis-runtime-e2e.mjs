#!/usr/bin/env node
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const dotnet = path.join(repoRoot, 'scripts', 'dotnet');
const redisConnectionString = process.env.REDIS_E2E_CONNECTION_STRING || '127.0.0.1:6379';
const builderPort = Number(process.env.REDIS_E2E_BUILDER_PORT || 5192);
const zabbixPort = Number(process.env.REDIS_E2E_ZABBIX_PORT || 5193);
const unavailableRedis = process.env.REDIS_E2E_UNAVAILABLE_CONNECTION_STRING || '127.0.0.1:6390';

await assertRedisReachable(redisConnectionString);

await buildProject('src/cmdbconfigbuilder/cmdbconfigbuilder.csproj');
await buildProject('src/zabbixconfig2api/zabbixconfig2api.csproj');

await withService({
  name: 'cmdbconfigbuilder',
  project: 'src/cmdbconfigbuilder/cmdbconfigbuilder.csproj',
  port: builderPort,
  redisConnectionString
}, async (baseUrl) => {
  await assertRedisCheck(`${baseUrl}/redis/check`, {
    configured: true,
    success: true,
    backend: 'redis',
    redisAvailable: true,
    fallbackActive: false,
    blockingOnRedisUnavailable: false
  });
  await assertSemanticDedupCheck(`${baseUrl}/redis/semantic-dedup/check`, {
    configured: true,
    success: true,
    backend: 'redis',
    redisAvailable: true,
    fallbackActive: false,
    blockingOnRedisUnavailable: false,
    firstDuplicate: false,
    secondDuplicate: true
  });
});

await withService({
  name: 'cmdbconfigbuilder fallback',
  project: 'src/cmdbconfigbuilder/cmdbconfigbuilder.csproj',
  port: builderPort,
  redisConnectionString: unavailableRedis,
  failureMode: 'fallback'
}, async (baseUrl) => {
  await assertRedisCheck(`${baseUrl}/redis/check`, {
    configured: true,
    success: true,
    backend: 'in-memory-fallback',
    redisAvailable: false,
    fallbackActive: true,
    blockingOnRedisUnavailable: false
  });
  await assertSemanticDedupCheck(`${baseUrl}/redis/semantic-dedup/check`, {
    configured: true,
    success: true,
    backend: 'in-memory-fallback',
    redisAvailable: false,
    fallbackActive: true,
    blockingOnRedisUnavailable: false,
    firstDuplicate: false,
    secondDuplicate: true
  });
});

await withService({
  name: 'cmdbconfigbuilder fail',
  project: 'src/cmdbconfigbuilder/cmdbconfigbuilder.csproj',
  port: builderPort,
  redisConnectionString: unavailableRedis,
  failureMode: 'fail'
}, async (baseUrl) => {
  await assertRedisCheck(`${baseUrl}/redis/check`, {
    configured: true,
    success: false,
    backend: 'redis',
    redisAvailable: false,
    fallbackActive: false,
    blockingOnRedisUnavailable: true
  });
  await assertSemanticDedupCheck(`${baseUrl}/redis/semantic-dedup/check`, {
    configured: true,
    success: false,
    backend: 'redis',
    redisAvailable: false,
    fallbackActive: false,
    blockingOnRedisUnavailable: true
  });
});

await withService({
  name: 'zabbixconfig2api',
  project: 'src/zabbixconfig2api/zabbixconfig2api.csproj',
  port: zabbixPort,
  redisConnectionString
}, async (baseUrl) => {
  await assertRedisCheck(`${baseUrl}/redis/check`, {
    configured: true,
    success: true,
    backend: 'redis',
    redisAvailable: true,
    fallbackActive: false,
    blockingOnRedisUnavailable: false
  });
  await assertRuntimeStorageStatus(`${baseUrl}/runtime-storage/status`, {
    lookupCacheBackend: 'redis',
    lookupCacheRedisAvailable: true,
    lookupCacheFallbackActive: false
  });
});

await withService({
  name: 'zabbixconfig2api fallback',
  project: 'src/zabbixconfig2api/zabbixconfig2api.csproj',
  port: zabbixPort,
  redisConnectionString: unavailableRedis,
  failureMode: 'fallback'
}, async (baseUrl) => {
  await assertRedisCheck(`${baseUrl}/redis/check`, {
    configured: true,
    success: true,
    backend: 'local-memory-fallback',
    redisAvailable: false,
    fallbackActive: true,
    blockingOnRedisUnavailable: false
  });
  await assertRuntimeStorageStatus(`${baseUrl}/runtime-storage/status`, {
    lookupCacheBackend: 'no-cache-fallback',
    lookupCacheRedisAvailable: false,
    lookupCacheFallbackActive: true
  });
});

await withService({
  name: 'zabbixconfig2api fail',
  project: 'src/zabbixconfig2api/zabbixconfig2api.csproj',
  port: zabbixPort,
  redisConnectionString: unavailableRedis,
  failureMode: 'fail'
}, async (baseUrl) => {
  await assertRedisCheck(`${baseUrl}/redis/check`, {
    configured: true,
    success: false,
    backend: 'redis',
    redisAvailable: false,
    fallbackActive: false,
    blockingOnRedisUnavailable: true
  });
  await assertRuntimeStorageStatus(`${baseUrl}/runtime-storage/status`, {
    lookupCacheBackend: 'no-cache-fallback',
    lookupCacheRedisAvailable: false,
    lookupCacheFallbackActive: true
  });
});

console.log('Redis runtime e2e checks passed.');

async function buildProject(project) {
  await run(dotnet, ['build', path.join(repoRoot, project), '-v', 'minimal', '/p:NuGetAudit=false', '-m:1'], {
    cwd: repoRoot,
    name: `build ${project}`,
    timeoutMs: 120000
  });
}

async function withService(options, test) {
  const baseUrl = `http://127.0.0.1:${options.port}`;
  const child = spawn(dotnet, [
    'run',
    '--project',
    path.join(repoRoot, options.project),
    '--no-build',
    '--no-launch-profile'
  ], {
    cwd: repoRoot,
    env: {
      ...process.env,
      ASPNETCORE_URLS: baseUrl,
      Redis__Enabled: 'true',
      Redis__ConnectionString: options.redisConnectionString,
      Redis__FailureMode: options.failureMode || 'fallback',
      Redis__KeyPrefix: `cmdb2m:test:e2e:${sanitize(options.name)}`
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  let output = '';
  child.stdout.on('data', (chunk) => {
    output += chunk.toString();
  });
  child.stderr.on('data', (chunk) => {
    output += chunk.toString();
  });

  try {
    await waitForHttp(`${baseUrl}/health`, 45000, () => {
      if (child.exitCode !== null) {
        throw new Error(`${options.name} exited before health check.\n${trimOutput(output)}`);
      }
    });
    await test(baseUrl);
  } finally {
    await stopProcess(child);
  }
}

async function assertRedisCheck(url, expected) {
  const response = await fetch(url, { headers: { accept: 'application/json' } });
  const body = await response.json();
  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}: ${JSON.stringify(body)}`);
  }

  for (const [key, value] of Object.entries(expected)) {
    if (body[key] !== value) {
      throw new Error(`${url}: expected ${key}=${JSON.stringify(value)}, got ${JSON.stringify(body[key])}. Full response: ${JSON.stringify(body)}`);
    }
  }
}

async function assertRuntimeStorageStatus(url, expected) {
  const response = await fetch(url, { headers: { accept: 'application/json' } });
  const body = await response.json();
  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}: ${JSON.stringify(body)}`);
  }

  const lookupCache = body.lookupCache ?? {};
  const actual = {
    lookupCacheBackend: lookupCache.backend,
    lookupCacheRedisAvailable: lookupCache.redisAvailable,
    lookupCacheFallbackActive: lookupCache.fallbackActive
  };
  for (const [key, value] of Object.entries(expected)) {
    if (actual[key] !== value) {
      throw new Error(`${url}: expected ${key}=${JSON.stringify(value)}, got ${JSON.stringify(actual[key])}. Full lookupCache: ${JSON.stringify(lookupCache)}`);
    }
  }
}

async function assertSemanticDedupCheck(url, expected) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { accept: 'application/json' }
  });
  const body = await response.json();
  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}: ${JSON.stringify(body)}`);
  }

  for (const [key, value] of Object.entries(expected)) {
    if (body[key] !== value) {
      throw new Error(`${url}: expected ${key}=${JSON.stringify(value)}, got ${JSON.stringify(body[key])}. Full response: ${JSON.stringify(body)}`);
    }
  }
}

async function waitForHttp(url, timeoutMs, tick) {
  const started = Date.now();
  let lastError;
  while (Date.now() - started < timeoutMs) {
    tick?.();
    try {
      const response = await fetch(url, { headers: { accept: 'application/json' } });
      if (response.ok) {
        return;
      }
      lastError = new Error(`${url} returned HTTP ${response.status}`);
    } catch (error) {
      lastError = error;
    }
    await sleep(500);
  }

  throw new Error(`Timed out waiting for ${url}: ${lastError?.message || 'no response'}`);
}

async function assertRedisReachable(connectionString) {
  const endpoint = parseRedisEndpoint(connectionString);
  await new Promise((resolve, reject) => {
    const socket = net.createConnection(endpoint, () => {
      socket.end();
      resolve();
    });
    socket.setTimeout(3000);
    socket.on('timeout', () => {
      socket.destroy();
      reject(new Error(`Redis e2e endpoint ${endpoint.host}:${endpoint.port} timed out.`));
    });
    socket.on('error', (error) => {
      reject(new Error(`Redis e2e endpoint ${endpoint.host}:${endpoint.port} is not reachable: ${error.message}`));
    });
  });
}

function parseRedisEndpoint(connectionString) {
  if (connectionString.startsWith('redis://')) {
    const url = new URL(connectionString);
    return { host: url.hostname || '127.0.0.1', port: Number(url.port || 6379) };
  }

  const [hostPort] = connectionString.split(',');
  const [host, port] = hostPort.split(':');
  return { host: host || '127.0.0.1', port: Number(port || 6379) };
}

function run(command, args, options) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: options.cwd,
      env: process.env,
      stdio: ['ignore', 'pipe', 'pipe']
    });
    let output = '';
    const timer = setTimeout(() => {
      child.kill('SIGKILL');
      reject(new Error(`${options.name} timed out.\n${trimOutput(output)}`));
    }, options.timeoutMs);
    child.stdout.on('data', (chunk) => {
      output += chunk.toString();
    });
    child.stderr.on('data', (chunk) => {
      output += chunk.toString();
    });
    child.on('error', (error) => {
      clearTimeout(timer);
      reject(error);
    });
    child.on('exit', (code) => {
      clearTimeout(timer);
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${options.name} exited with ${code}.\n${trimOutput(output)}`));
      }
    });
  });
}

async function stopProcess(child) {
  if (child.exitCode !== null) {
    return;
  }
  child.kill('SIGTERM');
  const stopped = await Promise.race([
    new Promise((resolve) => child.once('exit', () => resolve(true))),
    sleep(5000).then(() => false)
  ]);
  if (!stopped && child.exitCode === null) {
    child.kill('SIGKILL');
    await new Promise((resolve) => child.once('exit', resolve));
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function sanitize(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'service';
}

function trimOutput(output) {
  return output.length > 4000 ? output.slice(-4000) : output;
}
