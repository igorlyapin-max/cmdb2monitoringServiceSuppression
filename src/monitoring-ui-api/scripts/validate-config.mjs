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
const cmdbconfigbuilderConfig = await readJsonConfig('cmdbconfigbuilder', path.resolve(root, '..', 'cmdbconfigbuilder', 'appsettings.json'));
const cmdbmodelmaterializerConfig = await readJsonConfig('cmdbmodelmaterializer', path.resolve(root, '..', 'cmdbmodelmaterializer', 'appsettings.json'));
const cmdbwebhooksConfig = await readJsonConfig('cmdbwebhooks2kafka', path.resolve(root, '..', 'cmdbwebhooks2kafka', 'appsettings.json'));

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
if (!stringValue(config.readiness?.route).startsWith('/')) {
  errors.push('readiness.route must start with /');
}
if (!Number.isInteger(config.readiness?.checkTimeoutMs) || config.readiness.checkTimeoutMs <= 0) {
  errors.push('readiness.checkTimeoutMs must be a positive integer');
}

validateMonitoringUiRuntimeConfig();

validateHardeningConfig('monitoring-ui-api', {
  allowedHosts: config.allowedHosts,
  hostValidation: config.hostValidation,
  trustedProxies: config.trustedProxies,
  rateLimiting: config.rateLimiting,
  metrics: config.metrics
});

if (!config.managedMicroservices?.zabbixconfig2api?.configFile) {
  errors.push('managedMicroservices.zabbixconfig2api.configFile is required');
}

const zabbixconfig2api = applierConfigs.find((item) => item.name === 'zabbixconfig2api')?.config ?? {};
const cmdbconfigbuilder = cmdbconfigbuilderConfig.config ?? {};
const cmdbmodelmaterializer = cmdbmodelmaterializerConfig.config ?? {};
for (const item of [
  ...applierConfigs,
  cmdbconfigbuilderConfig,
  cmdbmodelmaterializerConfig,
  cmdbwebhooksConfig
]) {
  validateHardeningConfig(item.name, {
    allowedHosts: item.config.AllowedHosts,
    hostValidation: item.config.HostValidation,
    trustedProxies: item.config.TrustedProxies,
    rateLimiting: item.config.RateLimiting,
    metrics: item.config.Metrics,
    readiness: item.config.Readiness
  });
}
if (!zabbixconfig2api.Redis) {
  errors.push('zabbixconfig2api Redis section is required');
}
if (!['fallback', 'fail'].includes(stringValue(zabbixconfig2api.Redis?.FailureMode))) {
  errors.push('zabbixconfig2api Redis:FailureMode must be fallback or fail');
}
if (zabbixconfig2api.Redis?.Enabled === true && !stringValue(zabbixconfig2api.Redis?.ConnectionString)) {
  errors.push('zabbixconfig2api Redis:ConnectionString is required when Redis:Enabled=true');
}
if (!cmdbconfigbuilder.Redis) {
  errors.push('cmdbconfigbuilder Redis section is required');
}
if (!['fallback', 'fail'].includes(stringValue(cmdbconfigbuilder.Redis?.FailureMode))) {
  errors.push('cmdbconfigbuilder Redis:FailureMode must be fallback or fail');
}
if (cmdbconfigbuilder.Redis?.Enabled === true && !stringValue(cmdbconfigbuilder.Redis?.ConnectionString)) {
  errors.push('cmdbconfigbuilder Redis:ConnectionString is required when Redis:Enabled=true');
}
if (cmdbconfigbuilder.ZabbixDirtyScopes?.Enabled === true && !stringValue(cmdbconfigbuilder.ZabbixDirtyScopes?.Endpoint)) {
  errors.push('cmdbconfigbuilder ZabbixDirtyScopes:Endpoint is required when ZabbixDirtyScopes:Enabled=true');
}
if (!stringValue(cmdbconfigbuilder.ConversionRules?.ServiceTemplatesFilePath)) {
  errors.push('cmdbconfigbuilder ConversionRules:ServiceTemplatesFilePath is required');
}
if (!stringValue(cmdbconfigbuilder.ConversionRules?.SuppressionTemplatesFilePath)) {
  errors.push('cmdbconfigbuilder ConversionRules:SuppressionTemplatesFilePath is required');
}
if (!stringValue(cmdbconfigbuilder.KafkaTopics?.CmdbModelMissingDimensions)) {
  errors.push('cmdbconfigbuilder KafkaTopics:CmdbModelMissingDimensions is required');
}
if (!stringValue(cmdbmodelmaterializer.KafkaTopics?.CmdbModelMissingDimensions)) {
  errors.push('cmdbmodelmaterializer KafkaTopics:CmdbModelMissingDimensions is required');
}
if (stringValue(cmdbmodelmaterializer.KafkaTopics?.CmdbModelMissingDimensions)
  && stringValue(cmdbconfigbuilder.KafkaTopics?.CmdbModelMissingDimensions)
  && stringValue(cmdbmodelmaterializer.KafkaTopics?.CmdbModelMissingDimensions) !== stringValue(cmdbconfigbuilder.KafkaTopics?.CmdbModelMissingDimensions)) {
  errors.push('cmdbmodelmaterializer KafkaTopics:CmdbModelMissingDimensions must match cmdbconfigbuilder');
}
if (!stringValue(cmdbmodelmaterializer.ConversionConfigStore?.BaseUrl)) {
  errors.push('cmdbmodelmaterializer ConversionConfigStore:BaseUrl is required');
} else {
  try {
    new URL(cmdbmodelmaterializer.ConversionConfigStore.BaseUrl);
  } catch {
    errors.push('cmdbmodelmaterializer ConversionConfigStore:BaseUrl must be an absolute URL');
  }
}
if (!stringValue(cmdbmodelmaterializer.ConversionConfigStore?.CurrentPath)?.startsWith('/')) {
  errors.push('cmdbmodelmaterializer ConversionConfigStore:CurrentPath must start with /');
}
if (!stringValue(cmdbmodelmaterializer.ConversionConfigStore?.DeployPath)?.startsWith('/')) {
  errors.push('cmdbmodelmaterializer ConversionConfigStore:DeployPath must start with /');
}
if (!cmdbmodelmaterializer.Materializer) {
  errors.push('cmdbmodelmaterializer Materializer section is required');
}
if (cmdbmodelmaterializer.Materializer?.Enabled !== false
  && !Array.isArray(cmdbmodelmaterializer.Materializer?.ReloadTargets)) {
  errors.push('cmdbmodelmaterializer Materializer:ReloadTargets must be configured');
}
if (cmdbmodelmaterializer.Replay?.Enabled !== false) {
  try {
    new URL(cmdbmodelmaterializer.Replay?.ReprocessUrl ?? '');
  } catch {
    errors.push('cmdbmodelmaterializer Replay:ReprocessUrl must be an absolute URL when replay is enabled');
  }
  if (!Number.isInteger(cmdbmodelmaterializer.Replay?.MaxBackfillCards) || cmdbmodelmaterializer.Replay.MaxBackfillCards <= 0) {
    errors.push('cmdbmodelmaterializer Replay:MaxBackfillCards must be a positive integer');
  }
}
if (cmdbmodelmaterializer.GraphOverlay) {
  if (cmdbmodelmaterializer.GraphOverlay.Enabled === true) {
    try {
      new URL(cmdbmodelmaterializer.GraphOverlay.ApplyCurrentUrl ?? '');
    } catch {
      errors.push('cmdbmodelmaterializer GraphOverlay:ApplyCurrentUrl must be an absolute URL when graph overlay is enabled');
    }
  }
  const targets = Array.isArray(cmdbmodelmaterializer.GraphOverlay.Targets)
    ? cmdbmodelmaterializer.GraphOverlay.Targets.map((item) => stringValue(item).replaceAll('_', '-').toLowerCase()).filter(Boolean)
    : [];
  if (cmdbmodelmaterializer.GraphOverlay.Targets !== undefined && !Array.isArray(cmdbmodelmaterializer.GraphOverlay.Targets)) {
    errors.push('cmdbmodelmaterializer GraphOverlay:Targets must be an array when configured');
  }
  for (const target of targets) {
    if (!['zabbix', 'zabbix-direct'].includes(target)) {
      errors.push('cmdbmodelmaterializer GraphOverlay:Targets may contain only zabbix or zabbix-direct');
    }
  }
  const usesDirectTarget = targets.length === 0
    ? Boolean(stringValue(cmdbmodelmaterializer.GraphOverlay.ZabbixCommandApplyUrl))
    : targets.includes('zabbix-direct');
  if (cmdbmodelmaterializer.GraphOverlay.Enabled === true && usesDirectTarget) {
    try {
      new URL(cmdbmodelmaterializer.GraphOverlay.ZabbixCommandApplyUrl ?? '');
    } catch {
      errors.push('cmdbmodelmaterializer GraphOverlay:ZabbixCommandApplyUrl must be an absolute URL when zabbix-direct is used');
    }
  }
  if (!['changes', 'full'].includes(stringValue(cmdbmodelmaterializer.GraphOverlay.PublishMode || 'changes').toLowerCase())) {
    errors.push('cmdbmodelmaterializer GraphOverlay:PublishMode must be changes or full');
  }
  const topologyReadMode = stringValue(cmdbmodelmaterializer.GraphOverlay.TopologyReadMode || 'rules').replaceAll('_', '-').toLowerCase();
  if (!['auto', 'rules', 'rule', 'scoped', 'scope', 'runtime-rules', 'full', 'cmdbuild', 'cmdbuild-full', 'legacy-full'].includes(topologyReadMode)) {
    errors.push('cmdbmodelmaterializer GraphOverlay:TopologyReadMode must be auto, rules, or full');
  }
  if (!Number.isInteger(cmdbmodelmaterializer.GraphOverlay.ScopeDepth) || cmdbmodelmaterializer.GraphOverlay.ScopeDepth < 0) {
    errors.push('cmdbmodelmaterializer GraphOverlay:ScopeDepth must be zero or greater');
  }
  if (!Number.isInteger(cmdbmodelmaterializer.GraphOverlay.TimeoutMs) || cmdbmodelmaterializer.GraphOverlay.TimeoutMs <= 0) {
    errors.push('cmdbmodelmaterializer GraphOverlay:TimeoutMs must be a positive integer');
  }
}
for (const target of cmdbmodelmaterializer.Materializer?.ReloadTargets ?? []) {
  if (target?.Enabled === false) {
    continue;
  }
  if (!stringValue(target?.Name)) {
    errors.push('cmdbmodelmaterializer Materializer:ReloadTargets entries must have Name');
  }
  try {
    new URL(target?.Url ?? '');
  } catch {
    errors.push(`cmdbmodelmaterializer reload target ${target?.Name ?? 'unknown'} Url must be an absolute URL`);
  }
}
if (!zabbixconfig2api.DurableStore) {
  errors.push('zabbixconfig2api DurableStore section is required');
}
if (!['file', 'sqlite'].includes(stringValue(zabbixconfig2api.DurableStore?.Provider))) {
  errors.push('zabbixconfig2api DurableStore:Provider must be file or sqlite');
}
if (!zabbixconfig2api.MonitoringCoverageAudit) {
  errors.push('zabbixconfig2api MonitoringCoverageAudit section is required');
}
if (stringValue(zabbixconfig2api.MonitoringCoverageAudit?.HostIdAttribute) !== config.readiness?.zabbixHostIdAttribute) {
  errors.push('zabbixconfig2api MonitoringCoverageAudit:HostIdAttribute must match readiness.zabbixHostIdAttribute');
}
if (!['manual', 'scheduled', 'manual_and_scheduled'].includes(stringValue(zabbixconfig2api.MonitoringCoverageAudit?.TriggerMode))) {
  errors.push('zabbixconfig2api MonitoringCoverageAudit:TriggerMode must be manual, scheduled, or manual_and_scheduled');
}

for (const key of [
  'rulesValidateUrl',
  'rulesApplyCurrentUrl',
  'modelMaterializerStatusUrl',
  'modelMaterializerProcessUrl',
  'cmdbConfigBuilderRedisCheckUrl',
  'zabbixApplyStatusUrl',
  'zabbixRuntimeStorageStatusUrl',
  'zabbixRedisCheckUrl',
  'zabbixRuntimeStorageMigrationDryRunUrl',
  'zabbixRuntimeStorageMigrationApplyUrl',
  'zabbixDirtyScopesUrl',
  'zabbixMonitoringCoverageSnapshotUrl',
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

const conversionConfigStoreBackend = stringValue(config.conversionConfig?.storeBackend || 'folder').toLowerCase();
if (!['folder', 'postgresql', 'postgres', 'pg'].includes(conversionConfigStoreBackend)) {
  errors.push('conversionConfig.storeBackend must be folder or postgresql');
}

if (['postgresql', 'postgres', 'pg'].includes(conversionConfigStoreBackend)) {
  const postgres = config.conversionConfig?.postgres ?? {};
  if (!stringValue(postgres.connectionString) && !stringValue(postgres.connectionStringSecret)) {
    errors.push('conversionConfig.postgres.connectionString is required when conversionConfig.storeBackend=postgresql');
  }
  if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(stringValue(postgres.schema || 'monitoring_ui'))) {
    errors.push('conversionConfig.postgres.schema must be a PostgreSQL identifier');
  }
}

if (!config.conversionConfig?.runtimeRulesFile) {
  errors.push('conversionConfig.runtimeRulesFile is required');
}

for (const key of ['serviceRulesFile', 'suppressionRulesFile', 'serviceTemplatesFile', 'suppressionTemplatesFile', 'sharedTemplatesFile', 'manifestFile', 'auditFile']) {
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
  const materializerReloadTargets = (cmdbmodelmaterializer.Materializer?.ReloadTargets ?? [])
    .filter((item) => item?.Enabled !== false)
    .map((item) => ({
      name: `cmdbmodelmaterializer reload target ${item?.Name ?? 'unknown'}`,
      source: reloadTokenSource(item, 'BearerToken', 'BearerTokenSecret')
    }));
  const sources = [
    ...(config.appliers?.reloadEnabled === false ? [] : [{
      name: 'monitoring-ui-api',
      source: reloadTokenSource(config.appliers, 'reloadBearerToken', 'reloadBearerTokenSecret')
    }]),
    ...applierConfigs
      .filter((item) => item.config.ConfigurationReload?.Enabled !== false)
      .map((item) => ({
      name: item.name,
      source: reloadTokenSource(item.config.ConfigurationReload, 'BearerToken', 'BearerTokenSecret')
    })),
    ...(cmdbmodelmaterializer.ConfigurationReload?.Enabled === false ? [] : [{
      name: 'cmdbmodelmaterializer',
      source: reloadTokenSource(cmdbmodelmaterializer.ConfigurationReload, 'BearerToken', 'BearerTokenSecret')
    }]),
    ...materializerReloadTargets
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
      errors.push(`Applier reload Bearer Token source must be identical in monitoring-ui-api, appliers, and cmdbmodelmaterializer; ${item.name} does not match`);
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

function validateHardeningConfig(name, sections) {
  const rootAllowedHosts = Array.isArray(sections.allowedHosts)
    ? sections.allowedHosts.map(stringValue).filter(Boolean)
    : stringValue(sections.allowedHosts).split(/[;,]/).map(stringValue).filter(Boolean);
  const hostValidation = sections.hostValidation ?? {};
  const sectionAllowedHosts = Array.isArray(hostValidation.AllowedHosts ?? hostValidation.allowedHosts)
    ? (hostValidation.AllowedHosts ?? hostValidation.allowedHosts).map(stringValue).filter(Boolean)
    : [];
  const allowedHosts = rootAllowedHosts.length > 0 ? rootAllowedHosts : sectionAllowedHosts;
  if (hostValidation.Enabled !== false && hostValidation.enabled !== false && allowedHosts.length === 0) {
    errors.push(`${name} AllowedHosts must contain at least one host when host validation is enabled`);
  }

  const trustedProxies = sections.trustedProxies ?? {};
  const proxyNetworks = Array.isArray(trustedProxies.Networks ?? trustedProxies.networks)
    ? (trustedProxies.Networks ?? trustedProxies.networks).map(stringValue).filter(Boolean)
    : [];
  if (trustedProxies.Enabled !== false && trustedProxies.enabled !== false && proxyNetworks.length === 0) {
    errors.push(`${name} TrustedProxies networks must contain at least one entry when enabled`);
  }

  const rateLimiting = sections.rateLimiting ?? {};
  const permitLimit = rateLimiting.PermitLimit ?? rateLimiting.permitLimit;
  const windowSeconds = rateLimiting.WindowSeconds ?? rateLimiting.windowSeconds;
  if (rateLimiting.Enabled !== false && rateLimiting.enabled !== false) {
    if (!Number.isInteger(permitLimit) || permitLimit <= 0) {
      errors.push(`${name} RateLimiting permit limit must be a positive integer`);
    }
    if (!Number.isInteger(windowSeconds) || windowSeconds <= 0) {
      errors.push(`${name} RateLimiting window must be a positive integer`);
    }
  }

  const metrics = sections.metrics ?? {};
  const metricsRoute = stringValue(metrics.Route ?? metrics.route);
  if (!metricsRoute.startsWith('/')) {
    errors.push(`${name} Metrics route must start with /`);
  }
  if ((metrics.RequireBearerToken === true || metrics.requireBearerToken === true)
    && !stringValue(metrics.BearerToken ?? metrics.bearerToken)
    && !stringValue(metrics.BearerTokenSecret ?? metrics.bearerTokenSecret)) {
    errors.push(`${name} Metrics bearer token or secret is required when RequireBearerToken=true`);
  }

  if (sections.readiness) {
    const readinessRoute = stringValue(sections.readiness.Route ?? sections.readiness.route);
    if (!readinessRoute.startsWith('/')) {
      errors.push(`${name} Readiness route must start with /`);
    }
    const readinessTimeout = sections.readiness.CheckTimeoutMs ?? sections.readiness.checkTimeoutMs;
    if (readinessTimeout !== undefined && (!Number.isInteger(readinessTimeout) || readinessTimeout <= 0)) {
      errors.push(`${name} Readiness check timeout must be a positive integer`);
    }
  }
}

function validateMonitoringUiRuntimeConfig() {
  if (!['basic', 'verbose'].includes(stringValue(config.debug?.level || 'Basic').toLowerCase())) {
    errors.push('debug.level must be Basic or Verbose');
  }

  validateLogLevel('logging.minimumLevel', config.logging?.minimumLevel || 'Information');
  validateLogLevel('kafkaLogging.minimumLevel', config.kafkaLogging?.minimumLevel || 'Information');
  validateLogLevel('elkLogging.minimumLevel', config.elkLogging?.minimumLevel || 'Information');

  const kafkaLoggingEnabled = config.kafkaLogging?.enabled === true;
  const elkLoggingEnabled = config.elkLogging?.enabled === true;
  if (kafkaLoggingEnabled) {
    if (!stringValue(config.kafkaLogging?.topic)) {
      errors.push('kafkaLogging.topic is required when kafkaLogging.enabled=true');
    }
    const brokers = Array.isArray(config.kafkaLogging?.brokers)
      ? config.kafkaLogging.brokers.map(stringValue).filter(Boolean)
      : stringValue(config.kafkaLogging?.bootstrapServers).split(',').map(stringValue).filter(Boolean);
    if (brokers.length === 0) {
      errors.push('kafkaLogging.bootstrapServers or kafkaLogging.brokers is required when kafkaLogging.enabled=true');
    }
  }

  if (elkLoggingEnabled) {
    try {
      new URL(config.elkLogging?.endpoint ?? '');
    } catch {
      errors.push('elkLogging.endpoint must be an absolute URL when elkLogging.enabled=true');
    }
    if (!Number.isInteger(config.elkLogging?.timeoutMs) || config.elkLogging.timeoutMs <= 0) {
      errors.push('elkLogging.timeoutMs must be a positive integer');
    }
  }

  if (config.logging?.requireExternalSink === true && !kafkaLoggingEnabled && !elkLoggingEnabled) {
    errors.push('logging.requireExternalSink=true requires kafkaLogging.enabled=true or elkLogging.enabled=true');
  }
}

function validateLogLevel(name, value) {
  const normalized = stringValue(value).toLowerCase();
  if (!['trace', 'debug', 'information', 'info', 'warning', 'warn', 'error', 'critical', 'fatal'].includes(normalized)) {
    errors.push(`${name} has invalid value`);
  }
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
