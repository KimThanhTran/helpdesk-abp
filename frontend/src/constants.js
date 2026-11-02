// The API sends enums as numbers. These tables turn them into words and back.
export const Status = { New: 1, Open: 2, Waiting: 3, Resolved: 4, Closed: 5, Cancelled: 6 };
export const Priority = { Low: 1, Normal: 2, High: 3, Urgent: 4 };
export const Visibility = { Public: 1, Internal: 2 };
export const Scope = { Unassigned: 1, Mine: 2, All: 3 };

export const Action = {
  Comment: 1,
  InternalNote: 2,
  Claim: 3,
  Assign: 4,
  ChangePriority: 5,
  Wait: 6,
  Resume: 7,
  Resolve: 8,
  Confirm: 9,
  Reject: 10,
  Reopen: 11,
  Cancel: 12,
};

// English names, for CSS classes and for reading what the server sent; the text people see comes from locales.js
export const statusLabel = (value) => Object.keys(Status).find((key) => Status[key] === value) ?? String(value);
export const priorityLabel = (value) => Object.keys(Priority).find((key) => Priority[key] === value) ?? String(value);

// What each action needs from the person before it is sent, and the route it calls.
// Its button text is the key `action.<code>` and its question `prompt.<code>` (see locales.js).
export const ACTIONS = {
  [Action.Claim]: { path: 'claim' },
  [Action.Assign]: { path: 'assign', field: 'assignee' },
  [Action.ChangePriority]: { path: 'change-priority', field: 'priority' },
  [Action.Wait]: { path: 'wait', field: 'reason', body: 'reason' },
  [Action.Resume]: { path: 'resume' },
  [Action.Resolve]: { path: 'resolve', field: 'text', body: 'resolution' },
  [Action.Confirm]: { path: 'confirm', tone: 'good' },
  [Action.Reject]: { path: 'reject', field: 'text', body: 'reason' },
  [Action.Reopen]: { path: 'reopen', field: 'text', body: 'reason' },
  [Action.Cancel]: { path: 'cancel', field: 'text', body: 'reason', tone: 'danger' },
};

// Shown in this order; Comment and InternalNote have their own box, not a button
export const ACTION_ORDER = [
  Action.Claim, Action.Assign, Action.ChangePriority, Action.Wait, Action.Resume, Action.Resolve,
  Action.Confirm, Action.Reject, Action.Reopen, Action.Cancel,
];
