import { api } from '../api';
import { ErrorBox } from '../components/bits';
import ExportButton from '../components/ExportButton';
import { useLoad } from '../hooks';
import { useT } from '../i18n';
import { useTicketChanges } from '../notifications';

function Bars({ title, rows }) {
  const { t } = useT();
  const max = Math.max(1, ...rows.map((row) => row.count));
  return (
    <section className="card">
      <h2>{title}</h2>
      {rows.length === 0 ? <p className="empty">{t('reports.nothing')}</p> : null}
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
  const { t } = useT();
  const { data, error, reload } = useLoad((signal) => api.summary({}, signal), []);
  useTicketChanges(reload);
  if (error && !data) return <ErrorBox error={error} />;
  if (!data) return <p>{t('loading')}</p>;

  const hours = data.averageResolutionHours;
  return (
    <>
      <div className="row">
        <h1>{t('reports.title')}</h1>
        <ExportButton fetchFile={api.exportSummary} />
      </div>
      <div className="stats">
        <div className="card"><strong>{data.total}</strong><span>{t('reports.tickets')}</span></div>
        <div className={`card ${data.overdueNow ? 'bad' : ''}`}><strong>{data.overdueNow}</strong><span>{t('reports.overdue')}</span></div>
        <div className={`card ${data.waitingForFirstResponseOverdue ? 'bad' : ''}`}><strong>{data.waitingForFirstResponseOverdue}</strong><span>{t('reports.noAnswer')}</span></div>
        <div className="card"><strong>{hours == null ? '—' : t('reports.hours', { n: hours.toFixed(1) })}</strong><span>{t('reports.average')}</span></div>
      </div>
      <Bars title={t('reports.byStatus')} rows={data.byStatus.map((row) => ({ label: t(`status.${row.key}`), count: row.count }))} />
      <Bars title={t('reports.byPriority')} rows={data.byPriority.map((row) => ({ label: t(`priority.${row.key}`), count: row.count }))} />
      <Bars title={t('reports.byCategory')} rows={data.byCategory.map((row) => ({ label: row.key, count: row.count }))} />
    </>
  );
}
