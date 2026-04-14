using System.Collections.Concurrent;
using System.Reflection;

namespace Diwink.Extensions.EntityFrameworkCore.GraphUpdate;

/// <summary>
/// Thread-safe cache for PropertyInfo lookups, avoiding repeated reflection
/// on the same (Type, propertyName) pair during graph update traversal.
/// </summary>
internal static class PropertyAccessorCache
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> Cache = new();

    /// <summary>
    /// Gets the PropertyInfo for the given type and property name, caching the result.
    /// Returns null if the property does not exist on the type.
    /// </summary>
    public static PropertyInfo? GetProperty(Type type, string propertyName)
    {
        return Cache.GetOrAdd((type, propertyName), static key => key.Type.GetProperty(key.Name));
    }
}
