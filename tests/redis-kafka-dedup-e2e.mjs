#!/usr/bin/env node
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const dotnet = path.join(repoRoot, 'scripts', 'dotnet');
const redisConnectionString = process.env.REDIS_E2E_CONNECTION_STRING || '127.0.0.1:6379';
const kafkaBootstrap = process.env.KAFKA_E2E_BOOTSTRAP_SERVERS || '127.0.0.1:9092';
const kafkaContainer = process.env.KAFKA_E2E_CONTAINER || 'kafka';
const kafkaContainerBootstrap = process.env.KAFKA_E2E_CONTAINER_BOOTSTRAP || 'kafka:29092';
const kafkaCliDir = process.env.KAFKA_E2E_CLI_DIR || '/opt/kafka/bin';
const builderPortA = Number(process.env.REDIS_KAFKA_E2E_BUILDER_PORT_A || 5194);
const builderPortB = Number(process.env.REDIS_KAFKA_E2E_BUILDER_PORT_B || 5195);
const suffix = `${Date.now()}-${process.pid}`;
const topicPrefix = process.env.REDIS_KAFKA_E2E_TOPIC_PREFIX || `cmdb2m.redis-dedup.${suffix}`;
const rawTopic = `${topicPrefix}.raw`;
const aggregationTopic = `${topicPrefix}.aggregation`;
const serviceTopic = `${topicPrefix}.service`;
const suppressionTopic = `${topicPrefix}.suppression`;
const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cmdb2m-redis-kafka-e2e-'));
const rulesPath = path.join(tempDir, 'rules.json');

await assertTcpReachable(redisConnectionString, 'Redis');
await assertTcpReachable(kafkaBootstrap, 'Kafka bootstrap');
await run(dotnet, ['build', path.join(repoRoot, 'src/cmdbconfigbuilder/cmdbconfigbuilder.csproj'), '-v', 'minimal', '/p:NuGetAudit=false', '-m:1'], {
  cwd: repoRoot,
  name: 'build cmdbconfigbuilder',
  timeoutMs: 120000
});
writeRules();

const topics = [rawTopic, aggregationTopic, serviceTopic, suppressionTopic];
try {
  for (const topic of topics) {
    await kafka(['kafka-topics', '--bootstrap-server', kafkaContainerBootstrap, '--create', '--if-not-exists', '--topic', topic, '--partitions', '1', '--replication-factor', '1'], {
      name: `create topic ${topic}`,
      timeoutMs: 30000
    });
  }

  await withBuilders(async () => {
    const expectedRawOffset = (await topicEndOffset(rawTopic)) + 1;
    await produceRawEvent();
    const rawEndOffset = await waitForTopicEndOffset(rawTopic, expectedRawOffset, 30000);
    if (rawEndOffset < expectedRawOffset) {
      throw new Error(`Expected the raw CMDB event to be visible in Kafka after publishing; end offset is ${rawEndOffset}, expected at least ${expectedRawOffset}.`);
    }

    const messages = await waitForMessages(
      aggregationTopic,
      currentMessages => currentMessages
        .map(parseConsoleMessage)
        .filter(item => item?.rule_id === 'redis-kafka-dedup-host')
        .length >= 1,
      30000);
    const matching = messages
      .map(parseConsoleMessage)
      .filter(item => item?.rule_id === 'redis-kafka-dedup-host');

    if (matching.length !== 1) {
      throw new Error(`Expected exactly one aggregation command after two builder consumers processed the same raw event; got ${matching.length}. Raw messages: ${JSON.stringify(messages)}`);
    }
  });
} finally {
  for (const topic of topics) {
    await kafka(['kafka-topics', '--bootstrap-server', kafkaContainerBootstrap, '--delete', '--topic', topic], {
      name: `delete topic ${topic}`,
      timeoutMs: 30000,
      allowFailure: true
    });
  }
  fs.rmSync(tempDir, { recursive: true, force: true });
}

console.log('Redis Kafka semantic dedup e2e checks passed.');

function writeRules() {
  const rules = {
    version: 'redis-kafka-e2e',
    rules: [
      {
        rule_id: 'redis-kafka-dedup-host',
        name: 'Redis Kafka Dedup Host',
        layer: 'service',
        source: {
          class_code: 'Host',
          key_attribute: 'Code'
        },
        target: {
          class_code: 'C2M_ServiceResource',
          idempotency_key: '${source.Code}',
          create_instance: true,
          card_description: '${source.Code}'
        }
      }
    ]
  };
  fs.writeFileSync(rulesPath, `${JSON.stringify(rules, null, 2)}\n`, 'utf8');
}

async function withBuilders(test) {
  const builderA = startBuilder('a', builderPortA);
  const builderB = startBuilder('b', builderPortB);
  try {
    await Promise.all([
      waitForHttp(`http://127.0.0.1:${builderPortA}/health`, 45000, builderA),
      waitForHttp(`http://127.0.0.1:${builderPortB}/health`, 45000, builderB)
    ]);
    await Promise.all([
      waitForOutput(builderA, `subscribed to ${rawTopic}`, 30000),
      waitForOutput(builderB, `subscribed to ${rawTopic}`, 30000)
    ]);
    await sleep(2000);
    await test({ builderA, builderB });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    const details = [
      message,
      '',
      'Builder A output:',
      trimOutput(builderA.output),
      '',
      'Builder B output:',
      trimOutput(builderB.output)
    ].join('\n');
    throw new Error(details);
  } finally {
    await Promise.all([
      stopProcess(builderA.child),
      stopProcess(builderB.child)
    ]);
  }
}

function startBuilder(instance, port) {
  const baseUrl = `http://127.0.0.1:${port}`;
  const child = spawn(dotnet, [
    'run',
    '--project',
    path.join(repoRoot, 'src/cmdbconfigbuilder/cmdbconfigbuilder.csproj'),
    '--no-build',
    '--no-launch-profile'
  ], {
    cwd: repoRoot,
    env: {
      ...process.env,
      ASPNETCORE_URLS: baseUrl,
      ConversionRules__FilePath: rulesPath,
      Redis__Enabled: 'true',
      Redis__ConnectionString: redisConnectionString,
      Redis__FailureMode: 'fail',
      Redis__KeyPrefix: `cmdb2m:test:redis-kafka-e2e:${suffix}`,
      Kafka__Enabled: 'true',
      Kafka__BootstrapServers: kafkaBootstrap,
      Kafka__ClientId: `cmdbconfigbuilder-redis-kafka-e2e-${instance}-${suffix}`,
      Kafka__ConsumerGroupId: `cmdbconfigbuilder-redis-kafka-e2e-${instance}-${suffix}`,
      Kafka__AutoOffsetReset: 'Earliest',
      Kafka__ConsumeTimeoutMs: '200',
      KafkaTopics__CmdbWebhookEvents: rawTopic,
      KafkaTopics__AggregationCommands: aggregationTopic,
      KafkaTopics__ZabbixServiceApplyPlans: serviceTopic,
      KafkaTopics__ZabbixSuppressionApplyPlans: suppressionTopic,
      KafkaTopics__ZabbixApplyPlans: `${topicPrefix}.zabbix`,
      KafkaTopics__ConfigBuildRequests: `${topicPrefix}.config`,
      KafkaTopics__DebugLogs: `${topicPrefix}.logs`,
      Cmdbuild__AuthMode: 'None',
      Zabbix__AuthMode: 'None'
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  const state = { child, output: '' };
  child.stdout.on('data', chunk => {
    state.output += chunk.toString();
  });
  child.stderr.on('data', chunk => {
    state.output += chunk.toString();
  });
  return state;
}

async function produceRawEvent() {
  const event = {
    event_id: `redis-kafka-e2e-${suffix}`,
    source: 'redis-kafka-e2e',
    event_type: 'UPDATE',
    class_code: 'Host',
    card_id: 'redis-kafka-card-001',
    occurred_at: new Date().toISOString(),
    attributes: {
      className: 'Host',
      Code: 'redis-kafka-host-001',
      zabbix_main_hostid: 'redis-kafka-zabbix-host-001'
    }
  };
  const producerCommand = `printf '%s\\n' ${shellQuote(JSON.stringify(event))} | ${kafkaCommand('kafka-console-producer')} --bootstrap-server ${shellQuote(kafkaContainerBootstrap)} --topic ${shellQuote(rawTopic)}`;
  await run('docker', ['exec', kafkaContainer, 'sh', '-lc', producerCommand], {
    cwd: repoRoot,
    name: 'produce raw event',
    timeoutMs: 30000
  });
}

async function consumeTopic(topic, timeoutMs) {
  const result = await kafka(['kafka-console-consumer', '--bootstrap-server', kafkaContainerBootstrap, '--topic', topic, '--partition', '0', '--offset', 'earliest', '--timeout-ms', String(timeoutMs), '--max-messages', '100', '--property', 'print.key=true', '--property', 'key.separator=\t'], {
    name: `consume ${topic}`,
    timeoutMs: timeoutMs + 5000,
    allowFailure: true
  });
  return result.stdout
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean);
}

async function waitForMessages(topic, predicate, timeoutMs) {
  const started = Date.now();
  let lastMessages = [];
  while (Date.now() - started < timeoutMs) {
    lastMessages = await consumeTopic(topic, 3000);
    if (predicate(lastMessages)) {
      return lastMessages;
    }

    await sleep(1000);
  }

  return lastMessages;
}

async function waitForTopicEndOffset(topic, minimumOffset, timeoutMs) {
  const started = Date.now();
  let lastOffset = 0;
  while (Date.now() - started < timeoutMs) {
    lastOffset = await topicEndOffset(topic);
    if (lastOffset >= minimumOffset) {
      return lastOffset;
    }

    await sleep(1000);
  }

  return lastOffset;
}

async function topicEndOffset(topic) {
  const result = await kafka(['kafka-get-offsets', '--bootstrap-server', kafkaContainerBootstrap, '--topic', topic], {
    name: `offset ${topic}`,
    timeoutMs: 30000,
    allowFailure: true
  });
  let maxOffset = 0;
  for (const line of result.stdout.split(/\r?\n/)) {
    const match = /:(\d+)$/.exec(line.trim());
    if (match) {
      maxOffset = Math.max(maxOffset, Number(match[1]));
    }
  }

  return maxOffset;
}

function parseConsoleMessage(line) {
  const separator = line.indexOf('\t');
  const json = separator >= 0 ? line.slice(separator + 1) : line;
  try {
    return JSON.parse(json);
  } catch {
    return null;
  }
}

async function kafka(args, options) {
  const dockerArgs = options?.input
    ? ['exec', '-i', kafkaContainer, kafkaCommand(args[0]), ...args.slice(1)]
    : ['exec', kafkaContainer, kafkaCommand(args[0]), ...args.slice(1)];
  return await run('docker', dockerArgs, {
    cwd: repoRoot,
    ...options
  });
}

function kafkaCommand(command) {
  if (command.includes('/')) {
    return command;
  }

  return `${kafkaCliDir.replace(/\/$/, '')}/${command}.sh`;
}

function shellQuote(value) {
  return `'${String(value).replace(/'/g, `'\\''`)}'`;
}

async function run(command, args, options = {}) {
  const child = spawn(command, args, {
    cwd: options.cwd || repoRoot,
    stdio: ['pipe', 'pipe', 'pipe']
  });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', chunk => {
    stdout += chunk.toString();
  });
  child.stderr.on('data', chunk => {
    stderr += chunk.toString();
  });
  if (options.input) {
    child.stdin.end(options.input);
  } else {
    child.stdin.end();
  }

  const timeout = setTimeout(() => {
    child.kill('SIGKILL');
  }, options.timeoutMs || 30000);

  const code = await new Promise((resolve) => {
    child.on('close', resolve);
  });
  clearTimeout(timeout);

  if (code !== 0 && !options.allowFailure) {
    throw new Error(`${options.name || command} failed with exit code ${code}.\nSTDOUT:\n${trimOutput(stdout)}\nSTDERR:\n${trimOutput(stderr)}`);
  }

  return { stdout, stderr, code };
}

async function waitForHttp(url, timeoutMs, serviceState) {
  const started = Date.now();
  let lastError;
  while (Date.now() - started < timeoutMs) {
    if (serviceState.child.exitCode !== null) {
      throw new Error(`${url} service exited before health check.\n${trimOutput(serviceState.output)}`);
    }

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

  throw new Error(`Timed out waiting for ${url}: ${lastError?.message || 'no response'}\n${trimOutput(serviceState.output)}`);
}

async function stopProcess(child) {
  if (child.exitCode !== null) {
    return;
  }
  child.kill('SIGTERM');
  const exited = await Promise.race([
    new Promise(resolve => child.once('exit', resolve)),
    sleep(5000).then(() => false)
  ]);
  if (exited === false && child.exitCode === null) {
    child.kill('SIGKILL');
    await new Promise(resolve => child.once('exit', resolve));
  }
}

async function waitForOutput(serviceState, text, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (serviceState.output.includes(text)) {
      return;
    }

    if (serviceState.child.exitCode !== null) {
      throw new Error(`Service exited before output contained ${text}.\n${trimOutput(serviceState.output)}`);
    }

    await sleep(250);
  }

  throw new Error(`Timed out waiting for service output to contain ${text}.\n${trimOutput(serviceState.output)}`);
}

async function assertTcpReachable(connectionString, label) {
  const endpoint = parseEndpoint(connectionString);
  await new Promise((resolve, reject) => {
    const socket = net.createConnection(endpoint, () => {
      socket.end();
      resolve();
    });
    socket.setTimeout(3000);
    socket.on('timeout', () => {
      socket.destroy();
      reject(new Error(`${label} endpoint ${endpoint.host}:${endpoint.port} timed out.`));
    });
    socket.on('error', error => {
      reject(new Error(`${label} endpoint ${endpoint.host}:${endpoint.port} is not reachable: ${error.message}`));
    });
  });
}

function parseEndpoint(connectionString) {
  const value = connectionString.includes('://')
    ? new URL(connectionString)
    : new URL(`tcp://${connectionString}`);
  return {
    host: value.hostname || '127.0.0.1',
    port: Number(value.port || 6379)
  };
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function trimOutput(value) {
  if (!value) {
    return '';
  }
  return value.length <= 5000 ? value : `${value.slice(0, 5000)}\n... truncated ...`;
}
