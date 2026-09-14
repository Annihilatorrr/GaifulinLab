using GaifulinLab.Web;
using GaifulinLab.Web.Articles;
using GaifulinLab.Web.Authentication;
using GaifulinLab.Web.Components;
using GaifulinLab.Web.Content;
using GaifulinLab.Web.Media;
using GaifulinLab.Web.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var apiBaseAddress = new Uri(
    new Uri(builder.HostEnvironment.BaseAddress),
    builder.Configuration["Api:BaseUrl"] ?? "/");

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped(serviceProvider => new LocalizationService(
    serviceProvider.GetRequiredService<IJSRuntime>(),
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) }));
builder.Services.AddScoped<AccessTokenStore>();
builder.Services.AddScoped(_ => new SessionRefreshClient(new HttpClient { BaseAddress = apiBaseAddress }));
builder.Services.AddScoped<AccessTokenRefreshCoordinator>();
builder.Services.AddScoped(serviceProvider => new TokenAuthenticationStateProvider(
    serviceProvider.GetRequiredService<AccessTokenStore>(),
    serviceProvider.GetRequiredService<AccessTokenRefreshCoordinator>()));
builder.Services.AddScoped<AuthenticationStateProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<TokenAuthenticationStateProvider>());
builder.Services.AddScoped(serviceProvider =>
{
    var handler = new AdminAuthorizationHandler(
        serviceProvider.GetRequiredService<AccessTokenStore>(),
        serviceProvider.GetRequiredService<TokenAuthenticationStateProvider>(),
        serviceProvider.GetRequiredService<NavigationManager>(),
        serviceProvider.GetRequiredService<AccessTokenRefreshCoordinator>())
    {
        InnerHandler = new HttpClientHandler()
    };

    return new HttpClient(handler)
    {
        BaseAddress = apiBaseAddress
    };
});
builder.Services.AddScoped<AdminAuthClient>();
builder.Services.AddScoped<RegistrationClient>();
builder.Services.AddScoped<ProfileClient>();
builder.Services.AddScoped<AdminArticlesClient>();
builder.Services.AddScoped<AdminHtmlClient>();
builder.Services.AddScoped<AdminMediaClient>();
builder.Services.AddScoped(serviceProvider => new PublicContentClient(
    serviceProvider.GetRequiredService<HttpClient>()));

await builder.Build().RunAsync();
