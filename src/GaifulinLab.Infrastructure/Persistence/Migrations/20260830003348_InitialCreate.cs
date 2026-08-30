using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_articles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                    table.CheckConstraint("ck_media_assets_dimensions_positive", "(\"Width\" IS NULL OR \"Width\" > 0) AND (\"Height\" IS NULL OR \"Height\" > 0)");
                    table.CheckConstraint("ck_media_assets_size_positive", "\"Size\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.CheckConstraint("ck_tags_normalized_name_lowercase", "\"NormalizedName\" = lower(\"NormalizedName\")");
                });

            migrationBuilder.CreateTable(
                name: "topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "article_localizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Markdown = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_localizations", x => x.Id);
                    table.CheckConstraint("ck_article_localizations_language_code", "\"LanguageCode\" ~ '^[a-z]{2}$'");
                    table.CheckConstraint("ck_article_localizations_published_content", "\"Status\" <> 'Published' OR (\"PublishedAt\" IS NOT NULL AND \"Slug\" IS NOT NULL AND length(btrim(\"Title\")) > 0 AND length(btrim(\"Markdown\")) > 0)");
                    table.CheckConstraint("ck_article_localizations_slug_format", "\"Slug\" IS NULL OR \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("ck_article_localizations_status", "\"Status\" IN ('Draft', 'Published')");
                    table.ForeignKey(
                        name: "FK_article_localizations_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_series",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_series", x => new { x.SeriesId, x.ArticleId });
                    table.CheckConstraint("ck_article_series_position_positive", "\"Position\" > 0");
                    table.ForeignKey(
                        name: "FK_article_series_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_series_series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_localizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_localizations", x => x.Id);
                    table.CheckConstraint("ck_series_localizations_language_code", "\"LanguageCode\" ~ '^[a-z]{2}$'");
                    table.CheckConstraint("ck_series_localizations_slug_format", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "FK_series_localizations_series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_tags",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_tags", x => new { x.ArticleId, x.TagId });
                    table.ForeignKey(
                        name: "FK_article_tags_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_topics",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_topics", x => new { x.ArticleId, x.TopicId });
                    table.ForeignKey(
                        name: "FK_article_topics_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_topics_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topic_localizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_localizations", x => x.Id);
                    table.CheckConstraint("ck_topic_localizations_language_code", "\"LanguageCode\" ~ '^[a-z]{2}$'");
                    table.CheckConstraint("ck_topic_localizations_slug_format", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "FK_topic_localizations_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_localizations_public_listing",
                table: "article_localizations",
                columns: new[] { "LanguageCode", "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_article_localizations_article_language",
                table: "article_localizations",
                columns: new[] { "ArticleId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_article_localizations_language_slug",
                table: "article_localizations",
                columns: new[] { "LanguageCode", "Slug" },
                unique: true,
                filter: "\"Slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_article_series_article_id",
                table: "article_series",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "ux_article_series_series_position",
                table: "article_series",
                columns: new[] { "SeriesId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_tags_tag_id",
                table: "article_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "ix_article_topics_topic_id",
                table: "article_topics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "ix_articles_created_at",
                table: "articles",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ux_media_assets_relative_path",
                table: "media_assets",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_media_assets_stored_file_name",
                table: "media_assets",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_series_localizations_language_slug",
                table: "series_localizations",
                columns: new[] { "LanguageCode", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_series_localizations_series_language",
                table: "series_localizations",
                columns: new[] { "SeriesId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tags_normalized_name",
                table: "tags",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_topic_localizations_language_slug",
                table: "topic_localizations",
                columns: new[] { "LanguageCode", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_topic_localizations_topic_language",
                table: "topic_localizations",
                columns: new[] { "TopicId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_localizations");

            migrationBuilder.DropTable(
                name: "article_series");

            migrationBuilder.DropTable(
                name: "article_tags");

            migrationBuilder.DropTable(
                name: "article_topics");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "series_localizations");

            migrationBuilder.DropTable(
                name: "topic_localizations");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "series");

            migrationBuilder.DropTable(
                name: "topics");
        }
    }
}
