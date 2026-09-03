using System.Text.Json;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Topics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Infrastructure.Persistence;

public static class TaxonomyBootstrapExtensions
{
    public static async Task SeedTaxonomyAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var topics = ReadTopics(configuration);
        if (topics.Count == 0)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        foreach (var topic in topics)
        {
            var exists = await dbContext.TopicLocalizations.AnyAsync(
                localization => localization.LanguageCode == topic.LanguageCode
                    && localization.Slug == topic.Slug,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            var entity = Topic.Create(
                topic.LanguageCode,
                topic.Name,
                topic.Slug,
                topic.Description,
                now);
            dbContext.Topics.Add(entity);
            dbContext.TopicLocalizations.AddRange(entity.Localizations);
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<BootstrapTopic> ReadTopics(IConfiguration configuration)
    {
        var json = configuration["TAXONOMY_BOOTSTRAP_TOPICS_JSON"]
            ?? configuration["Taxonomy:BootstrapTopicsJson"];
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        BootstrapTopicConfiguration[] definitions;
        try
        {
            definitions = JsonSerializer.Deserialize<BootstrapTopicConfiguration[]>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "TAXONOMY_BOOTSTRAP_TOPICS_JSON must contain a JSON array of topics.",
                exception);
        }

        var topics = new List<BootstrapTopic>(definitions.Length);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var languageCode = DomainRules.NormalizeLanguageCode(definition.LanguageCode ?? string.Empty);
            var name = DomainRules.RequireTrimmed(definition.Name ?? string.Empty, "name");
            var slug = DomainRules.NormalizeSlug(definition.Slug ?? string.Empty);
            var key = $"{languageCode}:{slug}";
            if (!keys.Add(key))
            {
                throw new InvalidOperationException(
                    $"TAXONOMY_BOOTSTRAP_TOPICS_JSON contains duplicate topic '{key}'.");
            }

            topics.Add(new BootstrapTopic(
                languageCode,
                name,
                slug,
                string.IsNullOrWhiteSpace(definition.Description)
                    ? null
                    : definition.Description.Trim()));
        }

        return topics;
    }

    private sealed record BootstrapTopicConfiguration(
        string? LanguageCode,
        string? Name,
        string? Slug,
        string? Description);

    private sealed record BootstrapTopic(
        string LanguageCode,
        string Name,
        string Slug,
        string? Description);
}
