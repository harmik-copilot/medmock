using Xunit;

namespace Med.ApiTests;

/// <summary>Who are you? (401 territory)</summary>
[Collection("api")]
public class AuthTests
{
    private readonly ApiFixture _api;
    public AuthTests(ApiFixture api) => _api = api;

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task Valid_login_returns_200_and_a_token()
    {
        var result = await _api.Post("/auth/login",
            body: new { username = "dr.lee", password = TestEnvironment.QaPassword });

        Assert.Equal(200, result.Status);
        var token = result.Json().GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Theory]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    [InlineData("dr.lee", "wrong-password")]
    [InlineData("nobody", "anything")]
    [InlineData("", "")]
    public async Task Bad_credentials_return_401(string username, string password)
    {
        var result = await _api.Post("/auth/login", body: new { username, password });
        Assert.Equal(401, result.Status);
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Login_failure_does_not_reveal_whether_the_username_exists()
    {
        var wrongPassword = await _api.Post("/auth/login", body: new { username = "dr.lee", password = "nope" });
        var unknownUser = await _api.Post("/auth/login", body: new { username = "nobody", password = "nope" });

        Assert.Equal(wrongPassword.Status, unknownUser.Status);
        Assert.Equal(wrongPassword.Body, unknownUser.Body);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Listing_studies_without_a_token_returns_401()
    {
        var result = await _api.Get("/studies");
        Assert.Equal(401, result.Status);
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task A_made_up_token_returns_401()
    {
        var result = await _api.Get("/studies", token: "not-a-real-token");
        Assert.Equal(401, result.Status);
    }
}
