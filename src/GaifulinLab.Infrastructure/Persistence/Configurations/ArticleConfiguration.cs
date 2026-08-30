using GaifulinLab.Domain.Articles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("articles");
        builder.HasKey(article => article.Id);

        builder.Property(article => article.CreatedAt).IsRequired();
        builder.Property(article => article.UpdatedAt).IsRequired();

        builder.HasIndex(article => article.CreatedAt)
            .HasDatabaseName("ix_articles_created_at");
    }
}
