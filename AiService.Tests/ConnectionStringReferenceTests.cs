using AiService.Services;
using Microsoft.Extensions.Configuration;

namespace AiService.Tests;

/// <summary>
/// Ссылка ${ИМЯ} на переменную окружения в Database:ConnectionString.
/// </summary>
public class ConnectionStringReferenceTests
{
    private static IConfiguration ConfigWith(string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = value })
            .Build();

    [Fact]
    public void RawString_Passthrough() =>
        Assert.Equal("Server=x;Database=y",
            ConnectionStringReference.Resolve(ConfigWith("Server=x;Database=y")));

    [Fact]
    public void Empty_Passthrough() =>
        Assert.Equal("", ConnectionStringReference.Resolve(ConfigWith("")));

    [Fact]
    public void DollarInsideRaw_Passthrough() =>
        Assert.Equal("Server=x;Password=a$b",
            ConnectionStringReference.Resolve(ConfigWith("Server=x;Password=a$b")));

    [Fact]
    public void Reference_ResolvesFromEnvironment()
    {
        Environment.SetEnvironmentVariable("CNCJ_TEST_REF_A", "Server=env;Database=db");
        try
        {
            Assert.Equal("Server=env;Database=db",
                ConnectionStringReference.Resolve(ConfigWith("${CNCJ_TEST_REF_A}")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CNCJ_TEST_REF_A", null);
        }
    }

    [Fact]
    public void Reference_MissingVariable_Empty()
    {
        Environment.SetEnvironmentVariable("CNCJ_TEST_REF_MISSING_XYZ", null);
        Assert.Equal("",
            ConnectionStringReference.Resolve(ConfigWith("${CNCJ_TEST_REF_MISSING_XYZ}")));
    }

    [Fact]
    public void Reference_EmptyName_Empty() =>
        Assert.Equal("", ConnectionStringReference.Resolve(ConfigWith("${}")));
}
