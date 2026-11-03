using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Taskloom.Infrastructure;

/// <summary>Lets `dotnet ef migrations add` run without booting the API.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TaskloomDbContext>
{
    public TaskloomDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Taskloom")
                 ?? "Host=localhost;Port=5432;Database=taskloom;Username=taskloom;Password=taskloom";
        return new TaskloomDbContext(new DbContextOptionsBuilder<TaskloomDbContext>().UseNpgsql(cs).Options);
    }
}
