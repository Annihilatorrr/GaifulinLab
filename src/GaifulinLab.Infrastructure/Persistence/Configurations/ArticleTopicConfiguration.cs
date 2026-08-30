using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Topics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleTopicConfiguration : IEntityTypeConfiguration<ArticleTopic>
{
    public void Configure(EntityTypeBuilder<ArticleTopic> builder)
    {
        builder.ToTable("article_topics");
        builder.HasKey(link => new { link.ArticleId, link.TopicId });

        builder.HasOne<Article>()
            .WithMany(article => article.Topics)
            .HasForeignKey(link => link.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Topic>()
            .WithMany()
            .HasForeignKey(link => link.TopicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(link => link.TopicId)
            .HasDatabaseName("ix_article_topics_topic_id");
    }
}
