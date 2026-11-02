using System;

namespace Helpdesk.Tickets;

/// <summary>
/// The result of measuring a pause: the new total of working hours spent waiting, and the targets that follow from it.
/// Computed by <see cref="TicketManager"/> because it needs the business calendar.
/// </summary>
public readonly record struct SlaResume(double PausedBusinessHours, DateTime FirstResponseDueAt, DateTime ResolutionDueAt);
