using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal static class SearchVectorConfiguration
{
    private const string LocalizedConfig = "CASE \"LanguageCode\" WHEN 'ru' THEN 'russian'::regconfig WHEN 'en' THEN 'english'::regconfig ELSE 'simple'::regconfig END";

    public static void Configure(ModelBuilder modelBuilder)
    {
        var articles = modelBuilder.Entity<ArticleLocalization>();
        Add(articles, "TitleSearchVector", "Title", "ix_article_search_title_vector");
        Add(articles, "SummarySearchVector", "Summary", "ix_article_search_summary_vector");
        Add(articles, "BodySearchVector", "SearchText", "ix_article_search_body_vector");
        Add(modelBuilder.Entity<TopicLocalization>(), "NameSearchVector", "Name", "ix_topic_search_name_vector");
        var tags = modelBuilder.Entity<Tag>();
        Add(tags, "EnglishSearchVector", "Name", "ix_tag_search_en_vector", "english");
        Add(tags, "RussianSearchVector", "Name", "ix_tag_search_ru_vector", "russian");
        Add(tags, "SimpleSearchVector", "Name", "ix_tag_search_simple_vector", "simple");
    }

    private static void Add(EntityTypeBuilder builder, string property, string source, string index, string? language = null)
    {
        var config = language is null ? LocalizedConfig : $"'{language}'::regconfig";
        // Schema-only SQL: built-in functions keep vectors current without application
        // callbacks or custom DbFunction mappings. Match NormalizeQuery's technical terms.
        var expression = $$"""
            to_tsvector({{config}}, regexp_replace(regexp_replace(regexp_replace(lower(coalesce("{{source}}", '')),
                '\mc\+\+(?=$|\W)', 'glcpp', 'g'), '\mc#(?=$|\W)', 'glcsharp', 'g'), '\.net\M', 'gldotnet', 'g'))
            """;
        builder.Property<NpgsqlTsVector>(property).HasComputedColumnSql(expression, stored: true);
        builder.HasIndex(property).HasMethod("GIN").HasDatabaseName(index);
    }
}
