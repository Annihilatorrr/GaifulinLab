using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Media;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Domain.Series;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using Microsoft.EntityFrameworkCore;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Application.Persistence;

public interface IAppDbContext
{
    DbSet<Article> Articles { get; }

    DbSet<ArticleLocalization> ArticleLocalizations { get; }

    DbSet<ArticleTopic> ArticleTopics { get; }

    DbSet<ArticleTag> ArticleTags { get; }

    DbSet<Topic> Topics { get; }

    DbSet<TopicLocalization> TopicLocalizations { get; }

    DbSet<SeriesAggregate> Series { get; }

    DbSet<SeriesLocalization> SeriesLocalizations { get; }

    DbSet<ArticleSeries> ArticleSeries { get; }

    DbSet<Tag> Tags { get; }

    DbSet<MediaAsset> MediaAssets { get; }

    DbSet<PdfExportJob> PdfExportJobs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
