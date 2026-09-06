using GaifulinLab.Domain.Articles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleViewConfiguration : IEntityTypeConfiguration<ArticleView>
{
    public void Configure(EntityTypeBuilder<ArticleView> builder)
    {
        builder.ToTable("article_views");

        // This is the rule that makes a visitor unique for an article, including all its translations.
        builder.HasKey(view => new { view.ArticleId, view.VisitorHash });

        builder.Property(view => view.VisitorHash)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(view => view.FirstViewedAt).IsRequired();

        builder.HasOne<Article>()
            .WithMany()
            .HasForeignKey(view => view.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
