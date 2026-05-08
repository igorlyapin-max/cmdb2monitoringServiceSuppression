#!/usr/bin/env node
const args = parseArgs(process.argv.slice(2));

const baseUrl = trimTrailingSlash(args.baseUrl ?? process.env.CMDBUILD_BASE_URL ?? 'http://127.0.0.1:8090/cmdbuild/services/rest/v3');
const username = args.username ?? process.env.CMDBUILD_USERNAME ?? 'admin';
const password = args.password ?? process.env.CMDBUILD_PASSWORD ?? 'admin';
const classCode = args.classCode ?? process.env.CMDBUILD_CLASS ?? 'NTbook';
const attribute = args.attribute ?? process.env.CMDBUILD_TOUCH_ATTRIBUTE ?? 'Description';
const pageSize = numberArg(args.pageSize ?? process.env.CMDBUILD_PAGE_SIZE, 100);
const maxCards = optionalNumberArg(args.maxCards ?? args.limit ?? process.env.CMDBUILD_MAX_CARDS);
const concurrency = numberArg(args.concurrency ?? process.env.CMDBUILD_CONCURRENCY, 8);
const dryRun = Boolean(args.dryRun);
const marker = args.marker ?? process.env.CMDBUILD_TOUCH_MARKER ?? `[agg-test ${formatLocalTimestamp()}]`;
const markerPattern = /\s*\[agg-test [^\]]+\]$/u;
const maxDescriptionLength = numberArg(args.maxLength ?? process.env.CMDBUILD_DESCRIPTION_MAX_LENGTH, 250);
const auth = `Basic ${Buffer.from(`${username}:${password}`).toString('base64')}`;

const cards = await readAllCards();
let updated = 0;
let failed = 0;

console.log(`class=${classCode} cards=${cards.length} attribute=${attribute} marker="${marker}" dryRun=${dryRun} pageSize=${pageSize}`);

await runLimited(cards, Math.max(1, concurrency), async (card, index) => {
  const nextValue = nextMarkedValue(card[attribute]);
  if (dryRun) {
    updated += 1;
    logProgress(updated, cards.length, card);
    return;
  }

  const response = await fetch(`${baseUrl}/classes/${encodeURIComponent(classCode)}/cards/${encodeURIComponent(String(card._id))}`, {
    method: 'PUT',
    headers: {
      authorization: auth,
      accept: 'application/json',
      'content-type': 'application/json'
    },
    body: JSON.stringify({ [attribute]: nextValue })
  });
  const text = await response.text();
  const payload = parseJson(text);
  if (!response.ok || payload?.success === false) {
    failed += 1;
    console.error(`failed ${index + 1}/${cards.length}: id=${card._id} code=${card.Code ?? ''} status=${response.status} ${text.slice(0, 300)}`);
    return;
  }

  updated += 1;
  logProgress(updated, cards.length, card);
});

console.log(JSON.stringify({ classCode, total: cards.length, updated, failed, dryRun }));
if (failed > 0) {
  process.exitCode = 1;
}

async function readAllCards() {
  const result = [];
  const seen = new Set();
  let start = 0;
  while (true) {
    const remaining = maxCards === undefined ? pageSize : Math.min(pageSize, maxCards - result.length);
    if (remaining <= 0) {
      return result;
    }

    const url = `${baseUrl}/classes/${encodeURIComponent(classCode)}/cards?limit=${remaining}&start=${start}`;
    const response = await fetch(url, {
      headers: {
        authorization: auth,
        accept: 'application/json'
      }
    });
    const text = await response.text();
    const payload = parseJson(text);
    if (!response.ok || payload?.success === false) {
      throw new Error(`failed to list ${classCode}: HTTP ${response.status} ${text.slice(0, 500)}`);
    }

    const page = Array.isArray(payload?.data) ? payload.data : [];
    let added = 0;
    for (const card of page) {
      const id = String(card?._id ?? '');
      if (!id || seen.has(id)) {
        continue;
      }
      seen.add(id);
      result.push(card);
      added += 1;
    }

    const total = Number(payload?.meta?.total ?? result.length);
    if (page.length === 0 || result.length >= total || (maxCards !== undefined && result.length >= maxCards)) {
      return result;
    }

    if (added === 0) {
      throw new Error(`CMDBuild pagination did not advance for ${classCode}: start=${start} limit=${remaining}`);
    }

    start += page.length;
  }
}

function nextMarkedValue(value) {
  const current = String(value ?? '').replace(markerPattern, '').trimEnd();
  const separator = current ? ' ' : '';
  const maxBaseLength = Math.max(0, maxDescriptionLength - marker.length - separator.length);
  return `${current.slice(0, maxBaseLength)}${separator}${marker}`;
}

async function runLimited(items, size, worker) {
  let next = 0;
  const workers = Array.from({ length: Math.min(size, items.length) }, async () => {
    while (next < items.length) {
      const index = next;
      next += 1;
      await worker(items[index], index);
    }
  });
  await Promise.all(workers);
}

function logProgress(count, total, card) {
  if (count <= 5 || count % 25 === 0 || count === total) {
    console.log(`updated ${count}/${total}: ${card._id} ${card.Code ?? ''}`);
  }
}

function parseArgs(items) {
  const parsed = {};
  for (let index = 0; index < items.length; index += 1) {
    const item = items[index];
    if (item === '--dry-run') {
      parsed.dryRun = true;
      continue;
    }
    if (!item.startsWith('--')) {
      continue;
    }

    const [rawKey, inlineValue] = item.slice(2).split('=', 2);
    const key = rawKey.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
    parsed[key] = inlineValue ?? items[index + 1] ?? '';
    if (inlineValue === undefined) {
      index += 1;
    }
  }
  return parsed;
}

function numberArg(value, fallback) {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : fallback;
}

function optionalNumberArg(value) {
  if (value === undefined || value === null || value === '') {
    return undefined;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : undefined;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function trimTrailingSlash(value) {
  return String(value).replace(/\/+$/, '');
}

function formatLocalTimestamp() {
  const date = new Date();
  const pad = (value) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
