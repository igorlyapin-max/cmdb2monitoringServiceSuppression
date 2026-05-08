import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const config = JSON.parse(await readFile(path.join(root, 'config', 'appsettings.json'), 'utf8'));
const errors = [];
const applierConfigs = await Promise.all([
  readJsonConfig('zabbixconfig2api', path.resolve(root, '..', 'zabbixconfig2api', 'appsettings.json')),
  readJsonConfig('cmdbaggregation2cmdbuild', path.resolve(root, '..', 'cmdbaggregation2cmdbuild', 'appsettings.json'))
]);

const roles = new Set(config.auth?.roles ?? []);

for (const role of ['admin', 'serviceadmin', 'suppressionadmin']) {
  if (!roles.has(role)) {
    errors.push(`Missing role: ${role}`);
  }
}

if (!config.server?.host) {
  errors.push('server.host is required');
}

if (!Number.isInteger(config.server?.port) || config.server.port <= 0) {
  errors.push('server.port must be a positive integer');
}

if (!['local', 'saml', 'oauth', 'ldap'].includes(config.auth?.mode)) {
  errors.push('auth.mode must be local, saml, oauth, or ldap');
}

if (!config.readiness?.zabbixHostIdAttribute) {
  errors.push('readiness.zabbixHostIdAttribute is required');
}

if (!config.conversionConfig?.storageFolder) {
  errors.push('conversionConfig.storageFolder is required');
}

if (!config.conversionConfig?.runtimeRulesFile) {
  errors.push('conversionConfig.runtimeRulesFile is required');
}

const reloadableHealthChecks = (config.healthChecks ?? []).filter((item) => item.reloadUrl);
if (reloadableHealthChecks.length > 0) {
  validateApplierReloadTokenSources();
}

for (const item of reloadableHealthChecks) {
  if (!item.id) {
    errors.push('reloadable healthChecks entries must have id');
  }
  try {
    new URL(item.reloadUrl);
  } catch {
    errors.push(`healthChecks.${item.id ?? 'unknown'}.reloadUrl must be an absolute URL`);
  }
}

for (const item of config.healthChecks ?? []) {
  if (!item.rulesStatusUrl) {
    continue;
  }
  try {
    new URL(item.rulesStatusUrl);
  } catch {
    errors.push(`healthChecks.${item.id ?? 'unknown'}.rulesStatusUrl must be an absolute URL`);
  }
}

if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exitCode = 1;
} else {
  console.log('UI configuration is valid.');
}

async function readJsonConfig(name, filePath) {
  try {
    return {
      name,
      config: JSON.parse(await readFile(filePath, 'utf8'))
    };
  } catch (error) {
    errors.push(`${name} config cannot be read: ${error.message}`);
    return {
      name,
      config: {}
    };
  }
}

function validateApplierReloadTokenSources() {
  const sources = [
    {
      name: 'monitoring-ui-api',
      source: reloadTokenSource(config.appliers, 'reloadBearerToken', 'reloadBearerTokenSecret')
    },
    ...applierConfigs.map((item) => ({
      name: item.name,
      source: reloadTokenSource(item.config.ConfigurationReload, 'BearerToken', 'BearerTokenSecret')
    }))
  ];

  for (const item of sources) {
    if (!item.source) {
      errors.push(`${item.name} must configure the applier reload Bearer Token as a literal token or PAM secret reference`);
    }
  }

  const configured = sources.filter((item) => item.source);
  const reference = configured[0]?.source;
  if (!reference) {
    return;
  }

  for (const item of configured.slice(1)) {
    if (item.source.mode !== reference.mode || item.source.value !== reference.value) {
      errors.push(`Applier reload Bearer Token source must be identical in monitoring-ui-api, zabbixconfig2api, and cmdbaggregation2cmdbuild; ${item.name} does not match`);
    }
  }
}

function reloadTokenSource(section, tokenKey, secretKey) {
  const secret = stringValue(section?.[secretKey]);
  if (secret) {
    return {
      mode: 'secret',
      value: normalizeSecretReference(secret)
    };
  }

  const token = stringValue(section?.[tokenKey]);
  if (!token) {
    return null;
  }

  return parseSecretId(token)
    ? {
        mode: 'secret',
        value: normalizeSecretReference(token)
      }
    : {
        mode: 'literal',
        value: token
      };
}

function normalizeSecretReference(value) {
  const secretId = parseSecretId(value);
  return secretId ? `secret://${secretId}` : stringValue(value);
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

function stringValue(value) {
  return String(value ?? '').trim();
}
