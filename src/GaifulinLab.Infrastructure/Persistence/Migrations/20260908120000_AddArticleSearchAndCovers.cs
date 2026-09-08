using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaifulinLab.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260908120000_AddArticleSearchAndCovers")]
public sealed class AddArticleSearchAndCovers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("CoverMediaAssetId", "article_localizations", nullable: true);
        migrationBuilder.AddColumn<string>("SearchText", "article_localizations", type: "text", nullable: true);
        migrationBuilder.AddColumn<int>("ReadingMinutes", "article_localizations", nullable: false, defaultValue: 1);
        migrationBuilder.CreateIndex("IX_article_localizations_CoverMediaAssetId", "article_localizations", "CoverMediaAssetId");
        migrationBuilder.AddForeignKey("FK_article_localizations_media_assets_CoverMediaAssetId",
            "article_localizations", "CoverMediaAssetId", "media_assets", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX ix_tag_search_ru; DROP INDEX ix_tag_search_en; DROP INDEX ix_topic_search_name;
            DROP INDEX ix_article_search_body; DROP INDEX ix_article_search_summary; DROP INDEX ix_article_search_title;
            DROP FUNCTION public.gl_search_vector(text, text); DROP FUNCTION public.gl_search_normalize(text); DROP FUNCTION public.gl_search_config(text);
            """);
        migrationBuilder.DropForeignKey("FK_article_localizations_media_assets_CoverMediaAssetId", "article_localizations");
        migrationBuilder.DropIndex("IX_article_localizations_CoverMediaAssetId", "article_localizations");
        migrationBuilder.DropColumn("CoverMediaAssetId", "article_localizations");
        migrationBuilder.DropColumn("SearchText", "article_localizations");
        migrationBuilder.DropColumn("ReadingMinutes", "article_localizations");
    }
}
