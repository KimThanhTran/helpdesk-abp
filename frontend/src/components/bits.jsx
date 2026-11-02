import { Link } from 'react-router-dom';
import { priorityLabel, statusLabel } from '../constants';
import { sla } from '../format';
import { errorText, useT } from '../i18n';

export function StatusBadge({ status }) {
  const { t } = useT();
  return <span className={`badge status-${statusLabel(status).toLowerCase()}`}>{t(`status.${status}`)}</span>;
}

export function PriorityBadge({ priority }) {
  const { t } = useT();
  return <span className={`badge priority-${priorityLabel(priority).toLowerCase()}`}>{t(`priority.${priority}`)}</span>;
}

export function SlaBadge({ ticket }) {
  const { t } = useT();
  const info = sla(ticket);
  if (!info) return null;
  const text = info.key ? t(info.key) : t(`sla.${info.rel.late ? 'late' : 'in'}.${info.rel.unit}`, { n: info.rel.n });
  return <span className={`sla sla-${info.tone}`}>{text}</span>;
}

export function ErrorBox({ error }) {
  const { t } = useT();
  return error ? <p role="alert" className="error">{errorText(t, error)}</p> : null;
}

export function Pager({ page, size, total, onPage }) {
  const { t } = useT();
  const pages = Math.max(1, Math.ceil(total / size));
  if (pages === 1) return null;
  return (
    <nav className="pager" aria-label={t('pager.label')}>
      <button type="button" disabled={page === 0} onClick={() => onPage(page - 1)}>{t('pager.prev')}</button>
      <span>{t('pager.page', { page: page + 1, pages })}</span>
      <button type="button" disabled={page + 1 >= pages} onClick={() => onPage(page + 1)}>{t('pager.next')}</button>
    </nav>
  );
}

export function TicketTable({ tickets, showRequester }) {
  const { t, formatDate } = useT();
  if (tickets.length === 0) return <p className="empty">{t('table.empty')}</p>;
  return (
    <table className="tickets">
      <thead>
        <tr>
          <th>{t('col.ticket')}</th><th>{t('col.status')}</th><th>{t('col.priority')}</th><th>{t('col.category')}</th>
          {showRequester ? <th>{t('col.requester')}</th> : null}<th>{t('col.assignee')}</th><th>{t('col.created')}</th><th>{t('col.due')}</th>
        </tr>
      </thead>
      <tbody>
        {tickets.map((ticket) => (
          <tr key={ticket.id}>
            <td>
              <Link to={`/tickets/${ticket.id}`}><strong>{ticket.code}</strong> {ticket.title}</Link>
            </td>
            <td><StatusBadge status={ticket.status} /></td>
            <td><PriorityBadge priority={ticket.priority} /></td>
            <td>{ticket.categoryName}</td>
            {showRequester ? <td>{ticket.requester?.name}</td> : null}
            <td>{ticket.assignee?.name ?? '—'}</td>
            <td>{formatDate(ticket.creationTime)}</td>
            <td><SlaBadge ticket={ticket} /></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
