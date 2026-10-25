using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.Tests.Infrastructure;

// The open connection keeps this private in-memory SQLite database alive for one test.
internal sealed class TestDatabase<TContext> : IDisposable where TContext : DbContext
{
    private readonly SqliteConnection _connection;

    public TestDatabase(Func<DbContextOptions<TContext>, TContext> createContext)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TContext>().UseSqlite(_connection).Options;
        Context = createContext(options);
        Context.Database.Migrate();
    }

    public TContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

