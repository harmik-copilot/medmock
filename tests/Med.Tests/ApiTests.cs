using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Med.Tests;

public sealed record LoginResponse(string Token);
public sealed record StudySummaryDto(Guid Id, string PatientName, string Modality, string BodyPart);
public sealed record IngestResponse(int Accepted, int Duplicates);
public sealed record AuditDto(string UserId, string Action, Guid? StudyId, bool Allowed);

/// <summary>
/// End-to-end through the HTTP API, with the seeded synthetic data
/// (Sam's chest X-ray under Dr Lee, Chris Manter's knee MRI under Dr Patel, one EU CT).
/// Tests are written to be independent of run order.
/// </summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<HttpClient> ClientFor(string username, string password = "test-pass")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private static async Task<List<StudySummaryDto>> ListStudies(HttpClient client) =>
        (await client.GetFromJsonAsync<List<StudySummaryDto>>("/studies"))!;

    // ---------- authentication ----------

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { username = "dr.lee", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listing_studies_without_a_token_returns_401()
    {
        var response = await _factory.CreateClient().GetAsync("/studies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_made_up_token_is_rejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/studies")).StatusCode);
    }

    // ---------- permissions: the highest-risk area ----------

    [Fact]
    public async Task Referrer_sees_only_their_own_studies()
    {
        var studies = await ListStudies(await ClientFor("dr.lee"));
        var only = Assert.Single(studies);
        Assert.Equal("EXAMPLE^SAM", only.PatientName);
    }

    [Fact]
    public async Task Referrer_can_open_their_own_study()
    {
        var client = await ClientFor("dr.lee");
        var id = (await ListStudies(client)).Single().Id;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/studies/{id}")).StatusCode);
    }

    [Fact]
    public async Task Referrer_cannot_open_another_referrers_study_and_no_data_leaks()
    {
        var samStudyId = (await ListStudies(await ClientFor("dr.lee"))).Single().Id;
        var patel = await ClientFor("dr.patel");

        var response = await patel.GetAsync($"/studies/{samStudyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SAM", body);
        Assert.DoesNotContain("CHEST", body);
    }

    [Fact]
    public async Task Patient_can_open_own_study_but_not_another_patients()
    {
        var sam = await ClientFor("sam");
        var chris = await ClientFor("chris");
        var samStudyId = (await ListStudies(sam)).Single().Id;

        Assert.Equal(HttpStatusCode.OK, (await sam.GetAsync($"/studies/{samStudyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await chris.GetAsync($"/studies/{samStudyId}")).StatusCode);
    }

    [Fact]
    public async Task Chris_sees_only_the_knee_mri()
    {
        var studies = await ListStudies(await ClientFor("chris"));
        var only = Assert.Single(studies);
        Assert.Equal("MR", only.Modality);
        Assert.Equal("KNEE", only.BodyPart);
    }

    [Fact]
    public async Task Unknown_study_id_returns_404()
    {
        var client = await ClientFor("dr.lee");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/studies/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Malformed_study_id_does_not_return_500()
    {
        var client = await ClientFor("dr.lee");
        var status = (await client.GetAsync("/studies/not-a-guid")).StatusCode;
        Assert.NotEqual(HttpStatusCode.InternalServerError, status);
    }

    // ---------- data residency ----------

    [Fact]
    public async Task European_referrer_cannot_see_or_open_ANZ_studies()
    {
        var anzStudyId = (await ListStudies(await ClientFor("dr.lee"))).Single().Id;
        var novak = await ClientFor("dr.novak");

        var visible = await ListStudies(novak);
        var only = Assert.Single(visible);
        Assert.Equal("CT", only.Modality);
        Assert.Equal(HttpStatusCode.Forbidden, (await novak.GetAsync($"/studies/{anzStudyId}")).StatusCode);
    }

    // ---------- acquire / ingest ----------

    [Theory]
    [InlineData("dr.lee")]
    [InlineData("sam")]
    public async Task Only_providers_can_trigger_ingest(string username)
    {
        var client = await ClientFor(username);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/ingest/run", null)).StatusCode);
    }

    [Fact]
    public async Task Provider_ingest_rerun_adds_nothing_new()
    {
        var admin = await ClientFor("clinic.admin");
        var response = await admin.PostAsync("/ingest/run", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<IngestResponse>();
        Assert.Equal(0, summary!.Accepted);
        Assert.True(summary.Duplicates >= 1);
    }

    // ---------- audit ----------

    [Fact]
    public async Task Denied_access_attempts_are_audited_for_the_owning_provider()
    {
        var samStudyId = (await ListStudies(await ClientFor("dr.lee"))).Single().Id;
        await (await ClientFor("dr.patel")).GetAsync($"/studies/{samStudyId}");   // denied attempt

        var audit = await (await ClientFor("clinic.admin"))
            .GetFromJsonAsync<List<AuditDto>>("/audit");

        Assert.Contains(audit!, e => e.UserId == "ref-002" && e.Action == "OPEN_STUDY"
                                     && e.StudyId == samStudyId && !e.Allowed);
    }

    [Fact]
    public async Task Another_providers_audit_view_does_not_include_these_studies()
    {
        var samStudyId = (await ListStudies(await ClientFor("dr.lee"))).Single().Id;
        await (await ClientFor("dr.patel")).GetAsync($"/studies/{samStudyId}");

        var euAudit = await (await ClientFor("clinic.eu"))
            .GetFromJsonAsync<List<AuditDto>>("/audit");

        Assert.DoesNotContain(euAudit!, e => e.StudyId == samStudyId);
    }
}
