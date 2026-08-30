using GaifulinLab.Domain.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class SeriesLocalizationConfiguration : IEntityTypeConfiguration<SeriesLocalization>
{
    public void Configure(EntityTypeBuilder<SeriesLocalization> builder)
    {
        builder.ToTable("series_localizations", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_series_localizations_language_code",
                "\"LanguageCode\" ~ '^[a-z]{2}$'");
            tableBuilder.HasCheckConstraint(
                "ck_series_localizations_slug_format",
                "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
        });

        builder.HasKey(localization => localization.Id);

        builder.Property(localization => localization.LanguageCode)
            .HasMaxLength(2)
            .IsRequired();
        builder.Property(localization => localization.Title)
            .HasMaxLength(300)
            .IsRequired();
        builder.Property(localization => localization.Slug)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(localization => localization.Description)
            .HasMaxLength(2_000);

        builder.HasOne<SeriesAggregate>()
            .WithMany(series => series.Localizations)
            .HasForeignKey(localization => localization.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(localization => new { localization.SeriesId, localization.LanguageCode })
            .IsUnique()
            .HasDatabaseName("ux_series_localizations_series_language");
        builder.HasIndex(localization => new { localization.LanguageCode, localization.Slug })
            .IsUnique()
            .HasDatabaseName("ux_series_localizations_language_slug");
    }
}
