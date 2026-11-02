import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth';
import { useNotifications } from '../notifications';

function Bell() {
  const { items, unread, connected, markRead, dismiss } = useNotifications();
  const [open, setOpen] = useState(false);

  const toggle = () => {
    if (!open) markRead();
    setOpen(!open);
  };

  return (
    <div className="bell">
      <button type="button" onClick={toggle} aria-expanded={open} aria-label={`Notifications, ${unread} unread`}>
        🔔{unread > 0 ? <span className="count">{unread}</span> : null}
      </button>
      <span className={`live ${connected ? 'on' : 'off'}`} title={connected ? 'Live updates on' : 'Live updates off'} />
      {open ? (
        <div className="menu" role="region" aria-label="Notifications">
          {items.length === 0 ? <p className="empty">Nothing new.</p> : null}
          {items.map((item) => (
            <div key={item.eventId} className="note">
              <Link to={`/tickets/${item.ticketId}`} onClick={() => setOpen(false)}>
                <strong>{item.code}</strong> {item.message}
              </Link>
              <button type="button" aria-label="Dismiss" onClick={() => dismiss(item.eventId)}>×</button>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

/** A short pop-up for the newest notification, so news is seen without opening the bell. */
function Toast() {
  const { items } = useNotifications();
  const latest = items[0];
  const [shown, setShown] = useState(null);

  useEffect(() => {
    if (!latest) return undefined;
    setShown(latest);
    const timer = setTimeout(() => setShown(null), 6000);
    return () => clearTimeout(timer);
  }, [latest?.eventId]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="toasts" role="status" aria-live="polite">
      {shown ? (
        <Link className="toast" to={`/tickets/${shown.ticketId}`} onClick={() => setShown(null)}>
          <strong>{shown.code}</strong> {shown.message}
        </Link>
      ) : null}
    </div>
  );
}

export default function Layout() {
  const { user, signOut } = useAuth();
  return (
    <>
      <header className="top">
        <Link to="/" className="brand">Helpdesk</Link>
        <nav>
          <NavLink to="/" end>My tickets</NavLink>
          <NavLink to="/new">New ticket</NavLink>
          {user.isAgent ? <NavLink to="/queue">Queue</NavLink> : null}
          {user.isAgent ? <NavLink to="/reports">Reports</NavLink> : null}
        </nav>
        <div className="who">
          <Bell />
          <span>{user.name}{user.isAgent ? ' (agent)' : ''}</span>
          <button type="button" onClick={signOut}>Sign out</button>
        </div>
      </header>
      <main><Outlet /></main>
      <Toast />
    </>
  );
}
