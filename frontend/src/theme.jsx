import { useEffect, useState } from 'react';
import { useT } from './i18n';

const STORAGE_KEY = 'helpdesk.theme';

/** The saved choice, else the system's, else light. */
export function detectTheme() {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'light' || saved === 'dark') return saved;
  } catch {
    // storage blocked: fall through to the system's setting
  }
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/** The stylesheet reads data-theme on <html>; setting it overrides the system's choice either way. */
export function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
}

export function ThemeSwitch() {
  const { t } = useT();
  const [theme, setTheme] = useState(detectTheme);
  const next = theme === 'dark' ? 'light' : 'dark';

  useEffect(() => applyTheme(theme), [theme]);

  const toggle = () => {
    setTheme(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // the choice then lasts until the page is reloaded
    }
  };

  const label = t(`theme.${next}`);
  return (
    <button type="button" className="icon" onClick={toggle} aria-label={label} title={label}>
      {theme === 'dark' ? '☀️' : '🌙'}
    </button>
  );
}
