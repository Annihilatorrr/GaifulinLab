using GaifulinLab.Domain.Topics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.ToTable("topics");
        builder.HasKey(topic => topic.Id);

        builder.Property(topic => topic.CreatedAt).IsRequired();
        builder.Property(topic => topic.UpdatedAt).IsRequired();
    }
}
