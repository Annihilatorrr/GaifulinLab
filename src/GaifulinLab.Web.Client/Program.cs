using GaifulinLab.Web.Client.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

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
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});
builder.Services.AddScoped<AdminAuthClient>();

await builder.Build().RunAsync();
