using HipoSim.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HipoSim.Api.Auth;

public sealed class AuthService(
    HipoSimDbContext database,
    IPasswordHasher<AppUser> passwordHasher,
    JwtTokenService tokens,
    TimeProvider clock)
{
    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var emailNormalized = AccountValidation.NormalizeEmail(request.Email!);
        if (await database.Users.AnyAsync(u => u.EmailNormalized == emailNormalized, cancellationToken))
            return new RegistrationResult(RegistrationStatus.Conflict);

        var now = clock.GetUtcNow();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName!.Trim(),
            Email = request.Email!.Trim(),
            EmailNormalized = emailNormalized,
            Phone = request.Phone!.Trim(),
            Role = "buyer", // Public registration must not grant advisor/admin privileges.
            CreatedAt = now,
            AcceptedTermsAt = now,
            AcceptedDataProcessingAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        database.Users.Add(user);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_users_email_normalized" })
        {
            database.Entry(user).State = EntityState.Detached;
            return new RegistrationResult(RegistrationStatus.Conflict);
        }
        return new RegistrationResult(RegistrationStatus.Created, tokens.Create(user));
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = AccountValidation.NormalizeEmail(request.Email!);
        var user = await database.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.EmailNormalized == normalized, cancellationToken);
        if (user is null)
            return null;

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password!);
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
            database.Users.Update(user);
            await database.SaveChangesAsync(cancellationToken);
        }
        return tokens.Create(user);
    }
}
