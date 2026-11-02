import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox } from '../components/bits';
import { useDebounced, useLoad } from '../hooks';

const MIN_FOR_HINT = 8; // characters in the title before it is worth asking for a suggestion

export default function NewTicket() {
  const navigate = useNavigate();
  const categories = useLoad((signal) => api.categories(signal), []);
  const [categoryId, setCategoryId] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [suggested, setSuggested] = useState([]);

  const list = categories.data?.items ?? [];
  const typedTitle = useDebounced(title, 600);
  const typedDescription = useDebounced(description, 600);

  useEffect(() => {
    if (typedTitle.trim().length < MIN_FOR_HINT) {
      setSuggested([]);
      return undefined;
    }
    const controller = new AbortController();
    api.suggestion({ title: typedTitle, description: typedDescription }, controller.signal)
      .then((result) => setSuggested(result.categories ?? []))
      .catch(() => setSuggested([])); // a missing hint never blocks the form
    return () => controller.abort();
  }, [typedTitle, typedDescription]);

  const submit = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const ticket = await api.createTicket({ categoryId: categoryId || list[0]?.id, title: title.trim(), description: description.trim() });
      navigate(`/tickets/${ticket.id}`);
    } catch (failure) {
      setError(failure);
      setBusy(false);
    }
  };

  return (
    <main className="narrow">
      <h1>New ticket</h1>
      <form onSubmit={submit} className="card">
        <label>Title
          <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} required />
        </label>
        <label>What is going on?
          <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={6} required />
        </label>
        <label>Category
          <select value={categoryId || list[0]?.id || ''} onChange={(e) => setCategoryId(e.target.value)} required>
            {list.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
          </select>
        </label>
        {suggested.length > 0 ? (
          <p className="hint">
            Looks like:{' '}
            {suggested.map((item) => (
              <button key={item.categoryId} type="button" className="link" onClick={() => setCategoryId(item.categoryId)}>
                {item.name} ({Math.round(item.confidence * 100)}%)
              </button>
            ))}
          </p>
        ) : null}
        <ErrorBox error={error ?? categories.error} />
        <button type="submit" className="primary" disabled={busy || list.length === 0}>{busy ? 'Sending…' : 'Send'}</button>
      </form>
    </main>
  );
}
