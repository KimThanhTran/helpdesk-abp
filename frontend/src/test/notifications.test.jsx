import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../auth';
import { NotificationProvider, useNotifications, useTicketChanges } from '../notifications';
import * as api from '../api';

const ME = { id: 'me', name: 'alice', isAgent: false };

function fakeConnection() {
  const handlers = {};
  return {
    on: (name, handler) => { handlers[name] = handler; },
    onclose: vi.fn(),
    onreconnected: vi.fn(),
    start: vi.fn().mockResolvedValue(),
    stop: vi.fn(),
    emit: (payload) => act(() => handlers.ticketChanged(payload)),
  };
}

function Probe({ onChange }) {
  const { items, unread, connected } = useNotifications();
  useTicketChanges(onChange);
  return <p>{`${connected ? 'live' : 'offline'} unread=${unread} items=${items.map((i) => i.message).join('|')}`}</p>;
}

async function setup(onChange = vi.fn()) {
  sessionStorage.setItem('helpdesk.token', 'tok');
  vi.spyOn(api.api, 'me').mockResolvedValue(ME);
  const connection = fakeConnection();
  const view = render(
    <MemoryRouter>
      <AuthProvider>
        <NotificationProvider connect={() => connection}>
          <Probe onChange={onChange} />
        </NotificationProvider>
      </AuthProvider>
    </MemoryRouter>
  );
  await screen.findByText(/live/);
  return Object.assign(connection, { unmount: view.unmount });
}

const note = (overrides) => ({ eventId: 'e1', ticketId: 't1', code: 'HD-1', message: 'Resolved', actorId: 'other', ...overrides });

beforeEach(() => vi.restoreAllMocks());

describe('NotificationProvider', () => {
  it('connects once signed in and counts what arrives', async () => {
    const connection = await setup();
    await connection.emit(note());
    expect(screen.getByText('live unread=1 items=Resolved')).toBeInTheDocument();
  });

  it('ignores a repeated event id', async () => {
    const connection = await setup();
    await connection.emit(note());
    await connection.emit(note());
    expect(screen.getByText(/unread=1 /)).toBeInTheDocument();
  });

  it('does not notify you of your own action, but still lets pages reload', async () => {
    const onChange = vi.fn();
    const connection = await setup(onChange);
    await connection.emit(note({ actorId: 'me' }));
    expect(screen.getByText(/unread=0 /)).toBeInTheDocument();
    expect(onChange).toHaveBeenCalledOnce();
  });

  it('closes the connection when the page goes away', async () => {
    const connection = await setup();
    connection.unmount();
    expect(connection.stop).toHaveBeenCalledOnce();
  });
});
