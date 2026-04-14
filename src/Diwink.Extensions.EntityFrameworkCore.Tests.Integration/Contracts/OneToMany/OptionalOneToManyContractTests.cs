using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Contracts.OneToMany;

/// <summary>
/// Integration contract tests for optional one-to-many (Course -> CourseReview).
/// Optional FK removal => null FK, preserve entity (SetNull behavior).
/// </summary>
public class OptionalOneToManyContractTests : IntegrationTestBase
{
    public OptionalOneToManyContractTests(SqlServerContainerFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var ctx = CreateContext();
        await SeedData.SeedFullScenarioAsync(ctx);
    }

    [Fact]
    public async Task Add_review_to_course()
    {
        var newReviewId = Guid.NewGuid();

        await using var ctx = CreateContext();
        var existing = await ctx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        var updated = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = existing.Title,
            Code = existing.Code,
            Reviews =
            [
                ..existing.Reviews.Select(r => new CourseReview
                {
                    Id = r.Id,
                    CourseId = r.CourseId,
                    ReviewerName = r.ReviewerName,
                    Rating = r.Rating,
                    Comment = r.Comment
                }),
                new CourseReview
                {
                    Id = newReviewId,
                    CourseId = SeedData.Course1Id,
                    ReviewerName = "Charlie",
                    Rating = 3,
                    Comment = "New review"
                }
            ]
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var course = await verifyCtx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);
        course.Reviews.Should().HaveCount(3);
        course.Reviews.Should().Contain(r => r.Id == newReviewId && r.ReviewerName == "Charlie");
    }

    [Fact]
    public async Task Remove_review_nulls_fk_preserves_entity()
    {
        await using var ctx = CreateContext();
        var existing = await ctx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        // Keep Review1, remove Review2
        var updated = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = existing.Title,
            Code = existing.Code,
            Reviews =
            [
                new CourseReview
                {
                    Id = SeedData.Review1Id,
                    CourseId = SeedData.Course1Id,
                    ReviewerName = "Alice",
                    Rating = 5,
                    Comment = "Excellent course"
                }
            ]
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var course = await verifyCtx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);
        course.Reviews.Should().ContainSingle().Which.Id.Should().Be(SeedData.Review1Id);

        // Review2 should still exist with null FK
        var detachedReview = await verifyCtx.CourseReviews.FirstOrDefaultAsync(r => r.Id == SeedData.Review2Id);
        detachedReview.Should().NotBeNull("optional FK removal should preserve the entity");
        detachedReview!.CourseId.Should().BeNull("FK should be nulled on removal");
    }

    [Fact]
    public async Task Update_review_scalars()
    {
        await using var ctx = CreateContext();
        var existing = await ctx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        var updated = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = existing.Title,
            Code = existing.Code,
            Reviews = existing.Reviews.Select(r => new CourseReview
            {
                Id = r.Id,
                CourseId = r.CourseId,
                ReviewerName = r.ReviewerName,
                Rating = r.Id == SeedData.Review1Id ? 1 : r.Rating,
                Comment = r.Id == SeedData.Review1Id ? "Updated comment" : r.Comment
            }).ToList()
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var review = await verifyCtx.CourseReviews.FirstAsync(r => r.Id == SeedData.Review1Id);
        review.Rating.Should().Be(1);
        review.Comment.Should().Be("Updated comment");
    }

    [Fact]
    public async Task Mixed_add_update_remove()
    {
        var newReviewId = Guid.NewGuid();

        await using var ctx = CreateContext();
        var existing = await ctx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        // Keep Review1 (updated), remove Review2, add new
        var updated = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = existing.Title,
            Code = existing.Code,
            Reviews =
            [
                new CourseReview
                {
                    Id = SeedData.Review1Id,
                    CourseId = SeedData.Course1Id,
                    ReviewerName = "Alice",
                    Rating = 2,
                    Comment = "Changed my mind"
                },
                new CourseReview
                {
                    Id = newReviewId,
                    CourseId = SeedData.Course1Id,
                    ReviewerName = "Dana",
                    Rating = 5,
                    Comment = "Brand new"
                }
            ]
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var course = await verifyCtx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        course.Reviews.Should().HaveCount(2);

        var updatedReview = course.Reviews.First(r => r.Id == SeedData.Review1Id);
        updatedReview.Rating.Should().Be(2);
        updatedReview.Comment.Should().Be("Changed my mind");

        course.Reviews.Should().Contain(r => r.Id == newReviewId && r.ReviewerName == "Dana");

        // Removed review preserved with null FK
        var removedReview = await verifyCtx.CourseReviews.FirstOrDefaultAsync(r => r.Id == SeedData.Review2Id);
        removedReview.Should().NotBeNull();
        removedReview!.CourseId.Should().BeNull();
    }

    [Fact]
    public async Task Empty_collection_detaches_all_reviews()
    {
        await using var ctx = CreateContext();
        var existing = await ctx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        var updated = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = existing.Title,
            Code = existing.Code,
            Reviews = []
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var course = await verifyCtx.Courses
            .Include(c => c.Reviews)
            .FirstAsync(c => c.Id == SeedData.Course1Id);
        course.Reviews.Should().BeEmpty();

        // Both reviews should still exist with null FK
        var allReviews = await verifyCtx.CourseReviews
            .Where(r => r.Id == SeedData.Review1Id || r.Id == SeedData.Review2Id)
            .ToListAsync();
        allReviews.Should().HaveCount(2);
        allReviews.Should().AllSatisfy(r => r.CourseId.Should().BeNull());
    }
}
