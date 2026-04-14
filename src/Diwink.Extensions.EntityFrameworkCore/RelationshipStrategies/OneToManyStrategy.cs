using Diwink.Extensions.EntityFrameworkCore.GraphUpdate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Diwink.Extensions.EntityFrameworkCore.RelationshipStrategies;

/// <summary>
/// Handles one-to-many (principal-side collection) mutations.
/// Adds new children, updates existing children (scalars + recursive navigations),
/// and removes missing children (delete for required FK, null FK for optional FK).
/// </summary>
internal static class OneToManyStrategy
{
    /// <summary>
    /// Synchronizes a one-to-many collection navigation so its child entities
    /// match the provided updated collection.
    /// </summary>
    /// <param name="context">The DbContext used to access entity metadata and change tracking.</param>
    /// <param name="existingNavigation">The tracked collection navigation to update.</param>
    /// <param name="updatedCollection">The desired child entities to reconcile the navigation with.</param>
    /// <param name="aggregateType">The aggregate root CLR type, passed through to recursive navigation updates on children.</param>
    public static void Apply(
        DbContext context,
        CollectionEntry existingNavigation,
        IEnumerable<object> updatedCollection,
        Type aggregateType,
        HashSet<object> visitedEntities)
    {
        var existingItems = existingNavigation.CurrentValue?.Cast<object>().ToList() ?? [];
        var updatedItems = updatedCollection.ToList();

        var navMetadata = (INavigation)existingNavigation.Metadata;
        var foreignKey = navMetadata.ForeignKey;
        var isFkRequired = foreignKey.IsRequired;

        // Remove children not present in the updated collection
        foreach (var existingItem in existingItems)
        {
            var existingKeys = EntityKeyHelper.GetKeyValues(context.Entry(existingItem));
            var match = EntityKeyHelper.FindByKey(context, updatedItems, existingKeys);
            if (match is null)
            {
                if (isFkRequired)
                {
                    // Required FK — delete the child entity
                    context.Remove(existingItem);
                }
                else
                {
                    // Optional FK — null the FK, preserve the entity
                    NullForeignKey(context, existingItem, foreignKey);
                    CollectionHelper.Remove(existingNavigation, existingItem);
                }
            }
        }

        // Update existing or add new children
        foreach (var updatedItem in updatedItems)
        {
            var updatedKeys = EntityKeyHelper.GetKeyValues(context, updatedItem);
            var existingMatch = EntityKeyHelper.FindByKeyInTracked(
                context, existingItems, updatedKeys);

            if (existingMatch is not null)
            {
                // Update existing child — scalars + recursive navigations
                var childEntry = context.Entry(existingMatch);
                childEntry.CurrentValues.SetValues(updatedItem);
                GraphUpdateOrchestrator.ApplyNavigations(
                    context, childEntry, updatedItem, aggregateType, visitedEntities);
            }
            else
            {
                // New child — track as Added and add to collection
                var newEntry = context.Entry(updatedItem);
                if (newEntry.State == EntityState.Detached)
                    newEntry.State = EntityState.Added;

                CollectionHelper.Add(existingNavigation, updatedItem);
            }
        }
    }

    /// <summary>
    /// Nulls all FK properties on the child entity and marks them as modified.
    /// </summary>
    private static void NullForeignKey(
        DbContext context,
        object childEntity,
        IForeignKey foreignKey)
    {
        var childEntry = context.Entry(childEntity);
        foreach (var fkProperty in foreignKey.Properties)
        {
            childEntry.Property(fkProperty.Name).CurrentValue = null;
            childEntry.Property(fkProperty.Name).IsModified = true;
        }
    }
}
