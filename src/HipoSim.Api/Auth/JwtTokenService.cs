using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HipoSim.Api.Data;
using HipoSim.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HipoSim.Api.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> settings, TimeProvider clock)
{
    public AuthResponse Create(AppUser user)
    {
        var options = settings.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role)
        };
        if (user.Role == "advisor" && user.AgencyId is Guid agencyId)
            claims.Add(new Claim("agencyId", agencyId.ToString()));
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(options.ExpiresInSeconds),
            signingCredentials: credentials);
        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            "Bearer",
            options.ExpiresInSeconds,
            new UserResponse(user.Id, user.FullName, user.Email, user.Role));
    }
}
