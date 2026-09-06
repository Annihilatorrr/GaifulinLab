using System.Net;

namespace GaifulinLab.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class ArticleViewConcurrencyTests(E2EEnvironment environment)
{
    [Fact]
    public async Task ConcurrentRequestsFromOneVisitor_CreateOnlyOneView()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var article = await environment.SeedPublishedArticleAsync(
            $"View concurrency {uniqueId}",
            $"view-concurrency-{uniqueId}",
            "View concurrency test article.");
        using var client = new HttpClient { BaseAddress = environment.ApiBaseUri };

        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => RecordViewAsync(
            client,
            article.Slug,
            "198.51.100.20")));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.Equal(1, await environment.CountArticleViewsAsync(article.Id));
    }

    private static Task<HttpResponseMessage> RecordViewAsync(
        HttpClient client,
        string slug,
        string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/articles/en/{slug}/views");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }
}
