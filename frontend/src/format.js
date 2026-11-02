// The API returns UTC times, sometimes without a trailing Z; read them as UTC either way.
export const utc = (iso) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`);

export const formatDate = (iso) =>
  iso ? utc(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '';

/** "in 3h", "2d late": how far a target is from now. */
export function relative(iso, now = Date.now()) {
  const minutes = Math.round((utc(iso).getTime() - now) / 60000);
  const abs = Math.abs(minutes);
  const text = abs < 60 ? `${abs}m` : abs < 2880 ? `${Math.round(abs / 60)}h` : `${Math.round(abs / 1440)}d`;
  return minutes < 0 ? `${text} late` : `in ${text}`;
}

const ACTIVE = new Set([1, 2, 3]); // New, Open, Waiting

/** What to say about a ticket's deadline: nothing once it is finished, "paused" while it waits for the requester. */
export function sla(ticket, now = Date.now()) {
  if (!ACTIVE.has(ticket.status)) return null;
  if (ticket.waitingSince) return { text: 'SLA paused', tone: 'muted' };
  const late = ticket.isResolutionOverdue || ticket.isFirstResponseOverdue;
  const due = ticket.firstRespondedAt || !ticket.firstResponseDueAt ? ticket.resolutionDueAt : ticket.firstResponseDueAt;
  return { text: relative(due, now), tone: late ? 'bad' : 'ok' };
}
