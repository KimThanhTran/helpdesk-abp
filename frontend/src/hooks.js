import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Runs `load(signal)` on mount and whenever `deps` change, and again when `reload()` is called.
 * An older answer never overwrites a newer one, and nothing is set after the page is gone.
 */
export function useLoad(load, deps) {
  const [state, setState] = useState({ data: null, error: null, loading: true });
  const [tick, setTick] = useState(0);
  const latest = useRef(load);
  latest.current = load;

  useEffect(() => {
    const controller = new AbortController();
    setState((current) => ({ ...current, loading: true }));
    latest.current(controller.signal)
      .then((data) => setState({ data, error: null, loading: false }))
      .catch((error) => {
        if (error.name !== 'AbortError') setState((current) => ({ data: current.data, error, loading: false }));
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  const reload = useCallback(() => setTick((value) => value + 1), []);
  return { ...state, reload };
}

/** Returns `value` once it has stopped changing for `ms`. */
export function useDebounced(value, ms = 500) {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return settled;
}

const EXTENSION = { 'image/png': 'png', 'image/jpeg': 'jpg', 'image/webp': 'webp' };

/** A screenshot from the clipboard is called "image.png"; give it a name that tells screenshots apart. */
export function nameScreenshot(file, now = new Date()) {
  const two = (n) => String(n).padStart(2, '0');
  const stamp = `${now.getFullYear()}${two(now.getMonth() + 1)}${two(now.getDate())}-${two(now.getHours())}${two(now.getMinutes())}${two(now.getSeconds())}`;
  return new File([file], `screenshot-${stamp}.${EXTENSION[file.type] ?? 'png'}`, { type: file.type });
}

/**
 * Calls `onImages(files)` when the person pastes (Ctrl+V) an image anywhere on the page, such as a screenshot.
 * Pasting text is left alone: the page only steps in when the clipboard holds an image.
 */
export function usePastedImages(onImages, enabled = true) {
  const latest = useRef(onImages);
  latest.current = onImages;
  useEffect(() => {
    if (!enabled) return undefined;
    const onPaste = (event) => {
      const images = [...(event.clipboardData?.files ?? [])].filter((file) => file.type.startsWith('image/'));
      if (images.length === 0) return;
      event.preventDefault();
      latest.current(images.map((file) => nameScreenshot(file)));
    };
    document.addEventListener('paste', onPaste);
    return () => document.removeEventListener('paste', onPaste);
  }, [enabled]);
}
