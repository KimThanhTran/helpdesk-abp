using Helpdesk.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Helpdesk;

/// <summary>One throw-away PostgreSQL for all tests in this project, migrated exactly as production is.</summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public DbContextOptions<HelpdeskDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // ABP's EF Core module turns this on at startup (and when migrations are generated), so the
        // real application and the migrations both use "timestamp without time zone" for DateTime.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        await _container.StartAsync();

        // Same call the DbMigrator makes: this is what proves the generated migrations are valid PostgreSQL.
        Options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public HelpdeskDbContext NewContext() => new(Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
