// In-memory stand-ins for the Google services Code.gs uses (Sheets, Properties, Lock, Utilities, ContentService).
// Used by Code.test.js and by local-server.js, so the script's real logic runs without a Google account.
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const SOURCE = fs.readFileSync(path.join(__dirname, 'Code.gs'), 'utf8');
const DEFAULT_SECRET = 'test-secret';

// ---------------------------------------------------------------- fakes of the Google services

class FakeSheet {
  constructor(name) {
    this.name = name;
    this.rows = [];
    this.formats = [];
    this.maxRows = 1000;
  }
  getName() { return this.name; }
  setName(name) { this.name = name; }
  getLastRow() { return this.rows.length; }
  getMaxRows() { return this.maxRows; }
  appendRow(values) { this.rows.push([...values]); }
  setFrozenRows() {}
  getRange(row, column, numRows = 1, numColumns = 1) {
    const sheet = this;
    return {
      getValues() {
        return Array.from({ length: numRows }, (_, i) =>
          Array.from({ length: numColumns }, (_, j) => (sheet.rows[row - 1 + i] || [])[column - 1 + j] ?? ''));
      },
      getValue() { return this.getValues()[0][0]; },
      setValues(values) {
        values.forEach((line, i) => line.forEach((value, j) => {
          sheet.rows[row - 1 + i] = sheet.rows[row - 1 + i] || [];
          sheet.rows[row - 1 + i][column - 1 + j] = value;
        }));
      },
      setNumberFormat(format) { sheet.formats.push({ column, rows: numRows, format }); return this; },
      setFontWeight() { return this; },
    };
  }
}

class FakeBook {
  constructor(id) {
    this.id = id;
    this.sheets = [new FakeSheet('Sheet1')];
  }
  getId() { return this.id; }
  getUrl() { return `https://docs.google.com/spreadsheets/d/${this.id}`; }
  getSheets() { return this.sheets; }
  getSheetByName(name) { return this.sheets.find((sheet) => sheet.name === name) || null; }
  insertSheet(name) { const sheet = new FakeSheet(name); this.sheets.push(sheet); return sheet; }
}

function createWorld({ now = Date.now(), secret = DEFAULT_SECRET } = {}) {
  const world = { now, props: secret ? { SECRET: secret } : {}, books: new Map(), created: 0, logs: [] };

  class FakeDate extends Date {
    static now() { return world.now; }
  }

  const sandbox = {
    Date: FakeDate,
    PropertiesService: {
      getScriptProperties: () => ({
        getProperty: (key) => (key in world.props ? world.props[key] : null),
        setProperty: (key, value) => { world.props[key] = value; },
      }),
    },
    LockService: { getScriptLock: () => ({ waitLock() {}, releaseLock() {} }) },
    Logger: { log: (message) => world.logs.push(message) },
    Utilities: {
      // Apps Script returns signed bytes, so the fake does too: the script must cope with negative values
      computeHmacSha256Signature: (value, key) =>
        Array.from(crypto.createHmac('sha256', key).update(value).digest()).map((b) => (b > 127 ? b - 256 : b)),
    },
    SpreadsheetApp: {
      create: () => {
        const book = new FakeBook(`book-${++world.created}`);
        world.books.set(book.id, book);
        return book;
      },
      openById: (id) => {
        if (!world.books.has(id)) throw new Error('Unable to open the spreadsheet');
        return world.books.get(id);
      },
    },
    ContentService: {
      MimeType: { JSON: 'JSON' },
      createTextOutput: (text) => ({ text, setMimeType(type) { this.mime = type; return this; } }),
    },
  };

  const context = vm.createContext(sandbox);
  vm.runInContext(SOURCE, context);
  world.script = context;
  return world;
}

module.exports = { createWorld, DEFAULT_SECRET };
