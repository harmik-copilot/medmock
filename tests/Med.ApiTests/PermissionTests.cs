using Xunit;

namespace Med.ApiTests;

/// <summary>
/// May you see this? (403 territory). The highest-risk area: the wrong person seeing a scan.
/// Every test creates its own synthetic studies, so these run safely on a shared environment.
/// QA accounts: dr.lee=ref-001, dr.patel=ref-002 (ANZ), dr.novak=ref-eu-1 (EUR),
///              sam=P-1001, chris=P-1002, clinic.admin (ANZ clinic), clinic.eu (EU clinic).
/// </summary>
[Collection("api")]
public class PermissionTests
{
    private readonly ApiFixture _api;
    public PermissionTests(ApiFixture api) => _api = api;

    [MutatingFact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Referrer_sees_the_studies_they_referred_and_not_other_referrers()
    {
        var mine = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var theirs = await _api.CreateStudyAsync(new StudyOptions("ref-002", "P-1002"));

        var ids = await _api.VisibleStudyIdsAsync(await _api.LoginAsync("dr.lee"));

        Assert.Contains(mine.Id, ids);
        Assert.DoesNotContain(theirs.Id, ids);
    }

    [MutatingFact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    public async Task Referrer_can_open_their_own_study()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var lee = await _api.LoginAsync("dr.lee");

        var result = await _api.Get($"/studies/{study.Id}", lee);

        Assert.Equal(200, result.Status);
        Assert.Equal(study.PatientName, result.Json().GetProperty("patientName").GetString());
    }

    [MutatingFact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Another_referrer_gets_403_with_no_patient_data_in_the_response()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var patel = await _api.LoginAsync("dr.patel");

        var result = await _api.Get($"/studies/{study.Id}", patel);

        Assert.Equal(403, result.Status);
        Assert.DoesNotContain(study.PatientName, result.Body);
        Assert.DoesNotContain("CHEST", result.Body);
        Assert.True(string.IsNullOrWhiteSpace(result.Body), "A 403 should not explain itself in the body.");
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Patient_sees_and_opens_their_own_study_but_not_another_patients()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var sam = await _api.LoginAsync("sam");
        var chris = await _api.LoginAsync("chris");

        Assert.Contains(study.Id, await _api.VisibleStudyIdsAsync(sam));
        Assert.DoesNotContain(study.Id, await _api.VisibleStudyIdsAsync(chris));
        Assert.Equal(200, (await _api.Get($"/studies/{study.Id}", sam)).Status);
        Assert.Equal(403, (await _api.Get($"/studies/{study.Id}", chris)).Status);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task Unknown_study_id_returns_404()
    {
        var lee = await _api.LoginAsync("dr.lee");
        var result = await _api.Get($"/studies/{Guid.NewGuid()}", lee);
        Assert.Equal(404, result.Status);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task Malformed_study_id_never_causes_a_server_error()
    {
        var lee = await _api.LoginAsync("dr.lee");
        var result = await _api.Get("/studies/not-a-guid", lee);
        Assert.True(result.Status < 500, $"Expected a client error, got HTTP {result.Status}");
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Data_residency_a_European_referrer_and_an_ANZ_referrer_never_see_each_others_studies()
    {
        var anz = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var eu = await _api.CreateStudyAsync(new StudyOptions("ref-eu-1", "P-2001")
            { ProviderUser = "clinic.eu", Modality = "CT", BodyPart = "HEAD" });
        var novak = await _api.LoginAsync("dr.novak");
        var lee = await _api.LoginAsync("dr.lee");

        var novakIds = await _api.VisibleStudyIdsAsync(novak);
        Assert.Contains(eu.Id, novakIds);
        Assert.DoesNotContain(anz.Id, novakIds);
        Assert.DoesNotContain(eu.Id, await _api.VisibleStudyIdsAsync(lee));

        Assert.Equal(403, (await _api.Get($"/studies/{anz.Id}", novak)).Status);
        Assert.Equal(403, (await _api.Get($"/studies/{eu.Id}", lee)).Status);
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task Data_residency_beats_referrer_of_record_a_European_GP_who_referred_to_an_ANZ_clinic_is_still_denied()
    {
        // A cross-border referral. Role rules alone would allow it (Dr Novak IS the referrer of record),
        // so only the region rule stops it. Without this test, deleting the region check goes unnoticed.
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-eu-1", "P-1001"));   // pushed by the ANZ clinic
        var novak = await _api.LoginAsync("dr.novak");

        Assert.DoesNotContain(study.Id, await _api.VisibleStudyIdsAsync(novak));
        Assert.Equal(403, (await _api.Get($"/studies/{study.Id}", novak)).Status);
    }

    [MutatingFact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Security")]
    public async Task A_provider_can_open_its_own_clinics_study_but_another_clinic_cannot()
    {
        var study = await _api.CreateStudyAsync(new StudyOptions("ref-001", "P-1001"));
        var owner = await _api.LoginAsync("clinic.admin");
        var other = await _api.LoginAsync("clinic.eu");

        Assert.Equal(200, (await _api.Get($"/studies/{study.Id}", owner)).Status);
        Assert.Equal(403, (await _api.Get($"/studies/{study.Id}", other)).Status);
    }
}
