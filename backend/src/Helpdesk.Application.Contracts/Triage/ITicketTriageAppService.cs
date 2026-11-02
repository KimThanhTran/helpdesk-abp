using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Helpdesk.Triage;

public interface ITicketTriageAppService : IApplicationService
{
    /// <summary>
    /// Suggestions for a ticket text, learned from the tickets already in the system.
    /// Everyone who can open tickets gets category suggestions. Similar tickets, a duplicate warning and a reply
    /// taken from a resolved ticket show other people's tickets, so only agents get them.
    /// </summary>
    Task<TriageSuggestionDto> GetSuggestionAsync(GetTriageInput input);
}
