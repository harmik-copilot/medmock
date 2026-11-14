using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Med.ApiTests;

/// <summary>What came back from the API: status code, raw body text, and headers.</summary>
public sealed record ApiResult(int Status, string Body, IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>Parses the body as JSON (fails the test with a clear error if it is not JSON).</summary>
    public JsonElement Json()
    {
        using var doc = JsonDocument.Parse(Body);
        return doc.RootElement.Clone();
    }
}

/// <summary>
/// Created once and shared by every test. Owns the Playwright instance and offers small helpers:
/// Get / Post / LoginAsync. Each request uses a fresh Playwright "API request context",
/// which is Playwright's equivalent of a tiny HTTP client (like one Postman request).
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public string BaseUrl { get; } = TestEnvironment.Current.BaseUrl;

    private IPlaywright _playwright = null!;

    // Logging in once per account keeps shared environments quiet.
    private readonly ConcurrentDictionary<string, string> _tokens = new();

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();

        // Fail fast, with a helpful message, if the API is not reachable.
        try
        {
            await Post("/auth/login", body: new { username = "probe", password = "probe" });
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach the API at {BaseUrl} (TARGET_ENV={TestEnvironment.Current.Name}). " +
                "Locally, start it with: dotnet run --project src/Med.Api   or run: bash scripts/run-api-tests.sh", ex);
        }
    }

    public Task DisposeAsync()
    {
        _playwright.Dispose();
        return Task.CompletedTask;
    }

    public Task<ApiResult> Get(string path, string? token = null, IDictionary<string, string>? headers = null) =>
        Send("GET", path, token, null, headers);

    public Task<ApiResult> Post(string path, string? token = null, object? body = null) =>
        Send("POST", path, token, body, null);

    private async Task<ApiResult> Send(
        string method, string path, string? token, object? body, IDictionary<string, string>? extraHeaders)
    {
        var headers = new Dictionary<string, string>();
        if (extraHeaders is not null)
            foreach (var pair in extraHeaders) headers[pair.Key] = pair.Value;
        if (token is not null) headers["Authorization"] = $"Bearer {token}";

        var context = await _playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = headers,
        });

        try
        {
            IAPIResponse response;
            if (method == "GET")
                response = await context.GetAsync(path);
            else if (body is null)
                response = await context.PostAsync(path);
            else
                response = await context.PostAsync(path, new() { DataObject = body });

            // Read everything BEFORE the context is disposed.
            var text = await response.TextAsync();
            var responseHeaders = response.Headers.ToDictionary(
                h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);
            return new ApiResult(response.Status, text, responseHeaders);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    /// <summary>Logs in as one of the QA accounts (or with an explicit password) and returns the token.</summary>
    public async Task<string> LoginAsync(string username, string? password = null)
    {
        if (password is null && _tokens.TryGetValue(username, out var cached)) return cached;

        var result = await Post("/auth/login",
            body: new { username, password = password ?? TestEnvironment.QaPassword });
        Assert.True(result.Status == 200,
            $"Login as '{username}' failed with HTTP {result.Status}: {result.Body}");

        var token = result.Json().GetProperty("token").GetString()!;
        if (password is null) _tokens[username] = token;
        return token;
    }

    /// <summary>The ids (case-insensitive) of every study this token may see.</summary>
    public async Task<HashSet<string>> VisibleStudyIdsAsync(string token)
    {
        var list = await Get("/studies", token);
        Assert.True(list.Status == 200, $"Listing studies returned HTTP {list.Status}");
        return list.Json().EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture> { }
