using Npgsql;

namespace HipoSim.Api.Data;

/// <summary>Translates a `postgres://user:password@host:port/database` URL into an Npgsql connection string.</summary>
public static class DatabaseUrl
{
    public static string? ToConnectionString(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException("DATABASE_URL must look like postgres://user:password@host:port/database.");
        }

        var credentials = uri.UserInfo.Split(':', 2);
        var database = uri.AbsolutePath.Trim('/');
        if (credentials[0].Length == 0 || uri.Host.Length == 0 || database.Length == 0)
            throw new InvalidOperationException("DATABASE_URL must include the user, the host and the database.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(database),
            Username = Uri.UnescapeDataString(credentials[0])
        };
        if (credentials.Length > 1)
            builder.Password = Uri.UnescapeDataString(credentials[1]);
        return builder.ConnectionString;
    }
}
