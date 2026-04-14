using Diwink.Extensions.EntityFrameworkCore.TestModel;
using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Unit.RelationshipSemantics;

/// <summary>
/// Unit tests for one-to-many update behavior — required FK (cascade delete)
/// and optional FK (SetNull) removal, in-place update, and add via UpdateGraph().
/// Uses InMemory provider for fast isolated testing.
/// </summary>
public class OneToManyUpdateBehaviorTests
{
    private static TestDbContext CreateInMemoryContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task Required_FK_remove_child_deletes_it()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var course1Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses =
                [
                    new Course { Id = course1Id, CatalogId = catalogId, Title = "Keep", Code = "C-001" },
                    new Course { Id = course2Id, CatalogId = catalogId, Title = "Remove", Code = "C-002" }
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
                    new Course { Id = course1Id, CatalogId = catalogId, Title = "Keep", Code = "C-001" }
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
                .Which.Id.Should().Be(course1Id);

            var deletedCourse = await verifyCtx.Courses.FirstOrDefaultAsync(c => c.Id == course2Id);
            deletedCourse.Should().BeNull("required FK removal should delete the child");
        }
    }

    [Fact]
    public async Task Optional_FK_remove_child_nulls_fk_preserves_entity()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var review1Id = Guid.NewGuid();
        var review2Id = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog { Id = catalogId, Name = "Cat" });
            seedCtx.Courses.Add(new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = review1Id, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Great" },
                    new CourseReview { Id = review2Id, CourseId = courseId, ReviewerName = "Bob", Rating = 3, Comment = "OK" }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            var updated = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = review1Id, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Great" }
                ]
            };

            ctx.UpdateGraph(existing, updated);
            await ctx.SaveChangesAsync();
        }

        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var course = await verifyCtx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            course.Reviews.Should().ContainSingle()
                .Which.Id.Should().Be(review1Id);

            var detachedReview = await verifyCtx.CourseReviews.FirstOrDefaultAsync(r => r.Id == review2Id);
            detachedReview.Should().NotBeNull("optional FK removal should preserve the entity");
            detachedReview!.CourseId.Should().BeNull("FK should be nulled");
        }
    }

    [Fact]
    public async Task Update_child_scalar_properties()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog { Id = catalogId, Name = "Cat" });
            seedCtx.Courses.Add(new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = reviewId, CourseId = courseId, ReviewerName = "Alice", Rating = 3, Comment = "Original" }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            var updated = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = reviewId, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Updated" }
                ]
            };

            ctx.UpdateGraph(existing, updated);
            await ctx.SaveChangesAsync();
        }

        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var review = await verifyCtx.CourseReviews.FirstAsync(r => r.Id == reviewId);
            review.Rating.Should().Be(5);
            review.Comment.Should().Be("Updated");
        }
    }

    [Fact]
    public async Task Add_new_child_to_collection()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var existingReviewId = Guid.NewGuid();
        var newReviewId = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog { Id = catalogId, Name = "Cat" });
            seedCtx.Courses.Add(new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = existingReviewId, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Existing" }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            var updated = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = existingReviewId, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Existing" },
                    new CourseReview { Id = newReviewId, CourseId = courseId, ReviewerName = "Bob", Rating = 4, Comment = "New review" }
                ]
            };

            ctx.UpdateGraph(existing, updated);
            await ctx.SaveChangesAsync();
        }

        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var course = await verifyCtx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            course.Reviews.Should().HaveCount(2);
            course.Reviews.Should().Contain(r => r.Id == newReviewId && r.ReviewerName == "Bob");
        }
    }

    [Fact]
    public async Task Mixed_add_update_remove()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var keepReviewId = Guid.NewGuid();
        var removeReviewId = Guid.NewGuid();
        var newReviewId = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog { Id = catalogId, Name = "Cat" });
            seedCtx.Courses.Add(new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = keepReviewId, CourseId = courseId, ReviewerName = "Alice", Rating = 3, Comment = "Original" },
                    new CourseReview { Id = removeReviewId, CourseId = courseId, ReviewerName = "Bob", Rating = 2, Comment = "Will remove" }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            var updated = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = keepReviewId, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Updated" },
                    new CourseReview { Id = newReviewId, CourseId = courseId, ReviewerName = "Charlie", Rating = 4, Comment = "New" }
                ]
            };

            ctx.UpdateGraph(existing, updated);
            await ctx.SaveChangesAsync();
        }

        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var course = await verifyCtx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            course.Reviews.Should().HaveCount(2);

            var keptReview = course.Reviews.First(r => r.Id == keepReviewId);
            keptReview.Rating.Should().Be(5);
            keptReview.Comment.Should().Be("Updated");

            course.Reviews.Should().Contain(r => r.Id == newReviewId && r.ReviewerName == "Charlie");

            // Removed review should still exist with null FK
            var removedReview = await verifyCtx.CourseReviews.FirstOrDefaultAsync(r => r.Id == removeReviewId);
            removedReview.Should().NotBeNull();
            removedReview!.CourseId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Empty_updated_collection_removes_all_children()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var review1Id = Guid.NewGuid();
        var review2Id = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog { Id = catalogId, Name = "Cat" });
            seedCtx.Courses.Add(new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews =
                [
                    new CourseReview { Id = review1Id, CourseId = courseId, ReviewerName = "Alice", Rating = 5, Comment = "Great" },
                    new CourseReview { Id = review2Id, CourseId = courseId, ReviewerName = "Bob", Rating = 4, Comment = "Good" }
                ]
            });
            await seedCtx.SaveChangesAsync();
        }

        {
            await using var ctx = CreateInMemoryContext(dbName);
            var existing = await ctx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            var updated = new Course
            {
                Id = courseId,
                CatalogId = catalogId,
                Title = "Course",
                Code = "C-001",
                Reviews = []
            };

            ctx.UpdateGraph(existing, updated);
            await ctx.SaveChangesAsync();
        }

        {
            await using var verifyCtx = CreateInMemoryContext(dbName);
            var course = await verifyCtx.Courses
                .Include(c => c.Reviews)
                .FirstAsync(c => c.Id == courseId);

            course.Reviews.Should().BeEmpty();

            // Both reviews should still exist with null FK (optional FK → SetNull)
            var allReviews = await verifyCtx.CourseReviews.Where(r => r.Id == review1Id || r.Id == review2Id).ToListAsync();
            allReviews.Should().HaveCount(2);
            allReviews.Should().AllSatisfy(r => r.CourseId.Should().BeNull());
        }
    }

    [Fact]
    public async Task Unchanged_collection_is_no_op()
    {
        var dbName = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid();
        var course1Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();

        {
            await using var seedCtx = CreateInMemoryContext(dbName);
            seedCtx.LearningCatalogs.Add(new LearningCatalog
            {
                Id = catalogId,
                Name = "Catalog",
                Courses =
                [
                    new Course { Id = course1Id, CatalogId = catalogId, Title = "Course 1", Code = "C-001" },
                    new Course { Id = course2Id, CatalogId = catalogId, Title = "Course 2", Code = "C-002" }
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
                    new Course { Id = course1Id, CatalogId = catalogId, Title = "Course 1", Code = "C-001" },
                    new Course { Id = course2Id, CatalogId = catalogId, Title = "Course 2", Code = "C-002" }
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

            catalog.Courses.Should().HaveCount(2);
        }
    }
}
