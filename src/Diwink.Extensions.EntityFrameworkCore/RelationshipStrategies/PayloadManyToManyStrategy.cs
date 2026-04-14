using Diwink.Extensions.EntityFrameworkCore.GraphUpdate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Diwink.Extensions.EntityFrameworkCore.RelationshipStrategies;

/// <summary>
/// Handles many-to-many with payload (association entity) mutations.
/// Creates, updates, and removes association entities with payload.
/// Related entities are preserved on removal (FR-002, FR-005, FR-006).
/// </summary>
internal static class PayloadManyToManyStrategy
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
        IEnumerable<object> updatedCollection,
        Type aggregateType,
        HashSet<object> visitedEntities)
    {
        var existingItems = existingNavigation.CurrentValue?.Cast<object>().ToList() ?? [];
        var updatedItems = updatedCollection.ToList();

        // Remove association entities not present in updated collection
        foreach (var existingItem in existingItems)
        {
            var existingKeys = EntityKeyHelper.GetKeyValues(context.Entry(existingItem));
            var match = EntityKeyHelper.FindByKey(context, updatedItems, existingKeys);
            if (match is null)
            {
                // Remove the association entity — EF Core will delete the row
                // Related entities are NOT deleted (FR-003 for payload associations)
                context.Remove(existingItem);
            }
        }

        // Add new or update existing association entities
        foreach (var updatedItem in updatedItems)
        {
            var updatedKeys = EntityKeyHelper.GetKeyValues(context, updatedItem);
            var existingMatch = EntityKeyHelper.FindByKeyInTracked(context, existingItems, updatedKeys);

            if (existingMatch is not null)
            {
                // Update payload fields + recursive navigations on existing association entity
                var associationEntry = context.Entry(existingMatch);
                associationEntry.CurrentValues.SetValues(updatedItem);
                GraphUpdateOrchestrator.ApplyNavigations(
                    context, associationEntry, updatedItem, aggregateType, visitedEntities);
            }
            else
            {
                // New association entity — add to collection
                CollectionHelper.Add(existingNavigation, updatedItem);
            }
        }
    }
}
