using GaifulinLab.Domain.Articles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleLocalizationConfiguration : IEntityTypeConfiguration<ArticleLocalization>
{
    public void Configure(EntityTypeBuilder<ArticleLocalization> builder)
    {
        builder.ToTable("article_localizations", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_article_localizations_language_code",
                "\"LanguageCode\" ~ '^[a-z]{2}$'");
            tableBuilder.HasCheckConstraint(
                "ck_article_localizations_slug_format",
                "\"Slug\" IS NULL OR \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            tableBuilder.HasCheckConstraint(
                "ck_article_localizations_status",
                "\"Status\" IN ('Draft', 'Published', 'Unpublished', 'Deleted')");
            tableBuilder.HasCheckConstraint(
                "ck_article_localizations_published_content",
                "\"Status\" <> 'Published' OR (\"PublishedAt\" IS NOT NULL AND \"Slug\" IS NOT NULL AND length(btrim(\"Title\")) > 0 AND length(btrim(\"Markdown\")) > 0)");
        });

        builder.HasKey(localization => localization.Id);

        builder.Property(localization => localization.LanguageCode)
            .HasMaxLength(2)
            .IsRequired();
        builder.Property(localization => localization.Slug)
            .HasMaxLength(200);
        builder.Property(localization => localization.Title)
            .HasMaxLength(300)
            .IsRequired();
        builder.Property(localization => localization.Summary)
            .HasMaxLength(1_000);
        builder.Property(localization => localization.Markdown)
            .HasColumnType("text")
            .IsRequired();
        builder.Property(localization => localization.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(localization => localization.UpdatedAt).IsRequired();
        builder.Property(localization => localization.LastEditedAt).IsRequired();

        builder.HasOne<Article>()
            .WithMany(article => article.Localizations)
            .HasForeignKey(localization => localization.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(localization => new { localization.ArticleId, localization.LanguageCode })
            .IsUnique()
            .HasDatabaseName("ux_article_localizations_article_language");
        builder.HasIndex(localization => new { localization.LanguageCode, localization.Slug })
            .IsUnique()
            .HasFilter("\"Slug\" IS NOT NULL")
            .HasDatabaseName("ux_article_localizations_language_slug");
        builder.HasIndex(localization => new { localization.LanguageCode, localization.Status, localization.PublishedAt })
            .HasDatabaseName("ix_article_localizations_public_listing");
    }
}
