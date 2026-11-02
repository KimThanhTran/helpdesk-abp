import { useEffect, useState } from 'react';
import { api } from '../api';
import { Action, Visibility } from '../constants';
import { formatBytes } from '../download';
import { formatDate } from '../format';
import { useLoad } from '../hooks';
import { useTicketChanges } from '../notifications';
import { ErrorBox } from './bits';

// The server decides; these only save a round trip (and a 5 MB upload) when the answer is obvious.
export const ACCEPTED = ['image/png', 'image/jpeg', 'image/webp'];
export const MAX_BYTES = 5 * 1024 * 1024;
export const MAX_FILES = 5;

/** Why a file cannot be attached, or null when it looks fine. */
export function problemWith(file, count) {
  if (count >= MAX_FILES) return `A ticket can hold at most ${MAX_FILES} images.`;
  if (!ACCEPTED.includes(file.type)) return 'Only PNG, JPEG and WebP images can be attached.';
  if (file.size > MAX_BYTES) return `The image is too large (${formatBytes(file.size)}). The limit is 5 MB.`;
  if (file.size === 0) return 'The file is empty.';
  return null;
}

/** An image fetched with the sign-in token (an <img src> cannot send one) and shown from memory. */
function Thumbnail({ attachment }) {
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

  if (failed) return <span className="thumb missing">Cannot load</span>;
  if (!url) return <span className="thumb missing">…</span>;
  return (
    <a href={url} target="_blank" rel="noreferrer" className="thumb" title="Open full size">
      <img src={url} alt={attachment.fileName} />
    </a>
  );
}

export default function Attachments({ ticket }) {
  const { data, error, reload } = useLoad((signal) => api.attachments(ticket.id, signal), [ticket.id]);
  useTicketChanges((change) => change.ticketId === ticket.id && reload());
  const [problem, setProblem] = useState(null);
  const [internal, setInternal] = useState(false);
  const [busy, setBusy] = useState(false);

  const items = data?.items ?? [];
  const canPublic = ticket.actions.includes(Action.Comment);
  const canInternal = ticket.actions.includes(Action.InternalNote);

  const choose = async (event) => {
    const input = event.target;
    const file = input.files?.[0];
    input.value = ''; // the same file can be picked again later
    if (!file) return;

    const refusal = problemWith(file, items.length);
    if (refusal) {
      setProblem(new Error(refusal));
      return;
    }
    setBusy(true);
    setProblem(null);
    try {
      await api.uploadAttachment(ticket.id, file, internal || !canPublic);
      reload();
    } catch (failure) {
      setProblem(failure);
    } finally {
      setBusy(false);
    }
  };

  if (items.length === 0 && !canPublic && !canInternal) return null;
  return (
    <section aria-label="Images">
      <h2>Images</h2>
      <ErrorBox error={error} />
      {items.length === 0 ? <p className="empty">No images yet.</p> : null}
      <ul className="gallery">
        {items.map((attachment) => (
          <li key={attachment.id}>
            <Thumbnail attachment={attachment} />
            <span className="caption">
              <strong className="name">{attachment.fileName}</strong>
              {attachment.visibility === Visibility.Internal ? <span className="badge">Internal</span> : null}
              <br />{formatBytes(attachment.size)} · {attachment.uploader.name} · {formatDate(attachment.creationTime)}
            </span>
          </li>
        ))}
      </ul>
      {canPublic || canInternal ? (
        <div className="card upload">
          <label>Attach an image (PNG, JPEG or WebP, up to 5 MB, {MAX_FILES} per ticket)
            <input type="file" accept={ACCEPTED.join(',')} onChange={choose} disabled={busy || items.length >= MAX_FILES} />
          </label>
          {canInternal && canPublic ? (
            <label className="check"><input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)} /> Internal (the requester will not see it)</label>
          ) : null}
          {busy ? <p>Uploading…</p> : null}
          <ErrorBox error={problem} />
        </div>
      ) : null}
    </section>
  );
}
