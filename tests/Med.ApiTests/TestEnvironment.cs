using System.Text.Json;
using System.Text.RegularExpressions;

namespace Med.ApiTests;

/// <summary>One file per environment in tests/environments, shared with the TypeScript suite.</summary>
public sealed class EnvironmentConfig
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BaseUrl { get; set; } = "";

    /// <summary>false = read-only environment: tests that create data are skipped.</summary>
    public bool AllowMutations { get; set; }

    /// <summary>What GET /version should report, to prove we are testing the environment we think we are.</summary>
    public string ExpectedAspNetEnvironment { get; set; } = "";
}

/// <summary>
/// TARGET_ENV   local (default) | test | staging
/// MED_API_URL  optional override of the address in the environment file
/// MED_QA_PASSWORD  password of the pre-provisioned QA accounts. Secrets come from the environment, never the repo.
/// </summary>
public static class TestEnvironment
{
    public static EnvironmentConfig Current { get; } = Load();

    private static EnvironmentConfig Load()
    {
        var name = Environment.GetEnvironmentVariable("TARGET_ENV") ?? "local";
        if (!Regex.IsMatch(name, "^[a-z]+$"))
            throw new InvalidOperationException($"TARGET_ENV '{name}' is not valid. Use local, test or staging.");

        var file = Path.Combine(AppContext.BaseDirectory, "environments", $"{name}.json");
        if (!File.Exists(file))
            throw new InvalidOperationException($"Unknown TARGET_ENV '{name}': expected a file at {file}");

        var config = JsonSerializer.Deserialize<EnvironmentConfig>(
                         File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException($"Could not read {file}");

        var overrideUrl = Environment.GetEnvironmentVariable("MED_API_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl)) config.BaseUrl = overrideUrl;
        return config;
    }

    public static string QaPassword
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MED_QA_PASSWORD");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
            if (Current.Name == "local") return "test-pass";   // local development only
            throw new InvalidOperationException(
                $"MED_QA_PASSWORD must be set when TARGET_ENV is '{Current.Name}'.");
        }
    }
}
