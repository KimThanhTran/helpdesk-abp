using System.Threading.Tasks;

namespace Helpdesk.Tickets;

public interface ITicketCodeGenerator
{
    /// <summary>
    /// Next code for <paramref name="year"/>, like HD-2026-000123. Numbers restart every year and are
    /// never handed out twice, even when tickets are created at the same moment.
    /// </summary>
    Task<string> NextAsync(int year);
}
