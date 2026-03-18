#if TOOLS

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SQLitePCL;

// ReSharper disable UnusedType.Global

namespace Content.Server.Database;

public sealed class DesignTimeContextFactoryPostgres : IDesignTimeDbContextFactory<PostgresServerDbContext>
{
    public PostgresServerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresServerDbContext>();
        optionsBuilder.UseNpgsql("Server=localhost");
        return new PostgresServerDbContext(optionsBuilder.Options);
    }
}

public sealed class DesignTimeContextFactorySqlite : IDesignTimeDbContextFactory<SqliteServerDbContext>
{
    public SqliteServerDbContext CreateDbContext(string[] args)
    {
#if !USE_SYSTEM_SQLITE
        var batteriesType = System.Type.GetType("SQLitePCL.Batteries_V2, SQLitePCLRaw.batteries_v2");
        batteriesType?
            .GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);
#endif

        var optionsBuilder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        optionsBuilder.UseSqlite("Data Source=:memory:");
        return new SqliteServerDbContext(optionsBuilder.Options);
    }
}

#endif
