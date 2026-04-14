using System.Collections;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Diwink.Extensions.EntityFrameworkCore.GraphUpdate;

/// <summary>
/// Shared helpers for adding and removing items from EF Core collection navigations.
/// Handles both IList (fast path) and generic ICollection&lt;T&gt; (reflection fallback).
/// </summary>
internal static class CollectionHelper
{
    /// <summary>
    /// Add an entity instance to the given navigation collection.
    /// </summary>
    internal static void Add(CollectionEntry navigation, object item)
    {
        ExecuteOperation(navigation, item, "add", static (list, value) =>
        {
            list.Add(value);
        });
    }

    /// <summary>
    /// Remove an entity instance from the given navigation collection.
    /// </summary>
    internal static void Remove(CollectionEntry navigation, object item)
    {
        ExecuteOperation(navigation, item, "remove", static (list, value) =>
        {
            list.Remove(value);
        });
    }

    /// <summary>
    /// Performs an add/remove operation against the runtime collection held by a navigation's CurrentValue.
    /// </summary>
    private static void ExecuteOperation(
        CollectionEntry navigation,
        object item,
        string operation,
        Action<IList, object> listOperation)
    {
        var currentValue = navigation.CurrentValue ?? throw new InvalidOperationException(
            $"Collection navigation '{navigation.Metadata.DeclaringEntityType.ClrType.Name}.{navigation.Metadata.Name}' has null CurrentValue; cannot {operation} item '{item}'.");

        if (currentValue is IList list)
        {
            listOperation(list, item);
            return;
        }

        var collectionInterface = currentValue.GetType().GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ICollection<>) &&
                i.GenericTypeArguments[0].IsAssignableFrom(item.GetType()));

        if (collectionInterface is null)
        {
            throw new InvalidOperationException(
                $"Collection navigation '{navigation.Metadata.DeclaringEntityType.ClrType.Name}.{navigation.Metadata.Name}' with current value type '{currentValue.GetType().FullName}' does not support {operation} for item type '{item.GetType().FullName}'.");
        }

        var methodName = operation == "add" ? nameof(ICollection<object>.Add) : nameof(ICollection<object>.Remove);
        var method = collectionInterface.GetMethod(methodName) ?? throw new InvalidOperationException(
            $"Collection interface '{collectionInterface.FullName}' for navigation '{navigation.Metadata.DeclaringEntityType.ClrType.Name}.{navigation.Metadata.Name}' does not expose '{methodName}'.");

        method.Invoke(currentValue, [item]);
    }
}
