using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfReaderAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GenerationVersion",
                table: "pdf_export_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Retain the newest row for each localization before enforcing the
            // one-current-export invariant. Legacy storage files are not
            // deleted by a database migration.
            migrationBuilder.Sql("""
                DELETE FROM "pdf_export_jobs" older
                USING "pdf_export_jobs" newer
                WHERE older."ArticleLocalizationId" = newer."ArticleLocalizationId"
                  AND (older."CreatedAt" < newer."CreatedAt"
                       OR (older."CreatedAt" = newer."CreatedAt" AND older."Id" < newer."Id"));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_pdf_export_jobs_localization",
                table: "pdf_export_jobs",
                column: "ArticleLocalizationId",
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "PdfSubscriptionTier",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "pdf_download_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PdfExportJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_pdf_download_usages", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "ix_pdf_download_usage_quota",
                table: "pdf_download_usages",
                columns: new[] { "UserId", "Period" });
            migrationBuilder.CreateIndex(
                name: "ux_pdf_download_usage_file",
                table: "pdf_download_usages",
                columns: new[] { "UserId", "PdfExportJobId", "RelativePath", "Period" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "pdf_download_usages");
            migrationBuilder.DropColumn(name: "PdfSubscriptionTier", table: "AspNetUsers");
            migrationBuilder.DropIndex(name: "ux_pdf_export_jobs_localization", table: "pdf_export_jobs");
            migrationBuilder.DropColumn(name: "GenerationVersion", table: "pdf_export_jobs");

        }
    }
}
