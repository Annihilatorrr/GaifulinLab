using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaifulinLab.Infrastructure.Tests.Persistence;

public sealed class ArticleConcurrencyTests
{
    [Fact]
    public async Task ReplaceTopics_RejectsAStaleArticleSnapshot()
    {
        var databaseName = $"article-concurrency-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var createdAt = DateTimeOffset.UtcNow;
        Guid firstTopicId;
        Guid secondTopicId;

        await using (var seedContext = new AppDbContext(options))
        {
            var article = Article.Create("author", "en", createdAt, "Title", null, "# Content", "article");
            var seededFirstTopic = Topic.Create("en", "First topic", "first-topic", null, createdAt);
            var seededSecondTopic = Topic.Create("en", "Second topic", "second-topic", null, createdAt);
            seedContext.AddRange(
                article,
                seededFirstTopic,
                seededSecondTopic);
            await seedContext.SaveChangesAsync();
            firstTopicId = seededFirstTopic.Id;
            secondTopicId = seededSecondTopic.Id;
        }

        await using var firstContext = new AppDbContext(options);
        await using var secondContext = new AppDbContext(options);
        var firstArticle = await firstContext.Articles
            .Include(article => article.Topics)
            .SingleAsync();
        var secondArticle = await secondContext.Articles
            .Include(article => article.Topics)
            .SingleAsync();
        var firstTopic = await firstContext.Topics.SingleAsync(topic => topic.Id == firstTopicId);
        var secondTopic = await secondContext.Topics.SingleAsync(topic => topic.Id == secondTopicId);

        // The first complete replacement advances the aggregate version.
        firstArticle.ReplaceTopics([firstTopic], createdAt.AddMinutes(1));
        await firstContext.SaveChangesAsync();

        // A stale complete replacement must not merge a second topic into the first one.
        secondArticle.ReplaceTopics([secondTopic], createdAt.AddMinutes(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());
    }
}
