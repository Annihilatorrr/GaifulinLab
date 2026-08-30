using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleTagConfiguration : IEntityTypeConfiguration<ArticleTag>
{
    public void Configure(EntityTypeBuilder<ArticleTag> builder)
    {
        builder.ToTable("article_tags");
        builder.HasKey(link => new { link.ArticleId, link.TagId });

        builder.HasOne<Article>()
            .WithMany(article => article.Tags)
            .HasForeignKey(link => link.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(link => link.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(link => link.TagId)
            .HasDatabaseName("ix_article_tags_tag_id");
    }
}
