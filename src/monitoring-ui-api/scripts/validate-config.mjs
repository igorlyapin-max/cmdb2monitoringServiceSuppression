import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const config = JSON.parse(await readFile(path.join(root, 'config', 'appsettings.json'), 'utf8'));

const errors = [];
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

if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exitCode = 1;
} else {
  console.log('UI configuration is valid.');
}
