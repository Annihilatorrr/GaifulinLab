using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Topics;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.CreateTopic;

internal sealed class CreateTopicCommandHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<CreateTopicCommand, AdminTopicDto>
{
    public async Task<AdminTopicDto> Handle(CreateTopicCommand request, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Slug, request.Description);

        var topic = Topic.Create(
            request.LanguageCode,
            request.Name,
            request.Slug,
            request.Description,
            timeProvider.GetUtcNow());
        var localization = topic.Localizations.Single();

        await EnsureSlugIsAvailable(localization.LanguageCode, localization.Slug, null, cancellationToken);
        dbContext.Topics.Add(topic);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw SlugConflict(localization.LanguageCode, localization.Slug);
        }

        return ToDto(topic);
    }

    private async Task EnsureSlugIsAvailable(
        string languageCode,
        string slug,
        Guid? excludedLocalizationId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.TopicLocalizations.AnyAsync(
            candidate => candidate.LanguageCode == languageCode
                && candidate.Slug == slug
                && candidate.Id != excludedLocalizationId,
            cancellationToken);
        if (exists)
        {
            throw SlugConflict(languageCode, slug);
        }
    }

    internal static void Validate(string name, string slug, string? description)
    {
        DomainRules.EnsureMaximumLength(name, 200, nameof(name));
        DomainRules.EnsureMaximumLength(slug, 200, nameof(slug));
        DomainRules.EnsureMaximumLength(description, 2_000, nameof(description));
    }

    internal static RequestConflictException SlugConflict(string languageCode, string slug) =>
        new($"Slug '{slug}' is already used for language '{languageCode}'.", "taxonomy_slug_conflict");

    internal static AdminTopicDto ToDto(Topic topic) => new(
        topic.Id,
        topic.CreatedAt,
        topic.UpdatedAt,
        topic.Localizations
            .OrderBy(localization => localization.LanguageCode)
            .Select(localization => new TopicLocalizationDto(
                localization.Id,
                localization.LanguageCode,
                localization.Name,
                localization.Slug,
                localization.Description))
            .ToArray());
}
