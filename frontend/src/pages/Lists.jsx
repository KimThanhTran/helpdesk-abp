import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, Pager, TicketTable } from '../components/bits';
import ExportButton from '../components/ExportButton';
import { Priority, Scope, Status } from '../constants';
import { useDebounced, useLoad } from '../hooks';
import { useT } from '../i18n';
import { useTicketChanges } from '../notifications';

const SIZE = 15;

function Filters({ filter, setFilter, status, setStatus, priority, setPriority }) {
  const { t } = useT();
  return (
    <div className="filters">
      <input type="search" placeholder={t('filter.search')} aria-label={t('filter.searchLabel')} value={filter} onChange={(e) => setFilter(e.target.value)} />
      <select aria-label={t('filter.status')} value={status} onChange={(e) => setStatus(e.target.value)}>
        <option value="">{t('filter.anyStatus')}</option>
        {Object.values(Status).map((value) => <option key={value} value={value}>{t(`status.${value}`)}</option>)}
      </select>
      {setPriority ? (
        <select aria-label={t('filter.priority')} value={priority} onChange={(e) => setPriority(e.target.value)}>
          <option value="">{t('filter.anyPriority')}</option>
          {Object.values(Priority).map((value) => <option key={value} value={value}>{t(`priority.${value}`)}</option>)}
        </select>
      ) : null}
    </div>
  );
}

/** One page of tickets that reloads itself when a ticket changes. */
function useTickets(fetcher, params) {
  const result = useLoad((signal) => fetcher({ ...params, maxResultCount: SIZE }, signal), [JSON.stringify(params)]);
  useTicketChanges(result.reload);
  return result;
}

export function MyTickets() {
  const { t } = useT();
  const [filter, setFilter] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(0);
  const text = useDebounced(filter);
  const { data, error, loading } = useTickets(api.myTickets, { filter: text, status, skipCount: page * SIZE });

  return (
    <>
      <div className="row">
        <h1>{t('lists.mine')}</h1>
        <Link to="/new" className="button primary">{t('lists.new')}</Link>
      </div>
      <Filters filter={filter} setFilter={(v) => { setFilter(v); setPage(0); }} status={status} setStatus={(v) => { setStatus(v); setPage(0); }} />
      <ErrorBox error={error} />
      {data ? <TicketTable tickets={data.items} /> : loading ? <p>{t('loading')}</p> : null}
      {data ? <Pager page={page} size={SIZE} total={data.totalCount} onPage={setPage} /> : null}
    </>
  );
}

const SCOPES = [Scope.Unassigned, Scope.Mine, Scope.All];

export function Queue() {
  const { t } = useT();
  const [scope, setScope] = useState(Scope.Unassigned);
  const [filter, setFilter] = useState('');
  const [status, setStatus] = useState('');
  const [priority, setPriority] = useState('');
  const [page, setPage] = useState(0);
  const text = useDebounced(filter);
  const { data, error, loading } = useTickets(api.queue, { scope, filter: text, status, priority, skipCount: page * SIZE });
  const reset = (set) => (value) => { set(value); setPage(0); };

  return (
    <>
      <div className="row">
        <h1>{t('queue.title')}</h1>
        <ExportButton fetchFile={() => api.exportTickets({ scope, filter: text, status, priority })} />
      </div>
      <div className="tabs" role="tablist">
        {SCOPES.map((value) => (
          <button key={value} type="button" role="tab" aria-selected={scope === value} onClick={() => reset(setScope)(value)}>{t(`scope.${value}`)}</button>
        ))}
      </div>
      <Filters
        filter={filter} setFilter={reset(setFilter)} status={status} setStatus={reset(setStatus)}
        priority={priority} setPriority={reset(setPriority)}
      />
      <ErrorBox error={error} />
      {data ? <TicketTable tickets={data.items} showRequester /> : loading ? <p>{t('loading')}</p> : null}
      {data ? <Pager page={page} size={SIZE} total={data.totalCount} onPage={setPage} /> : null}
    </>
  );
}
