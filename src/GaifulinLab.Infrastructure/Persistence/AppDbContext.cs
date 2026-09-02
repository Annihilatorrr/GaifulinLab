using GaifulinLab.Application.Persistence;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Media;
using GaifulinLab.Domain.Pdf;
using GaifulinLab.Domain.Series;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IAppDbContext
{
    public DbSet<Article> Articles => Set<Article>();

    public DbSet<ArticleLocalization> ArticleLocalizations => Set<ArticleLocalization>();

    public DbSet<ArticleTopic> ArticleTopics => Set<ArticleTopic>();

    public DbSet<ArticleTag> ArticleTags => Set<ArticleTag>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<TopicLocalization> TopicLocalizations => Set<TopicLocalization>();

    public DbSet<SeriesAggregate> Series => Set<SeriesAggregate>();

    public DbSet<SeriesLocalization> SeriesLocalizations => Set<SeriesLocalization>();

    public DbSet<ArticleSeries> ArticleSeries => Set<ArticleSeries>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<PdfExportJob> PdfExportJobs => Set<PdfExportJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
