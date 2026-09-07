using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260908000000_OptimizePublicArticleListingPagination")]
public partial class OptimizePublicArticleListingPagination : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_article_localizations_public_listing",
            table: "article_localizations");

        migrationBuilder.CreateIndex(
            name: "ix_article_localizations_public_listing",
            table: "article_localizations",
            columns: new[] { "LanguageCode", "Status", "PublishedAt", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_article_localizations_public_listing",
            table: "article_localizations");

        migrationBuilder.CreateIndex(
            name: "ix_article_localizations_public_listing",
            table: "article_localizations",
            columns: new[] { "LanguageCode", "Status", "PublishedAt" });
    }
}
