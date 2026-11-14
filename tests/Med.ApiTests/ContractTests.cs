using System.Text.RegularExpressions;
using Xunit;

namespace Med.ApiTests;

/// <summary>
/// The "shape" of responses and headers. A contract change silently breaks the apps (viewer, mobile)
/// that depend on it, and missing security headers are easy to overlook.
/// </summary>
[Collection("api")]
public class ContractTests
{
    private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,64}$");

    private readonly ApiFixture _api;
    public ContractTests(ApiFixture api) => _api = api;

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task Study_list_is_json_and_each_item_has_the_agreed_fields()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var token = await _api.LoginAsync("dr.lee");

        var result = await _api.Get("/studies", token);

        Assert.Equal(200, result.Status);
        Assert.Contains("application/json", result.Headers["content-type"]);
        var items = result.Json().EnumerateArray().ToList();
        Assert.Contains(items, i => string.Equals(i.GetProperty("id").GetString(), study.Id, StringComparison.OrdinalIgnoreCase));
        foreach (var item in items)
            foreach (var field in new[] { "id", "patientName", "modality", "bodyPart", "studyDate" })
                Assert.True(item.TryGetProperty(field, out _), $"Missing field '{field}' in study list item.");
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Study_list_does_not_expose_internal_fields()
    {
        await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var token = await _api.LoginAsync("dr.lee");

        var result = await _api.Get("/studies", token);

        foreach (var item in result.Json().EnumerateArray())
            foreach (var field in new[] { "checksum", "providerId", "referrerId", "studyInstanceUid" })
                Assert.False(item.TryGetProperty(field, out _), $"Internal field '{field}' should not be in the list.");
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Study_detail_has_the_agreed_fields_and_hides_internal_ones()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001") { Modality = "DX", BodyPart = "CHEST" });
        var sam = await _api.LoginAsync("sam");

        var result = await _api.Get($"/studies/{study.Id}", sam);

        Assert.Equal(200, result.Status);
        var detail = result.Json();
        foreach (var field in new[] { "id", "studyInstanceUid", "patientId", "patientName", "modality",
                                       "bodyPart", "studyDate", "seriesCount", "sizeBytes", "region" })
            Assert.True(detail.TryGetProperty(field, out _), $"Missing field '{field}' in study detail.");

        foreach (var field in new[] { "checksum", "providerId", "referrerId" })
            Assert.False(detail.TryGetProperty(field, out _), $"Internal field '{field}' should not be in the detail.");

        Assert.Equal("ANZ", detail.GetProperty("region").GetString());
        Assert.Equal("DX", detail.GetProperty("modality").GetString());
        Assert.Equal(study.PatientName, detail.GetProperty("patientName").GetString());
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task Every_response_carries_a_correlation_id()
    {
        var result = await _api.Get("/studies");   // a 401, which must still carry one
        Assert.Matches(SafeId, result.Headers["x-correlation-id"]);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task A_supplied_correlation_id_is_echoed_back()
    {
        var result = await _api.Get("/health/live", headers: new Dictionary<string, string> { ["X-Correlation-ID"] = "qa-trace-12345" });
        Assert.Equal("qa-trace-12345", result.Headers["x-correlation-id"]);
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task An_unsafe_correlation_id_is_replaced_not_echoed()
    {
        var result = await _api.Get("/health/live",
            headers: new Dictionary<string, string> { ["X-Correlation-ID"] = "bad id with spaces & <tags>" });

        var returned = result.Headers["x-correlation-id"];
        Assert.DoesNotContain("<", returned);
        Assert.Matches(SafeId, returned);
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Responses_are_sent_with_nosniff()
    {
        var result = await _api.Get("/health/live");
        Assert.Equal("nosniff", result.Headers["x-content-type-options"]);
    }
}
