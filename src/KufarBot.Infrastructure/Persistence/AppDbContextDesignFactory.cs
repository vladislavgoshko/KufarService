using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KufarBot.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so that `dotnet ef` never needs to bootstrap KufarBot.App.Program
/// (which requires a bot token to start). Console tooling uses this factory directly.
/// </summary>
public class AppDbContextDesignFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("DataSource=kufar_design_time.db");
        return new AppDbContext(optionsBuilder.Options);
    }
}
