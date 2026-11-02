import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { Action, Priority, Status, Visibility } from '../constants';
import * as auth from '../auth';
import Ticket from '../pages/Ticket';

vi.mock('../notifications', () => ({ useTicketChanges: () => {} }));

const person = (name) => ({ id: name, name });
const ticket = (actions, extra = {}) => ({
  id: 't1', code: 'HD-7', title: 'VPN is down', description: 'Cannot connect', status: Status.Open, priority: Priority.High,
  categoryName: 'IT support', requester: person('alice'), assignee: person('agent1'), creationTime: '2026-11-02T08:00:00Z',
  firstResponseDueAt: '2026-11-02T12:00:00Z', resolutionDueAt: '2026-11-04T12:00:00Z',
  actions, comments: [], events: [], ...extra,
});

function show(user, data, state) {
  vi.spyOn(auth, 'useAuth').mockReturnValue({ user });
  vi.spyOn(api, 'ticket').mockResolvedValue(data);
  vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
  vi.spyOn(api, 'suggestion').mockResolvedValue({ categories: [], similarTickets: [], suggestedReply: null });
  render(
    <MemoryRouter initialEntries={[{ pathname: '/tickets/t1', state }]}>
      <Routes><Route path="/tickets/:id" element={<Ticket />} /></Routes>
    </MemoryRouter>
  );
}

beforeEach(() => vi.restoreAllMocks());

describe('Ticket page', () => {
  it('shows exactly the buttons the server allows', async () => {
    show({ id: 'alice', isAgent: false }, ticket([Action.Comment, Action.Cancel]));
    expect(await screen.findByRole('button', { name: 'Cancel ticket…' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Take it' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Resolve/ })).not.toBeInTheDocument();
  });

  it('runs an action with no input straight away and reloads', async () => {
    show({ id: 'agent1', isAgent: true }, ticket([Action.Claim, Action.Comment]));
    const act = vi.spyOn(api, 'act').mockResolvedValue({});
    await userEvent.click(await screen.findByRole('button', { name: 'Take it' }));
    expect(act).toHaveBeenCalledWith('t1', Action.Claim, undefined);
    await waitFor(() => expect(api.ticket).toHaveBeenCalledTimes(2));
  });

  it('asks for the reason before resolving, and shows a server refusal', async () => {
    show({ id: 'agent1', isAgent: true }, ticket([Action.Resolve]));
    vi.spyOn(api, 'act').mockRejectedValue(Object.assign(new Error('The ticket changed. Reload it.'), { status: 409 }));
    await userEvent.click(await screen.findByRole('button', { name: 'Resolve…' }));
    expect(api.act).not.toHaveBeenCalled();
    await userEvent.type(screen.getByPlaceholderText('How was it resolved?'), 'Restarted the gateway');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(api.act).toHaveBeenCalledWith('t1', Action.Resolve, 'Restarted the gateway');
    expect(await screen.findByRole('alert')).toHaveTextContent('The ticket changed');
  });

  it('only offers internal notes to people allowed to write them', async () => {
    show({ id: 'alice', isAgent: false }, ticket([Action.Comment]));
    await screen.findByText('Add a comment');
    expect(screen.queryByText(/Internal note/)).not.toBeInTheDocument();
  });

  it('marks internal comments and says when the SLA is paused', async () => {
    show({ id: 'agent1', isAgent: true }, ticket([Action.Comment, Action.InternalNote], {
      status: Status.Waiting, waitingSince: '2026-11-02T09:00:00Z', waitingNote: 'Need the log file',
      comments: [{ id: 'c1', author: person('agent1'), body: 'Customer is on leave', visibility: Visibility.Internal, creationTime: '2026-11-02T09:00:00Z' }],
    }));
    expect(await screen.findByText('SLA paused')).toBeInTheDocument();
    expect(screen.getByText(/Need the log file/)).toBeInTheDocument();
    expect(screen.getAllByText('Internal').length).toBeGreaterThan(0);
  });

  it('fills the reply box from a suggestion, for agents', async () => {
    show({ id: 'agent1', isAgent: true }, ticket([Action.Comment]));
    api.suggestion.mockResolvedValue({
      categories: [], similarTickets: [{ id: 't0', code: 'HD-2', title: 'VPN drops', status: Status.Closed, similarity: 0.7 }],
      suggestedReply: { fromTicketCode: 'HD-2', text: 'Reinstall the client', similarity: 0.7 }, possibleDuplicate: false,
    });
    await userEvent.click(await screen.findByRole('button', { name: 'Use as a reply' }));
    expect(screen.getByLabelText('Add a comment')).toHaveValue('Reinstall the client');
  });

  it('shows the notice left by the new ticket form when an image did not upload', async () => {
    show({ id: 'alice', isAgent: false }, ticket([Action.Comment]), { notice: 'The ticket was sent, but one image could not be attached: bad.png.' });
    expect(await screen.findByRole('status')).toHaveTextContent('could not be attached: bad.png');
  });

  it('does not ask for suggestions when the person is a requester', async () => {
    show({ id: 'alice', isAgent: false }, ticket([Action.Comment]));
    await screen.findByText('Add a comment');
    expect(api.suggestion).not.toHaveBeenCalled();
  });
});

describe('Ticket page notice', () => {
  it('is shown once and then dropped from the history entry, so a reload does not repeat it', async () => {
    let seen;
    function Spy() {
      seen = useLocation();
      return null;
    }
    vi.spyOn(auth, 'useAuth').mockReturnValue({ user: { id: 'alice', isAgent: false } });
    vi.spyOn(api, 'ticket').mockResolvedValue(ticket([Action.Comment]));
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    render(
      <MemoryRouter initialEntries={[{ pathname: '/tickets/t1', state: { notice: 'One image failed.' } }]}>
        <Spy />
        <Routes><Route path="/tickets/:id" element={<Ticket />} /></Routes>
      </MemoryRouter>
    );

    expect(await screen.findByRole('status')).toHaveTextContent('One image failed.');
    await waitFor(() => expect(seen.state).toBeNull());
    expect(screen.getByRole('status')).toHaveTextContent('One image failed.'); // still on screen for this visit
  });
});
