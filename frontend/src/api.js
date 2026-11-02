import { ACTIONS } from './constants';

// By default the API is on the same origin as the page (the dev server and nginx both proxy it).
// A static host such as GitHub Pages sets VITE_API_URL at build time to point at the API instead.
export const API_URL = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '');

export class ApiError extends Error {
  constructor(status, message, code) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

let token = null;
let onUnauthorized = () => {};

export const setToken = (value) => {
  token = value;
};
export const setUnauthorizedHandler = (handler) => {
  onUnauthorized = handler;
};

const query = (params = {}) => {
  const entries = Object.entries(params).filter(([, value]) => value !== undefined && value !== null && value !== '');
  return entries.length ? `?${new URLSearchParams(entries)}` : '';
};

/** ABP errors look like { error: { code, message, validationErrors: [{ message }] } }. Turn them into one sentence. */
async function readError(response) {
  try {
    const { error } = await response.json();
    const detail = error?.validationErrors?.map((item) => item.message).join(' ');
    return { message: detail || error?.message || response.statusText || 'Request failed', code: error?.code };
  } catch {
    return { message: response.statusText || 'Request failed' };
  }
}

async function request(method, path, { params, body, signal } = {}) {
  let response;
  try {
    response = await fetch(`${API_URL}${path}${query(params)}`, {
      method,
      signal,
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch (error) {
    if (error.name === 'AbortError') throw error;
    throw new ApiError(0, 'Cannot reach the server. Is the API running?');
  }

  if (response.status === 401 && token) {
    onUnauthorized();
    throw new ApiError(401, 'Your session has expired. Please sign in again.');
  }
  if (!response.ok) {
    const { message, code } = await readError(response);
    throw new ApiError(response.status, message, code);
  }
  return response.status === 204 ? null : response.json().catch(() => null);
}

/** Password sign-in with the demo client. Resolves to the access token. */
export async function signIn(username, password) {
  let response;
  try {
    response = await fetch(`${API_URL}/connect/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'password', client_id: 'Helpdesk_Demo', username, password, scope: 'Helpdesk',
      }),
    });
  } catch {
    throw new ApiError(0, 'Cannot reach the server. Is the API running?');
  }
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new ApiError(response.status, data.error_description || 'Invalid user name or password.');
  }
  return data.access_token;
}

export const api = {
  me: (signal) => request('GET', '/api/abp/application-configuration', { signal }).then((config) => ({
    id: config.currentUser.id,
    name: config.currentUser.userName,
    isAgent: Boolean(config.auth?.grantedPolicies?.['Helpdesk.Tickets.Work']),
    canManage: Boolean(config.auth?.grantedPolicies?.['Helpdesk.Categories.Manage']),
  })),
  categories: (signal) => request('GET', '/api/app/category', { params: { ActiveOnly: true }, signal }),
  createTicket: (body) => request('POST', '/api/app/ticket', { body }),
  ticket: (id, signal) => request('GET', `/api/app/ticket/${id}`, { signal }),
  myTickets: (params, signal) => request('GET', '/api/app/ticket/my-list', { params, signal }),
  queue: (params, signal) => request('GET', '/api/app/ticket/queue', { params, signal }),
  agents: (signal) => request('GET', '/api/app/ticket/assignable-agents', { signal }),
  comment: (id, body) => request('POST', `/api/app/ticket/${id}/comment`, { body }),
  suggestion: (params, signal) => request('GET', '/api/app/ticket-triage/suggestion', { params, signal }),
  summary: (params, signal) => request('GET', '/api/app/ticket-report/summary', { params, signal }),
  /** Runs one of the ticket actions (claim, resolve...). `value` is the text or id the action asks for. */
  act: (id, action, value) => {
    const { path, body, field } = ACTIONS[action];
    let payload;
    if (field === 'assignee') payload = { assigneeId: value };
    else if (field === 'priority') payload = { priority: Number(value) };
    else if (body) payload = { [body]: value };
    return request('POST', `/api/app/ticket/${id}/${path}`, { body: payload });
  },
};
