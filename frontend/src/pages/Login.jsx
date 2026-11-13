import { useState } from 'react';
import { useAuth } from '../auth';
import { ErrorBox } from '../components/bits';
import { LanguageSwitch, useT } from '../i18n';
import { ThemeSwitch } from '../theme';

// Demo accounts from the seed data. Hidden when the build sets VITE_SHOW_DEMO_USERS=false.
const DEMO = [['alice', 'requester'], ['bob', 'requester'], ['agent1', 'agent']];
const showDemo = import.meta.env.VITE_SHOW_DEMO_USERS !== 'false';

export default function Login() {
  const { t } = useT();
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
    } catch (failure) {
      setError(failure);
      setBusy(false);
    }
  };

  return (
    <main className="narrow">
      <div className="row">
        <h1>Helpdesk</h1>
        <span className="who"><LanguageSwitch /><ThemeSwitch /></span>
      </div>
      <form onSubmit={submit} className="card">
        <label>{t('login.user')}
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" required autoFocus />
        </label>
        <label>{t('login.password')}
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
        </label>
        <ErrorBox error={error} />
        <button type="submit" className="primary" disabled={busy}>{busy ? t('login.busy') : t('login.submit')}</button>
      </form>
      {showDemo ? (
        <p className="hint">
          {t('login.demo', { password: '1q2w3E*' })}{' '}
          {DEMO.map(([name, role]) => (
            <button key={name} type="button" className="link" onClick={() => { setUsername(name); setPassword('1q2w3E*'); }}>
              {name} ({t(`login.role.${role}`)})
            </button>
          ))}
        </p>
      ) : null}
    </main>
  );
}
