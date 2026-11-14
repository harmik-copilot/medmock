using Xunit;

namespace Med.ApiTests;

/// <summary>ACQUIRE: how studies get in. These create data, so they are skipped on read-only environments.</summary>
[Collection("api")]
public class IngestApiTests
{
    private readonly ApiFixture _api;
    public IngestApiTests(ApiFixture api) => _api = api;

    private static object[] OneStudy(StudyOptions? options = null) =>
        new object[] { TestData.NewStudyPayload(options ?? new StudyOptions("ref-001", "P-1001")).Payload };

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Pushing_studies_requires_authentication()
    {
        var result = await _api.Post("/ingest/studies", body: new { studies = OneStudy() });
        Assert.Equal(401, result.Status);
    }

    [MutatingTheory]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    [InlineData("dr.lee")]
    [InlineData("sam")]
    [InlineData("dr.novak")]
    public async Task Only_providers_can_ingest_on_both_endpoints(string username)
    {
        var token = await _api.LoginAsync(username);

        var push = await _api.Post("/ingest/studies", token, new { studies = OneStudy() });
        var pull = await _api.Post("/ingest/run", token);

        Assert.Equal(403, push.Status);
        Assert.Equal(403, pull.Status);
    }

    [MutatingFact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task A_pushed_study_is_accepted_gets_an_id_and_the_referrer_can_open_it()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var lee = await _api.LoginAsync("dr.lee");

        var result = await _api.Get($"/studies/{study.Id}", lee);

        Assert.Equal(200, result.Status);
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task Pushing_the_same_study_twice_is_reported_as_a_duplicate()
    {
        var studies = OneStudy();

        var first = (await _api.PushStudiesAsync("clinic.admin", studies)).Json();
        var second = (await _api.PushStudiesAsync("clinic.admin", studies)).Json();

        Assert.Equal(1, first.GetProperty("accepted").GetInt32());
        Assert.Equal(0, second.GetProperty("accepted").GetInt32());
        Assert.Equal(1, second.GetProperty("duplicates").GetInt32());
        Assert.Equal(0, second.GetProperty("acceptedStudyIds").GetArrayLength());
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task An_unsupported_modality_is_rejected_with_a_reason_and_does_not_block_the_rest_of_the_batch()
    {
        var bad = TestData.NewStudyPayload(new StudyOptions("ref-001", "P-1001") { Modality = "XX" }).Payload;
        var good = TestData.NewStudyPayload(new StudyOptions("ref-001", "P-1001")).Payload;

        var result = await _api.PushStudiesAsync("clinic.admin", new object[] { bad, good });

        Assert.Equal(200, result.Status);
        var summary = result.Json();
        Assert.Equal(1, summary.GetProperty("accepted").GetInt32());
        Assert.Equal(1, summary.GetProperty("rejected").GetArrayLength());
        Assert.Contains("modality", summary.GetProperty("rejected")[0].GetProperty("reason").GetString());
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task A_study_dated_in_the_future_is_rejected()
    {
        var future = OneStudy(new StudyOptions("ref-001", "P-1001") { StudyDate = TestData.DaysFromToday(3) });

        var summary = (await _api.PushStudiesAsync("clinic.admin", future)).Json();

        Assert.Equal(0, summary.GetProperty("accepted").GetInt32());
        Assert.Contains("future", summary.GetProperty("rejected")[0].GetProperty("reason").GetString());
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task Image_data_that_is_not_valid_base64_returns_400()
    {
        var payload = TestData.NewStudyPayload(new StudyOptions("ref-001", "P-1001")).Payload;
        payload["pixelsBase64"] = "***not base64***";

        var result = await _api.PushStudiesAsync("clinic.admin", new object[] { payload });

        Assert.Equal(400, result.Status);
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task An_empty_batch_returns_400()
    {
        var result = await _api.PushStudiesAsync("clinic.admin", Array.Empty<object>());
        Assert.Equal(400, result.Status);
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task A_batch_of_more_than_100_studies_returns_400()
    {
        var studies = Enumerable.Range(0, 101)
            .Select(_ => (object)TestData.NewStudyPayload(new StudyOptions("ref-001", "P-1001")).Payload)
            .ToList();

        var result = await _api.PushStudiesAsync("clinic.admin", studies);

        Assert.Equal(400, result.Status);
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    public async Task Pulling_from_the_clinic_PACS_twice_adds_nothing_the_second_time()
    {
        var admin = await _api.LoginAsync("clinic.admin");

        var first = await _api.Post("/ingest/run", admin);
        var second = await _api.Post("/ingest/run", admin);

        Assert.Equal(200, first.Status);
        Assert.Equal(200, second.Status);
        Assert.Equal(0, second.Json().GetProperty("accepted").GetInt32());
    }
}
