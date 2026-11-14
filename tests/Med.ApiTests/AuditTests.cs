using System.Text.Json;
using Xunit;

namespace Med.ApiTests;

/// <summary>Can we investigate afterwards? Every access attempt should leave a trace.</summary>
[Collection("api")]
public class AuditTests
{
    private readonly ApiFixture _api;
    public AuditTests(ApiFixture api) => _api = api;

    private static bool HasEntry(JsonElement audit, string userId, string studyId, bool allowed) =>
        audit.EnumerateArray().Any(e =>
            e.GetProperty("userId").GetString() == userId
            && e.GetProperty("action").GetString() == "OPEN_STUDY"
            && string.Equals(e.GetProperty("studyId").GetString(), studyId, StringComparison.OrdinalIgnoreCase)
            && e.GetProperty("allowed").GetBoolean() == allowed);

    private async Task<JsonElement> AuditAsAsync(string username)
    {
        var token = await _api.LoginAsync(username);
        var result = await _api.Get("/audit", token);
        Assert.Equal(200, result.Status);
        return result.Json();
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Denied_access_is_recorded_for_the_owning_provider()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        await _api.Get($"/studies/{study.Id}", await _api.LoginAsync("dr.patel"));   // the denied attempt

        var audit = await AuditAsAsync("clinic.admin");

        Assert.True(HasEntry(audit, "ref-002", study.Id, allowed: false),
            "Expected a denied OPEN_STUDY entry for ref-002 in the audit trail.");
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task Allowed_access_is_recorded_too()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        await _api.Get($"/studies/{study.Id}", await _api.LoginAsync("dr.lee"));

        var audit = await AuditAsAsync("clinic.admin");

        Assert.True(HasEntry(audit, "ref-001", study.Id, allowed: true),
            "Expected an allowed OPEN_STUDY entry for ref-001 in the audit trail.");
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Another_providers_audit_view_does_not_include_this_study()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        await _api.Get($"/studies/{study.Id}", await _api.LoginAsync("dr.patel"));

        var audit = await AuditAsAsync("clinic.eu");

        var leaked = audit.EnumerateArray().Any(e =>
            string.Equals(e.GetProperty("studyId").GetString(), study.Id, StringComparison.OrdinalIgnoreCase));
        Assert.False(leaked, "The EU clinic must not see audit entries about an ANZ clinic study.");
    }

    [Theory]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    [InlineData("dr.lee")]
    [InlineData("sam")]
    public async Task Only_providers_can_read_the_audit_trail(string username)
    {
        var token = await _api.LoginAsync(username);
        var result = await _api.Get("/audit", token);
        Assert.Equal(403, result.Status);
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Audit_trail_without_a_token_returns_401()
    {
        var result = await _api.Get("/audit");
        Assert.Equal(401, result.Status);
    }
}
