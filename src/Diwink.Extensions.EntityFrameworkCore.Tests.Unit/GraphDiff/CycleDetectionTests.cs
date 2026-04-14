using Diwink.Extensions.EntityFrameworkCore.TestModel;
using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Unit.GraphDiff;

/// <summary>
/// Tests that the graph update engine handles bidirectional navigation cycles
/// without infinite recursion. Uses the TestDbContext's Course ↔ TopicTag
/// bidirectional skip navigation (Course.Tags / TopicTag.Courses) which creates
/// a natural cycle when the aggregate root is LearningCatalog.
///
/// Cycle detection uses a permanent HashSet with ReferenceEqualityComparer —
/// first-wins semantics means an entity's navigations are processed exactly once.
/// </summary>
public class CycleDetectionTests
{
    private static TestDbContext CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task Bidirectional_skip_nav_completes_without_infinite_recursion()
    {
        // Cycle path: LearningCatalog → Course → TopicTag → Course (same instance)
        // Without cycle detection, ApplyNavigations would recurse infinitely.
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        // Seed
        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog
            {
                Id = catalogId,
                Name = "Original Catalog",
                Courses =
                [
                    new Course
                    {
                        Id = courseId,
                        CatalogId = catalogId,
                        Title = "Original Course",
                        Code = "C-001",
                        Tags = [new TopicTag { Id = tagId, Label = "Original Tag" }]
                    }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        // Act
        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                        .ThenInclude(t => t.Courses)
                .FirstAsync(c => c.Id == catalogId);

            var updatedCourse = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Updated Course",
                Code = "C-001",
            };
            var updatedTag = new TopicTag
            {
                Id = tagId,
                Label = "Updated Tag",
                Courses = [updatedCourse]
            };
            updatedCourse.Tags = [updatedTag];

            var updatedCatalog = new LearningCatalog
            {
                Id = catalogId,
                Name = "Updated Catalog",
                Courses = [updatedCourse]
            };

            // Would cause StackOverflowException without cycle detection
            ctx.UpdateGraph(existing, updatedCatalog);
            await ctx.SaveChangesAsync();
        }

        // Verify
        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var catalog = await verifyCtx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                .FirstAsync(c => c.Id == catalogId);

            catalog.Name.Should().Be("Updated Catalog");

            var course = catalog.Courses.Should().ContainSingle().Subject;
            course.Title.Should().Be("Updated Course");

            var tag = course.Tags.Should().ContainSingle().Subject;
            tag.Label.Should().Be("Updated Tag");
        }
    }

    [Fact]
    public async Task First_visit_scalars_win_when_entity_reached_via_multiple_paths()
    {
        // The same Course entity (by PK) appears in two places in the detached graph
        // with different Title values. The first processing path (Catalog → Course)
        // sets Title; the second path (Tag → Course) is skipped by cycle detection.
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        // Seed
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
                        Code = "C-001",
                        Tags = [new TopicTag { Id = tagId, Label = "Tag" }]
                    }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        // Act
        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                        .ThenInclude(t => t.Courses)
                .FirstAsync(c => c.Id == catalogId);

            // Two distinct detached objects with the same PK but different titles
            var courseViaCatalog = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "FromCatalogPath",
                Code = "C-001",
            };
            var courseViaTag = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "FromTagPath",
                Code = "C-001",
            };

            var updatedTag = new TopicTag
            {
                Id = tagId,
                Label = "Tag",
                Courses = [courseViaTag]
            };
            courseViaCatalog.Tags = [updatedTag];

            var updatedCatalog = new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses = [courseViaCatalog]
            };

            ctx.UpdateGraph(existing, updatedCatalog);
            await ctx.SaveChangesAsync();
        }

        // Verify — first visit (Catalog → Course) wins
        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var course = await verifyCtx.Courses.FirstAsync(c => c.Id == courseId);
            course.Title.Should().Be("FromCatalogPath");
        }
    }

    [Fact]
    public async Task Multiple_entities_sharing_related_entity_are_all_processed()
    {
        // Two courses share the same tag. Both courses are processed, the tag
        // is processed once via the first course's Tags navigation.
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var course1Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();
        var sharedTagId = Guid.NewGuid();

        // Seed
        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            var sharedTag = new TopicTag { Id = sharedTagId, Label = "Shared" };
            seedCtx.LearningCatalogs.Add(new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses =
                [
                    new Course
                    {
                        Id = course1Id,
                        CatalogId = catalogId,
                        Title = "Course 1",
                        Code = "C-001",
                        Tags = [sharedTag]
                    },
                    new Course
                    {
                        Id = course2Id,
                        CatalogId = catalogId,
                        Title = "Course 2",
                        Code = "C-002",
                        Tags = [sharedTag]
                    }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        // Act
        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                        .ThenInclude(t => t.Courses)
                .FirstAsync(c => c.Id == catalogId);

            // Detached graph — both courses reference the shared tag
            var updatedSharedTag = new TopicTag { Id = sharedTagId, Label = "Updated Shared" };

            var updatedCourse1 = new Course
            {
                Id = course1Id,
                CatalogId = catalogId,
                Title = "Updated Course 1",
                Code = "C-001",
                Tags = [updatedSharedTag]
            };
            var updatedCourse2 = new Course
            {
                Id = course2Id,
                CatalogId = catalogId,
                Title = "Updated Course 2",
                Code = "C-002",
                Tags = [updatedSharedTag]
            };
            updatedSharedTag.Courses = [updatedCourse1, updatedCourse2];

            var updatedCatalog = new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses = [updatedCourse1, updatedCourse2]
            };

            ctx.UpdateGraph(existing, updatedCatalog);
            await ctx.SaveChangesAsync();
        }

        // Verify
        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var catalog = await verifyCtx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                .FirstAsync(c => c.Id == catalogId);

            catalog.Courses.Should().HaveCount(2);
            catalog.Courses.Should().Contain(c => c.Title == "Updated Course 1");
            catalog.Courses.Should().Contain(c => c.Title == "Updated Course 2");

            var tag = await verifyCtx.TopicTags.FirstAsync(t => t.Id == sharedTagId);
            tag.Label.Should().Be("Updated Shared");
        }
    }

    [Fact]
    public async Task Deep_graph_with_cycle_applies_all_navigation_types()
    {
        // Catalog → Course → Tag (cycle via Tag.Courses) + Course → Policy (one-to-one)
        // Both the M:M cycle path and the one-to-one path are processed on first visit.
        // CoursePolicy.Course back-reference creates a second cycle that cycle detection handles.
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        // Seed
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
                        Title = "Original Course",
                        Code = "C-001",
                        Tags = [new TopicTag { Id = tagId, Label = "Original Tag" }],
                        Policy = new CoursePolicy
                        {
                            CourseId = courseId,
                            PolicyVersion = "1.0",
                            IsMandatory = false
                        }
                    }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        // Act
        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                        .ThenInclude(t => t.Courses)
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Policy)
                .FirstAsync(c => c.Id == catalogId);

            var updatedCourse = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Updated Course",
                Code = "C-001",
            };
            var updatedPolicy = new CoursePolicy
            {
                CourseId = courseId,
                PolicyVersion = "2.0",
                IsMandatory = true,
                Course = updatedCourse
            };
            updatedCourse.Policy = updatedPolicy;

            var updatedTag = new TopicTag
            {
                Id = tagId,
                Label = "Updated Tag",
                Courses = [updatedCourse]
            };
            updatedCourse.Tags = [updatedTag];

            var updatedCatalog = new LearningCatalog
            {
                Id = catalogId,
                Name = "Updated Catalog",
                Courses = [updatedCourse]
            };

            ctx.UpdateGraph(existing, updatedCatalog);
            await ctx.SaveChangesAsync();
        }

        // Verify all navigation types applied
        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var catalog = await verifyCtx.LearningCatalogs
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Tags)
                .Include(c => c.Courses)
                    .ThenInclude(c => c.Policy)
                .FirstAsync(c => c.Id == catalogId);

            catalog.Name.Should().Be("Updated Catalog");

            var course = catalog.Courses.Should().ContainSingle().Subject;
            course.Title.Should().Be("Updated Course");

            var tag = course.Tags.Should().ContainSingle().Subject;
            tag.Label.Should().Be("Updated Tag");

            course.Policy.Should().NotBeNull();
            course.Policy!.PolicyVersion.Should().Be("2.0");
            course.Policy.IsMandatory.Should().BeTrue();
        }
    }
}
