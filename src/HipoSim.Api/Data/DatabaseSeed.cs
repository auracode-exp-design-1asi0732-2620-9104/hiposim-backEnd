using HipoSim.Api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HipoSim.Api.Data;

/// <summary>
/// Idempotent demo seed: a demo agency and an optional advisor account. It runs in Development, and in a demo
/// deployment only when Seed:Enabled is set.
/// </summary>
public static class DatabaseSeed
{
    public static readonly Guid DemoAgencyId = Guid.Parse("a3e32c74-3e12-43e2-b609-3189294ff3e5");

    public static async Task SeedDevelopmentAsync(
        HipoSimDbContext database,
        IConfiguration configuration,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken = default)
    {
        if (!await database.Agencies.AnyAsync(a => a.Id == DemoAgencyId, cancellationToken))
        {
            database.Agencies.Add(new Agency
            {
                Id = DemoAgencyId,
                Name = "HipoSim Demo Agency",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync(cancellationToken);
        }

        var email = configuration["Seed:Advisor:Email"]?.Trim();
        var password = configuration["Seed:Advisor:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return; // Never create an account with a hard-coded password.
        if (password.Length < 8)
            throw new InvalidOperationException("Seed:Advisor:Password must be at least 8 characters.");
        var normalized = AccountValidation.NormalizeEmail(email);
        if (await database.Users.AnyAsync(u => u.EmailNormalized == normalized, cancellationToken))
            return;

        var now = DateTimeOffset.UtcNow;
        var advisor = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Demo Advisor",
            Email = email,
            EmailNormalized = normalized,
            Phone = "000000000",
            Role = "advisor",
            AgencyId = DemoAgencyId,
            AcceptedTermsAt = now,
            AcceptedDataProcessingAt = now,
            CreatedAt = now
        };
        advisor.PasswordHash = passwordHasher.HashPassword(advisor, password);
        database.Users.Add(advisor);
        await database.SaveChangesAsync(cancellationToken);
    }
}
