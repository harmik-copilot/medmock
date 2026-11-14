using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Med.Domain;

namespace Med.Api;

public sealed record LoginRequest(string? Username, string? Password);

/// <summary>
/// Deliberately simple authentication so the access rules can be exercised end to end.
/// A real platform would use an identity provider, multi-factor authentication and short-lived tokens.
///
/// The accounts below stand in for QA accounts that are pre-provisioned in every environment.
/// They all share one password that is supplied by configuration (Auth:QaPassword), never hard-coded here.
/// </summary>
public sealed class AuthService
{
    private readonly Dictionary<string, (string Password, AppUser User)> _users;
    private readonly ConcurrentDictionary<string, AppUser> _tokens = new();

    public AuthService(string qaPassword)
    {
        _users = new()
        {
            ["dr.lee"]       = (qaPassword, new AppUser("ref-001",  "Dr Lee (GP)",       Role.Referrer, Region.ANZ)),
            ["dr.patel"]     = (qaPassword, new AppUser("ref-002",  "Dr Patel (GP)",     Role.Referrer, Region.ANZ)),
            ["dr.novak"]     = (qaPassword, new AppUser("ref-eu-1", "Dr Novak (GP, EU)", Role.Referrer, Region.EUR)),
            ["sam"]          = (qaPassword, new AppUser("pat-1001", "Sam Example",       Role.Patient,  Region.ANZ, PatientId: "P-1001")),
            ["chris"]        = (qaPassword, new AppUser("pat-1002", "Chris Manter",      Role.Patient,  Region.ANZ, PatientId: "P-1002")),
            ["clinic.admin"] = (qaPassword, new AppUser("user-prov-001", "ANZ Clinic Admin", Role.Provider, Region.ANZ, ProviderId: "prov-001")),
            ["clinic.eu"]    = (qaPassword, new AppUser("user-prov-eu1", "EU Clinic Admin",  Role.Provider, Region.EUR, ProviderId: "prov-eu1")),
        };
    }

    public string? Login(string? username, string? password)
    {
        if (username is null || password is null) return null;
        if (!_users.TryGetValue(username, out var record) || !Matches(record.Password, password)) return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _tokens[token] = record.User;
        return token;
    }

    public AppUser? Resolve(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var token = header[prefix.Length..].Trim();
        return _tokens.TryGetValue(token, out var user) ? user : null;
    }

    // Constant-time comparison so response timing does not leak how much of a password was right.
    private static bool Matches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
