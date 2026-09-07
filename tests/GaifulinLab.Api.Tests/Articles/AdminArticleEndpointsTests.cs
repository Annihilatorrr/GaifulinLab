using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GaifulinLab.Contracts.Articles;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Contracts.Taxonomy;
using GaifulinLab.Domain.Series;
using GaifulinLab.Domain.Topics;
using GaifulinLab.Infrastructure.Persistence;
using GaifulinLab.Api.Tests.Authentication;
using GaifulinLab.Infrastructure.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace GaifulinLab.Api.Tests.Articles;

public sealed class AdminArticleEndpointsTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Articles_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ArticleLifecycle_PreservesIndependentLocalizations()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"article-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "English title", "Summary", "# Content", slug));

        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Created,
            $"Expected 201, received {(int)createResponse.StatusCode}: {await createResponse.Content.ReadAsStringAsync()}");
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);
        Assert.Equal($"/api/admin/articles/{created.ArticleId}", createResponse.Headers.Location?.ToString());

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/ru",
            new UpdateArticleLocalizationRequest(
                "Русский заголовок",
                null,
                "# Содержимое",
                $"statya-{Guid.NewGuid():N}"));
        Assert.True(
            updateResponse.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204, received {(int)updateResponse.StatusCode}: {await updateResponse.Content.ReadAsStringAsync()}");

        var publishResponse = await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish",
            null);
        Assert.Equal(HttpStatusCode.NoContent, publishResponse.StatusCode);

        var unpublishResponse = await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish",
            null);
        Assert.Equal(HttpStatusCode.NoContent, unpublishResponse.StatusCode);

        var unpublishedArticle = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        Assert.Equal(
            PublicationStatusDto.Unpublished,
            unpublishedArticle!.Localizations.Single(localization => localization.LanguageCode == "en").Status);

        var republishResponse = await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish",
            null);
        Assert.Equal(HttpStatusCode.NoContent, republishResponse.StatusCode);

        var article = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        Assert.NotNull(article);
        Assert.Collection(
            article.Localizations,
            english =>
            {
                Assert.Equal("en", english.LanguageCode);
                Assert.Equal(PublicationStatusDto.Published, english.Status);
            },
            russian =>
            {
                Assert.Equal("ru", russian.LanguageCode);
                Assert.Equal(PublicationStatusDto.Draft, russian.Status);
            });

        var list = await client.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>(
            "/api/admin/articles");
        Assert.Contains(list!, item => item.Id == created.ArticleId);

        var deleteResponse = await client.DeleteAsync($"/api/admin/articles/{created.ArticleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await client.GetAsync($"/api/admin/articles/{created.ArticleId}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/public/articles/en/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            $"/api/public/articles/en/{slug}/views", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(
            $"/api/public/articles/en/{slug}/pdf-exports", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Changed", null, "# Changed", "changed"))).StatusCode);

        var listAfterDelete = await client.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>(
            "/api/admin/articles");
        Assert.DoesNotContain(listAfterDelete!, item => item.Id == created.ArticleId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await dbContext.Articles.FindAsync(created.ArticleId);
        Assert.NotNull(deleted);
        Assert.NotNull(deleted!.DeletedAt);
        Assert.True(dbContext.ArticleLocalizations.Any(
            localization => localization.ArticleId == created.ArticleId));
    }

    [Fact]
    public async Task Unpublish_RejectsDraftAndMakesTheLocalizationPrivateUntilRepublished()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"lifecycle-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "Title", null, "# Content", slug));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(
            $"/api/admin/articles/{created!.ArticleId}/localizations/en/unpublish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/public/articles/en/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/public/articles/en/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish", null)).StatusCode);
    }

    [Fact]
    public async Task CreateArticle_WithDuplicateLanguageSlug_ReturnsConflict()
    {
        using var client = await CreateAuthenticatedClient();
        var slug = $"duplicate-{Guid.NewGuid():N}";
        var request = new CreateArticleRequest("en", "Title", null, "Content", slug);

        var firstResponse = await client.PostAsJsonAsync("/api/admin/articles", request);
        var secondResponse = await client.PostAsJsonAsync("/api/admin/articles", request);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var error = await secondResponse.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("conflict", error?.Code);
    }

    [Fact]
    public async Task Articles_AreVisibleAndEditableOnlyByTheirOwner()
    {
        var ownerLogin = $"owner-{Guid.NewGuid():N}";
        var otherLogin = $"other-{Guid.NewGuid():N}";
        const string password = "Strong-password-1!";
        using var ownerClient = await CreateUserClient(factory, ownerLogin, password);
        using var otherClient = await CreateUserClient(factory, otherLogin, password);

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest("en", "Owner article", null, "# Owner", $"owner-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        // A second account cannot discover or change a draft even when it knows its identifier.
        var otherList = await otherClient.GetFromJsonAsync<IReadOnlyList<AdminArticleListItemDto>>(
            "/api/admin/articles");
        Assert.DoesNotContain(otherList!, article => article.Id == created.ArticleId);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync(
            $"/api/admin/articles/{created.ArticleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en",
            new UpdateArticleLocalizationRequest("Changed", null, "# Changed", "changed"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/publish",
            null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync(
            $"/api/admin/articles/{created.ArticleId}/localizations/en/unpublish",
            null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync(
            $"/api/admin/articles/{created.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [], []))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync(
            $"/api/admin/articles/{created.ArticleId}")).StatusCode);

        var ownerArticle = await ownerClient.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{created.ArticleId}");
        Assert.Equal("Owner article", ownerArticle!.Localizations.Single().Title);

        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await userManager.FindByNameAsync(ownerLogin);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(owner!.Id, (await dbContext.Articles.FindAsync(created.ArticleId))!.OwnerUserId);
    }

    [Fact]
    public async Task Taxonomy_WhenDatabaseIsEmpty_ReturnsEmptyCollections()
    {
        using var client = await CreateAuthenticatedClient();

        var taxonomy = await client.GetFromJsonAsync<AdminTaxonomyDto>("/api/admin/taxonomy");

        Assert.NotNull(taxonomy);
        Assert.Empty(taxonomy.Topics);
        Assert.Empty(taxonomy.Series);
        Assert.Empty(taxonomy.Tags);
    }

    [Fact]
    public async Task UpdateTaxonomy_ReplacesAssignmentsAndCreatesMissingTags()
    {
        await using var isolatedFactory = new AuthWebApplicationFactory();
        Guid topicId;
        Guid seriesId;

        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var topic = Topic.Create("en", "Engineering", "engineering", null, now);
            var series = Series.Create("en", "Fourier transforms", "fourier-transforms", null, now);
            dbContext.AddRange(topic, series);
            await dbContext.SaveChangesAsync();
            topicId = topic.Id;
            seriesId = series.Id;
        }

        using var client = await CreateAuthenticatedClient(isolatedFactory);
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/articles",
            new CreateArticleRequest(
                "en",
                "FFT",
                null,
                "# FFT",
                $"fft-{Guid.NewGuid():N}"));
        var article = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();

        var assignResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{article!.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest(
                [topicId],
                [new SeriesAssignmentRequest(seriesId, 1)],
                ["dotnet", "Backend", "DOTNET"]));

        Assert.True(
            assignResponse.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204, received {(int)assignResponse.StatusCode}: {await assignResponse.Content.ReadAsStringAsync()}");
        var details = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{article.ArticleId}");
        Assert.Equal([topicId], details!.TopicIds);
        Assert.Equal(new SeriesAssignmentDto(seriesId, 1), Assert.Single(details.Series));
        Assert.Equal(["Backend", "dotnet"], details.Tags);

        var clearResponse = await client.PutAsJsonAsync(
            $"/api/admin/articles/{article.ArticleId}/taxonomy",
            new UpdateArticleTaxonomyRequest([], [], []));
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        details = await client.GetFromJsonAsync<AdminArticleDetailsDto>(
            $"/api/admin/articles/{article.ArticleId}");
        Assert.Empty(details!.TopicIds);
        Assert.Empty(details.Series);
        Assert.Empty(details.Tags);
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        return await CreateAuthenticatedClient(factory);
    }

    private static async Task<HttpClient> CreateAuthenticatedClient(
        AuthWebApplicationFactory applicationFactory)
    {
        var client = applicationFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(AuthWebApplicationFactory.AdminLogin, AuthWebApplicationFactory.AdminPassword));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task<HttpClient> CreateUserClient(
        AuthWebApplicationFactory applicationFactory,
        string login,
        string password)
    {
        await using (var scope = applicationFactory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.CreateAsync(new ApplicationUser { UserName = login }, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var client = applicationFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, password));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }
}
