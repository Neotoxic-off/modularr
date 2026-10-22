using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;

namespace Modularr.Core.Tests.Library;

// Options that never change, for services reading IOptionsMonitor.
internal sealed class FixedOptionsMonitor(ModularrOptions value) : IOptionsMonitor<ModularrOptions>
{
    public ModularrOptions CurrentValue => value;

    public ModularrOptions Get(string? name) => value;

    public IDisposable? OnChange(Action<ModularrOptions, string?> listener) => null;
}
