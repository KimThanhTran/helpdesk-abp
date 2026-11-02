import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { ACCEPTED, MAX_FILES, namedProblem, problemWith } from '../components/Attachments';
import { ErrorBox } from '../components/bits';
import { formatBytes } from '../download';
import { useDebounced, useLoad, usePastedImages } from '../hooks';
import { errorText, useT } from '../i18n';

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
  const { t } = useT();
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

  /** Chosen with the file dialog or pasted from the clipboard. */
  const addFiles = (chosen) => {
    let next = files;
    let problem = null;
    for (const file of chosen) {
      const refusal = problemWith(file, next.length);
      if (refusal) problem = namedProblem(t, file, refusal);
      else next = [...next, file];
    }
    setFiles(next);
    setFileProblem(problem);
  };

  const choose = (event) => {
    const input = event.target;
    const chosen = [...input.files];
    input.value = ''; // the same file can be picked again after removing it
    addFiles(chosen);
  };

  usePastedImages(addFiles, !busy);

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
          failed.push(`${file.name} (${errorText(t, failure)})`);
        }
      }
      const notice = failed.length ? t('new.imagesFailed', { count: failed.length, list: failed.join('; ') }) : undefined;
      navigate(`/tickets/${ticket.id}`, { state: notice ? { notice } : undefined });
    } catch (failure) {
      setError(failure);
      setBusy(false);
    }
  };

  return (
    <main className="narrow">
      <h1>{t('new.title')}</h1>
      <form onSubmit={submit} className="card">
        <label>{t('new.subject')}
          <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} required />
        </label>
        <label>{t('new.what')}
          <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={6} required />
        </label>
        <label>{t('new.category')}
          <select value={categoryId || list[0]?.id || ''} onChange={(e) => setCategoryId(e.target.value)} required>
            {list.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
          </select>
        </label>
        <label>{t('new.images', { max: MAX_FILES })}
          <input type="file" multiple accept={ACCEPTED.join(',')} onChange={choose} disabled={busy || files.length >= MAX_FILES} />
        </label>
        {files.length > 0 ? (
          <ul className="chosen">
            {files.map((file, index) => (
              <li key={`${file.name}-${index}`}>
                <Preview file={file} />
                <span>{file.name} · {formatBytes(file.size)}</span>
                <button type="button" aria-label={t('new.remove', { name: file.name })} onClick={() => setFiles(files.filter((_, i) => i !== index))} disabled={busy}>×</button>
              </li>
            ))}
          </ul>
        ) : null}
        <ErrorBox error={fileProblem} />
        {suggested.length > 0 ? (
          <p className="hint">
            {t('new.looksLike')}{' '}
            {suggested.map((item) => (
              <button key={item.categoryId} type="button" className="link" onClick={() => setCategoryId(item.categoryId)}>
                {item.name} ({Math.round(item.confidence * 100)}%)
              </button>
            ))}
          </p>
        ) : null}
        <ErrorBox error={error ?? categories.error} />
        <button type="submit" className="primary" disabled={busy || list.length === 0}>{busy ? t('new.sending') : t('new.send')}</button>
      </form>
    </main>
  );
}
