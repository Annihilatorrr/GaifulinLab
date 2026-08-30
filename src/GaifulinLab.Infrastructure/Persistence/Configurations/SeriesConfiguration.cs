using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Infrastructure.Persistence.Configurations;

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<SeriesAggregate>
{
    public void Configure(EntityTypeBuilder<SeriesAggregate> builder)
    {
        builder.ToTable("series");
        builder.HasKey(series => series.Id);

        builder.Property(series => series.CreatedAt).IsRequired();
        builder.Property(series => series.UpdatedAt).IsRequired();
    }
}
