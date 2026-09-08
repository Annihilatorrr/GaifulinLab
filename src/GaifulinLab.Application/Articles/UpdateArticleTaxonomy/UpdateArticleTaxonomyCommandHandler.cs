using GaifulinLab.Application.Common;
using GaifulinLab.Application.Persistence;
using GaifulinLab.Domain.Common;
using GaifulinLab.Domain.Tags;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GaifulinLab.Application.Articles.UpdateArticleTaxonomy;

internal sealed class UpdateArticleTaxonomyCommandHandler(
    IAppDbContext dbContext,
    TimeProvider timeProvider) : IRequestHandler<UpdateArticleTaxonomyCommand>
{
    public async Task Handle(UpdateArticleTaxonomyCommand request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var article = await dbContext.Articles
            .Include(candidate => candidate.Topics)
            .Include(candidate => candidate.Tags)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.ArticleId
                    && candidate.OwnerUserId == request.UserId
                    && candidate.DeletedAt == null,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Article", request.ArticleId);

        var topicIds = request.TopicIds.Distinct().ToArray();
        var topics = await dbContext.Topics
            .Where(topic => topicIds.Contains(topic.Id))
            .ToListAsync(cancellationToken);
        if (topics.Count != topicIds.Length)
        {
            throw new ResourceNotFoundException("Topic", "one or more requested identifiers");
        }

        var requestedSeriesIds = request.Series.Select(assignment => assignment.SeriesId).ToArray();
        var affectedSeries = await dbContext.Series
            .Include(series => series.Articles)
            .Where(series => requestedSeriesIds.Contains(series.Id)
                || series.Articles.Any(link => link.ArticleId == article.Id))
            .ToListAsync(cancellationToken);
        if (affectedSeries.Count(series => requestedSeriesIds.Contains(series.Id)) != requestedSeriesIds.Length)
        {
            throw new ResourceNotFoundException("Series", "one or more requested identifiers");
        }

        var tags = await ResolveTags(request.Tags, cancellationToken);
        var now = timeProvider.GetUtcNow();

        article.ReplaceTopics(topics, now);
        article.ReplaceTags(tags, now);

        foreach (var series in affectedSeries.Where(
                     series => !requestedSeriesIds.Contains(series.Id)))
        {
            series.RemoveArticle(article.Id, now);
        }

        foreach (var assignment in request.Series)
        {
            var series = affectedSeries.Single(candidate => candidate.Id == assignment.SeriesId);
            series.SetArticle(article, assignment.Position, now);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RequestConflictException(
                "The article was changed elsewhere. Reload it before changing taxonomy.",
                "article_taxonomy_conflict");
        }
    }

    private async Task<IReadOnlyList<Tag>> ResolveTags(
        IEnumerable<string> requestedNames,
        CancellationToken cancellationToken)
    {
        var names = requestedNames
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedNames = names.Select(name => name.ToLowerInvariant()).ToArray();
        var tags = await dbContext.Tags
            .Where(tag => normalizedNames.Contains(tag.NormalizedName))
            .ToListAsync(cancellationToken);

        foreach (var name in names.Where(name => tags.All(
                     tag => !string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase))))
        {
            var tag = Tag.Create(name);
            dbContext.Tags.Add(tag);
            tags.Add(tag);
        }

        return tags;
    }

    private static void ValidateRequest(UpdateArticleTaxonomyCommand request)
    {
        ArgumentNullException.ThrowIfNull(request.TopicIds);
        ArgumentNullException.ThrowIfNull(request.Series);
        ArgumentNullException.ThrowIfNull(request.Tags);

        if (request.TopicIds.Count != request.TopicIds.Distinct().Count())
        {
            throw new ArgumentException("Topic identifiers must be unique.", nameof(request));
        }

        if (request.Series.Count != request.Series.Select(item => item.SeriesId).Distinct().Count())
        {
            throw new ArgumentException("Series identifiers must be unique.", nameof(request));
        }

        if (request.Series.Any(item => item.Position <= 0))
        {
            throw new ArgumentException("Series positions must be greater than zero.", nameof(request));
        }

        if (request.Tags.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Tags cannot be empty.", nameof(request));
        }

        if (request.Tags.Any(tag => tag.Trim().Length > ContentLimits.TagName))
        {
            throw new ArgumentException(
                $"Each tag must not exceed {ContentLimits.TagName} characters.",
                "Tags");
        }
    }
}
