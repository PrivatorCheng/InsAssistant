using System.Reflection;
using API.Attributes;

namespace API.Infrastructure;

/// <summary>
/// 自動註冊服務擴充方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAttributedServices(this IServiceCollection services, Assembly assembly)
    {
        var candidates = assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Select(t => new
            {
                Type = t,
                Attribute = t.GetCustomAttribute<ServiceAttribute>()
            })
            .Where(x => x.Attribute is not null)
            .ToList();

        foreach (var candidate in candidates)
        {
            var serviceType = candidate.Type.GetInterfaces().FirstOrDefault() ?? candidate.Type;
            switch (candidate.Attribute!.Lifetime)
            {
                case ServiceLifetime.Transient:
                    services.AddTransient(serviceType, candidate.Type);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScoped(serviceType, candidate.Type);
                    break;
                default:
                    services.AddSingleton(serviceType, candidate.Type);
                    break;
            }
        }

        return services;
    }
}
