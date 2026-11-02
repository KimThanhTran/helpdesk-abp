import { api } from '../api';
import { ErrorBox } from '../components/bits';
import { priorityLabel, statusLabel } from '../constants';
import { useLoad } from '../hooks';
import { useTicketChanges } from '../notifications';

function Bars({ title, rows }) {
  const max = Math.max(1, ...rows.map((row) => row.count));
  return (
    <section className="card">
      <h2>{title}</h2>
      {rows.length === 0 ? <p className="empty">Nothing yet.</p> : null}
      {rows.map((row) => (
        <div key={row.label} className="bar">
          <span>{row.label}</span>
          <meter min="0" max={max} value={row.count} aria-label={row.label} />
          <strong>{row.count}</strong>
        </div>
      ))}
    </section>
  );
}

export default function Reports() {
  const { data, error, reload } = useLoad((signal) => api.summary({}, signal), []);
  useTicketChanges(reload);
  if (error && !data) return <ErrorBox error={error} />;
  if (!data) return <p>Loading…</p>;

  const hours = data.averageResolutionHours;
  return (
    <>
      <h1>Last 30 days</h1>
      <div className="stats">
        <div className="card"><strong>{data.total}</strong><span>tickets</span></div>
        <div className={`card ${data.overdueNow ? 'bad' : ''}`}><strong>{data.overdueNow}</strong><span>overdue now</span></div>
        <div className={`card ${data.waitingForFirstResponseOverdue ? 'bad' : ''}`}><strong>{data.waitingForFirstResponseOverdue}</strong><span>no answer, past target</span></div>
        <div className="card"><strong>{hours == null ? '—' : `${hours.toFixed(1)}h`}</strong><span>average to resolve</span></div>
      </div>
      <Bars title="By status" rows={data.byStatus.map((row) => ({ label: statusLabel(row.key), count: row.count }))} />
      <Bars title="By priority" rows={data.byPriority.map((row) => ({ label: priorityLabel(row.key), count: row.count }))} />
      <Bars title="By category" rows={data.byCategory.map((row) => ({ label: row.key, count: row.count }))} />
    </>
  );
}
