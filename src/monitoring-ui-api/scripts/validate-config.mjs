import { access, readFile } from 'node:fs/promises';
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

if (!config.managedMicroservices?.zabbixconfig2api?.configFile) {
  errors.push('managedMicroservices.zabbixconfig2api.configFile is required');
}

for (const key of [
  'rulesValidateUrl',
  'rulesApplyCurrentUrl',
  'zabbixApplyStatusUrl',
  'zabbixTriggerDependenciesStatusUrl',
  'zabbixTriggerDependenciesDryRunUrl',
  'zabbixTriggerDependenciesApplyUrl',
  'zabbixSlaStatusUrl',
  'zabbixSlaDryRunUrl',
  'zabbixSlaApplyUrl'
]) {
  try {
    new URL(config.backend?.[key] ?? '');
  } catch {
    errors.push(`backend.${key} must be an absolute URL`);
  }
}

if (!config.conversionConfig?.storageFolder) {
  errors.push('conversionConfig.storageFolder is required');
}

if (!config.conversionConfig?.runtimeRulesFile) {
  errors.push('conversionConfig.runtimeRulesFile is required');
}

for (const key of ['serviceRulesFile', 'suppressionRulesFile', 'serviceTemplatesFile', 'suppressionTemplatesFile', 'sharedTemplatesFile', 'manifestFile']) {
  if (!config.conversionConfig?.[key]) {
    errors.push(`conversionConfig.${key} is required`);
  }
}

await validateConversionDocuments();

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

async function validateConversionDocuments() {
  if (!config.conversionConfig?.storageFolder) {
    return;
  }

  const folder = await resolveConversionStorageFolder(config.conversionConfig.storageFolder);
  for (const item of [
    {
      layer: 'service',
      rulesFile: config.conversionConfig.serviceRulesFile,
      templatesFile: config.conversionConfig.serviceTemplatesFile
    },
    {
      layer: 'suppression',
      rulesFile: config.conversionConfig.suppressionRulesFile,
      templatesFile: config.conversionConfig.suppressionTemplatesFile
    }
  ]) {
    const rulesDocument = await readJsonConfig(`${item.layer} conversion rules`, path.join(folder, item.rulesFile));
    const templatesDocument = await readJsonConfig(`${item.layer} conversion templates`, path.join(folder, item.templatesFile));
    const rules = Array.isArray(rulesDocument.config.rules) ? rulesDocument.config.rules : [];
    const ruleIds = new Map();
    for (const [index, rule] of rules.entries()) {
      const ruleId = stringValue(rule?.rule_id);
      if (!ruleId) {
        errors.push(`${item.layer} rule at index ${index} must have rule_id`);
        continue;
      }

      const entries = ruleIds.get(ruleId) ?? [];
      entries.push(index);
      ruleIds.set(ruleId, entries);
    }

    for (const [ruleId, indexes] of ruleIds.entries()) {
      if (indexes.length > 1) {
        errors.push(`${item.layer} conversion rule_id '${ruleId}' is duplicated at indexes ${indexes.join(', ')}`);
      }
    }

    const currentRuleIds = new Set(ruleIds.keys());
    const templates = Array.isArray(templatesDocument.config.templates) ? templatesDocument.config.templates : [];
    const currentTemplateIds = new Set(templates
      .map((template) => stringValue(template?.template_id))
      .filter(Boolean));
    for (const [ruleIndex, rule] of rules.entries()) {
      for (const relation of rule?.managed_relations ?? []) {
        const targetRuleId = stringValue(relation?.target_rule_id);
        if (relation?.kind === 'rule' && targetRuleId && !currentRuleIds.has(targetRuleId)) {
          errors.push(`${item.layer}: ${describeRule(rule, ruleIndex)} has invalid relation ${describeRelation(relation)}: target rule '${targetRuleId}' does not exist.${genericRuleHint(targetRuleId)}`);
        }
        const targetTemplateId = stringValue(relation?.target_template_id);
        if (relation?.kind === 'template' && targetTemplateId && !currentTemplateIds.has(targetTemplateId)) {
          errors.push(`${item.layer}: ${describeRule(rule, ruleIndex)} has invalid relation ${describeRelation(relation)}: target template '${targetTemplateId}' does not exist`);
        }
      }
    }

    for (const template of templates) {
      for (const relation of template?.managed_relations ?? []) {
        const targetTemplateId = stringValue(relation?.target_template_id);
        if (relation?.kind === 'template' && targetTemplateId && !currentTemplateIds.has(targetTemplateId)) {
          errors.push(`${item.layer}: ${describeTemplate(template)} has invalid relation ${describeRelation(relation)}: target template '${targetTemplateId}' does not exist`);
        }
        const targetRuleId = stringValue(relation?.target_rule_id);
        if (relation?.kind === 'rule' && targetRuleId && !currentRuleIds.has(targetRuleId)) {
          errors.push(`${item.layer}: ${describeTemplate(template)} has invalid relation ${describeRelation(relation)}: target rule '${targetRuleId}' does not exist.${genericRuleHint(targetRuleId)}`);
        }
      }
    }
  }
}

function describeTemplate(template) {
  const id = stringValue(template?.template_id);
  const name = stringValue(template?.name) || id || '-';
  return `template '${name}'${id && id !== name ? ` [${id}]` : ''}`;
}

function describeRule(rule, index = 0) {
  const id = stringValue(rule?.rule_id) || String(index);
  const name = stringValue(rule?.name) || id;
  return `rule '${name}'${id && id !== name ? ` [${id}]` : ''}`;
}

function describeRelation(relation) {
  const role = stringValue(relation?.relation_role || relation?.role || 'uses');
  const target = relation?.kind === 'template'
    ? `template '${stringValue(relation?.target_template_id)}'`
    : relation?.kind === 'rule'
      ? `rule '${stringValue(relation?.target_rule_id)}'`
      : `kind '${stringValue(relation?.kind)}'`;
  return `${role} -> ${target}`;
}

function genericRuleHint(ruleId) {
  return stringValue(ruleId).toLowerCase() === 'rule'
    ? ' This is a placeholder value; delete this template-rule link and recreate it by selecting a real manual rule.'
    : '';
}

async function resolveConversionStorageFolder(storageFolder) {
  const configured = stringValue(storageFolder);
  if (!configured) {
    return '';
  }

  const candidates = path.isAbsolute(configured)
    ? [configured]
    : [
        path.resolve(process.cwd(), configured),
        path.resolve(root, '..', '..', configured),
        path.resolve(root, configured)
      ];
  for (const candidate of candidates) {
    try {
      await access(candidate);
      return candidate;
    } catch {
      // Try the next reasonable base directory.
    }
  }

  return candidates[0];
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
