import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { Priority, Status } from '../constants';
import { useAuth } from '../auth';
import { LanguageSwitch, useT } from '../i18n';
import { ThemeSwitch } from '../theme';
import { useNotifications } from '../notifications';

/**
 * The text of a notification, worded here so it follows the language chosen now. The server sends what changed
 * (kind, status, priority); its own English sentence is only the fallback for a kind this page does not know.
 */
export function notificationText(t, item) {
  const known = ['Created', 'Assigned', 'PriorityChanged', 'Reopened', 'AutoClosed', 'StatusChanged'].includes(item.kind);
  if (!known) return item.message;
  return t(`notify.${item.kind}`, {
    code: item.code,
    status: Status[item.status] ? t(`status.${Status[item.status]}`) : item.status,
    priority: Priority[item.priority] ? t(`priority.${Priority[item.priority]}`) : item.priority,
  });
}

function Bell() {
  const { t } = useT();
  const { items, unread, connected, markRead, dismiss } = useNotifications();
  const [open, setOpen] = useState(false);

  const toggle = () => {
    if (!open) markRead();
    setOpen(!open);
  };

  return (
    <div className="bell">
      <button type="button" onClick={toggle} aria-expanded={open} aria-label={t('bell.label', { count: unread })}>
        🔔{unread > 0 ? <span className="count">{unread}</span> : null}
      </button>
      <span className={`live ${connected ? 'on' : 'off'}`} title={connected ? t('live.on') : t('live.off')} />
      {open ? (
        <div className="menu" role="region" aria-label={t('bell.label', { count: unread })}>
          {items.length === 0 ? <p className="empty">{t('bell.none')}</p> : null}
          {items.map((item) => (
            <div key={item.eventId} className="note">
              <Link to={`/tickets/${item.ticketId}`} onClick={() => setOpen(false)}>
                {notificationText(t, item)}
              </Link>
              <button type="button" aria-label={t('bell.dismiss')} onClick={() => dismiss(item.eventId)}>×</button>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

/** A short pop-up for the newest notification, so news is seen without opening the bell. */
function Toast() {
  const { t } = useT();
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
          {notificationText(t, shown)}
        </Link>
      ) : null}
    </div>
  );
}

export default function Layout() {
  const { t } = useT();
  const { user, signOut } = useAuth();
  return (
    <>
      <header className="top">
        <Link to="/" className="brand">Helpdesk</Link>
        <nav>
          <NavLink to="/" end>{t('nav.myTickets')}</NavLink>
          <NavLink to="/new">{t('nav.newTicket')}</NavLink>
          {user.isAgent ? <NavLink to="/queue">{t('nav.queue')}</NavLink> : null}
          {user.isAgent ? <NavLink to="/reports">{t('nav.reports')}</NavLink> : null}
        </nav>
        <div className="who">
          <LanguageSwitch />
          <ThemeSwitch />
          <Bell />
          <span>{user.isAgent ? t('user.agent', { name: user.name }) : user.name}</span>
          <button type="button" onClick={signOut}>{t('nav.signOut')}</button>
        </div>
      </header>
      <main><Outlet /></main>
      <Toast />
    </>
  );
}
