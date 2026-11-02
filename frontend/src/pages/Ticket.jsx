import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import Attachments from '../components/Attachments';
import { ErrorBox, PriorityBadge, SlaBadge, StatusBadge } from '../components/bits';
import { ACTIONS, ACTION_ORDER, Action, Priority, Visibility } from '../constants';
import { useLoad } from '../hooks';
import { useT } from '../i18n';
import { useTicketChanges } from '../notifications';

/** Buttons for exactly the actions the server says are allowed; the ones that need input ask for it first. */
function ActionBar({ ticket, onDone }) {
  const { t } = useT();
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
    <section className="card actions" aria-label={t('ticket.actions')}>
      <div className="buttons">
        {allowed.map((action) => (
          <button key={action} type="button" className={ACTIONS[action].tone ?? ''} disabled={busy} onClick={() => choose(action)}>
            {t(`action.${action}`)}
          </button>
        ))}
      </div>
      {spec ? (
        <form onSubmit={(event) => { event.preventDefault(); run(pending, value); }}>
          {spec.field === 'assignee' ? (
            <select aria-label={t('ticket.assignee')} value={value} onChange={(e) => setValue(e.target.value)} required>
              <option value="">{t('ticket.chooseAgent')}</option>
              {(agents.data?.items ?? []).map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
            </select>
          ) : spec.field === 'priority' ? (
            <select aria-label={t('ticket.priority')} value={value} onChange={(e) => setValue(e.target.value)}>
              {Object.values(Priority).map((id) => <option key={id} value={id}>{t(`priority.${id}`)}</option>)}
            </select>
          ) : (
            <textarea aria-label={t(`prompt.${pending}`)} placeholder={t(`prompt.${pending}`)} value={value} onChange={(e) => setValue(e.target.value)} rows={3} required />
          )}
          <div className="buttons">
            <button type="submit" className="primary" disabled={busy}>{t('ticket.confirm')}</button>
            <button type="button" onClick={() => setPending(null)}>{t('ticket.nevermind')}</button>
          </div>
        </form>
      ) : null}
      <ErrorBox error={error} />
    </section>
  );
}

function CommentBox({ ticket, draft, setDraft, onDone }) {
  const { t } = useT();
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
      <label>{t('ticket.addComment')}
        <textarea value={draft} onChange={(e) => setDraft(e.target.value)} rows={3} required />
      </label>
      {canInternal && canPublic ? (
        <label className="check"><input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)} /> {t('ticket.internalNote')}</label>
      ) : null}
      <ErrorBox error={error} />
      <button type="submit" className="primary" disabled={busy}>{internal ? t('ticket.addNote') : t('ticket.send')}</button>
    </form>
  );
}

/** Agents only: what the history says about this ticket. The server sends nothing to requesters. */
function Hints({ ticket, onUseReply }) {
  const { t } = useT();
  const { data } = useLoad(
    (signal) => api.suggestion({ title: ticket.title, description: ticket.description, excludeTicketId: ticket.id }, signal).catch(() => null),
    [ticket.id]
  );
  if (!data || (!data.suggestedReply && data.similarTickets.length === 0)) return null;
  return (
    <aside className="card hints" aria-label={t('hints.title')}>
      <h2>{t('hints.title')}</h2>
      {data.possibleDuplicate ? <p className="warn">{t('hints.duplicate')}</p> : null}
      {data.similarTickets.length > 0 ? (
        <ul>
          {data.similarTickets.map((similar) => (
            <li key={similar.id}>
              <Link to={`/tickets/${similar.id}`}>{similar.code} {similar.title}</Link> — {t('hints.alike', { status: t(`status.${similar.status}`), percent: Math.round(similar.similarity * 100) })}
            </li>
          ))}
        </ul>
      ) : null}
      {data.suggestedReply ? (
        <>
          <p>{t('hints.solved', { code: data.suggestedReply.fromTicketCode })}</p>
          <blockquote>{data.suggestedReply.text}</blockquote>
          <button type="button" onClick={() => onUseReply(data.suggestedReply.text)}>{t('hints.useReply')}</button>
        </>
      ) : null}
    </aside>
  );
}

export default function Ticket() {
  const { t, formatDate } = useT();
  const { id } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  // set when the ticket was sent but an image did not upload; shown once, so a reload does not bring it back
  const [notice] = useState(location.state?.notice);
  useEffect(() => {
    if (location.state?.notice) navigate(location.pathname, { replace: true, state: null });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const { user } = useAuth();
  const [draft, setDraft] = useState('');
  const { data: ticket, error, reload } = useLoad((signal) => api.ticket(id, signal), [id]);
  useTicketChanges((change) => change.ticketId === id && reload());

  if (error && !ticket) return <ErrorBox error={error} />;
  if (!ticket) return <p>{t('loading')}</p>;

  return (
    <>
      <p><Link to={user.isAgent ? '/queue' : '/'}>{t('ticket.back')}</Link></p>
      <div className="row">
        <h1>{ticket.code} {ticket.title}</h1>
        <StatusBadge status={ticket.status} />
      </div>
      <p className="meta">
        <PriorityBadge priority={ticket.priority} /> {ticket.categoryName} · {t('ticket.openedBy', { name: ticket.requester.name, date: formatDate(ticket.creationTime) })} ·{' '}
        {t('ticket.assignedTo', { name: ticket.assignee?.name ?? t('ticket.nobody') })} · <SlaBadge ticket={ticket} />
      </p>
      {notice ? <p className="warn" role="status">{notice}</p> : null}
      {ticket.waitingNote ? <p className="warn">{t('ticket.waiting', { note: ticket.waitingNote })}</p> : null}
      <section className="card"><p className="text">{ticket.description}</p></section>
      {ticket.resolution ? <section className="card good"><strong>{t('ticket.resolution')}</strong><p className="text">{ticket.resolution}</p></section> : null}

      <ActionBar ticket={ticket} onDone={reload} />
      {user.isAgent ? <Hints ticket={ticket} onUseReply={setDraft} /> : null}

      <Attachments ticket={ticket} />

      <h2>{t('ticket.conversation')}</h2>
      {ticket.comments.length === 0 ? <p className="empty">{t('ticket.noComments')}</p> : null}
      {ticket.comments.map((comment) => (
        <article key={comment.id} className={`comment ${comment.visibility === Visibility.Internal ? 'internal' : ''}`}>
          <header>
            <strong>{comment.author.name}</strong> · {formatDate(comment.creationTime)}
            {comment.visibility === Visibility.Internal ? <span className="badge">{t('ticket.internal')}</span> : null}
          </header>
          <p className="text">{comment.body}</p>
        </article>
      ))}
      <CommentBox ticket={ticket} draft={draft} setDraft={setDraft} onDone={reload} />

      <h2>{t('ticket.history')}</h2>
      <ul className="history">
        {ticket.events.map((event) => (
          <li key={event.id}>
            {formatDate(event.creationTime)} — {t(`event.${event.kind}`) === `event.${event.kind}` ? t('event.other') : t(`event.${event.kind}`)}
            {event.toStatus ? ` → ${t(`status.${event.toStatus}`)}` : ''}
            {event.assignee ? ` → ${event.assignee.name}` : ''} {t('history.by')} {event.actor?.name ?? t('history.system')}
            {event.note ? ` (${event.note})` : ''}
          </li>
        ))}
      </ul>
    </>
  );
}
