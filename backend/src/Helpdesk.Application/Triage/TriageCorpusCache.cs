using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Triage;

/// <summary>One finished or running ticket as the triage sees it.</summary>
public sealed record TriageDoc(
    Guid Id, string Code, string Title, string Description, Guid CategoryId, TicketStatus Status, string? Resolution);

/// <summary>The recent tickets and the search index built from them. <see cref="Index"/> is null when there are none.</summary>
public sealed record TriageCorpus(IReadOnlyDictionary<Guid, TriageDoc> ById, TfIdfIndex? Index);

/// <summary>
/// Keeps the triage index in memory for a few seconds. Building it means reading and tokenizing the newest 2,000
/// tickets, which is cheap once and ruinous per request: under load it used up most of the server's processor.
/// One caller builds it while the others wait for that same result, and a failed build is not kept.
/// </summary>
public class TriageCorpusCache : ISingletonDependency
{
    private sealed record Entry(TriageCorpus Corpus, DateTime BuiltAt);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    /// <summary>The cached corpus if it is younger than <paramref name="lifetime"/>, otherwise a fresh one (a lifetime of 0 never caches).</summary>
    public async Task<TriageCorpus> GetAsync(Func<Task<TriageCorpus>> build, TimeSpan lifetime, DateTime now)
    {
        if (Fresh(_entry, lifetime, now) is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync();
        try
        {
            // someone else may have rebuilt it while this call waited for its turn
            if (Fresh(_entry, lifetime, now) is { } rebuilt)
            {
                return rebuilt;
            }

            var corpus = await build();
            _entry = new Entry(corpus, now);
            return corpus;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static TriageCorpus? Fresh(Entry? entry, TimeSpan lifetime, DateTime now) =>
        entry is not null && now - entry.BuiltAt < lifetime ? entry.Corpus : null;
}
