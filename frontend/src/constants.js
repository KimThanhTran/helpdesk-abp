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

export const statusLabel = (value) => Object.keys(Status).find((key) => Status[key] === value) ?? String(value);
export const priorityLabel = (value) => Object.keys(Priority).find((key) => Priority[key] === value) ?? String(value);

export const EventKind = { 1: 'Created', 2: 'Assigned', 3: 'Priority changed', 4: 'Status changed', 5: 'Reopened', 6: 'Closed automatically' };

// What each action needs from the person before it is sent, and the route it calls.
export const ACTIONS = {
  [Action.Claim]: { label: 'Take it', path: 'claim' },
  [Action.Assign]: { label: 'Assign…', path: 'assign', field: 'assignee' },
  [Action.ChangePriority]: { label: 'Change priority…', path: 'change-priority', field: 'priority' },
  [Action.Wait]: { label: 'Wait for requester…', path: 'wait', field: 'reason', prompt: 'What do you need from the requester?', body: 'reason' },
  [Action.Resume]: { label: 'Resume', path: 'resume' },
  [Action.Resolve]: { label: 'Resolve…', path: 'resolve', field: 'text', prompt: 'How was it resolved?', body: 'resolution' },
  [Action.Confirm]: { label: 'Confirm it is solved', path: 'confirm', tone: 'good' },
  [Action.Reject]: { label: 'Not solved…', path: 'reject', field: 'text', prompt: 'What is still wrong?', body: 'reason' },
  [Action.Reopen]: { label: 'Reopen…', path: 'reopen', field: 'text', prompt: 'Why are you reopening it?', body: 'reason' },
  [Action.Cancel]: { label: 'Cancel ticket…', path: 'cancel', field: 'text', prompt: 'Why are you cancelling it?', body: 'reason', tone: 'danger' },
};

// Shown in this order; Comment and InternalNote have their own box, not a button
export const ACTION_ORDER = [
  Action.Claim, Action.Assign, Action.ChangePriority, Action.Wait, Action.Resume, Action.Resolve,
  Action.Confirm, Action.Reject, Action.Reopen, Action.Cancel,
];
