using Xunit;

namespace Med.ApiTests;

/// <summary>Is it up, and is it the environment we think it is?</summary>
[Collection("api")]
public class HealthTests
{
    private readonly ApiFixture _api;
    public HealthTests(ApiFixture api) => _api = api;

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task Live_endpoint_returns_200()
    {
        var result = await _api.Get("/health/live");
        Assert.Equal(200, result.Status);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task Ready_endpoint_returns_200()
    {
        var result = await _api.Get("/health/ready");
        Assert.Equal(200, result.Status);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task Version_reports_the_environment_these_tests_expect()
    {
        var result = await _api.Get("/version");

        Assert.Equal(200, result.Status);
        var body = result.Json();
        var actual = body.GetProperty("environment").GetString();
        Assert.True(actual == TestEnvironment.Current.ExpectedAspNetEnvironment,
            $"Tests are pointed at '{TestEnvironment.Current.Name}' but the API says it is '{actual}'. Wrong URL?");
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
    }
}
