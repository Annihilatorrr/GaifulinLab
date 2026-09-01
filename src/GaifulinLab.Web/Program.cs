using GaifulinLab.Web;
using GaifulinLab.Web.Articles;
using GaifulinLab.Web.Authentication;
using GaifulinLab.Web.Components;
using GaifulinLab.Web.Content;
using GaifulinLab.Web.Media;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var apiBaseAddress = new Uri(
    new Uri(builder.HostEnvironment.BaseAddress),
    builder.Configuration["Api:BaseUrl"] ?? "/");
var pdfApiBaseAddress = new Uri(
    new Uri(builder.HostEnvironment.BaseAddress),
    builder.Configuration["Api:PdfBaseUrl"]
    ?? builder.Configuration["Api:BaseUrl"]
    ?? "/");

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AccessTokenStore>();
builder.Services.AddScoped<TokenAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<TokenAuthenticationStateProvider>());
builder.Services.AddScoped(serviceProvider =>
{
    var handler = new AdminAuthorizationHandler(
        serviceProvider.GetRequiredService<AccessTokenStore>(),
        serviceProvider.GetRequiredService<TokenAuthenticationStateProvider>(),
        serviceProvider.GetRequiredService<NavigationManager>())
    {
        InnerHandler = new HttpClientHandler()
    };

    return new HttpClient(handler)
    {
        BaseAddress = apiBaseAddress
    };
});
builder.Services.AddScoped<AdminAuthClient>();
builder.Services.AddScoped<AdminArticlesClient>();
builder.Services.AddScoped<AdminMarkdownClient>();
builder.Services.AddScoped<AdminMediaClient>();
builder.Services.AddScoped(serviceProvider => new PublicContentClient(
    serviceProvider.GetRequiredService<HttpClient>(),
    pdfApiBaseAddress));

await builder.Build().RunAsync();
