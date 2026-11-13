// Load test with k6: requesters and agents using the API at the same time.
//
//   docker run --rm -i -v "$PWD/scripts/load:/scripts" grafana/k6 run \
//     -e BASE_URL=http://host.docker.internal:8080 /scripts/load.js
//
// It needs the demo users (alice, agent1) and tickets to work on: the demo data, plus more if you want a realistic
// corpus for the triage (see docs/load-test.md). Every request is named so the summary shows each route on its own.
import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const PASSWORD = '1q2w3E*';

// the routes a person actually uses, each with the answer time we expect for 95 in 100 calls
const LIMITS = {
  my_list: 300, ticket: 300, create: 500, queue: 400, queue_search: 500, summary: 500, triage: 800, export: 1500,
};

export const options = {
  scenarios: {
    requesters: { executor: 'constant-vus', vus: Number(__ENV.REQUESTERS || 10), duration: __ENV.DURATION || '30s', exec: 'requester' },
    agents: { executor: 'constant-vus', vus: Number(__ENV.AGENTS || 10), duration: __ENV.DURATION || '30s', exec: 'agent' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    ...Object.fromEntries(Object.entries(LIMITS).map(([name, ms]) => [`http_req_duration{name:${name}}`, [`p(95)<${ms}`]])),
  },
  summaryTrendStats: ['avg', 'med', 'p(95)', 'max'],
};

function token(username) {
  const response = http.post(`${BASE}/connect/token`, {
    grant_type: 'password', client_id: 'Helpdesk_Demo', username, password: PASSWORD, scope: 'Helpdesk',
  });
  return response.json('access_token');
}

export function setup() {
  const alice = token('alice');
  const agent = token('agent1');
  const category = http.get(`${BASE}/api/app/category?ActiveOnly=true`, { headers: { Authorization: `Bearer ${alice}` } }).json('items.0.id');
  return { alice, agent, category };
}

const get = (path, token, name) => http.get(`${BASE}${path}`, { headers: { Authorization: `Bearer ${token}` }, tags: { name } });

function ok(response, name) {
  check(response, { [`${name} answers 200`]: (r) => r.status === 200 });
  return response;
}

const WORDS = ['VPN', 'printer', 'email', 'laptop', 'Wi-Fi', 'password', 'monitor', 'backup', 'badge', 'server'];
const pick = () => WORDS[Math.floor(Math.random() * WORDS.length)];

export function requester(data) {
  const list = ok(get('/api/app/ticket/my-list?MaxResultCount=15', data.alice, 'my_list'), 'my list').json('items');
  if (list.length > 0) {
    ok(get(`/api/app/ticket/${list[Math.floor(Math.random() * list.length)].id}`, data.alice, 'ticket'), 'ticket');
  }
  if (Math.random() < 0.2) {
    const created = http.post(`${BASE}/api/app/ticket`, JSON.stringify({
      categoryId: data.category, title: `${pick()} problem ${__VU}-${__ITER}`, description: `The ${pick()} stopped working this morning.`,
    }), { headers: { Authorization: `Bearer ${data.alice}`, 'Content-Type': 'application/json' }, tags: { name: 'create' } });
    ok(created, 'create');
  }
  sleep(0.5 + Math.random());
}

export function agent(data) {
  ok(get('/api/app/ticket/queue?Scope=3&MaxResultCount=15', data.agent, 'queue'), 'queue');
  ok(get(`/api/app/ticket/queue?Scope=3&MaxResultCount=15&Filter=${pick()}`, data.agent, 'queue_search'), 'queue search');
  ok(get('/api/app/ticket-report/summary', data.agent, 'summary'), 'summary');
  ok(get(`/api/app/ticket-triage/suggestion?Title=${pick()}%20keeps%20failing%20in%20the%20office&Description=It%20started%20yesterday`, data.agent, 'triage'), 'triage');
  if (Math.random() < 0.1) {
    ok(get('/api/app/ticket-export/tickets', data.agent, 'export'), 'export');
  }
  sleep(0.5 + Math.random());
}
