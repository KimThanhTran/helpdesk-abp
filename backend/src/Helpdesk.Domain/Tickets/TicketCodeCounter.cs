using Volo.Abp.Domain.Entities;

namespace Helpdesk.Tickets;

/// <summary>Last ticket number handed out in a year. Incremented by one atomic SQL statement.</summary>
public class TicketCodeCounter : Entity
{
    public int Year { get; private set; }

    public int Value { get; private set; }

    protected TicketCodeCounter()
    {
    }

    public override object[] GetKeys() => new object[] { Year };
}
