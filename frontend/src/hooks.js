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
