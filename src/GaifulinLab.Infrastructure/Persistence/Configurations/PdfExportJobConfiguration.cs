using GaifulinLab.Domain.Pdf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class PdfExportJobConfiguration : IEntityTypeConfiguration<PdfExportJob>
{
    public void Configure(EntityTypeBuilder<PdfExportJob> builder)
    {
        builder.ToTable("pdf_export_jobs", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_pdf_export_jobs_status",
                "\"Status\" IN ('Queued', 'Processing', 'Completed', 'Failed')");
            tableBuilder.HasCheckConstraint(
                "ck_pdf_export_jobs_attempt_count_positive",
                "\"AttemptCount\" >= 0");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.LanguageCode).HasMaxLength(2).IsRequired();
        builder.Property(job => job.Slug).HasMaxLength(200).IsRequired();
        builder.Property(job => job.Title).HasMaxLength(300).IsRequired();
        builder.Property(job => job.Summary).HasMaxLength(1_000);
        builder.Property(job => job.Markdown).HasColumnType("text").IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(job => job.RelativePath).HasMaxLength(500);
        builder.Property(job => job.ErrorMessage).HasMaxLength(1_000);
        builder.Property(job => job.CreatedAt).IsRequired();

        builder.HasIndex(job => new { job.Status, job.CreatedAt })
            .HasDatabaseName("ix_pdf_export_jobs_queue");
        builder.HasIndex(job => job.LeaseExpiresAt)
            .HasDatabaseName("ix_pdf_export_jobs_lease");
    }
}
