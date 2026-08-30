using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace GaifulinLab.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(ApplicationAssembly).Assembly));
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
