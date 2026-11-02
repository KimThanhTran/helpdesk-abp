import { useEffect, useState } from 'react';
import { api } from '../api';
import { Action, Visibility } from '../constants';
import { formatBytes } from '../download';
import { useLoad, usePastedImages } from '../hooks';
import { UiError, useT } from '../i18n';
import { useTicketChanges } from '../notifications';
import { ErrorBox } from './bits';

// The server decides; these only save a round trip (and a 5 MB upload) when the answer is obvious.
export const ACCEPTED = ['image/png', 'image/jpeg', 'image/webp'];
export const MAX_BYTES = 5 * 1024 * 1024;
export const MAX_FILES = 5;

/** Why a file cannot be attached (a UiError the page words in the chosen language), or null when it looks fine. */
export function problemWith(file, count) {
  if (count >= MAX_FILES) return new UiError('attach.limit', { max: MAX_FILES });
  if (!ACCEPTED.includes(file.type)) return new UiError('attach.type');
  if (file.size > MAX_BYTES) return new UiError('attach.big', { size: formatBytes(file.size) });
  if (file.size === 0) return new UiError('attach.empty');
  return null;
}

/** "name: reason", worded in the language the person uses now. */
export function namedProblem(t, file, problem) {
  return new UiError('attach.named', { name: file.name, problem: t(problem.key, problem.params) });
}

/** An image fetched with the sign-in token (an <img src> cannot send one) and shown from memory. */
function Thumbnail({ attachment }) {
  const { t } = useT();
  const [url, setUrl] = useState(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    let objectUrl = null;
    api.attachmentImage(attachment.id, controller.signal)
      .then(({ blob }) => {
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch((error) => error.name !== 'AbortError' && setFailed(true));
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [attachment.id]);

  if (failed) return <span className="thumb missing">{t('img.cannotLoad')}</span>;
  if (!url) return <span className="thumb missing">…</span>;
  return (
    <a href={url} target="_blank" rel="noreferrer" className="thumb" title={t('img.open')}>
      <img src={url} alt={attachment.fileName} />
    </a>
  );
}

export default function Attachments({ ticket }) {
  const { t, formatDate } = useT();
  const { data, error, reload } = useLoad((signal) => api.attachments(ticket.id, signal), [ticket.id]);
  useTicketChanges((change) => change.ticketId === ticket.id && reload());
  const [problem, setProblem] = useState(null);
  const [internal, setInternal] = useState(false);
  const [busy, setBusy] = useState(false);

  const items = data?.items ?? [];
  const canPublic = ticket.actions.includes(Action.Comment);
  const canInternal = ticket.actions.includes(Action.InternalNote);
  const canUpload = canPublic || canInternal;

  /** Chosen with the file dialog or pasted: check each one, upload the good ones, say what was wrong with the rest. */
  const attach = async (files) => {
    setBusy(true);
    setProblem(null);
    let count = items.length;
    let uploaded = false;
    try {
      for (const file of files) {
        const refusal = problemWith(file, count);
        if (refusal) {
          setProblem(namedProblem(t, file, refusal));
          continue;
        }
        await api.uploadAttachment(ticket.id, file, internal || !canPublic);
        count += 1;
        uploaded = true;
      }
    } catch (failure) {
      setProblem(failure);
    } finally {
      setBusy(false);
      if (uploaded) reload();
    }
  };

  const choose = (event) => {
    const input = event.target;
    const files = [...input.files];
    input.value = ''; // the same file can be picked again later
    if (files.length > 0) attach(files);
  };

  usePastedImages((files) => !busy && attach(files), canUpload);

  if (items.length === 0 && !canUpload) return null;
  return (
    <section aria-label={t('img.title')}>
      <h2>{t('img.title')}</h2>
      <ErrorBox error={error} />
      {items.length === 0 ? <p className="empty">{t('img.none')}</p> : null}
      <ul className="gallery">
        {items.map((attachment) => (
          <li key={attachment.id}>
            <Thumbnail attachment={attachment} />
            <span className="caption">
              <strong className="name">{attachment.fileName}</strong>
              {attachment.visibility === Visibility.Internal ? <span className="badge">{t('img.internal')}</span> : null}
              <br />{formatBytes(attachment.size)} · {attachment.uploader.name} · {formatDate(attachment.creationTime)}
            </span>
          </li>
        ))}
      </ul>
      {canUpload ? (
        <div className="card upload">
          <label>{t('img.attach', { max: MAX_FILES })}
            <input type="file" multiple accept={ACCEPTED.join(',')} onChange={choose} disabled={busy || items.length >= MAX_FILES} />
          </label>
          {canInternal && canPublic ? (
            <label className="check"><input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)} /> {t('img.internalCheck')}</label>
          ) : null}
          {busy ? <p>{t('img.uploading')}</p> : null}
          <ErrorBox error={problem} />
        </div>
      ) : null}
    </section>
  );
}
