import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { setLanguage as tellApi } from './api';
import { dictionaries } from './locales';
import { utc } from './format';

export const LANGUAGES = [['en', 'English'], ['vi', 'Tiếng Việt']];
const STORAGE_KEY = 'helpdesk.lang';
const LOCALE = { en: 'en-US', vi: 'vi-VN' };

/** The saved choice, else the browser's language (Vietnamese or English), else English. */
export function detectLanguage() {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved && dictionaries[saved]) return saved;
  } catch {
    // storage blocked: fall through to the browser's language
  }
  return (typeof navigator !== 'undefined' && navigator.language?.toLowerCase().startsWith('vi')) ? 'vi' : 'en';
}

/**
 * Looks a key up in a language, falling back to English and then to the key itself, and fills {placeholders}.
 * With a `count`, `key_one` / `key_other` are tried first, for the few texts that differ by number.
 */
export function translate(lang, key, params = {}) {
  const plural = params.count === undefined ? [] : [`${key}_${params.count === 1 ? 'one' : 'other'}`];
  const found = [lang, 'en']
    .flatMap((code) => [...plural, key].map((candidate) => dictionaries[code]?.[candidate]))
    .find((text) => text !== undefined);
  return (found ?? key).replace(/\{(\w+)\}/g, (whole, name) => (params[name] === undefined ? whole : String(params[name])));
}

const build = (lang, setLang) => ({
  lang,
  setLang,
  t: (key, params) => translate(lang, key, params),
  formatDate: (iso) => (iso ? utc(iso).toLocaleString(LOCALE[lang], { dateStyle: 'medium', timeStyle: 'short' }) : ''),
});

// Without a provider (a test, a stray component) everything is in English.
const Context = createContext(build('en', () => {}));

export function LanguageProvider({ children, initial }) {
  const [lang, setLangState] = useState(() => initial ?? detectLanguage());

  const setLang = useCallback((next) => {
    if (!dictionaries[next]) return;
    setLangState(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // the choice then lasts until the page is reloaded
    }
  }, []);

  useEffect(() => {
    tellApi(lang); // so the server answers its own messages in the same language
    document.documentElement.lang = lang;
  }, [lang]);

  const value = useMemo(() => build(lang, setLang), [lang, setLang]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export const useT = () => useContext(Context);

/** An error with a text of our own, shown in the language the person has chosen. */
export class UiError extends Error {
  constructor(key, params) {
    super(key);
    this.key = key;
    this.params = params;
  }
}

/** One sentence for any error: ours (by key), the client's own (by kind), or the server's message as it came. */
export function errorText(t, error) {
  if (error?.key) return t(error.key, error.params);
  if (error?.kind) return t(`error.${error.kind}`);
  if (typeof error === 'string') return error;
  return error?.message || t('error.failed');
}

export function LanguageSwitch() {
  const { lang, setLang, t } = useT();
  return (
    <select className="lang" aria-label={t('language.label')} value={lang} onChange={(event) => setLang(event.target.value)}>
      {LANGUAGES.map(([code, name]) => <option key={code} value={code}>{name}</option>)}
    </select>
  );
}
