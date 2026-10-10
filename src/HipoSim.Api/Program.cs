using System.Security.Claims;
using System.Text;
using HipoSim.Api.Auth;
using HipoSim.Api.Data;
using HipoSim.Api.Options;
using HipoSim.Simulations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using HipoSim.Api.Leads;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
jwt.Validate(); // Fail fast rather than accidentally starting with a weak/empty signing key.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("HipoSimDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure ConnectionStrings__HipoSimDb before running the API.");
builder.Services.AddDbContext<HipoSimDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<LeadService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISimulationRepository, EfSimulationRepository>();
builder.Services.AddSimulations(builder.Configuration); // Preserves Franco's existing TS06 engine/controller.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("hiposim-clients", policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "HipoSim API", Version = "v0.1.0" });
    options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter the JWT returned by /api/auth/login or /api/auth/register."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseResponseCompression();
app.UseCors("hiposim-clients");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous().WithTags("Health");

// Development only: automated schema preparation + idempotent demo seed.
// Production migrations must be applied explicitly by the deployment pipeline/operator.
if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:AutoMigrate", false))
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
    await database.Database.MigrateAsync();
    await DatabaseSeed.SeedDevelopmentAsync(database, app.Configuration,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>());
}

await app.RunAsync();

public partial class Program { }
