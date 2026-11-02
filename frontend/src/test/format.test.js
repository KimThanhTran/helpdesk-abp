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
    ['2026-11-02T10:30:00Z', { late: false, n: 30, unit: 'm' }],
    ['2026-11-02T13:00:00Z', { late: false, n: 3, unit: 'h' }],
    ['2026-11-02T09:00:00Z', { late: true, n: 1, unit: 'h' }],
    ['2026-10-30T10:00:00Z', { late: true, n: 3, unit: 'd' }],
  ])('%s is %j', (iso, expected) => expect(relative(iso, NOW)).toEqual(expected));
});

describe('sla', () => {
  const base = { status: Status.Open, firstResponseDueAt: '2026-11-02T12:00:00Z', resolutionDueAt: '2026-11-04T12:00:00Z' };

  it('counts down to the first response, then to the resolution', () => {
    expect(sla(base, NOW)).toEqual({ rel: { late: false, n: 2, unit: 'h' }, tone: 'ok' });
    expect(sla({ ...base, firstRespondedAt: '2026-11-02T09:00:00Z' }, NOW)).toEqual({ rel: { late: false, n: 2, unit: 'd' }, tone: 'ok' });
  });

  it('flags a late ticket', () => {
    expect(sla({ ...base, isResolutionOverdue: true, resolutionDueAt: '2026-11-01T12:00:00Z', firstRespondedAt: 'x' }, NOW).tone).toBe('bad');
  });

  it('says paused while the ticket waits for the requester', () => {
    expect(sla({ ...base, status: Status.Waiting, waitingSince: '2026-11-02T08:00:00Z' }, NOW)).toEqual({ key: 'sla.paused', tone: 'muted' });
  });

  it('says nothing once the ticket is finished', () => {
    expect(sla({ ...base, status: Status.Closed }, NOW)).toBeNull();
    expect(sla({ ...base, status: Status.Resolved }, NOW)).toBeNull();
  });
});
