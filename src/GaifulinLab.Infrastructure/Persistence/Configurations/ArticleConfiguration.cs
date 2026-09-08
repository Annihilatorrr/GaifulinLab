using GaifulinLab.Domain.Articles;
using GaifulinLab.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("articles");
        builder.HasKey(article => article.Id);

        builder.Property(article => article.OwnerUserId)
            .IsRequired();
        builder.Property(article => article.Version)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(article => article.CreatedAt).IsRequired();
        builder.Property(article => article.UpdatedAt).IsRequired();
        builder.Property(article => article.DeletedAt);

        // A user sees only their own workspace, so this is the main list query index.
        builder.HasIndex(article => new { article.OwnerUserId, article.DeletedAt, article.UpdatedAt })
            .HasDatabaseName("ix_articles_owner_user_id_deleted_at_updated_at");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(article => article.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_articles_owner_user_id");
    }
}
