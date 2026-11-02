import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { ACCEPTED, MAX_FILES, problemWith } from '../components/Attachments';
import { ErrorBox } from '../components/bits';
import { formatBytes } from '../download';
import { useDebounced, useLoad } from '../hooks';

const MIN_FOR_HINT = 8; // characters in the title before it is worth asking for a suggestion

/** A small preview of a chosen image, from memory: nothing is uploaded until the ticket is sent. */
function Preview({ file }) {
  const [url, setUrl] = useState(null);
  useEffect(() => {
    const made = URL.createObjectURL(file);
    setUrl(made);
    return () => URL.revokeObjectURL(made);
  }, [file]);
  return url ? <img src={url} alt="" /> : <span className="thumb-small" />;
}

export default function NewTicket() {
  const navigate = useNavigate();
  const categories = useLoad((signal) => api.categories(signal), []);
  const [categoryId, setCategoryId] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [suggested, setSuggested] = useState([]);
  const [files, setFiles] = useState([]);
  const [fileProblem, setFileProblem] = useState(null);

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

  const choose = (event) => {
    const input = event.target;
    const chosen = [...input.files];
    input.value = ''; // the same file can be picked again after removing it
    let next = files;
    let problem = null;
    for (const file of chosen) {
      const refusal = problemWith(file, next.length);
      if (refusal) problem = `${file.name}: ${refusal}`;
      else next = [...next, file];
    }
    setFiles(next);
    setFileProblem(problem ? new Error(problem) : null);
  };

  const submit = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const ticket = await api.createTicket({ categoryId: categoryId || list[0]?.id, title: title.trim(), description: description.trim() });
      // the ticket exists now; an image that fails is reported on its page instead of losing the ticket
      const failed = [];
      for (const file of files) {
        try {
          await api.uploadAttachment(ticket.id, file);
        } catch (failure) {
          failed.push(`${file.name} (${failure.message})`);
        }
      }
      const notice = failed.length ? `The ticket was sent, but ${failed.length === 1 ? 'one image' : `${failed.length} images`} could not be attached: ${failed.join('; ')}. You can add it again below.` : undefined;
      navigate(`/tickets/${ticket.id}`, { state: notice ? { notice } : undefined });
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
        <label>Images (optional: PNG, JPEG or WebP, up to 5 MB each, {MAX_FILES} per ticket)
          <input type="file" multiple accept={ACCEPTED.join(',')} onChange={choose} disabled={busy || files.length >= MAX_FILES} />
        </label>
        {files.length > 0 ? (
          <ul className="chosen">
            {files.map((file, index) => (
              <li key={`${file.name}-${index}`}>
                <Preview file={file} />
                <span>{file.name} · {formatBytes(file.size)}</span>
                <button type="button" aria-label={`Remove ${file.name}`} onClick={() => setFiles(files.filter((_, i) => i !== index))} disabled={busy}>×</button>
              </li>
            ))}
          </ul>
        ) : null}
        <ErrorBox error={fileProblem} />
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
