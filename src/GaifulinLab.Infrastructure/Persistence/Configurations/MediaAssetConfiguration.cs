using GaifulinLab.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_media_assets_size_positive",
                "\"Size\" > 0");
            tableBuilder.HasCheckConstraint(
                "ck_media_assets_dimensions_positive",
                "(\"Width\" IS NULL OR \"Width\" > 0) AND (\"Height\" IS NULL OR \"Height\" > 0)");
        });

        builder.HasKey(asset => asset.Id);

        builder.Property(asset => asset.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(asset => asset.StoredFileName)
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(asset => asset.RelativePath)
            .HasMaxLength(500)
            .IsRequired();
        builder.Property(asset => asset.ContentType)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(asset => asset.CreatedAt).IsRequired();

        builder.HasIndex(asset => asset.StoredFileName)
            .IsUnique()
            .HasDatabaseName("ux_media_assets_stored_file_name");
        builder.HasIndex(asset => asset.RelativePath)
            .IsUnique()
            .HasDatabaseName("ux_media_assets_relative_path");
    }
}
