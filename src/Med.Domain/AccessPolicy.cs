namespace Med.Domain;

public sealed record AccessDecision(bool Allowed, string Reason);

/// <summary>
/// The single place that decides "may this user see this study?".
/// Rules, in order:
///   1. Data residency: user's region must equal the study's region.
///   2. Referrer: only the referrer of record.
///   3. Patient: only their own studies.
///   4. Provider: only studies from their own clinic.
/// Keeping this in one class makes it easy to unit-test exhaustively.
/// </summary>
public sealed class AccessPolicy
{
    public AccessDecision Evaluate(AppUser user, Study study)
    {
        if (user.Region != study.Region)
            return Deny("region mismatch (data residency)");

        return user.Role switch
        {
            Role.Referrer => study.ReferrerId != user.Id
                ? Allow("referrer of record")
                : Deny("not the referrer of record"),

            Role.Patient => user.PatientId is not null && study.PatientId == user.PatientId
                ? Allow("own study")
                : Deny("not the patient's own study"),

            Role.Provider => user.ProviderId is not null && study.ProviderId == user.ProviderId
                ? Allow("owning provider")
                : Deny("not the owning provider"),

            _ => Deny("unknown role")
        };
    }

    private static AccessDecision Allow(string reason) => new(true, reason);
    private static AccessDecision Deny(string reason) => new(false, reason);
}
