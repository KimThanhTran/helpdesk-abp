import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { API_URL } from './api';
import { useAuth } from './auth';

const NotificationContext = createContext(null);
const KEEP = 20;

/**
 * Keeps one live connection to the server while someone is signed in. A change to a ticket they follow arrives
 * as a notification; pages can also ask to be told (subscribe) so they can reload themselves.
 * Pass `connect` in tests to avoid a real network.
 */
export function NotificationProvider({ children, connect = defaultConnect }) {
  const { token, user } = useAuth();
  const [items, setItems] = useState([]);
  const [unread, setUnread] = useState(0);
  const [connected, setConnected] = useState(false);
  const listeners = useRef(new Set());
  const seen = useRef(new Set());

  useEffect(() => {
    if (!token || !user) return undefined;

    const connection = connect(token);
    // the handler returns nothing: SignalR treats a returned value as an answer the server never asked for
    connection.on('ticketChanged', (notification) => {
      if (seen.current.has(notification.eventId)) return;
      seen.current.add(notification.eventId);

      listeners.current.forEach((listener) => listener(notification));
      if (notification.actorId === user.id) return; // your own action is not news to you

      setItems((current) => [{ ...notification, read: false }, ...current].slice(0, KEEP));
      setUnread((count) => count + 1);
    });
    connection.onclose(() => setConnected(false));
    connection.onreconnected(() => setConnected(true));

    let cancelled = false;
    connection.start().then(() => !cancelled && setConnected(true)).catch(() => setConnected(false));

    return () => {
      cancelled = true;
      setConnected(false);
      connection.stop();
    };
  }, [token, user, connect]);

  const subscribe = useCallback((listener) => {
    listeners.current.add(listener);
    return () => listeners.current.delete(listener);
  }, []);

  const markRead = useCallback(() => {
    setUnread(0);
    setItems((current) => current.map((item) => ({ ...item, read: true })));
  }, []);

  const dismiss = useCallback((eventId) => setItems((current) => current.filter((item) => item.eventId !== eventId)), []);

  const value = useMemo(
    () => ({ items, unread, connected, subscribe, markRead, dismiss }),
    [items, unread, connected, subscribe, markRead, dismiss]
  );
  return <NotificationContext.Provider value={value}>{children}</NotificationContext.Provider>;
}

function defaultConnect(token) {
  return new HubConnectionBuilder()
    .withUrl(`${API_URL}/signalr-hubs/tickets`, { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

export const useNotifications = () => useContext(NotificationContext);

/** Calls `callback` whenever a ticket changes (for this page to reload itself). */
export function useTicketChanges(callback) {
  const { subscribe } = useNotifications();
  const latest = useRef(callback);
  latest.current = callback;
  useEffect(() => subscribe((notification) => latest.current(notification)), [subscribe]);
}
