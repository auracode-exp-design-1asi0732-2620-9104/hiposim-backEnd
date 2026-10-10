using System.Reflection;
using System.Security.Claims;
using System.Text;
using HipoSim.Api.Auth;
using HipoSim.Api.Data;
using HipoSim.Api.Options;
using HipoSim.Api.Swagger;
using HipoSim.Simulations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using HipoSim.Api.Leads;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
jwt.Validate();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// Hosting platforms such as Render hand out the database as a URL (DATABASE_URL); local runs use the connection string.
var connectionString = builder.Configuration.GetConnectionString("HipoSimDb");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = DatabaseUrl.ToConnectionString(builder.Configuration["DATABASE_URL"]);
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure ConnectionStrings__HipoSimDb or DATABASE_URL before running the API.");
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
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HipoSim API",
        Version = "v0.1.0",
        Description = """
            REST API of HipoSim, the mortgage simulator shared by the Android app, the advisor web app and the Landing Page.

            **Conventions**
            - JSON with camelCase property names.
            - Rates are fractions: `0.085` means 8.5%.
            - Amounts are in PEN with 2 decimals; dates use ISO 8601 (`2026-10-01`).
            - Errors use `application/problem+json`; validation errors list each failing field under `errors`.
            - Protected endpoints expect `Authorization: Bearer <accessToken>`, obtained from `/api/auth/login` or `/api/auth/register`.
            """
    });

    options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter the JWT returned by /api/auth/login or /api/auth/register."
    });

    foreach (var xmlFile in new[]
             {
                 $"{Assembly.GetExecutingAssembly().GetName().Name}.xml",
                 "HipoSim.Simulations.xml"
             })
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }

    options.SchemaFilter<ExamplesSchemaFilter>();
    options.OperationFilter<ApiDocumentationOperationFilter>();
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
app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .AllowAnonymous()
    .WithName("getHealth")
    .WithTags("Health")
    .WithSummary("Check that the API is available")
    .WithDescription("Returns 200 with `{ \"status\": \"Healthy\" }` when the API is up. It requires no token.")
    .Produces<HealthResponse>(StatusCodes.Status200OK);

// Opt-in schema preparation (Database__AutoMigrate=true). It is on in Development; a deployment without a separate
// migration step turns it on explicitly. The idempotent demo seed runs in Development, or with Seed__Enabled=true.
if (builder.Configuration.GetValue("Database:AutoMigrate", false))
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<HipoSimDbContext>();
    await database.Database.MigrateAsync();
    if (app.Environment.IsDevelopment() || builder.Configuration.GetValue("Seed:Enabled", false))
    {
        await DatabaseSeed.SeedDevelopmentAsync(database, app.Configuration,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>());
    }
}

await app.RunAsync();

public sealed record HealthResponse(string Status);

public partial class Program { }