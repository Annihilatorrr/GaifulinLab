using GaifulinLab.Domain.Pdf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class PdfDownloadUsageConfiguration : IEntityTypeConfiguration<PdfDownloadUsage>
{
    public void Configure(EntityTypeBuilder<PdfDownloadUsage> builder)
    {
        builder.ToTable("pdf_download_usages");
        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.UserId).HasMaxLength(450).IsRequired();
        builder.Property(usage => usage.RelativePath).HasMaxLength(500).IsRequired();
        builder.HasIndex(usage => new { usage.UserId, usage.PdfExportJobId, usage.RelativePath, usage.Period })
            .IsUnique()
            .HasDatabaseName("ux_pdf_download_usage_file");
        builder.HasIndex(usage => new { usage.UserId, usage.Period })
            .HasDatabaseName("ix_pdf_download_usage_quota");
    }
}
