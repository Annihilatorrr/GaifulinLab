using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleSeriesConfiguration : IEntityTypeConfiguration<ArticleSeries>
{
    public void Configure(EntityTypeBuilder<ArticleSeries> builder)
    {
        builder.ToTable("article_series", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "ck_article_series_position_positive",
                "\"Position\" > 0"));

        builder.HasKey(link => new { link.SeriesId, link.ArticleId });

        builder.HasOne<SeriesAggregate>()
            .WithMany(series => series.Articles)
            .HasForeignKey(link => link.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Article>()
            .WithMany()
            .HasForeignKey(link => link.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(link => link.ArticleId)
            .HasDatabaseName("ix_article_series_article_id");
        builder.HasIndex(link => new { link.SeriesId, link.Position })
            .IsUnique()
            .HasDatabaseName("ux_article_series_series_position");
    }
}
