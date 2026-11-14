using Med.Domain;

namespace Med.Api;

public sealed class ProviderDirectory
{
    private readonly Dictionary<string, Provider> _providers = new()
    {
        ["prov-001"]  = new Provider("prov-001",  "Example Radiology (ANZ)", Region.ANZ),
        ["prov-eu1"]  = new Provider("prov-eu1",  "Example Imaging (EU)",    Region.EUR),
    };

    public Provider? Find(string id) => _providers.TryGetValue(id, out var p) ? p : null;
    public IEnumerable<Provider> All() => _providers.Values;
}
