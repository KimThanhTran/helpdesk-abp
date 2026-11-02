import { useState } from 'react';
import { saveFile } from '../download';
import { useT } from '../i18n';
import { ErrorBox } from './bits';

/** Runs `fetchFile` (an API call that resolves to { blob, filename }) and saves the result. */
export default function ExportButton({ fetchFile }) {
  const { t } = useT();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const run = async () => {
    setBusy(true);
    setError(null);
    try {
      saveFile(await fetchFile());
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  };

  return (
    <span className="export">
      <button type="button" onClick={run} disabled={busy}>{busy ? t('export.busy') : t('export.button')}</button>
      <ErrorBox error={error} />
    </span>
  );
}
