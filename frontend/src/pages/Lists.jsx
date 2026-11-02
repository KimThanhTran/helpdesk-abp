import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, Pager, TicketTable } from '../components/bits';
import { Priority, Scope, Status } from '../constants';
import { useDebounced, useLoad } from '../hooks';
import { useTicketChanges } from '../notifications';

const SIZE = 15;

function Filters({ filter, setFilter, status, setStatus, priority, setPriority }) {
  return (
    <div className="filters">
      <input type="search" placeholder="Search code or title" aria-label="Search" value={filter} onChange={(e) => setFilter(e.target.value)} />
      <select aria-label="Status" value={status} onChange={(e) => setStatus(e.target.value)}>
        <option value="">Any status</option>
        {Object.entries(Status).map(([name, value]) => <option key={value} value={value}>{name}</option>)}
      </select>
      {setPriority ? (
        <select aria-label="Priority" value={priority} onChange={(e) => setPriority(e.target.value)}>
          <option value="">Any priority</option>
          {Object.entries(Priority).map(([name, value]) => <option key={value} value={value}>{name}</option>)}
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
  const [filter, setFilter] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(0);
  const text = useDebounced(filter);
  const { data, error, loading } = useTickets(api.myTickets, { filter: text, status, skipCount: page * SIZE });

  return (
    <>
      <div className="row">
        <h1>My tickets</h1>
        <Link to="/new" className="button primary">New ticket</Link>
      </div>
      <Filters filter={filter} setFilter={(v) => { setFilter(v); setPage(0); }} status={status} setStatus={(v) => { setStatus(v); setPage(0); }} />
      <ErrorBox error={error} />
      {data ? <TicketTable tickets={data.items} /> : loading ? <p>Loading…</p> : null}
      {data ? <Pager page={page} size={SIZE} total={data.totalCount} onPage={setPage} /> : null}
    </>
  );
}

const SCOPES = [['Unassigned', Scope.Unassigned], ['Mine', Scope.Mine], ['All', Scope.All]];

export function Queue() {
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
      <h1>Queue</h1>
      <div className="tabs" role="tablist">
        {SCOPES.map(([name, value]) => (
          <button key={value} type="button" role="tab" aria-selected={scope === value} onClick={() => reset(setScope)(value)}>{name}</button>
        ))}
      </div>
      <Filters
        filter={filter} setFilter={reset(setFilter)} status={status} setStatus={reset(setStatus)}
        priority={priority} setPriority={reset(setPriority)}
      />
      <ErrorBox error={error} />
      {data ? <TicketTable tickets={data.items} showRequester /> : loading ? <p>Loading…</p> : null}
      {data ? <Pager page={page} size={SIZE} total={data.totalCount} onPage={setPage} /> : null}
    </>
  );
}
