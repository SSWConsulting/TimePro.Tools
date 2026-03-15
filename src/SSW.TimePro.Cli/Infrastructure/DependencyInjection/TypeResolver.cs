using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Infrastructure.DependencyInjection;

/// <summary>
/// Resolves types from the DI container for Spectre.Console.Cli commands.
/// </summary>
public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
            return null;

        return _provider.GetService(type);
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }
}
