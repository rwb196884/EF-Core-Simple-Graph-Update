using System.Collections;
using Diwink.Extensions.EntityFrameworkCore.GraphUpdate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Diwink.Extensions.EntityFrameworkCore.RelationshipStrategies;

/// <summary>
/// Handles one-to-many mutations.
/// Creates, updates, and removes entities.
/// </summary>
internal static class OneToManyStrategy
{
    /// <summary>
    /// Synchronizes a payload-based many-to-many collection navigation so its association entities match the provided target collection.
    /// </summary>
    /// <remarks>
    /// Removes association entities that are not present in <paramref name="updatedCollection"/>, updates payload values on matching tracked associations, and adds new association entities to the navigation when no tracked match exists. Deleting an association entity only removes the association row; related non-association entities are not deleted.
    /// </remarks>
    /// <param name="context">The DbContext used to inspect entity keys and apply changes.</param>
    /// <param name="existingNavigation">The collection navigation that currently holds the association entities (may be null/empty).</param>
    /// <param name="updatedCollection">The desired collection of association entity instances to reconcile the navigation with.</param>
    /// <exception cref="System.InvalidOperationException">Thrown if the navigation's current collection value is null or does not expose a public Add method when attempting to add a new association entity.</exception>
    public static void Apply(
        DbContext context,
        CollectionEntry existingNavigation,
        IEnumerable<object> updatedCollection)
    {
        List<object> existingItems = existingNavigation.CurrentValue?.Cast<object>().ToList() ?? [];
        List<object> updatedItems = updatedCollection.ToList();

        // Remove association entities not present in updated collection
        foreach (object existingItem in existingItems)
        {
            object[] existingKeys = EntityKeyHelper.GetKeyValues(context.Entry(existingItem));
            object? match = EntityKeyHelper.FindByKey(context, updatedItems, existingKeys);
            if (match is null)
            {
                // Remove the association entity — EF Core will delete the row.
                context.Remove(existingItem);
            }
        }

        // Add new or update existing association entities
        foreach (object updatedItem in updatedItems)
        {
            object[] updatedKeys = EntityKeyHelper.GetKeyValues(context, updatedItem);
            object? existingMatch = FindInTracked(context, existingItems, updatedKeys);

            if (existingMatch is not null)
            {
                // Update fields on existing entity.
                context.Entry(existingMatch).CurrentValues.SetValues(updatedItem);
            }
            else
            {
                // New entity --— add to collection.
                AddToCollection(existingNavigation, updatedItem);
            }
        }
    }

    /// <summary>
    /// Finds the first object in the trackedItems whose entity key values match the provided targetKeys.
    /// </summary>
    /// <param name="trackedItems">List of currently tracked entity instances to search for a key match.</param>
    /// <param name="targetKeys">Array of key values to match against each tracked item's entity key.</param>
    /// <returns>The first tracked item whose key values equal <paramref name="targetKeys"/>, or <c>null</c> if no match is found.</returns>
    private static object? FindInTracked(
        DbContext context,
        List<object> trackedItems,
        object[] targetKeys)
    {
        foreach (var item in trackedItems)
        {
            var itemKeys = EntityKeyHelper.GetKeyValues(context.Entry(item));
            if (EntityKeyHelper.KeysEqual(itemKeys, targetKeys))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Adds an association entity instance to the specified collection navigation.
    /// </summary>
    /// <param name="navigation">The collection navigation whose underlying collection will receive the item.</param>
    /// <param name="item">The association entity instance to add to the collection.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the navigation's current collection value is null or if the collection type does not expose a public Add method.
    /// </exception>
    private static void AddToCollection(CollectionEntry navigation, object item)
    {
        var currentValue = navigation.CurrentValue ?? throw new InvalidOperationException(
            $"Collection navigation '{navigation.Metadata.DeclaringEntityType.ClrType.Name}.{navigation.Metadata.Name}' is null; cannot add item type '{item.GetType().FullName}'.");

        if (currentValue is IList list)
        {
            list.Add(item);
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
                $"Collection type '{currentValue.GetType().FullName}' for navigation '{navigation.Metadata.DeclaringEntityType.ClrType.Name}.{navigation.Metadata.Name}' does not expose a public Add method for item type '{item.GetType().FullName}'.");
        }

        var addMethod = collectionInterface.GetMethod(nameof(ICollection<object>.Add)) ?? throw new InvalidOperationException(
            $"Collection type '{currentValue.GetType().FullName}' for navigation '{navigation.Metadata.DeclaringEntityType.ClrType.Name}.{navigation.Metadata.Name}' does not expose a public Add method for item type '{item.GetType().FullName}'.");

        addMethod.Invoke(currentValue, [item]);
    }
}
