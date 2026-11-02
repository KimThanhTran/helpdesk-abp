import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api, setToken, setUnauthorizedHandler, signIn } from './api';

const AuthContext = createContext(null);
const STORAGE_KEY = 'helpdesk.token';

// sessionStorage keeps the token for this tab only. It is readable by any script on the page, so the page must stay
// free of third-party scripts; a production setup would keep the token in an HttpOnly cookie instead.
const stored = () => {
  try {
    return sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
};
const remember = (value) => {
  try {
    if (value) sessionStorage.setItem(STORAGE_KEY, value);
    else sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // storage unavailable: the session just lasts until the page is reloaded
  }
};

export function AuthProvider({ children }) {
  const [token, setTokenState] = useState(() => {
    const value = stored();
    setToken(value);
    return value;
  });
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(Boolean(token));

  const signOut = useCallback(() => {
    setToken(null);
    remember(null);
    setTokenState(null);
    setUser(null);
  }, []);

  useEffect(() => {
    setUnauthorizedHandler(signOut);
  }, [signOut]);

  useEffect(() => {
    if (!token) {
      setLoading(false);
      return undefined;
    }
    const controller = new AbortController();
    setLoading(true);
    api.me(controller.signal)
      .then((me) => (me.id ? setUser(me) : signOut()))
      .catch((error) => {
        if (error.name !== 'AbortError') signOut();
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [token, signOut]);

  const login = useCallback(async (username, password) => {
    const value = await signIn(username, password);
    setToken(value);
    remember(value);
    setTokenState(value);
  }, []);

  const value = useMemo(() => ({ token, user, loading, login, signOut }), [token, user, loading, login, signOut]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);
