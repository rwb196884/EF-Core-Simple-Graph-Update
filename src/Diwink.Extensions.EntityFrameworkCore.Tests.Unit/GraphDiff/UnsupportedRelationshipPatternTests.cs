using Diwink.Extensions.EntityFrameworkCore.TestModel;
using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Unit.GraphDiff;

public class UnsupportedRelationshipPatternTests
{
    private static TestDbContext CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task In_place_scalar_edit_in_one_to_many_is_applied()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses =
                [
                    new Course
                    {
                        Id = courseId,
                        CatalogId = catalogId,
                        Title = "Original",
                        Code = "C-001"
                    }
                ]
            });

            await seedCtx.SaveChangesAsync();
        }

        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.LearningCatalogs
                .Include(c => c.Courses)
                .FirstAsync(c => c.Id == catalogId);

            var updated = new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses =
                [
                    new Course
                    {
                        Id = courseId,
                        CatalogId = catalogId,
                        Title = "Retitled",
                        Code = "C-001"
                    }
                ]
            };

            ctx.UpdateGraph(existing, updated);
            await ctx.SaveChangesAsync();
        }

        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var catalog = await verifyCtx.LearningCatalogs
                .Include(c => c.Courses)
                .FirstAsync(c => c.Id == catalogId);

            catalog.Courses.Should().ContainSingle()
                .Which.Title.Should().Be("Retitled");
        }
    }
}
