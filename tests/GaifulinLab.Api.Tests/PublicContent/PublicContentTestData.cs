using System.Net.Http.Headers;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Domain.Tags;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Authentication;
using GaifulinLab.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using SeriesAggregate = GaifulinLab.Domain.Series.Series;

namespace GaifulinLab.Api.Tests.PublicContent;

internal static class PublicContentTestData
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        dbContext.Users.Add(new ApplicationUser
        {
            Id = "test-owner",
            UserName = "private-owner@example.com",
            DisplayName = "Test Author"
        });

        var article = Article.Create(
            "test-owner",
            "en",
            now,
            "Understanding FFT",
            "A practical introduction",
            "**safe** <script>alert('xss')</script>",
            "understanding-fft");
        article.PublishLocalization("en", now);
        article.AddLocalization(
            "ru",
            now,
            "Как работает FFT",
            "Практическое введение",
            "**безопасно**",
            "kak-rabotaet-fft");
        article.PublishLocalization("ru", now);

        var topic = Topic.Create("en", "Signal processing", "signal-processing", null, now);
        topic.AddLocalization("ru", "Обработка сигналов", "obrabotka-signalov", null, now);
        var series = SeriesAggregate.Create("en", "Fourier notes", "fourier-notes", null, now);
        series.AddLocalization("ru", "Заметки о Фурье", "zametki-o-fure", null, now);
        var tag = Tag.Create(".NET");

        article.AssignTopic(topic, now);
        article.AssignTag(tag, now);
        series.AddArticle(article, 1, now);

        var draft = Article.Create(
            "test-owner",
            "en",
            now,
            "Future draft",
            null,
            "Not published",
            "future-draft");
        draft.AssignTopic(topic, now);
        draft.AssignTag(tag, now);
        series.AddArticle(draft, 2, now);

        var deleted = Article.Create(
            "test-owner",
            "en",
            now,
            "Deleted published article",
            null,
            "This article must remain private.",
            "deleted-published");
        deleted.PublishLocalization("en", now);
        deleted.AssignTopic(topic, now);
        deleted.AssignTag(tag, now);
        series.AddArticle(deleted, 3, now);
        deleted.Delete(now.AddHours(1));

        dbContext.AddRange(article, draft, deleted, topic, series, tag);
        await dbContext.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> PostArticleViewAsync(
        HttpClient client,
        string languageCode,
        string slug,
        string forwardedFor)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/public/articles/{languageCode}/{slug}/views");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }
}
