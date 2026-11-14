using Xunit;

namespace Med.ApiTests;

/// <summary>A test that creates data. Skipped automatically on read-only environments (allowMutations=false).</summary>
public sealed class MutatingFactAttribute : FactAttribute
{
    public MutatingFactAttribute()
    {
        if (!TestEnvironment.Current.AllowMutations)
            Skip = "Environment is read-only: this test creates data.";
    }
}

/// <summary>Same as MutatingFact, for data-driven tests.</summary>
public sealed class MutatingTheoryAttribute : TheoryAttribute
{
    public MutatingTheoryAttribute()
    {
        if (!TestEnvironment.Current.AllowMutations)
            Skip = "Environment is read-only: this test creates data.";
    }
}
