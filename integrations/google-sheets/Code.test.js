// Tests for Code.gs. Google's services are replaced by small in-memory fakes, so the script's own logic
// (signature check, replay protection, upsert, idempotency, ordering) runs for real, with no Google account.
//
//   node --test integrations/google-sheets
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');

const { createWorld, DEFAULT_SECRET: SECRET } = require('./fake-google');
const NOW = Date.UTC(2026, 10, 2, 9, 0, 0); // a fixed "current time" in milliseconds

// ---------------------------------------------------------------- helpers

const row = (overrides = {}) => ({
  code: 'HD-2026-000001',
  title: null,
  category: 'IT support',
  status: 'New',
  priority: 'Normal',
  requester: null,
  assignee: null,
  createdAt: '2026-11-02T09:00:00Z',
  updatedAt: '2026-11-02T09:00:00Z',
  firstResponseDueAt: '2026-11-02T17:00:00Z',
  resolutionDueAt: '2026-11-09T17:00:00Z',
  firstRespondedAt: null,
  resolvedAt: null,
  closedAt: null,
  reopenCount: 0,
  event: 'Created',
  eventId: crypto.randomUUID(),
  ...overrides,
});

const sign = (secret, timestamp, payload) =>
  crypto.createHmac('sha256', secret).update(`${timestamp}.${payload}`).digest('hex');

/** Builds the request exactly as the C# SheetsExporter does, and sends it through doPost. */
function post(world, ticketRow, { secret = SECRET, timestamp, tamper } = {}) {
  const ts = String(timestamp ?? Math.floor(world.now / 1000));
  const payload = JSON.stringify(ticketRow);
  const envelope = { timestamp: ts, payload, signature: sign(secret, ts, payload) };
  if (tamper) tamper(envelope);
  const output = world.script.doPost({ postData: { contents: JSON.stringify(envelope) } });
  return JSON.parse(output.text);
}

const book = (world) => world.books.get(world.props.SHEET_ID);
const tickets = (world) => book(world).getSheetByName('Tickets');
const events = (world) => book(world).getSheetByName('Events');

// ---------------------------------------------------------------- tests

test('a signed request creates the spreadsheet by itself and stores the ticket', () => {
  const world = createWorld();

  assert.deepEqual(post(world, row()), { ok: true });

  assert.equal(world.created, 1, 'the spreadsheet is created automatically, nobody has to make one');
  assert.ok(world.props.SHEET_ID, 'its id is remembered');
  assert.equal(tickets(world).rows.length, 2, 'a header and one ticket');
  assert.equal(tickets(world).rows[1][0], 'HD-2026-000001');
  assert.equal(tickets(world).rows[1][3], 'New');
  assert.equal(tickets(world).rows[1][2], 'IT support');
});

test('the empty default sheet is reused instead of left behind', () => {
  const world = createWorld();
  post(world, row());

  assert.deepEqual(book(world).getSheets().map((sheet) => sheet.name), ['Tickets', 'Events']);
});

test('the spreadsheet is created once and reused for every later request', () => {
  const world = createWorld();

  post(world, row({ code: 'HD-2026-000001' }));
  post(world, row({ code: 'HD-2026-000002' }));

  assert.equal(world.created, 1);
  assert.equal(tickets(world).rows.length, 3);
});

test('a lost spreadsheet is replaced with a new one', () => {
  const world = createWorld();
  post(world, row());
  world.books.clear(); // the owner deleted it

  assert.deepEqual(post(world, row({ code: 'HD-2026-000009' })), { ok: true });
  assert.equal(world.created, 2);
});

test('a later change to the same ticket updates its row instead of adding another', () => {
  const world = createWorld();
  post(world, row());

  post(world, row({
    status: 'Open', assignee: 'agent1', event: 'Assigned', updatedAt: '2026-11-02T10:00:00Z',
  }));

  assert.equal(tickets(world).rows.length, 2, 'still one ticket');
  assert.equal(tickets(world).rows[1][3], 'Open');
  assert.equal(tickets(world).rows[1][6], 'agent1');
  assert.equal(tickets(world).rows[1][14], 'Assigned');
  assert.equal(events(world).rows.length, 3, 'the events log keeps every change: header + 2');
});

test('an older delivery that arrives late never overwrites newer state', () => {
  const world = createWorld();
  post(world, row({ status: 'Closed', event: 'StatusChanged', updatedAt: '2026-11-02T12:00:00Z' }));

  post(world, row({ status: 'Open', event: 'Assigned', updatedAt: '2026-11-02T10:00:00Z' }));

  assert.equal(tickets(world).rows[1][3], 'Closed');
  assert.equal(events(world).rows.length, 3, 'the late event is still logged for the record');
});

test('the same delivery twice is stored once', () => {
  const world = createWorld();
  const delivery = row();

  post(world, delivery);
  post(world, delivery);

  assert.equal(events(world).rows.length, 2, 'header and one event');
  assert.equal(tickets(world).rows.length, 2);
});

test('title and names are blank when the API leaves them out', () => {
  const world = createWorld();
  post(world, row());

  const [, title, , , , requester, assignee] = tickets(world).rows[1];
  assert.deepEqual([title, requester, assignee], ['', '', '']);
});

test('dates are stored as real dates so the sheet can sort and calculate with them', () => {
  const world = createWorld();
  post(world, row({ firstRespondedAt: '2026-11-02T09:30:00Z' }));

  const stored = tickets(world).rows[1];
  assert.ok(stored[7] instanceof Date);
  assert.equal(stored[7].toISOString(), '2026-11-02T09:00:00.000Z');
  assert.ok(stored[10] instanceof Date);
  assert.equal(stored[11], '', 'a date that does not exist yet stays empty');
});

// ---------------------------------------------------------------- security

test('a wrong signature is refused and nothing is stored', () => {
  const world = createWorld();

  const result = post(world, row(), { secret: 'someone-elses-secret' });

  assert.deepEqual(result, { ok: false, error: 'bad_signature' });
  assert.equal(world.created, 0, 'not even the spreadsheet is created for a stranger');
});

test('changing the payload after it was signed is detected', () => {
  const world = createWorld();

  const result = post(world, row(), {
    tamper: (envelope) => { envelope.payload = envelope.payload.replace('New', 'Closed'); },
  });

  assert.deepEqual(result, { ok: false, error: 'bad_signature' });
});

test('changing the timestamp after signing is detected', () => {
  const world = createWorld();

  const result = post(world, row(), {
    tamper: (envelope) => { envelope.timestamp = String(Number(envelope.timestamp) + 1); },
  });

  assert.deepEqual(result, { ok: false, error: 'bad_signature' });
});

test('a captured request cannot be replayed later', () => {
  const world = createWorld();
  const delivery = row();
  const hourAgo = Math.floor(world.now / 1000) - 3600;

  assert.deepEqual(post(world, delivery, { timestamp: hourAgo }), { ok: false, error: 'bad_signature' });
});

test('a request from the future is refused too', () => {
  const world = createWorld();
  const tomorrow = Math.floor(world.now / 1000) + 86400;

  assert.deepEqual(post(world, row(), { timestamp: tomorrow }), { ok: false, error: 'bad_signature' });
});

test('a small clock difference is tolerated', () => {
  const world = createWorld();
  const fourMinutesAgo = Math.floor(world.now / 1000) - 240;

  assert.deepEqual(post(world, row(), { timestamp: fourMinutesAgo }), { ok: true });
});

test('without a configured secret everything is refused', () => {
  const world = createWorld({ secret: null });

  assert.deepEqual(post(world, row(), { secret: 'anything' }), { ok: false, error: 'not_configured' });
  assert.equal(world.created, 0);
});

test('missing fields and broken JSON are refused without throwing', () => {
  const world = createWorld();

  for (const contents of ['not json', '{}', '{"timestamp":"1","payload":5,"signature":"x"}', 'null']) {
    const output = world.script.doPost({ postData: { contents } });
    assert.equal(JSON.parse(output.text).ok, false, contents);
  }
  assert.equal(world.created, 0);
});

test('the health check returns no ticket data', () => {
  const world = createWorld();
  post(world, row({ title: 'Salary problem' }));

  const body = JSON.parse(world.script.doGet().text);

  assert.deepEqual(body, { ok: true, service: 'helpdesk-sheets' });
});

test('an upper-case signature is accepted, a truncated one is not', () => {
  const world = createWorld();

  assert.equal(post(world, row(), { tamper: (e) => { e.signature = e.signature.toUpperCase(); } }).ok, true);
  assert.equal(post(world, row(), { tamper: (e) => { e.signature = e.signature.slice(0, 40); } }).ok, false);
});

// ---------------------------------------------------------------- agreement with the C# side

test('the fields the script reads are exactly the ones the .NET API sends', () => {
  // the same list is asserted in SheetsContractTests.cs
  const fields = [
    'code', 'title', 'category', 'status', 'priority', 'requester', 'assignee', 'createdAt', 'updatedAt',
    'firstResponseDueAt', 'resolutionDueAt', 'firstRespondedAt', 'resolvedAt', 'closedAt', 'reopenCount',
    'event', 'eventId',
  ];

  assert.deepEqual(Object.keys(row()).sort(), [...fields].sort());
});

test('the signature matches the golden vector that the .NET tests also check', () => {
  const world = createWorld({ now: 1700000000 * 1000 });
  const payload = '{"code":"HD-2026-000001","status":"New"}';
  const golden = '7087aaccacb78cdb6231d9ea02fbec5d60f5c29c1b6671719578e2e35a51ece8';

  assert.equal(sign('golden-secret', '1700000000', payload), golden, 'Node agrees with the vector');
  assert.equal(
    world.script.verify_({ timestamp: '1700000000', payload, signature: golden }, 'golden-secret'),
    true,
    'the script accepts a signature made the way the C# code makes it');
});

test('signed bytes from Apps Script are turned into the right hex', () => {
  const world = createWorld();

  assert.equal(world.script.toHex_([0, 1, 15, 16, 127, -128, -1]), '00010f107f80ff');
});

test('the script tells the owner where the spreadsheet is', () => {
  const world = createWorld();

  world.script.showSpreadsheetUrl();

  assert.match(world.logs[0], /^https:\/\/docs\.google\.com\/spreadsheets\/d\/book-1$/);
});
