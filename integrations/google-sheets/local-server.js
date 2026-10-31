// Runs the real Code.gs on your machine with an in-memory fake of Google Sheets.
// It lets you try the whole chain (API -> signed webhook -> script) without a Google account:
//
//   HELPDESK_SHEETS_SECRET=dev-secret node integrations/google-sheets/local-server.js
//
// then start the API with
//   Helpdesk__Sheets__WebhookUrl=http://localhost:8787/exec   Helpdesk__Sheets__Secret=dev-secret
//
// GET /dump shows what the "spreadsheet" contains. This is a development aid, not part of the product.
'use strict';

const http = require('node:http');
const { createWorld } = require('./fake-google');

const secret = process.env.HELPDESK_SHEETS_SECRET || 'dev-secret';
const port = Number(process.env.PORT || 8787);
const host = process.env.HOST || '127.0.0.1'; // set HOST=0.0.0.0 inside a container
const world = createWorld({ secret });

function dump() {
  const book = world.books.get(world.props.SHEET_ID);
  if (!book) return { spreadsheet: null };
  return {
    spreadsheet: book.getUrl(),
    sheets: Object.fromEntries(book.getSheets().map((sheet) => [sheet.name, sheet.rows])),
  };
}

const server = http.createServer((request, response) => {
  const chunks = [];
  request.on('data', (chunk) => chunks.push(chunk));
  request.on('end', () => {
    world.now = Date.now(); // the script checks the request timestamp against "now"
    let output;
    if (request.method === 'POST' && request.url.startsWith('/exec')) {
      output = world.script.doPost({ postData: { contents: Buffer.concat(chunks).toString('utf8') } }).text;
    } else if (request.method === 'GET' && request.url.startsWith('/dump')) {
      output = JSON.stringify(dump(), null, 2);
    } else if (request.method === 'GET') {
      output = world.script.doGet().text;
    } else {
      response.writeHead(404).end();
      return;
    }
    response.writeHead(200, { 'Content-Type': 'application/json' }).end(output);
  });
});

server.listen(port, host, () => {
  console.log(`Fake Apps Script web app on http://${host}:${port}/exec (GET /dump to inspect the sheet)`);
});
