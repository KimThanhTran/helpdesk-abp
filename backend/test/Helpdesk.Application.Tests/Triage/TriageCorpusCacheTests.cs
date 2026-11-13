using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Helpdesk.Triage;

public class TriageCorpusCacheTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    private static TriageCorpus Corpus() => new(new Dictionary<Guid, TriageDoc>(), null);

    private sealed class Builder
    {
        public int Calls;

        public Task<TriageCorpus> Build()
        {
            Calls++;
            return Task.FromResult(Corpus());
        }
    }

    [Fact]
    public async Task Builds_once_and_serves_the_same_corpus_until_it_is_old()
    {
        var cache = new TriageCorpusCache();
        var builder = new Builder();

        var first = await cache.GetAsync(builder.Build, Lifetime, T0);
        var later = await cache.GetAsync(builder.Build, Lifetime, T0.AddSeconds(14));

        builder.Calls.ShouldBe(1);
        later.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task Rebuilds_once_the_lifetime_has_passed_so_new_tickets_show_up()
    {
        var cache = new TriageCorpusCache();
        var builder = new Builder();

        var first = await cache.GetAsync(builder.Build, Lifetime, T0);
        var second = await cache.GetAsync(builder.Build, Lifetime, T0.AddSeconds(15));
        await cache.GetAsync(builder.Build, Lifetime, T0.AddSeconds(16));

        builder.Calls.ShouldBe(2);
        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public async Task A_lifetime_of_zero_never_caches()
    {
        var cache = new TriageCorpusCache();
        var builder = new Builder();

        await cache.GetAsync(builder.Build, TimeSpan.Zero, T0);
        await cache.GetAsync(builder.Build, TimeSpan.Zero, T0);

        builder.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Callers_that_arrive_together_share_one_build()
    {
        var cache = new TriageCorpusCache();
        var release = new TaskCompletionSource();
        var calls = 0;
        async Task<TriageCorpus> Slow()
        {
            calls++;
            await release.Task;
            return Corpus();
        }

        var callers = Enumerable.Range(0, 20).Select(_ => cache.GetAsync(Slow, Lifetime, T0)).ToList();
        await Task.Delay(50);
        release.SetResult();
        var results = await Task.WhenAll(callers);

        calls.ShouldBe(1);
        results.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_build_is_not_kept_and_the_next_caller_tries_again()
    {
        var cache = new TriageCorpusCache();
        var attempts = 0;
        Task<TriageCorpus> Flaky()
        {
            attempts++;
            return attempts == 1 ? throw new InvalidOperationException("database down") : Task.FromResult(Corpus());
        }

        await Should.ThrowAsync<InvalidOperationException>(() => cache.GetAsync(Flaky, Lifetime, T0));
        var recovered = await cache.GetAsync(Flaky, Lifetime, T0);

        attempts.ShouldBe(2);
        recovered.ShouldNotBeNull();
    }
}
