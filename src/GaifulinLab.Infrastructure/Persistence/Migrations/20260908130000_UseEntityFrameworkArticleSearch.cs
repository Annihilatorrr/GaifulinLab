using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseEntityFrameworkArticleSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX ix_tag_search_ru; DROP INDEX ix_tag_search_en; DROP INDEX ix_topic_search_name;
                DROP INDEX ix_article_search_body; DROP INDEX ix_article_search_summary; DROP INDEX ix_article_search_title;
                DROP FUNCTION public.gl_search_vector(text, text); DROP FUNCTION public.gl_search_normalize(text); DROP FUNCTION public.gl_search_config(text);
                """);
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "NameSearchVector",
                table: "topic_localizations",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector(CASE \"LanguageCode\" WHEN 'ru' THEN 'russian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"Name\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "EnglishSearchVector",
                table: "tags",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('english'::regconfig, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"Name\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "RussianSearchVector",
                table: "tags",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('russian'::regconfig, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"Name\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SimpleSearchVector",
                table: "tags",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple'::regconfig, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"Name\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "BodySearchVector",
                table: "article_localizations",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector(CASE \"LanguageCode\" WHEN 'ru' THEN 'russian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"SearchText\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SummarySearchVector",
                table: "article_localizations",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector(CASE \"LanguageCode\" WHEN 'ru' THEN 'russian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"Summary\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "TitleSearchVector",
                table: "article_localizations",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector(CASE \"LanguageCode\" WHEN 'ru' THEN 'russian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END, regexp_replace(regexp_replace(regexp_replace(lower(coalesce(\"Title\", '')),\n    '\\mc\\+\\+(?=$|\\W)', 'glcpp', 'g'), '\\mc#(?=$|\\W)', 'glcsharp', 'g'), '\\.net\\M', 'gldotnet', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_topic_search_name_vector",
                table: "topic_localizations",
                column: "NameSearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_tag_search_en_vector",
                table: "tags",
                column: "EnglishSearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_tag_search_ru_vector",
                table: "tags",
                column: "RussianSearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_tag_search_simple_vector",
                table: "tags",
                column: "SimpleSearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_article_search_body_vector",
                table: "article_localizations",
                column: "BodySearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_article_search_summary_vector",
                table: "article_localizations",
                column: "SummarySearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_article_search_title_vector",
                table: "article_localizations",
                column: "TitleSearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION public.gl_search_config(language text) RETURNS regconfig
                LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                    SELECT CASE language WHEN 'ru' THEN 'russian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.gl_search_normalize(value text) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                    SELECT regexp_replace(regexp_replace(regexp_replace(lower(coalesce(value, '')),
                        '\mc\+\+(?=$|\W)', 'glcpp', 'g'), '\mc#(?=$|\W)', 'glcsharp', 'g'),
                        '\.net\M', 'gldotnet', 'g')
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION public.gl_search_vector(language text, value text) RETURNS tsvector
                LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                    SELECT to_tsvector(public.gl_search_config(language), public.gl_search_normalize(value))
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_article_search_title ON article_localizations USING gin (public.gl_search_vector("LanguageCode", "Title"));
                CREATE INDEX ix_article_search_summary ON article_localizations USING gin (public.gl_search_vector("LanguageCode", coalesce("Summary", '')));
                CREATE INDEX ix_article_search_body ON article_localizations USING gin (public.gl_search_vector("LanguageCode", "SearchText"));
                CREATE INDEX ix_topic_search_name ON topic_localizations USING gin (public.gl_search_vector("LanguageCode", "Name"));
                CREATE INDEX ix_tag_search_en ON tags USING gin (public.gl_search_vector('en', "Name"));
                CREATE INDEX ix_tag_search_ru ON tags USING gin (public.gl_search_vector('ru', "Name"));
                """);
            migrationBuilder.DropIndex(
                name: "ix_topic_search_name_vector",
                table: "topic_localizations");

            migrationBuilder.DropIndex(
                name: "ix_tag_search_en_vector",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ix_tag_search_ru_vector",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ix_tag_search_simple_vector",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ix_article_search_body_vector",
                table: "article_localizations");

            migrationBuilder.DropIndex(
                name: "ix_article_search_summary_vector",
                table: "article_localizations");

            migrationBuilder.DropIndex(
                name: "ix_article_search_title_vector",
                table: "article_localizations");

            migrationBuilder.DropColumn(
                name: "NameSearchVector",
                table: "topic_localizations");

            migrationBuilder.DropColumn(
                name: "EnglishSearchVector",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "RussianSearchVector",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "SimpleSearchVector",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "BodySearchVector",
                table: "article_localizations");

            migrationBuilder.DropColumn(
                name: "SummarySearchVector",
                table: "article_localizations");

            migrationBuilder.DropColumn(
                name: "TitleSearchVector",
                table: "article_localizations");
        }
    }
}
