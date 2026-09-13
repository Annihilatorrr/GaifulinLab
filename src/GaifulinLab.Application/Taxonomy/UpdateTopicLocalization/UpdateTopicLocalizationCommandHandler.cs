using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Application.Taxonomy.CreateTopic;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Taxonomy.UpdateTopicLocalization;

internal sealed class UpdateTopicLocalizationCommandHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateTopicLocalizationCommand, AdminTopicDto>
{
    public async Task<AdminTopicDto> Handle(
        UpdateTopicLocalizationCommand request,
        CancellationToken cancellationToken)
    {
        CreateTopicCommandHandler.Validate(request.Name, request.Slug, request.Description);

        var topic = await dbContext.Topics
            .Include(candidate => candidate.Localizations)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.TopicId, cancellationToken)
            ?? throw new ResourceNotFoundException("Topic", request.TopicId);

        var languageCode = DomainRules.NormalizeLanguageCode(request.LanguageCode);
        var current = topic.FindLocalization(languageCode)
            ?? throw new ResourceNotFoundException("Topic localization", languageCode);
        var normalizedSlug = DomainRules.NormalizeSlug(request.Slug);

        var slugExists = await dbContext.TopicLocalizations.AnyAsync(
            candidate => candidate.Id != current.Id
                && candidate.LanguageCode == languageCode
                && candidate.Slug == normalizedSlug,
            cancellationToken);
        if (slugExists)
        {
            throw CreateTopicCommandHandler.SlugConflict(languageCode, normalizedSlug);
        }

        topic.UpdateLocalization(
            languageCode,
            request.Name,
            normalizedSlug,
            request.Description,
            timeProvider.GetUtcNow());

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw CreateTopicCommandHandler.SlugConflict(languageCode, normalizedSlug);
        }

        return CreateTopicCommandHandler.ToDto(topic);
    }
}
