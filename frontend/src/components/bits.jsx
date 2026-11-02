import { Link } from 'react-router-dom';
import { formatDate, sla } from '../format';
import { priorityLabel, statusLabel } from '../constants';

export const StatusBadge = ({ status }) => (
  <span className={`badge status-${statusLabel(status).toLowerCase()}`}>{statusLabel(status)}</span>
);

export const PriorityBadge = ({ priority }) => (
  <span className={`badge priority-${priorityLabel(priority).toLowerCase()}`}>{priorityLabel(priority)}</span>
);

export function SlaBadge({ ticket }) {
  const info = sla(ticket);
  return info ? <span className={`sla sla-${info.tone}`}>{info.text}</span> : null;
}

export const ErrorBox = ({ error }) =>
  error ? <p role="alert" className="error">{error.message ?? String(error)}</p> : null;

export function Pager({ page, size, total, onPage }) {
  const pages = Math.max(1, Math.ceil(total / size));
  if (pages === 1) return null;
  return (
    <nav className="pager" aria-label="Pages">
      <button type="button" disabled={page === 0} onClick={() => onPage(page - 1)}>Previous</button>
      <span>Page {page + 1} of {pages}</span>
      <button type="button" disabled={page + 1 >= pages} onClick={() => onPage(page + 1)}>Next</button>
    </nav>
  );
}

export function TicketTable({ tickets, showRequester }) {
  if (tickets.length === 0) return <p className="empty">No tickets here.</p>;
  return (
    <table className="tickets">
      <thead>
        <tr>
          <th>Ticket</th><th>Status</th><th>Priority</th><th>Category</th>
          {showRequester ? <th>Requester</th> : null}<th>Assignee</th><th>Created</th><th>Due</th>
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
