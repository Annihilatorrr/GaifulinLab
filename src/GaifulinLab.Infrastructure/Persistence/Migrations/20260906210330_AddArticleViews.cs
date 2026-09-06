using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "article_views",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstViewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_views", x => new { x.ArticleId, x.VisitorHash });
                    table.ForeignKey(
                        name: "FK_article_views_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_views");
        }
    }
}
