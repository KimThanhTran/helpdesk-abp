/** Hands a downloaded file ({ blob, filename } from the API client) to the browser's save dialog. */
export function saveFile({ blob, filename }) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  // some browsers start the download after the click returns, so the address is released a little later
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export const formatBytes = (bytes) => (bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1048576).toFixed(1)} MB`);
