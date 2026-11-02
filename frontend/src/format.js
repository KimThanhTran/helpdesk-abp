// The API returns UTC times, sometimes without a trailing Z; read them as UTC either way.
export const utc = (iso) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`);

/** How far a time is from now, as numbers: { late, n, unit: 'm' | 'h' | 'd' }. The words come from the chosen language. */
export function relative(iso, now = Date.now()) {
  const minutes = Math.round((utc(iso).getTime() - now) / 60000);
  const abs = Math.abs(minutes);
  const [n, unit] = abs < 60 ? [abs, 'm'] : abs < 2880 ? [Math.round(abs / 60), 'h'] : [Math.round(abs / 1440), 'd'];
  return { late: minutes < 0, n, unit };
}

const ACTIVE = new Set([1, 2, 3]); // New, Open, Waiting

/**
 * What to say about a ticket's deadline: nothing once it is finished, "paused" while it waits for the requester,
 * otherwise the time to the next target. Returns { tone, key } or { tone, rel }.
 */
export function sla(ticket, now = Date.now()) {
  if (!ACTIVE.has(ticket.status)) return null;
  if (ticket.waitingSince) return { key: 'sla.paused', tone: 'muted' };
  const late = ticket.isResolutionOverdue || ticket.isFirstResponseOverdue;
  const due = ticket.firstRespondedAt || !ticket.firstResponseDueAt ? ticket.resolutionDueAt : ticket.firstResponseDueAt;
  return { rel: relative(due, now), tone: late ? 'bad' : 'ok' };
}
