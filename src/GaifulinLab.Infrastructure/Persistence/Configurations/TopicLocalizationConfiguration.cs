using GaifulinLab.Domain.Topics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class TopicLocalizationConfiguration : IEntityTypeConfiguration<TopicLocalization>
{
    public void Configure(EntityTypeBuilder<TopicLocalization> builder)
    {
        builder.ToTable("topic_localizations", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_topic_localizations_language_code",
                "\"LanguageCode\" ~ '^[a-z]{2}$'");
            tableBuilder.HasCheckConstraint(
                "ck_topic_localizations_slug_format",
                "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
        });

        builder.HasKey(localization => localization.Id);

        builder.Property(localization => localization.LanguageCode)
            .HasMaxLength(2)
            .IsRequired();
        builder.Property(localization => localization.Name)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(localization => localization.Slug)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(localization => localization.Description)
            .HasMaxLength(2_000);

        builder.HasOne<Topic>()
            .WithMany(topic => topic.Localizations)
            .HasForeignKey(localization => localization.TopicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(localization => new { localization.TopicId, localization.LanguageCode })
            .IsUnique()
            .HasDatabaseName("ux_topic_localizations_topic_language");
        builder.HasIndex(localization => new { localization.LanguageCode, localization.Slug })
            .IsUnique()
            .HasDatabaseName("ux_topic_localizations_language_slug");
    }
}
