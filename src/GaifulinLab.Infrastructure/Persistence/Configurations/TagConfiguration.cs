using GaifulinLab.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "ck_tags_normalized_name_lowercase",
                "\"NormalizedName\" = lower(\"NormalizedName\")"));

        builder.HasKey(tag => tag.Id);

        builder.Property(tag => tag.Name)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(tag => tag.NormalizedName)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(tag => tag.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_tags_normalized_name");
    }
}
