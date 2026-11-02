import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Action, Priority } from '../constants';
import { ApiError, api, setToken, setUnauthorizedHandler, signIn } from '../api';

const reply = (status, body) => new Response(body === undefined ? null : JSON.stringify(body), { status });
let fetchMock;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
  setToken(null);
});
afterEach(() => vi.unstubAllGlobals());

describe('request', () => {
  it('sends the bearer token and drops empty query values', async () => {
    setToken('abc');
    fetchMock.mockResolvedValue(reply(200, { items: [] }));
    await api.myTickets({ filter: '', status: 2, skipCount: 0 });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/app/ticket/my-list?status=2&skipCount=0');
    expect(init.headers.Authorization).toBe('Bearer abc');
  });

  it('shows ABP validation messages as one sentence', async () => {
    fetchMock.mockResolvedValue(reply(400, { error: { message: 'Validation failed', validationErrors: [{ message: 'Title is required.' }, { message: 'Too long.' }] } }));
    await expect(api.createTicket({})).rejects.toMatchObject({ status: 400, message: 'Title is required. Too long.' });
  });

  it('keeps the status of a business error', async () => {
    fetchMock.mockResolvedValue(reply(409, { error: { code: 'Helpdesk:TicketClosed', message: 'The ticket is closed.' } }));
    await expect(api.act('t1', Action.Claim)).rejects.toMatchObject({ status: 409, code: 'Helpdesk:TicketClosed' });
  });

  it('signs out on 401 only when a token was sent', async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    fetchMock.mockResolvedValue(reply(401, {}));
    await expect(api.ticket('t1')).rejects.toBeInstanceOf(ApiError);
    expect(handler).not.toHaveBeenCalled();

    setToken('abc');
    await expect(api.ticket('t1')).rejects.toMatchObject({ status: 401 });
    expect(handler).toHaveBeenCalledOnce();
  });

  it('explains a network failure', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(api.ticket('t1')).rejects.toMatchObject({ status: 0, message: expect.stringContaining('Cannot reach') });
  });

  it('lets an aborted request stay an abort', async () => {
    fetchMock.mockRejectedValue(Object.assign(new Error('aborted'), { name: 'AbortError' }));
    await expect(api.ticket('t1')).rejects.toMatchObject({ name: 'AbortError' });
  });
});

describe('act', () => {
  beforeEach(() => fetchMock.mockResolvedValue(reply(200, {})));
  const sent = () => {
    const [url, init] = fetchMock.mock.calls[0];
    return [url, init.body ? JSON.parse(init.body) : undefined];
  };

  it.each([
    [Action.Claim, undefined, '/api/app/ticket/t1/claim', undefined],
    [Action.Assign, 'u1', '/api/app/ticket/t1/assign', { assigneeId: 'u1' }],
    [Action.ChangePriority, String(Priority.Urgent), '/api/app/ticket/t1/change-priority', { priority: 4 }],
    [Action.Wait, 'need logs', '/api/app/ticket/t1/wait', { reason: 'need logs' }],
    [Action.Resolve, 'restarted', '/api/app/ticket/t1/resolve', { resolution: 'restarted' }],
    [Action.Reject, 'still broken', '/api/app/ticket/t1/reject', { reason: 'still broken' }],
    [Action.Confirm, undefined, '/api/app/ticket/t1/confirm', undefined],
  ])('action %i maps to the right route and body', async (action, value, url, body) => {
    await api.act('t1', action, value);
    expect(sent()).toEqual([url, body]);
  });
});

describe('signIn', () => {
  it('returns the access token', async () => {
    fetchMock.mockResolvedValue(reply(200, { access_token: 'tok' }));
    await expect(signIn('alice', 'pw')).resolves.toBe('tok');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/connect/token');
    expect(init.body.toString()).toContain('grant_type=password');
  });

  it('reports bad credentials', async () => {
    fetchMock.mockResolvedValue(reply(400, { error_description: 'Invalid username or password!' }));
    await expect(signIn('alice', 'no')).rejects.toMatchObject({ message: 'Invalid username or password!' });
  });
});

describe('attachments and files', () => {
  it('uploads as multipart without forcing a JSON content type, and marks internal ones', async () => {
    setToken('abc');
    fetchMock.mockResolvedValue(reply(200, { id: 'a1' }));
    const file = new File([new Uint8Array(4)], 'shot.png', { type: 'image/png' });

    await api.uploadAttachment('t1', file);
    await api.uploadAttachment('t1', file, true);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/app/ticket-attachment/upload/t1?visibility=1');
    expect(init.method).toBe('POST');
    expect(init.body).toBeInstanceOf(FormData);
    expect(init.body.get('file').name).toBe('shot.png');
    expect(init.headers['Content-Type']).toBeUndefined(); // the browser adds it, with the boundary
    expect(init.headers.Authorization).toBe('Bearer abc');
    expect(fetchMock.mock.calls[1][0]).toContain('visibility=2');
  });

  it('lists the attachments of one ticket', async () => {
    fetchMock.mockResolvedValue(reply(200, { items: [] }));
    await api.attachments('t1');
    expect(fetchMock.mock.calls[0][0]).toBe('/api/app/ticket-attachment?ticketId=t1');
  });

  it('returns a file with the name the server suggests', async () => {
    fetchMock.mockResolvedValue(new Response('xlsx-bytes', {
      status: 200,
      headers: { 'Content-Disposition': "attachment; filename=helpdesk-tickets-2026-11-02.xlsx; filename*=UTF-8''helpdesk-tickets-2026-11-02.xlsx" },
    }));

    const { blob, filename } = await api.exportTickets({ scope: 3, filter: '', status: '2' });

    expect(filename).toBe('helpdesk-tickets-2026-11-02.xlsx');
    expect(blob.size).toBe('xlsx-bytes'.length);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/app/ticket-export/tickets?scope=3&status=2');
  });

  it('falls back to a default name and decodes an encoded one', async () => {
    fetchMock.mockResolvedValueOnce(new Response('x', { status: 200 }));
    expect((await api.exportSummary()).filename).toBe('helpdesk-report.xlsx');

    fetchMock.mockResolvedValueOnce(new Response('x', { status: 200, headers: { 'Content-Disposition': "attachment; filename*=UTF-8''b%C3%A1o%20c%C3%A1o.xlsx" } }));
    expect((await api.exportSummary()).filename).toBe('báo cáo.xlsx');
  });

  it('reports a refused export in the server words', async () => {
    fetchMock.mockResolvedValue(reply(403, { error: { message: 'Your role does not allow this.' } }));
    await expect(api.exportTickets({})).rejects.toMatchObject({ status: 403, message: 'Your role does not allow this.' });
  });
});
