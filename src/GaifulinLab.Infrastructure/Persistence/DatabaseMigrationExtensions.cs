using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Infrastructure.Persistence;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.BackfillArticleSearchAsync(cancellationToken);
    }

    public static async Task BackfillArticleSearchAsync(this AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        // Nullable migration default marks existing rows. Process in batches without changing edit versions.
        while (true)
        {
            var batch = await dbContext.ArticleLocalizations.AsNoTracking()
                .Where(x => x.SearchText == null).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Version, x.Markdown }).Take(100).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            foreach (var item in batch)
            {
                var text = Content.ArticleSearchText.Extract(item.Markdown);
                var minutes = Content.ArticleSearchText.ReadingMinutes(text);
                await dbContext.ArticleLocalizations.Where(x => x.Id == item.Id && x.Version == item.Version)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SearchText, text)
                        .SetProperty(x => x.ReadingMinutes, minutes), cancellationToken);
            }
        }
    }
}
