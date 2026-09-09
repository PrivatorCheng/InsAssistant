namespace API.Attributes;

/// <summary>
/// 標記可自動註冊到 DI 容器的服務類別。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute
{
    public ServiceAttribute(ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Lifetime = lifetime;
    }

    public ServiceLifetime Lifetime { get; }
}
