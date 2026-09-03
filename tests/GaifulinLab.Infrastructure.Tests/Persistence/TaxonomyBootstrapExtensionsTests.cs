using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Infrastructure.Tests.Persistence;

public sealed class TaxonomyBootstrapExtensionsTests
{
    [Fact]
    public async Task SeedTaxonomyAsync_CreatesConfiguredTopicsOnlyOnce()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TAXONOMY_BOOTSTRAP_TOPICS_JSON"] =
                    """
                    [
                      { "languageCode": "en", "name": "Signal Processing", "slug": "signal-processing", "description": "Digital signal processing." },
                      { "languageCode": "ru", "name": "Сигналы", "slug": "signaly", "description": null }
                    ]
                    """
            })
            .Build();
        Assert.False(string.IsNullOrWhiteSpace(configuration["TAXONOMY_BOOTSTRAP_TOPICS_JSON"]));

        var databaseName = $"taxonomy-bootstrap-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        await using var provider = services.BuildServiceProvider();

        await provider.SeedTaxonomyAsync(configuration);
        await provider.SeedTaxonomyAsync(configuration);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var localizations = await dbContext.TopicLocalizations
            .OrderBy(localization => localization.LanguageCode)
            .ToListAsync();

        Assert.Equal(2, localizations.Count);
        Assert.Equal("Signal Processing", localizations[0].Name);
        Assert.Equal("signal-processing", localizations[0].Slug);
        Assert.Equal("Сигналы", localizations[1].Name);
        Assert.Equal("signaly", localizations[1].Slug);
        Assert.Equal(2, await dbContext.Set<Topic>().CountAsync());
    }

    [Fact]
    public async Task SeedTaxonomyAsync_WithInvalidJson_FailsBeforeWritingData()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TAXONOMY_BOOTSTRAP_TOPICS_JSON"] = "not-json"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"taxonomy-bootstrap-{Guid.NewGuid():N}"));
        await using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SeedTaxonomyAsync(configuration));

        Assert.Contains("TAXONOMY_BOOTSTRAP_TOPICS_JSON", exception.Message);
    }
}
