using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Helpdesk.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace Helpdesk.Tickets;

/// <summary>
/// Takes the next number with a single INSERT ... ON CONFLICT ... RETURNING statement. The database serialises
/// concurrent callers on the counter row, so two tickets created at the same moment never share a number.
/// The same statement works on PostgreSQL (production) and SQLite (tests).
/// </summary>
public class EfTicketCodeGenerator : ITicketCodeGenerator, ITransientDependency
{
    private readonly IDbContextProvider<HelpdeskDbContext> _dbContextProvider;

    public EfTicketCodeGenerator(IDbContextProvider<HelpdeskDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<string> NextAsync(int year)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var table = db.Model.FindEntityType(typeof(TicketCodeCounter))!.GetTableName()!;

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync();
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            $"INSERT INTO \"{table}\" (\"Year\", \"Value\") VALUES (@year, 1) " +
            $"ON CONFLICT (\"Year\") DO UPDATE SET \"Value\" = \"{table}\".\"Value\" + 1 " +
            "RETURNING \"Value\"";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@year";
        parameter.Value = year;
        command.Parameters.Add(parameter);

        var value = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        return $"{TicketConsts.CodePrefix}-{year}-{value:D6}";
    }
}
