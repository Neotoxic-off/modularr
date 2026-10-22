using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Modularr.Core.Persistence;

// Lets `dotnet ef migrations add` build the model without starting the app.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ModularrDbContext>
{
    public ModularrDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ModularrDbContext>().UseSqlite("Data Source=modularr.db").Options);
}
