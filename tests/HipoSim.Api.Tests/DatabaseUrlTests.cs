using HipoSim.Api.Data;
using Npgsql;

namespace HipoSim.Api.Tests;

public sealed class DatabaseUrlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_url_returns_null(string? url)
    {
        Assert.Null(DatabaseUrl.ToConnectionString(url));
    }

    [Theory]
    [InlineData("postgres://hiposim:s3cret@db.internal:5433/hiposim_db", 5433)]
    [InlineData("postgresql://hiposim:s3cret@db.internal/hiposim_db", 5432)]
    public void Url_is_translated_to_a_connection_string(string url, int expectedPort)
    {
        var parsed = new NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString(url));

        Assert.Equal("db.internal", parsed.Host);
        Assert.Equal(expectedPort, parsed.Port);
        Assert.Equal("hiposim_db", parsed.Database);
        Assert.Equal("hiposim", parsed.Username);
        Assert.Equal("s3cret", parsed.Password);
    }

    [Fact]
    public void Escaped_characters_in_the_password_are_decoded()
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            DatabaseUrl.ToConnectionString("postgres://hiposim:p%40ss%3Bword@db.internal/hiposim_db"));

        Assert.Equal("p@ss;word", parsed.Password);
    }

    [Theory]
    [InlineData("Host=localhost;Database=hiposim")]
    [InlineData("https://db.internal/hiposim_db")]
    [InlineData("postgres://db.internal/hiposim_db")]
    [InlineData("postgres://hiposim:s3cret@db.internal")]
    public void Invalid_url_is_rejected(string url)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseUrl.ToConnectionString(url));
    }
}
