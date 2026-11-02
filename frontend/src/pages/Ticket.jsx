import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { ErrorBox, PriorityBadge, SlaBadge, StatusBadge } from '../components/bits';
import { ACTIONS, ACTION_ORDER, Action, EventKind, Priority, Visibility, statusLabel } from '../constants';
import { formatDate } from '../format';
import { useLoad } from '../hooks';
import { useTicketChanges } from '../notifications';

/** Buttons for exactly the actions the server says are allowed; the ones that need input ask for it first. */
function ActionBar({ ticket, onDone }) {
  const [pending, setPending] = useState(null);
  const [value, setValue] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const agents = useLoad((signal) => (pending === Action.Assign ? api.agents(signal) : Promise.resolve(null)), [pending]);

  const allowed = ACTION_ORDER.filter((action) => ticket.actions.includes(action));
  if (allowed.length === 0) return null;

  const run = async (action, input) => {
    setBusy(true);
    setError(null);
    try {
      await api.act(ticket.id, action, input);
      setPending(null);
      setValue('');
      onDone();
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  };

  const choose = (action) => {
    setError(null);
    setValue(action === Action.ChangePriority ? String(Priority.Normal) : '');
    if (ACTIONS[action].field) setPending(action);
    else run(action);
  };

  const spec = pending ? ACTIONS[pending] : null;
  return (
    <section className="card actions" aria-label="Actions">
      <div className="buttons">
        {allowed.map((action) => (
          <button key={action} type="button" className={ACTIONS[action].tone ?? ''} disabled={busy} onClick={() => choose(action)}>
            {ACTIONS[action].label}
          </button>
        ))}
      </div>
      {spec ? (
        <form onSubmit={(event) => { event.preventDefault(); run(pending, value); }}>
          {spec.field === 'assignee' ? (
            <select aria-label="Assignee" value={value} onChange={(e) => setValue(e.target.value)} required>
              <option value="">Choose an agent…</option>
              {(agents.data?.items ?? []).map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
            </select>
          ) : spec.field === 'priority' ? (
            <select aria-label="Priority" value={value} onChange={(e) => setValue(e.target.value)}>
              {Object.entries(Priority).map(([name, id]) => <option key={id} value={id}>{name}</option>)}
            </select>
          ) : (
            <textarea aria-label={spec.prompt} placeholder={spec.prompt} value={value} onChange={(e) => setValue(e.target.value)} rows={3} required />
          )}
          <div className="buttons">
            <button type="submit" className="primary" disabled={busy}>Confirm</button>
            <button type="button" onClick={() => setPending(null)}>Never mind</button>
          </div>
        </form>
      ) : null}
      <ErrorBox error={error} />
    </section>
  );
}

function CommentBox({ ticket, draft, setDraft, onDone }) {
  const [internal, setInternal] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  if (!ticket.actions.includes(Action.Comment) && !ticket.actions.includes(Action.InternalNote)) return null;
  const canInternal = ticket.actions.includes(Action.InternalNote);
  const canPublic = ticket.actions.includes(Action.Comment);

  const send = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.comment(ticket.id, { body: draft.trim(), visibility: internal || !canPublic ? Visibility.Internal : Visibility.Public });
      setDraft('');
      onDone();
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={send} className="card">
      <label>Add a comment
        <textarea value={draft} onChange={(e) => setDraft(e.target.value)} rows={3} required />
      </label>
      {canInternal && canPublic ? (
        <label className="check"><input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)} /> Internal note (the requester will not see it)</label>
      ) : null}
      <ErrorBox error={error} />
      <button type="submit" className="primary" disabled={busy}>{internal ? 'Add note' : 'Send'}</button>
    </form>
  );
}

/** Agents only: what the history says about this ticket. The server sends nothing to requesters. */
function Hints({ ticket, onUseReply }) {
  const { data } = useLoad(
    (signal) => api.suggestion({ title: ticket.title, description: ticket.description, excludeTicketId: ticket.id }, signal).catch(() => null),
    [ticket.id]
  );
  if (!data || (!data.suggestedReply && data.similarTickets.length === 0)) return null;
  return (
    <aside className="card hints" aria-label="Suggestions">
      <h2>Suggestions</h2>
      {data.possibleDuplicate ? <p className="warn">This looks like a ticket that is still open.</p> : null}
      {data.similarTickets.length > 0 ? (
        <ul>
          {data.similarTickets.map((similar) => (
            <li key={similar.id}>
              <Link to={`/tickets/${similar.id}`}>{similar.code} {similar.title}</Link> — {statusLabel(similar.status)}, {Math.round(similar.similarity * 100)}% alike
            </li>
          ))}
        </ul>
      ) : null}
      {data.suggestedReply ? (
        <>
          <p>How {data.suggestedReply.fromTicketCode} was solved:</p>
          <blockquote>{data.suggestedReply.text}</blockquote>
          <button type="button" onClick={() => onUseReply(data.suggestedReply.text)}>Use as a reply</button>
        </>
      ) : null}
    </aside>
  );
}

export default function Ticket() {
  const { id } = useParams();
  const { user } = useAuth();
  const [draft, setDraft] = useState('');
  const { data: ticket, error, reload } = useLoad((signal) => api.ticket(id, signal), [id]);
  useTicketChanges((change) => change.ticketId === id && reload());

  if (error && !ticket) return <ErrorBox error={error} />;
  if (!ticket) return <p>Loading…</p>;

  return (
    <>
      <p><Link to={user.isAgent ? '/queue' : '/'}>← Back</Link></p>
      <div className="row">
        <h1>{ticket.code} {ticket.title}</h1>
        <StatusBadge status={ticket.status} />
      </div>
      <p className="meta">
        <PriorityBadge priority={ticket.priority} /> {ticket.categoryName} · opened by {ticket.requester.name} {formatDate(ticket.creationTime)} ·
        assigned to {ticket.assignee?.name ?? 'nobody yet'} · <SlaBadge ticket={ticket} />
      </p>
      {ticket.waitingNote ? <p className="warn">Waiting for the requester: {ticket.waitingNote}</p> : null}
      <section className="card"><p className="text">{ticket.description}</p></section>
      {ticket.resolution ? <section className="card good"><strong>Resolution</strong><p className="text">{ticket.resolution}</p></section> : null}

      <ActionBar ticket={ticket} onDone={reload} />
      {user.isAgent ? <Hints ticket={ticket} onUseReply={setDraft} /> : null}

      <h2>Conversation</h2>
      {ticket.comments.length === 0 ? <p className="empty">No comments yet.</p> : null}
      {ticket.comments.map((comment) => (
        <article key={comment.id} className={`comment ${comment.visibility === Visibility.Internal ? 'internal' : ''}`}>
          <header>
            <strong>{comment.author.name}</strong> · {formatDate(comment.creationTime)}
            {comment.visibility === Visibility.Internal ? <span className="badge">Internal</span> : null}
          </header>
          <p className="text">{comment.body}</p>
        </article>
      ))}
      <CommentBox ticket={ticket} draft={draft} setDraft={setDraft} onDone={reload} />

      <h2>History</h2>
      <ul className="history">
        {ticket.events.map((event) => (
          <li key={event.id}>
            {formatDate(event.creationTime)} — {EventKind[event.kind] ?? 'Changed'}
            {event.toStatus ? ` → ${statusLabel(event.toStatus)}` : ''}
            {event.assignee ? ` → ${event.assignee.name}` : ''} by {event.actor?.name ?? 'the system'}
            {event.note ? ` (${event.note})` : ''}
          </li>
        ))}
      </ul>
    </>
  );
}
