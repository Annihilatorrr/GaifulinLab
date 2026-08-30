using Xunit;

namespace GaifulinLab.E2E.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2ECollection : ICollectionFixture<E2EEnvironment>
{
    public const string Name = "GaifulinLab E2E";
}
