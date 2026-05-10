using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

/// <summary>
/// Bridges Microsoft.Extensions.DependencyInjection with Spectre.Console.Cli.
/// </summary>
public class DependencyInjectionTypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public DependencyInjectionTypeRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public ITypeResolver Build()
    {
        return new DependencyInjectionTypeResolver(_services.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }
}

internal class DependencyInjectionTypeResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;

    public DependencyInjectionTypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type == null) return null;
        return _provider.GetService(type);
    }
}
