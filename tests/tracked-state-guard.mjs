import { spawnSync } from 'node:child_process';
import path from 'node:path';

const gitResult = spawnSync('git', ['ls-files'], { encoding: 'utf8' });
if (gitResult.status !== 0 || !gitResult.stdout) {
  const detail = gitResult.stderr || gitResult.error?.message || 'unknown error';
  throw new Error(`git ls-files failed: ${detail}`);
}

const trackedFiles = gitResult.stdout
  .split('\n')
  .map((item) => item.trim())
  .filter(Boolean);

const blocked = trackedFiles.filter((file) => isRuntimeState(file));

if (blocked.length > 0) {
  console.error('Runtime state files must not be tracked in git:');
  for (const file of blocked) {
    console.error(`- ${file}`);
  }
  process.exitCode = 1;
} else {
  console.log('Tracked runtime state guard passed.');
}

function isRuntimeState(file) {
  const normalized = file.replaceAll('\\', '/');
  const name = path.posix.basename(normalized);
  return normalized.startsWith('src/zabbixconfig2api/state/')
    || name === 'apply-membership.json'
    || /\.db(?:-.+)?$/i.test(name);
}
