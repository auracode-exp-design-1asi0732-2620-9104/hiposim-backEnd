using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HipoSim.Api.Auth;
using HipoSim.Api.Data;
using HipoSim.Api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HipoSim.Api.Tests;

public sealed class AuthTests
{
    private static readonly JwtOptions JwtSettings = new()
    {
        Issuer = "HipoSimTests",
        Audience = "HipoSimTestClients",
        SigningKey = "Use_a_test_only_HMAC_signing_key_with_over_32_bytes!",
        ExpiresInSeconds = 3600
    };

    private static (HipoSimDbContext Database, AuthService Service) NewService()
    {
        var options = new DbContextOptionsBuilder<HipoSimDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var database = new HipoSimDbContext(options);
        var service = new AuthService(
            database,
            new PasswordHasher<AppUser>(),
            new JwtTokenService(Microsoft.Extensions.Options.Options.Create(JwtSettings), TimeProvider.System),
            TimeProvider.System);
        return (database, service);
    }

    private static RegisterRequest ValidRegistration(string email = "buyer@example.com") =>
        new("Ana Torres", email, "987654321", "Secure-Password-2026", true, true);

    [Fact]
    public void Register_requires_both_acceptances()
    {
        var errors = AccountValidation.Validate(ValidRegistration() with
        {
            AcceptedTerms = false,
            AcceptedDataProcessing = false
        });
        Assert.Contains("acceptedTerms", errors.Keys);
        Assert.Contains("acceptedDataProcessing", errors.Keys);
    }

    [Fact]
    public void Register_rejects_short_password_and_invalid_email()
    {
        var errors = AccountValidation.Validate(ValidRegistration("invalid-email") with { Password = "123" });
        Assert.Contains("email", errors.Keys);
        Assert.Contains("password", errors.Keys);
    }

    [Fact]
    public void Login_requires_password_and_email()
    {
        var errors = AccountValidation.Validate(new LoginRequest("", ""));
        Assert.Contains("email", errors.Keys);
        Assert.Contains("password", errors.Keys);
    }

    [Fact]
    public async Task Registration_hashes_password_and_returns_buyer_token()
    {
        var (db, service) = NewService();
        using (db)
        {
            var result = await service.RegisterAsync(ValidRegistration(), CancellationToken.None);
            Assert.Equal(RegistrationStatus.Created, result.Status);
            Assert.NotNull(result.Value);
            Assert.Equal("buyer", result.Value.User.Role);
            Assert.Equal("Bearer", result.Value.TokenType);
            var user = Assert.Single(db.Users);
            Assert.NotEqual("Secure-Password-2026", user.PasswordHash);
            Assert.NotNull(user.PasswordHash);
            Assert.Equal("buyer@example.com", user.EmailNormalized);
            Assert.NotEqual(default, user.AcceptedTermsAt);
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
            Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        }
    }

    [Fact]
    public async Task Duplicate_email_ignores_case_and_spaces()
    {
        var (db, service) = NewService();
        using (db)
        {
            await service.RegisterAsync(ValidRegistration(), CancellationToken.None);
            var result = await service.RegisterAsync(ValidRegistration("  BUYER@example.com "), CancellationToken.None);
            Assert.Equal(RegistrationStatus.Conflict, result.Status);
            Assert.Single(db.Users);
        }
    }

    [Fact]
    public async Task Valid_login_returns_token_and_invalid_login_fails()
    {
        var (db, service) = NewService();
        using (db)
        {
            await service.RegisterAsync(ValidRegistration(), CancellationToken.None);
            var success = await service.LoginAsync(new LoginRequest("BUYER@example.com", "Secure-Password-2026"), CancellationToken.None);
            var failure = await service.LoginAsync(new LoginRequest("buyer@example.com", "bad-password"), CancellationToken.None);
            var missing = await service.LoginAsync(new LoginRequest("absent@example.com", "bad-password"), CancellationToken.None);
            Assert.NotNull(success);
            Assert.Null(failure);
            Assert.Null(missing);
        }
    }

    [Fact]
    public void Advisor_token_contains_agency_and_role()
    {
        var advisor = new AppUser
        {
            Id = Guid.NewGuid(), Email = "advisor@example.com", FullName = "Advisor One",
            Role = "advisor", AgencyId = Guid.NewGuid()
        };
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(JwtSettings), TimeProvider.System);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(service.Create(advisor).AccessToken);
        Assert.Contains(token.Claims, c => c.Type == "agencyId" && c.Value == advisor.AgencyId.ToString());
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "advisor");
    }

    [Fact]
    public async Task Development_seed_is_idempotent_and_creates_no_default_password()
    {
        var (db, _) = NewService();
        using (db)
        {
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var hasher = new PasswordHasher<AppUser>();
            await DatabaseSeed.SeedDevelopmentAsync(db, configuration, hasher);
            await DatabaseSeed.SeedDevelopmentAsync(db, configuration, hasher);
            Assert.Single(db.Agencies);
            Assert.Empty(db.Users);
        }
    }

    [Fact]
    public void Signing_key_must_be_strong()
    {
        Assert.Throws<InvalidOperationException>(() => new JwtOptions
        {
            Issuer = "issuer", Audience = "audience", SigningKey = "short"
        }.Validate());
    }
}
