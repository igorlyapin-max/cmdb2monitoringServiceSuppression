import fs from 'node:fs';
import path from 'node:path';

const repoRoot = path.resolve(import.meta.dirname, '..');

const files = {
  diagnostics: read('scripts/test-diagnostics.sh'),
  integration: read('scripts/test-integration.sh'),
  testConfigs: read('scripts/test-configs.sh'),
  uiRegressions: read('tests/ui-regressions.mjs'),
  sharedContracts: read('tests/sharedcontracts/Program.cs'),
  integrationChecks: read('tests/integrationchecks/Program.cs'),
  redisRuntimeE2e: read('tests/redis-runtime-e2e.mjs'),
  redisKafkaE2e: read('tests/redis-kafka-dedup-e2e.mjs'),
  validateConfig: read('src/monitoring-ui-api/scripts/validate-config.mjs'),
  gitlabCi: read('.gitlab-ci.yml'),
  deployment: read('DEPLOYMENT.md'),
  compose: read('docker-compose.yml'),
  index: read('src/monitoring-ui-api/public/index.html'),
  app: read('src/monitoring-ui-api/public/app.js'),
  server: read('src/monitoring-ui-api/server.mjs'),
  serviceDefaults: read('src/shared/Configuration/ServiceDefaults.cs'),
  cmdbConfigBuilder: read('src/cmdbconfigbuilder/Program.cs'),
  zabbixProgram: read('src/zabbixconfig2api/Program.cs'),
  zabbixTriggerApplier: read('src/zabbixconfig2api/ZabbixTriggerDependencyApplier.cs')
};

const contracts = [
  {
    name: 'default offline diagnostics',
    assert() {
      includes(files.diagnostics, 'node --check', 'diagnostics must check UI syntax');
      includes(files.diagnostics, 'validate-config.mjs', 'diagnostics must validate monitoring UI config');
      includes(files.diagnostics, 'tests/ui-regressions.mjs', 'diagnostics must run UI regression contracts');
      includes(files.diagnostics, 'tests/autotest-plan-contracts.mjs', 'diagnostics must run autotest coverage contracts');
      includes(files.diagnostics, 'tests/sharedcontracts/sharedcontracts.csproj', 'diagnostics must build and run shared .NET contracts');
      includes(files.testConfigs, 'INTEGRATION_PROFILE=offline', 'test-configs must be an offline compatibility gate');
    }
  },
  {
    name: 'UI and operator workflow regressions',
    assert() {
      includes(files.index, 'id="modelZabbixApplyView"', 'UI must expose compact Zabbix publication');
      includes(files.index, 'id="modelControlSummary"', 'UI must expose unified model control summary');
      includes(files.index, 'data-copy-json="zabbixTriggerDependencies"', 'UI must expose diagnostics copy actions');
      includes(files.app, 'function runCompactZabbixPublication(panel, options = {})', 'UI must run compact Zabbix publication');
      includes(files.app, 'function modelControlReport()', 'UI must build model control reports');
      includes(files.uiRegressions, 'legacy service/suppression menus must be physically removed', 'UI regression must protect removed legacy menus');
    }
  },
  {
    name: 'configuration and schema contracts',
    assert() {
      includes(files.validateConfig, 'appsettings', 'config validator must parse appsettings');
      includes(files.validateConfig, 'validateHardeningConfig', 'config validator must cover hardening settings');
      includes(files.sharedContracts, 'C2M_ServiceSlaCalendar', 'schema contracts must cover SLA calendar classes');
      includes(files.sharedContracts, 'C2M_ServiceSlaPolicy', 'schema contracts must cover SLA policy classes');
      includes(files.sharedContracts, 'C2M_SuppressionManagedObject', 'schema contracts must cover suppression superclass');
      includes(files.sharedContracts, 'service_depends_on', 'schema contracts must cover service object relations');
      includes(files.sharedContracts, 'must be able to suppress', 'schema contracts must cover universal suppression domains');
    }
  },
  {
    name: 'GitLab CI and HTTP hardening',
    assert() {
      includes(files.gitlabCi, 'tracked_state_guard', 'GitLab CI must block tracked runtime state');
      includes(files.gitlabCi, 'node_lint', 'GitLab CI must run minimal Node static checks');
      includes(files.gitlabCi, 'dotnet_analyzer', 'GitLab CI must run .NET analyzer/warnings gate');
      includes(files.serviceDefaults, 'UseHostValidation', '.NET services must validate Host headers');
      includes(files.serviceDefaults, 'TrustedProxies', '.NET services must configure trusted proxy networks');
      includes(files.serviceDefaults, 'MapServiceReadiness', '.NET services must expose readiness');
      includes(files.server, 'hostAllowed', 'UI BFF must validate Host headers');
      includes(files.server, 'trustedProxiesConfig', 'UI BFF must configure trusted proxy networks');
      includes(files.server, 'metricsAccessAllowed', 'UI BFF must protect metrics access');
      includes(files.server, 'readinessPayload', 'UI BFF must expose readiness');
      includes(files.compose, 'cmdb2m-state:/app/state', 'Compose must use a named state volume by default');
      includes(files.deployment, 'TLS is administrator-owned', 'Deployment docs must keep TLS scheme selection administrator-owned');
    }
  },
  {
    name: 'rule and template materialization',
    assert() {
      includes(files.index, 'templateDeletionPlans', 'template apply UI must expose deletion plans');
      includes(files.index, 'generated_from_template', 'template help must explain generated rules');
      includes(files.app, 'function materializeTemplatesForLayer(layerKey, plan)', 'UI must materialize templates by layer');
      includes(files.app, 'function templateMaterializationPlan(', 'UI must compute template materialization plans');
      includes(files.app, 'function detachedTemplateCleanupRules(layerKey)', 'UI must manage detached template rules');
      includes(files.uiRegressions, 'template-generated rules must be matched', 'UI regression must protect template-generated rule detection');
    }
  },
  {
    name: 'CMDBuild streaming, path, and dirty-scope logic',
    assert() {
      includes(files.cmdbConfigBuilder, 'MarkDirtyScopesForIntermediateWebhookAsync', 'builder must mark dirty scopes for intermediate path changes');
      includes(files.cmdbConfigBuilder, 'CmdbPathContainsIntermediateClass', 'builder must detect intermediate CMDBuild path classes');
      includes(files.cmdbConfigBuilder, 'TargetLookup', 'builder must preserve relation lookup metadata');
      includes(files.cmdbConfigBuilder, 'semantic-dedup', 'builder must protect Kafka streams with semantic deduplication');
      includes(files.sharedContracts, 'moving a source to a new dimension must remove it from the previous target membership', 'contracts must cover stale membership on dimension moves');
      includes(files.sharedContracts, 'Redis lookup cache wrapper must not cache when Redis is disabled', 'contracts must cover lookup cache behavior');
    }
  },
  {
    name: 'Zabbix service publication',
    assert() {
      includes(files.app, 'renderZabbixObjectPlan(plan, layerKey)', 'UI must render planned Zabbix objects before publication');
      includes(files.app, 'orphanVisibleNodes', 'UI must flag visible orphan service nodes');
      includes(files.app, 'runZabbixSlaPublication({ dryRun })', 'service publication must include SLA dry-run/apply flow');
      includes(files.zabbixProgram, 'ZabbixSlaPublisher', 'zabbixconfig2api must publish SLA objects');
      includes(files.zabbixProgram, 'ZabbixServiceAggregationCommandWorker', 'zabbixconfig2api must expose service apply flow');
      includes(files.sharedContracts, 'Zabbix relation must keep the CMDBuild target lookup', 'contracts must cover service graph relation mapping');
    }
  },
  {
    name: 'Zabbix suppression publication',
    assert() {
      includes(files.zabbixTriggerApplier, 'TransitiveGroupDependencyDepth', 'suppression dependencies must support configured transitive depth');
      includes(files.zabbixTriggerApplier, 'FilterMembershipsByScope', 'suppression dependencies must support scoped reconcile');
      includes(files.zabbixTriggerApplier, 'MaxDependenciesPerRun', 'suppression dependencies must guard run size');
      includes(files.zabbixProgram, 'trigger dependency', 'zabbixconfig2api must manage trigger dependencies');
      includes(files.zabbixProgram, 'is_critical', 'membership state must persist criticality metadata');
      includes(files.sharedContracts, 'suppression aggregate threshold must be based on hosts with selected supported triggers', 'contracts must cover aggregate trigger thresholding');
    }
  },
  {
    name: 'Redis and durable runtime state',
    assert() {
      includes(files.diagnostics, 'INTEGRATION_PROFILE', 'diagnostics must expose explicit integration profiles');
      includes(files.diagnostics, 'redis-kafka', 'diagnostics must include Redis Kafka profile');
      includes(files.integration, 'live|redis|redis-kafka|all', 'integration wrapper must expose live and Redis profiles');
      includes(files.zabbixProgram, 'SqliteZabbixApplyStateStorage', 'zabbixconfig2api must provide durable membership-state storage');
      includes(files.zabbixProgram, 'ZabbixDirtyScopeStore', 'zabbixconfig2api must provide dirty-scope storage');
      includes(files.zabbixProgram, 'RedisRuntimeCoordinationStore', 'zabbixconfig2api must integrate Redis runtime coordination');
      includes(files.sharedContracts, 'AssertSqliteDirtyScopeStoreContract', 'shared contracts must cover dirty-scope persistence');
    }
  },
  {
    name: 'Kafka and Redis stream e2e hooks',
    assert() {
      includes(files.redisKafkaE2e, 'Kafka__ConsumerGroupId', 'Redis Kafka e2e must run independent consumer groups');
      includes(files.redisKafkaE2e, 'KafkaTopics__CmdbWebhookEvents', 'Redis Kafka e2e must publish CMDB webhook events');
      includes(files.redisKafkaE2e, 'redis-kafka-dedup-host', 'Redis Kafka e2e must assert semantic dedup command output');
      includes(files.cmdbConfigBuilder, 'ZabbixDirtyScopeClient', 'builder must publish dirty scopes for downstream reconcile');
      includes(files.integrationChecks, 'Kafka auto-apply', 'live checks must detect disabled Kafka auto-apply');
      includes(files.integrationChecks, 'CMDBuild', 'live checks must include CMDBuild status');
      includes(files.integrationChecks, 'Zabbix', 'live checks must include Zabbix status');
    }
  },
  {
    name: 'performance and safety limits',
    assert() {
      includes(files.server, 'LongRunningRequestTimeoutMs', 'UI BFF must expose long-running timeout protection');
      includes(files.server, 'TriggerGetBatchSize', 'UI BFF must expose trigger batch size settings');
      includes(files.server, 'MaxSourceHostsPerAggregate', 'UI BFF must expose aggregate source-host limit');
      includes(files.server, 'MaxAggregateFormulaLength', 'UI BFF must expose aggregate formula length limit');
      includes(files.zabbixProgram, 'AggregateComplexityWarningCount', 'zabbixconfig2api must report aggregate complexity warnings');
      includes(files.zabbixTriggerApplier, 'AggregateComplexityWarningRatio', 'trigger dependency applier must warn before hard complexity limits');
      includes(files.uiRegressions, 'MaxSourceHostsPerAggregate', 'UI regressions must protect visible performance settings');
    }
  }
];

for (const contract of contracts) {
  contract.assert();
  console.log(`ok - ${contract.name}`);
}

console.log('Autotest plan contracts passed.');

function read(relativePath) {
  return fs.readFileSync(path.join(repoRoot, relativePath), 'utf8');
}

function includes(text, needle, message) {
  if (!text.includes(needle)) {
    throw new Error(`${message}. Missing: ${needle}`);
  }
}
