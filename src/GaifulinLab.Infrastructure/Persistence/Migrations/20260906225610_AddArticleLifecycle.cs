using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_articles_owner_user_id_updated_at",
                table: "articles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_article_localizations_status",
                table: "article_localizations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEditedAt",
                table: "article_localizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE article_localizations SET \"LastEditedAt\" = \"UpdatedAt\" WHERE \"LastEditedAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastEditedAt",
                table: "article_localizations",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_articles_owner_user_id_deleted_at_updated_at",
                table: "articles",
                columns: new[] { "OwnerUserId", "DeletedAt", "UpdatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_article_localizations_status",
                table: "article_localizations",
                sql: "\"Status\" IN ('Draft', 'Published', 'Unpublished', 'Deleted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_articles_owner_user_id_deleted_at_updated_at",
                table: "articles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_article_localizations_status",
                table: "article_localizations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                table: "article_localizations");

            migrationBuilder.CreateIndex(
                name: "ix_articles_owner_user_id_updated_at",
                table: "articles",
                columns: new[] { "OwnerUserId", "UpdatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_article_localizations_status",
                table: "article_localizations",
                sql: "\"Status\" IN ('Draft', 'Published')");
        }
    }
}
