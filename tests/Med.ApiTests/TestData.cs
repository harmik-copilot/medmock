using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Med.ApiTests;

public sealed record CreatedStudy(string Id, string Uid, string PatientName, string PatientId, string ReferrerId);

public sealed record StudyOptions(string ReferrerId, string PatientId)
{
    /// <summary>Which clinic pushes the study: clinic.admin (ANZ) or clinic.eu (EUR).</summary>
    public string ProviderUser { get; init; } = "clinic.admin";
    public string Modality { get; init; } = "DX";
    public string BodyPart { get; init; } = "CHEST";
    public int SeriesCount { get; init; } = 1;
    public string? StudyDate { get; init; }   // yyyy-MM-dd, default yesterday
}

/// <summary>
/// Test data rules for shared environments:
///  * every test creates the data it needs, through the same API a clinic connector uses;
///  * everything is clearly synthetic: patient names start with QA^ and carry a run id;
///  * identifiers are unique per run, so parallel runs (and other testers) never collide;
///  * assertions check that a study IS or IS NOT visible, never "exactly N studies in total".
/// </summary>
public static class TestData
{
    private static readonly string RunId = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
    private static readonly long RunNumber = Random.Shared.NextInt64(100_000_000, 999_999_999);
    private static int _counter;

    public static string DaysFromToday(int days) =>
        DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static (Dictionary<string, object> Payload, string Uid, string PatientName) NewStudyPayload(StudyOptions options)
    {
        var n = Interlocked.Increment(ref _counter);
        var uid = $"1.2.999.9.{RunNumber}.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{n}";
        var patientName = $"QA^{RunId}{n:D3}";

        var payload = new Dictionary<string, object>
        {
            ["studyInstanceUid"] = uid,
            ["patientId"] = options.PatientId,
            ["patientName"] = patientName,
            ["modality"] = options.Modality,
            ["bodyPart"] = options.BodyPart,
            ["studyDate"] = options.StudyDate ?? DaysFromToday(-1),
            ["seriesCount"] = options.SeriesCount,
            ["referrerId"] = options.ReferrerId,
            ["pixelsBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes($"synthetic-pixels-{uid}")),
        };
        return (payload, uid, patientName);
    }

    /// <summary>Pushes studies as a clinic would. Returns the raw result so tests can check any outcome.</summary>
    public static async Task<ApiResult> PushStudiesAsync(this ApiFixture api, string providerUser, IEnumerable<object> studies)
    {
        var token = await api.LoginAsync(providerUser);
        return await api.Post("/ingest/studies", token, new { studies });
    }

    /// <summary>Creates one synthetic study and returns its id. Fails loudly if the data was not accepted.</summary>
    public static async Task<CreatedStudy> CreateStudyAsync(this ApiFixture api, StudyOptions options)
    {
        var (payload, uid, patientName) = NewStudyPayload(options);

        var result = await api.PushStudiesAsync(options.ProviderUser, new object[] { payload });

        Assert.True(result.Status == 200, $"Creating test data failed: HTTP {result.Status} {result.Body}");
        var summary = result.Json();
        Assert.True(summary.GetProperty("accepted").GetInt32() == 1, $"Test data was rejected: {result.Body}");
        var id = summary.GetProperty("acceptedStudyIds")[0].GetString()!;
        return new CreatedStudy(id, uid, patientName, options.PatientId, options.ReferrerId);
    }
}
