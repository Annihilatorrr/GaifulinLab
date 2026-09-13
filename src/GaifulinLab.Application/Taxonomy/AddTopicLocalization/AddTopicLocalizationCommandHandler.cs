using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Application.Taxonomy.CreateTopic;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.AddTopicLocalization;

internal sealed class AddTopicLocalizationCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider,
    ITaxonomyPersistenceConflictDetector conflictDetector)
    : IRequestHandler<AddTopicLocalizationCommand, AdminTopicDto>
{
    public async Task<AdminTopicDto> Handle(
        AddTopicLocalizationCommand request,
        CancellationToken cancellationToken)
    {
        CreateTopicCommandHandler.Validate(request.Name, request.Slug, request.Description);

        var topic = await dbContext.Topics
            .Include(candidate => candidate.Localizations)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.TopicId, cancellationToken)
            ?? throw new ResourceNotFoundException("Topic", request.TopicId);

        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        if (topic.FindLocalization(languageCode) is not null)
        {
            throw LocalizationConflict("topic", languageCode);
        }

        var normalizedSlug = DomainRules.NormalizeSlug(request.Slug);
        if (await dbContext.TopicLocalizations.AnyAsync(
                candidate => candidate.LanguageCode == languageCode && candidate.Slug == normalizedSlug,
                cancellationToken))
        {
            throw CreateTopicCommandHandler.SlugConflict(languageCode, normalizedSlug);
        }

        var localization = topic.AddLocalization(
            languageCode,
            request.Name,
            normalizedSlug,
            request.Description,
            timeProvider.GetUtcNow());
        dbContext.TopicLocalizations.Add(localization);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            switch (conflictDetector.Detect(exception))
            {
                case TaxonomyPersistenceConflict.SlugAlreadyUsed:
                    throw CreateTopicCommandHandler.SlugConflict(languageCode, normalizedSlug);
                case TaxonomyPersistenceConflict.LocalizationAlreadyExists:
                    throw LocalizationConflict("topic", languageCode);
                default:
                    throw;
            }
        }

        return CreateTopicCommandHandler.ToDto(topic);
    }

    private static RequestConflictException LocalizationConflict(string taxonomyName, string languageCode) =>
        new($"The {taxonomyName} already has a '{languageCode}' localization.", "taxonomy_localization_conflict");
}
