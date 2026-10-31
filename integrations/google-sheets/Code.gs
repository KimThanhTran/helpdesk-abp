/**
 * Helpdesk -> Google Sheets
 *
 * A web app that receives a signed summary of a ticket from the Helpdesk API each time the ticket changes
 * and keeps two sheets up to date:
 *   - "Tickets": one row per ticket, always showing the latest state.
 *   - "Events":  one row per change, an append-only log.
 *
 * The spreadsheet is created in the owner's Drive the first time a request arrives, so there is nothing to
 * create by hand. Setup: see README.md in this folder.
 *
 * Security: the web app is reachable by anyone who knows its URL (Apps Script cannot restrict that for a
 * service call), so every request must carry an HMAC-SHA256 signature made with a shared secret that only the
 * API and this script know. Requests with a wrong signature or an old timestamp are ignored. The script has no
 * endpoint that reads ticket data.
 */

var TICKETS_SHEET = 'Tickets';
var EVENTS_SHEET = 'Events';
var MAX_CLOCK_SKEW_SECONDS = 300;

var TICKET_COLUMNS = [
  'Code', 'Title', 'Category', 'Status', 'Priority', 'Requester', 'Assignee', 'Created', 'First response due',
  'Resolution due', 'First responded', 'Resolved', 'Closed', 'Reopened', 'Last event', 'Updated'
];
var EVENT_COLUMNS = ['Time', 'Code', 'Event', 'Status', 'Event id'];
var DATE_COLUMNS = [8, 9, 10, 11, 12, 13, 16]; // 1-based columns of TICKET_COLUMNS that hold dates

/** Health check. Returns no ticket data. */
function doGet() {
  return json_({ ok: true, service: 'helpdesk-sheets' });
}

/** Entry point for the API. Always answers HTTP 200 (Apps Script cannot do otherwise): read the "ok" field. */
function doPost(e) {
  try {
    var secret = PropertiesService.getScriptProperties().getProperty('SECRET');
    if (!secret) {
      return json_({ ok: false, error: 'not_configured' });
    }

    var envelope = JSON.parse(e.postData.contents);
    if (!verify_(envelope, secret)) {
      return json_({ ok: false, error: 'bad_signature' });
    }

    var row = JSON.parse(envelope.payload);
    var lock = LockService.getScriptLock();
    lock.waitLock(20000);
    try {
      store_(row);
    } finally {
      lock.releaseLock();
    }
    return json_({ ok: true });
  } catch (error) {
    return json_({ ok: false, error: 'server_error' });
  }
}

/** Run this once from the editor to see where the spreadsheet is. */
function showSpreadsheetUrl() {
  Logger.log(book_().getUrl());
}

// ---------------------------------------------------------------------------------------------------------------

function verify_(envelope, secret) {
  if (!envelope || typeof envelope.payload !== 'string' || !envelope.timestamp || !envelope.signature) {
    return false;
  }

  var timestamp = Number(envelope.timestamp);
  if (!isFinite(timestamp) || Math.abs(nowSeconds_() - timestamp) > MAX_CLOCK_SKEW_SECONDS) {
    return false; // too old (a replay) or from the future
  }

  var bytes = Utilities.computeHmacSha256Signature(envelope.timestamp + '.' + envelope.payload, secret);
  return safeEquals_(toHex_(bytes), String(envelope.signature).toLowerCase());
}

/** Apps Script returns signed bytes (-128..127): turn them into lower-case hex. */
function toHex_(bytes) {
  return bytes.map(function (b) {
    return ('0' + (b & 0xff).toString(16)).slice(-2);
  }).join('');
}

/** Compares without stopping at the first difference. */
function safeEquals_(a, b) {
  if (a.length !== b.length) {
    return false;
  }
  var diff = 0;
  for (var i = 0; i < a.length; i++) {
    diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return diff === 0;
}

function store_(row) {
  var book = book_();
  var tickets = sheet_(book, TICKETS_SHEET, TICKET_COLUMNS);
  var events = sheet_(book, EVENTS_SHEET, EVENT_COLUMNS);

  // The same delivery can arrive twice; the event id makes the log idempotent.
  if (!eventSeen_(events, row.eventId)) {
    events.appendRow([toDate_(row.updatedAt), row.code, row.event, row.status, row.eventId]);
  }

  upsertTicket_(tickets, row);
}

function upsertTicket_(sheet, row) {
  var values = [
    row.code, row.title || '', row.category, row.status, row.priority, row.requester || '', row.assignee || '',
    toDate_(row.createdAt), toDate_(row.firstResponseDueAt), toDate_(row.resolutionDueAt),
    toDate_(row.firstRespondedAt), toDate_(row.resolvedAt), toDate_(row.closedAt), row.reopenCount,
    row.event, toDate_(row.updatedAt)
  ];

  var line = findRow_(sheet, 1, row.code);
  if (line === 0) {
    sheet.appendRow(values);
    return;
  }

  // Deliveries can overtake each other. Never replace a newer state with an older one.
  var stored = sheet.getRange(line, TICKET_COLUMNS.length).getValue();
  if (stored && row.updatedAt && new Date(stored).getTime() > new Date(row.updatedAt).getTime()) {
    return;
  }
  sheet.getRange(line, 1, 1, values.length).setValues([values]);
}

function eventSeen_(sheet, eventId) {
  return Boolean(eventId) && findRow_(sheet, EVENT_COLUMNS.length, eventId) !== 0;
}

/** 1-based row number of the first data row whose cell in `column` equals `value`, or 0. */
function findRow_(sheet, column, value) {
  var last = sheet.getLastRow();
  if (last < 2) {
    return 0;
  }
  var cells = sheet.getRange(2, column, last - 1, 1).getValues();
  for (var i = 0; i < cells.length; i++) {
    if (String(cells[i][0]) === String(value)) {
      return i + 2;
    }
  }
  return 0;
}

function toDate_(iso) {
  return iso ? new Date(iso) : '';
}

/** The spreadsheet is created once, in the owner's Drive, and remembered by id. */
function book_() {
  var props = PropertiesService.getScriptProperties();
  var id = props.getProperty('SHEET_ID');
  if (id) {
    try {
      return SpreadsheetApp.openById(id);
    } catch (error) {
      // deleted or no longer reachable: fall through and create a new one
    }
  }
  var book = SpreadsheetApp.create('Helpdesk tickets');
  props.setProperty('SHEET_ID', book.getId());
  return book;
}

function sheet_(book, name, columns) {
  var sheet = book.getSheetByName(name);
  if (sheet) {
    return sheet;
  }

  // A new spreadsheet starts with an empty "Sheet1": reuse it for the first sheet instead of leaving it behind.
  var first = book.getSheets()[0];
  if (first && first.getName() === 'Sheet1' && first.getLastRow() === 0) {
    first.setName(name);
    sheet = first;
  } else {
    sheet = book.insertSheet(name);
  }

  sheet.appendRow(columns);
  sheet.setFrozenRows(1);
  sheet.getRange(1, 1, 1, columns.length).setFontWeight('bold');

  var dateColumns = name === TICKETS_SHEET ? DATE_COLUMNS : [1];
  var dataRows = Math.max(sheet.getMaxRows() - 1, 1);
  dateColumns.forEach(function (column) {
    sheet.getRange(2, column, dataRows, 1).setNumberFormat('yyyy-mm-dd hh:mm');
  });
  return sheet;
}

function nowSeconds_() {
  return Date.now() / 1000;
}

function json_(object) {
  return ContentService.createTextOutput(JSON.stringify(object)).setMimeType(ContentService.MimeType.JSON);
}
