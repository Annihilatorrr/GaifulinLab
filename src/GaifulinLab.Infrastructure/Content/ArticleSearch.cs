using System.ComponentModel.DataAnnotations;
using GaifulinLab.Application.Articles.Public;
using GaifulinLab.Application.Authors;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace GaifulinLab.Infrastructure.Content;

internal sealed class ArticleSearch(AppDbContext db, IAuthorDisplayNameLookup authors, TimeProvider clock) : IArticleSearch
{
    public async Task<ArticleSearchResponse> SearchAsync(ArticleSearchRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true)
            || request.Tag.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > GaifulinLab.Domain.Common.ContentLimits.TagName))
            throw new ArgumentException("Invalid search parameters.");

        var text = request.Query?.Trim() ?? "";
        var normalizedText = ArticleSearchText.NormalizeQuery(text);
        var config = request.LanguageCode switch { "ru" => "russian", "en" => "english", _ => "simple" };
        var tagVector = request.LanguageCode switch
        {
            "ru" => "RussianSearchVector", "en" => "EnglishSearchVector", _ => "SimpleSearchVector"
        };
        var tags = request.Tag.Select(tag => tag.Trim().ToLowerInvariant()).Distinct().ToArray();
        var topic = request.Topic?.Trim().ToLowerInvariant();
        DateTimeOffset? since = request.Period switch
        {
            "week" => clock.GetUtcNow().AddDays(-7),
            "month" => clock.GetUtcNow().AddMonths(-1),
            "year" => clock.GetUtcNow().AddYears(-1),
            _ => null
        };
        var articles = db.ArticleLocalizations.AsNoTracking().Where(localization =>
            localization.LanguageCode == request.LanguageCode
            && localization.Status == PublicationStatus.Published && localization.Article.DeletedAt == null);
        if (since.HasValue) articles = articles.Where(localization => localization.PublishedAt >= since);
        if (!string.IsNullOrEmpty(topic))
            articles = articles.Where(localization => db.ArticleTopics.Any(link => link.ArticleId == localization.ArticleId
                && db.TopicLocalizations.Any(value => value.TopicId == link.TopicId
                    && value.LanguageCode == request.LanguageCode && value.Slug == topic)));
        if (tags.Length > 0)
            articles = articles.Where(localization => db.ArticleTags.Any(link => link.ArticleId == localization.ArticleId
                && db.Tags.Any(tag => tag.Id == link.TagId && tags.Contains(tag.NormalizedName))));

        var searchTitle = text.Length > 0 && request.Scope is "all" or "title";
        var searchContent = text.Length > 0 && request.Scope is "all" or "content";
        var searchTopics = text.Length > 0 && request.Scope is "all" or "topics";
        var searchTags = text.Length > 0 && request.Scope is "all" or "tags";
        // Npgsql translates these full-text methods into PostgreSQL operations. The
        // stored vectors and GIN indexes are maintained by the database, including renames.
        var matches = articles.Select(localization => new
        {
            Localization = localization,
            Title = searchTitle && EF.Property<NpgsqlTsVector>(localization, "TitleSearchVector")
                .Matches(EF.Functions.PlainToTsQuery(config, normalizedText)),
            Summary = searchContent && EF.Property<NpgsqlTsVector>(localization, "SummarySearchVector")
                .Matches(EF.Functions.PlainToTsQuery(config, normalizedText)),
            Body = searchContent && EF.Property<NpgsqlTsVector>(localization, "BodySearchVector")
                .Matches(EF.Functions.PlainToTsQuery(config, normalizedText)),
            Topic = searchTopics && db.ArticleTopics.Any(link => link.ArticleId == localization.ArticleId
                && db.TopicLocalizations.Any(value => value.TopicId == link.TopicId
                    && value.LanguageCode == request.LanguageCode && EF.Property<NpgsqlTsVector>(value, "NameSearchVector")
                        .Matches(EF.Functions.PlainToTsQuery(config, normalizedText)))),
            Tag = searchTags && db.ArticleTags.Any(link => link.ArticleId == localization.ArticleId
                && db.Tags.Any(tag => tag.Id == link.TagId && EF.Property<NpgsqlTsVector>(tag, tagVector)
                    .Matches(EF.Functions.PlainToTsQuery(config, normalizedText))))
        });
        if (text.Length > 0) matches = matches.Where(row => row.Title || row.Summary || row.Body || row.Topic || row.Tag);

        // Count separately so an out-of-range page can be clamped to the last page.
        // Both queries share the same filters; sorting and pagination stay in PostgreSQL.
        var count = await matches.LongCountAsync(cancellationToken);
        var totalPages = (int)Math.Min(int.MaxValue, (count + request.PageSize - 1) / request.PageSize);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, totalPages));
        if (count == 0) return new([], 0, 1, request.PageSize, 0);

        var ranked = matches.Select(row => new
        {
            row.Localization,
            Score = (row.Title ? 8 : 0) + (row.Topic || row.Tag ? 4 : 0)
                + (row.Summary ? 2 : 0) + (row.Body ? 1 : 0)
                + (searchTitle || searchContent ? 0.1 * (request.Scope == "title"
                    ? EF.Property<NpgsqlTsVector>(row.Localization, "TitleSearchVector")
                    : request.Scope == "content"
                        ? EF.Property<NpgsqlTsVector>(row.Localization, "SummarySearchVector")
                            .Concat(EF.Property<NpgsqlTsVector>(row.Localization, "BodySearchVector"))
                        : EF.Property<NpgsqlTsVector>(row.Localization, "TitleSearchVector")
                            .Concat(EF.Property<NpgsqlTsVector>(row.Localization, "SummarySearchVector"))
                            .Concat(EF.Property<NpgsqlTsVector>(row.Localization, "BodySearchVector")))
                    .Rank(EF.Functions.PlainToTsQuery(config, normalizedText), NpgsqlTsRankingNormalization.DivideByItselfPlusOne) : 0)
        });
        var ordered = request.Sort == "oldest"
            ? ranked.OrderBy(row => row.Localization.PublishedAt).ThenBy(row => row.Localization.Id)
            : request.Sort == "relevance" && text.Length > 0
                ? ranked.OrderByDescending(row => row.Score).ThenByDescending(row => row.Localization.PublishedAt)
                    .ThenByDescending(row => row.Localization.Id)
                : ranked.OrderByDescending(row => row.Localization.PublishedAt).ThenByDescending(row => row.Localization.Id);
        var rows = await ordered.Skip(checked((page - 1) * request.PageSize)).Take(request.PageSize).Select(row => new
        {
            row.Localization.ArticleId,
            row.Localization.Article.OwnerUserId,
            row.Localization.Slug,
            row.Localization.Title,
            row.Localization.Summary,
            row.Localization.PublishedAt,
            row.Localization.CoverMediaAssetId,
            row.Localization.ReadingMinutes,
            Snippet = text.Length == 0
                ? (row.Localization.Summary == null || row.Localization.Summary == ""
                    ? row.Localization.SearchText ?? "" : row.Localization.Summary).Substring(0, 260)
                : EF.Functions.PlainToTsQuery(config, text).GetResultHeadline(config: config,
                    document: ((row.Localization.Summary ?? "") + " " + (row.Localization.SearchText ?? ""))
                        .Replace("\uE000", "").Replace("\uE001", ""),
                    options: "StartSel=\uE000, StopSel=\uE001, MaxWords=42, MinWords=16, MaxFragments=1"),
            Headline = text.Length == 0 ? row.Localization.Title
                : EF.Functions.PlainToTsQuery(config, text).GetResultHeadline(config: config,
                    document: row.Localization.Title.Replace("\uE000", "").Replace("\uE001", ""),
                    options: "StartSel=\uE000, StopSel=\uE001, HighlightAll=true")
        }).ToListAsync(cancellationToken);

        // Reuse the existing batched loaders after pagination: three taxonomy queries
        // and one author query, independent of page size, without multiplying result rows.
        var taxonomy = await PublicArticleTaxonomyLoader.Load(db, rows.Select(row => row.ArticleId).ToArray(), request.LanguageCode, cancellationToken);
        var names = await authors.GetDisplayNamesAsync(rows.Select(row => row.OwnerUserId).ToArray(), cancellationToken);
        return new(rows.Select(row => new PublicArticleListItemDto(request.LanguageCode, row.Slug!, row.Title,
            row.Summary, row.PublishedAt!.Value, names.GetValueOrDefault(row.OwnerUserId, "Author"),
            taxonomy.TopicsFor(row.ArticleId), taxonomy.SeriesFor(row.ArticleId), taxonomy.TagsFor(row.ArticleId),
            row.CoverMediaAssetId, row.ReadingMinutes, row.Snippet, row.Headline)).ToArray(),
            count, page, request.PageSize, totalPages);
    }
}
