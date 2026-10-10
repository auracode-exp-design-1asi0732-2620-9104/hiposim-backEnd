using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HipoSim.Api.Swagger;

/// <summary>Adds the request and response examples of the v0.1.0 contract to the Swagger schemas (TS05).</summary>
public sealed class ExamplesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        schema.Example = context.Type.Name switch
        {
            "RegisterRequest" => new OpenApiObject
            {
                ["fullName"] = new OpenApiString("Ana Torres"),
                ["email"] = new OpenApiString("ana.torres@example.com"),
                ["phone"] = new OpenApiString("987654321"),
                ["password"] = new OpenApiString("S3gura-2026"),
                ["acceptedTerms"] = new OpenApiBoolean(true),
                ["acceptedDataProcessing"] = new OpenApiBoolean(true)
            },
            "LoginRequest" => new OpenApiObject
            {
                ["email"] = new OpenApiString("ana.torres@example.com"),
                ["password"] = new OpenApiString("S3gura-2026")
            },
            "UserResponse" => UserExample(),
            "AuthResponse" => new OpenApiObject
            {
                ["accessToken"] = new OpenApiString("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.example.signature"),
                ["tokenType"] = new OpenApiString("Bearer"),
                ["expiresIn"] = new OpenApiInteger(28800),
                ["user"] = UserExample()
            },
            "SimulationRequest" => new OpenApiObject
            {
                ["propertyPrice"] = new OpenApiDouble(280000),
                ["downPayment"] = new OpenApiDouble(42000),
                ["annualEffectiveRate"] = new OpenApiDouble(0.085),
                ["termInMonths"] = new OpenApiInteger(240),
                ["gracePeriodMonths"] = new OpenApiInteger(0),
                ["graceType"] = new OpenApiString("none"),
                ["applyGoodPayerBonus"] = new OpenApiBoolean(true),
                ["startDate"] = new OpenApiString("2026-10-01")
            },
            _ => schema.Example
        };
    }

    private static OpenApiObject UserExample() => new()
    {
        ["id"] = new OpenApiString("7c1d0f52-4b3e-4a8d-9a1e-2f6b8c3d5e10"),
        ["fullName"] = new OpenApiString("Ana Torres"),
        ["email"] = new OpenApiString("ana.torres@example.com"),
        ["role"] = new OpenApiString("buyer")
    };
}

/// <summary>
/// Adds the Bearer requirement to the endpoints that use a token and the error examples
/// (application/problem+json) to every documented 400, 401 and 409 response (TS05).
/// </summary>
public sealed class ApiDocumentationOperationFilter : IOperationFilter
{
    private static readonly OpenApiSecurityScheme BearerScheme = new()
    {
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" }
    };

    private static readonly Dictionary<(string Path, string Status), OpenApiObject> ErrorExamples = new()
    {
        [("/api/auth/register", "400")] = Problem(400, "One or more validation errors occurred.",
            "acceptedTerms", "Terms and Conditions must be accepted."),
        [("/api/auth/register", "409")] = Problem(409, "Email already registered."),
        [("/api/auth/login", "400")] = Problem(400, "One or more validation errors occurred.",
            "email", "A valid email address is required."),
        [("/api/auth/login", "401")] = Problem(401, "Invalid credentials."),
        [("/api/simulations", "400")] = Problem(400, "One or more validation errors occurred.",
            "downPayment", "The down payment must be at least 10% of the property price.")
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = "/" + (context.ApiDescription.RelativePath ?? string.Empty).Split('?')[0].Trim('/').ToLowerInvariant();

        if (path.StartsWith("/api/simulations", StringComparison.Ordinal))
        {
            operation.Security.Add(new OpenApiSecurityRequirement());
            operation.Security.Add(new OpenApiSecurityRequirement { [BearerScheme] = [] });
        }
        else if (path.StartsWith("/api/leads", StringComparison.Ordinal))
        {
            operation.Security.Add(new OpenApiSecurityRequirement { [BearerScheme] = [] });
        }

        foreach (var (status, response) in operation.Responses)
        {
            if (!ErrorExamples.TryGetValue((path, status), out var example))
                continue;
            if (response.Content.TryGetValue("application/problem+json", out var media))
                media.Example = example;
        }
    }

    private static OpenApiObject Problem(int status, string title, string? field = null, string? message = null)
    {
        var problem = new OpenApiObject
        {
            ["title"] = new OpenApiString(title),
            ["status"] = new OpenApiInteger(status)
        };
        if (field is not null && message is not null)
        {
            problem["errors"] = new OpenApiObject
            {
                [field] = new OpenApiArray { new OpenApiString(message) }
            };
        }
        return problem;
    }
}