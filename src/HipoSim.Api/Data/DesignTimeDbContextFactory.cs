using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HipoSim.Api.Data;

/// <summary>Lets dotnet ef work without starting the web host or exposing a JWT key.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HipoSimDbContext>
{
    public HipoSimDbContext CreateDbContext(string[] args)
    {
        var cwd = Directory.GetCurrentDirectory();
        var projectDir = File.Exists(Path.Combine(cwd, "appsettings.json"))
            ? cwd : Path.Combine(cwd, "src", "HipoSim.Api");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("HipoSimDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Configure ConnectionStrings__HipoSimDb for EF commands.");
        return new HipoSimDbContext(new DbContextOptionsBuilder<HipoSimDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}
