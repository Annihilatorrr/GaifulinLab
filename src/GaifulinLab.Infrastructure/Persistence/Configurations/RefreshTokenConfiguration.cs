using GaifulinLab.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.FamilyId).IsRequired();
        builder.Property(token => token.UserId).HasMaxLength(450).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();
        builder.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);
        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(token => token.ExpiresAtUtc).HasDatabaseName("ix_refresh_tokens_expires_at_utc");
        builder.HasIndex(token => token.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        builder.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
