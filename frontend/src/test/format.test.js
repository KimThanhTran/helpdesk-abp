import { describe, expect, it } from 'vitest';
import { Status } from '../constants';
import { relative, sla, utc } from '../format';

const NOW = Date.parse('2026-11-02T10:00:00Z');

describe('utc', () => {
  it('reads times without a zone as UTC', () => {
    expect(utc('2026-11-02T10:00:00').toISOString()).toBe('2026-11-02T10:00:00.000Z');
    expect(utc('2026-11-02T10:00:00Z').toISOString()).toBe('2026-11-02T10:00:00.000Z');
    expect(utc('2026-11-02T17:00:00+07:00').toISOString()).toBe('2026-11-02T10:00:00.000Z');
  });
});

describe('relative', () => {
  it.each([
    ['2026-11-02T10:30:00Z', 'in 30m'],
    ['2026-11-02T13:00:00Z', 'in 3h'],
    ['2026-11-02T09:00:00Z', '1h late'],
    ['2026-10-30T10:00:00Z', '3d late'],
  ])('%s is %s', (iso, text) => expect(relative(iso, NOW)).toBe(text));
});

describe('sla', () => {
  const base = { status: Status.Open, firstResponseDueAt: '2026-11-02T12:00:00Z', resolutionDueAt: '2026-11-04T12:00:00Z' };

  it('counts down to the first response, then to the resolution', () => {
    expect(sla(base, NOW)).toEqual({ text: 'in 2h', tone: 'ok' });
    expect(sla({ ...base, firstRespondedAt: '2026-11-02T09:00:00Z' }, NOW)).toEqual({ text: 'in 2d', tone: 'ok' });
  });

  it('flags a late ticket', () => {
    expect(sla({ ...base, isResolutionOverdue: true, resolutionDueAt: '2026-11-01T12:00:00Z', firstRespondedAt: 'x' }, NOW).tone).toBe('bad');
  });

  it('says paused while the ticket waits for the requester', () => {
    expect(sla({ ...base, status: Status.Waiting, waitingSince: '2026-11-02T08:00:00Z' }, NOW)).toEqual({ text: 'SLA paused', tone: 'muted' });
  });

  it('says nothing once the ticket is finished', () => {
    expect(sla({ ...base, status: Status.Closed }, NOW)).toBeNull();
    expect(sla({ ...base, status: Status.Resolved }, NOW)).toBeNull();
  });
});
