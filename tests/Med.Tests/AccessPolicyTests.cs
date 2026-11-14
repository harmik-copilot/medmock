using Xunit;
using Med.Domain;

namespace Med.Tests;

/// <summary>Unit level: the permission rules, exhaustively. Cheap, fast, highest risk.</summary>
public class AccessPolicyTests
{
    private static readonly Study AnzStudy = new(
        Guid.NewGuid(), "1.2.999.1", "P-1", "EXAMPLE^SAM", "DX", "CHEST",
        new DateOnly(2026, 11, 5), 1, Region.ANZ, "prov-001", "ref-001", "abc", 10);

    private readonly AccessPolicy _policy = new();

    [Fact]
    public void Referrer_of_record_is_allowed()
    {
        var user = new AppUser("ref-001", "Dr Lee", Role.Referrer, Region.ANZ);
        Assert.True(_policy.Evaluate(user, AnzStudy).Allowed);
    }

    [Fact]
    public void Other_referrer_is_denied()
    {
        var user = new AppUser("ref-002", "Dr Patel", Role.Referrer, Region.ANZ);
        Assert.False(_policy.Evaluate(user, AnzStudy).Allowed);
    }

    [Fact]
    public void Patient_can_see_own_study_only()
    {
        var own = new AppUser("pat-1", "Sam", Role.Patient, Region.ANZ, PatientId: "P-1");
        var other = new AppUser("pat-2", "Chris", Role.Patient, Region.ANZ, PatientId: "P-2");
        Assert.True(_policy.Evaluate(own, AnzStudy).Allowed);
        Assert.False(_policy.Evaluate(other, AnzStudy).Allowed);
    }

    [Fact]
    public void Patient_without_a_patient_id_is_denied()
    {
        var user = new AppUser("pat-x", "Nobody", Role.Patient, Region.ANZ);
        Assert.False(_policy.Evaluate(user, AnzStudy).Allowed);
    }

    [Fact]
    public void Region_mismatch_is_denied_even_for_the_referrer_of_record()
    {
        var user = new AppUser("ref-001", "Dr Lee", Role.Referrer, Region.EUR);
        var decision = _policy.Evaluate(user, AnzStudy);
        Assert.False(decision.Allowed);
        Assert.Contains("region", decision.Reason);
    }

    [Fact]
    public void Provider_sees_only_its_own_studies()
    {
        var own = new AppUser("u1", "Admin", Role.Provider, Region.ANZ, ProviderId: "prov-001");
        var other = new AppUser("u2", "Admin", Role.Provider, Region.ANZ, ProviderId: "prov-999");
        Assert.True(_policy.Evaluate(own, AnzStudy).Allowed);
        Assert.False(_policy.Evaluate(other, AnzStudy).Allowed);
    }
}
