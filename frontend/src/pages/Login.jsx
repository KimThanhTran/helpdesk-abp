import { useState } from 'react';
import { useAuth } from '../auth';
import { ErrorBox } from '../components/bits';

// Demo accounts from the seed data. Hidden when the build sets VITE_SHOW_DEMO_USERS=false.
const DEMO = [['alice', 'requester'], ['bob', 'requester'], ['agent1', 'agent']];
const showDemo = import.meta.env.VITE_SHOW_DEMO_USERS !== 'false';

export default function Login() {
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
      <h1>Helpdesk</h1>
      <form onSubmit={submit} className="card">
        <label>User name
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" required autoFocus />
        </label>
        <label>Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
        </label>
        <ErrorBox error={error} />
        <button type="submit" className="primary" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
      {showDemo ? (
        <p className="hint">
          Demo accounts (password <code>1q2w3E*</code>):{' '}
          {DEMO.map(([name, role]) => (
            <button key={name} type="button" className="link" onClick={() => { setUsername(name); setPassword('1q2w3E*'); }}>
              {name} ({role})
            </button>
          ))}
        </p>
      ) : null}
    </main>
  );
}
