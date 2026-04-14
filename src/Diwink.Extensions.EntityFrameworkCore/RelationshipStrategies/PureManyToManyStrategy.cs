using Diwink.Extensions.EntityFrameworkCore.GraphUpdate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Diwink.Extensions.EntityFrameworkCore.RelationshipStrategies;

/// <summary>
/// Handles pure many-to-many (skip navigation) mutations.
/// Adds missing links, removes excess links, creates new related entities,
/// and updates existing related entities (FR-002, FR-003, FR-004).
/// </summary>
internal static class PureManyToManyStrategy
{
    /// <summary>
    /// Reconciles a skip-navigation (pure many-to-many) collection with the provided updated set of related entities.
    /// </summary>
    /// <remarks>
    /// Removes links for entities that are no longer present in <paramref name="updatedCollection"/>, updates properties of already-tracked related entities, and adds links for new or discovered entities (marking new entities as added so EF will insert them).</remarks>
    /// <param name="context">The DbContext used to access entity metadata, track entities, and query the store.</param>
    /// <param name="existingNavigation">The existing collection navigation entry (skip navigation) to modify.</param>
    /// <param name="updatedCollection">The desired related entities to be reflected in the navigation; each element represents a related entity instance or a DTO with matching key values.</param>
    public static void Apply(
        DbContext context,
        CollectionEntry existingNavigation,
        IEnumerable<object> updatedCollection,
        Type aggregateType,
        HashSet<object> visitedEntities)
    {
        var existingItems = existingNavigation.CurrentValue?.Cast<object>().ToList() ?? [];
        var updatedItems = updatedCollection.ToList();

        // Remove links not present in the updated collection
        foreach (var existingItem in existingItems)
        {
            var existingKeys = EntityKeyHelper.GetKeyValues(context.Entry(existingItem));
            var match = EntityKeyHelper.FindByKey(context, updatedItems, existingKeys);
            if (match is null)
            {
                // Unlink only — remove from the collection navigation
                // EF Core will handle removing the join table row
                CollectionHelper.Remove(existingNavigation, existingItem);
            }
        }

        // Add new links or update existing related entities
        foreach (var updatedItem in updatedItems)
        {
            var updatedKeys = EntityKeyHelper.GetKeyValues(context, updatedItem);
            var existingMatch = EntityKeyHelper.FindByKeyInTracked(context, existingItems, updatedKeys);

            if (existingMatch is not null)
            {
                // Update existing related entity properties + recursive navigations
                ApplyValuesIfNotModified(context, existingMatch, updatedItem);
                GraphUpdateOrchestrator.ApplyNavigations(
                    context, context.Entry(existingMatch), updatedItem, aggregateType, visitedEntities);
            }
            else
            {
                // Entity not in this collection — resolve via tracker or store
                var entityType = context.Model.FindEntityType(updatedItem.GetType());
                var pk = entityType?.FindPrimaryKey();
                if (pk is not null)
                {
                    // Find checks the change tracker first (no DB hit if already tracked),
                    // then falls back to a store query. Per-item queries are acceptable
                    // here because this path only runs for entities not already in the
                    // current collection — typically a small count per graph update.
                    var knownEntity = context.Find(entityType!.ClrType, updatedKeys);

                    if (knownEntity is not null)
                    {
                        // Entity exists — update properties and create link
                        ApplyValuesIfNotModified(context, knownEntity, updatedItem);
                        CollectionHelper.Add(existingNavigation, knownEntity);
                    }
                    else
                    {
                        // New entity — explicitly track as Added so EF inserts it
                        context.Add(updatedItem);
                        CollectionHelper.Add(existingNavigation, updatedItem);
                    }
                }
                else
                {
                    CollectionHelper.Add(existingNavigation, updatedItem);
                }
            }
        }
    }

    private static void ApplyValuesIfNotModified(DbContext context, object trackedEntity, object updatedEntity)
    {
        var trackedEntry = context.Entry(trackedEntity);
        if (trackedEntry.State is EntityState.Unchanged or EntityState.Detached)
        {
            trackedEntry.CurrentValues.SetValues(updatedEntity);
        }
    }
}
