using System;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260901000000_AddPdfExportJobs")]
public partial class AddPdfExportJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pdf_export_jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArticleLocalizationId = table.Column<Guid>(type: "uuid", nullable: false),
                LanguageCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Markdown = table.Column<string>(type: "text", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LineHeight = table.Column<decimal>(type: "numeric", nullable: false),
                BlockSpacing = table.Column<decimal>(type: "numeric", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                OutputSize = table.Column<long>(type: "bigint", nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pdf_export_jobs", x => x.Id);
                table.CheckConstraint("ck_pdf_export_jobs_attempt_count_positive", "\"AttemptCount\" >= 0");
                table.CheckConstraint("ck_pdf_export_jobs_status", "\"Status\" IN ('Queued', 'Processing', 'Completed', 'Failed')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_pdf_export_jobs_lease",
            table: "pdf_export_jobs",
            column: "LeaseExpiresAt");

        migrationBuilder.CreateIndex(
            name: "ix_pdf_export_jobs_queue",
            table: "pdf_export_jobs",
            columns: new[] { "Status", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "pdf_export_jobs");
    }
}
