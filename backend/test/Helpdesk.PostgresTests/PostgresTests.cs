using Helpdesk.EntityFrameworkCore;
using Helpdesk.Tickets;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace Helpdesk;

/// <summary>What only a real PostgreSQL can prove: the migrations apply, and ticket numbers survive a rush.</summary>
[Collection(PostgresCollection.Name)]
public class PostgresTests
{
    private readonly PostgresFixture _postgres;

    public PostgresTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static EfTicketCodeGenerator GeneratorFor(HelpdeskDbContext db)
    {
        var provider = Substitute.For<IDbContextProvider<HelpdeskDbContext>>();
        provider.GetDbContextAsync().Returns(Task.FromResult(db));
        return new EfTicketCodeGenerator(provider);
    }

    [Fact]
    public async Task The_migrations_produce_exactly_the_model_the_code_expects()
    {
        await using var db = _postgres.NewContext();

        // false means nobody changed an entity and forgot to add a migration
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fifty_tickets_created_at_the_same_moment_get_fifty_different_numbers_with_no_gaps()
    {
        const int year = 2031;

        // every task opens its own connection and transaction, like 50 simultaneous requests would
        var codes = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            await using var db = _postgres.NewContext();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var code = await GeneratorFor(db).NextAsync(year);
            await transaction.CommitAsync();
            return code;
        }));

        codes.Distinct().Count().ShouldBe(50);
        codes.Select(code => int.Parse(code[^6..])).Order().ShouldBe(Enumerable.Range(1, 50));
        codes.ShouldAllBe(code => code.StartsWith("HD-2031-"));
    }

    [Fact]
    public async Task A_new_year_starts_again_from_one_without_touching_the_old_year()
    {
        await using var db = _postgres.NewContext();
        var generator = GeneratorFor(db);

        (await generator.NextAsync(2032)).ShouldBe("HD-2032-000001");
        (await generator.NextAsync(2032)).ShouldBe("HD-2032-000002");
        (await generator.NextAsync(2033)).ShouldBe("HD-2033-000001");
        (await generator.NextAsync(2032)).ShouldBe("HD-2032-000003");
    }

    [Fact]
    public async Task A_rolled_back_transaction_does_not_burn_a_number_that_was_never_used()
    {
        const int year = 2034;

        await using (var db = _postgres.NewContext())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            (await GeneratorFor(db).NextAsync(year)).ShouldBe("HD-2034-000001");
            await transaction.RollbackAsync();
        }

        await using var next = _postgres.NewContext();
        (await GeneratorFor(next).NextAsync(year)).ShouldBe("HD-2034-000001");
    }
}
