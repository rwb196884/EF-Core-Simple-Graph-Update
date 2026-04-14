using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Contracts.OneToMany;

/// <summary>
/// Integration contract tests for required one-to-many (LearningCatalog -> Course).
/// Required FK removal => cascade delete.
/// </summary>
public class RequiredOneToManyContractTests : IntegrationTestBase
{
    public RequiredOneToManyContractTests(SqlServerContainerFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var ctx = CreateContext();
        await SeedData.SeedFullScenarioAsync(ctx);
    }

    [Fact]
    public async Task Add_course_to_catalog()
    {
        var newCourseId = Guid.NewGuid();

        await using var ctx = CreateContext();
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        var updated = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = existing.Name,
            Courses =
            [
                ..existing.Courses.Select(c => new Course
                {
                    Id = c.Id,
                    CatalogId = c.CatalogId,
                    Title = c.Title,
                    Code = c.Code
                }),
                new Course
                {
                    Id = newCourseId,
                    CatalogId = SeedData.CatalogId,
                    Title = "New Course",
                    Code = "NC-301"
                }
            ]
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var catalog = await verifyCtx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);
        catalog.Courses.Should().HaveCount(3);
        catalog.Courses.Should().Contain(c => c.Id == newCourseId && c.Title == "New Course");
    }

    [Fact]
    public async Task Remove_course_from_catalog_deletes_it()
    {
        await using var ctx = CreateContext();
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        // Keep only Course1, remove Course2
        var updated = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = existing.Name,
            Courses =
            [
                new Course
                {
                    Id = SeedData.Course1Id,
                    CatalogId = SeedData.CatalogId,
                    Title = "Software Design",
                    Code = "SD-101"
                }
            ]
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var catalog = await verifyCtx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);
        catalog.Courses.Should().ContainSingle().Which.Id.Should().Be(SeedData.Course1Id);

        var deletedCourse = await verifyCtx.Courses.FirstOrDefaultAsync(c => c.Id == SeedData.Course2Id);
        deletedCourse.Should().BeNull("required FK removal should delete the child");
    }

    [Fact]
    public async Task Update_course_scalars()
    {
        await using var ctx = CreateContext();
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        var updated = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = existing.Name,
            Courses = existing.Courses.Select(c => new Course
            {
                Id = c.Id,
                CatalogId = c.CatalogId,
                Title = c.Id == SeedData.Course1Id ? "Retitled Course" : c.Title,
                Code = c.Code
            }).ToList()
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var course = await verifyCtx.Courses.FirstAsync(c => c.Id == SeedData.Course1Id);
        course.Title.Should().Be("Retitled Course");
    }

    [Fact]
    public async Task Update_course_with_nested_one_to_one()
    {
        // Tests recursive apply: update Course scalars AND its nested CoursePolicy
        await using var ctx = CreateContext();
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
            .ThenInclude(c => c.Policy)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        var updated = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = existing.Name,
            Courses = existing.Courses.Select(c => new Course
            {
                Id = c.Id,
                CatalogId = c.CatalogId,
                Title = c.Id == SeedData.Course1Id ? "Updated Title" : c.Title,
                Code = c.Code,
                Policy = c.Policy is not null
                    ? new CoursePolicy
                    {
                        CourseId = c.Id,
                        PolicyVersion = c.Id == SeedData.Course1Id ? "3.0-nested" : c.Policy.PolicyVersion,
                        IsMandatory = c.Policy.IsMandatory
                    }
                    : null
            }).ToList()
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var course = await verifyCtx.Courses
            .Include(c => c.Policy)
            .FirstAsync(c => c.Id == SeedData.Course1Id);
        course.Title.Should().Be("Updated Title");
        course.Policy!.PolicyVersion.Should().Be("3.0-nested");
    }

    [Fact]
    public async Task Replace_entire_collection()
    {
        var newCourse1Id = Guid.NewGuid();
        var newCourse2Id = Guid.NewGuid();

        await using var ctx = CreateContext();
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        var updated = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = existing.Name,
            Courses =
            [
                new Course { Id = newCourse1Id, CatalogId = SeedData.CatalogId, Title = "Brand New 1", Code = "BN-001" },
                new Course { Id = newCourse2Id, CatalogId = SeedData.CatalogId, Title = "Brand New 2", Code = "BN-002" }
            ]
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var catalog = await verifyCtx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);
        catalog.Courses.Should().HaveCount(2);
        catalog.Courses.Select(c => c.Id).Should().BeEquivalentTo([newCourse1Id, newCourse2Id]);

        // Old courses should be deleted (required FK)
        var oldCourse1 = await verifyCtx.Courses.FirstOrDefaultAsync(c => c.Id == SeedData.Course1Id);
        var oldCourse2 = await verifyCtx.Courses.FirstOrDefaultAsync(c => c.Id == SeedData.Course2Id);
        oldCourse1.Should().BeNull();
        oldCourse2.Should().BeNull();
    }

    [Fact]
    public async Task Unchanged_collection_is_no_op()
    {
        await using var ctx = CreateContext();
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        var updated = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = existing.Name,
            Courses = existing.Courses.Select(c => new Course
            {
                Id = c.Id,
                CatalogId = c.CatalogId,
                Title = c.Title,
                Code = c.Code
            }).ToList()
        };

        ctx.UpdateGraph(existing, updated);
        await ctx.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var catalog = await verifyCtx.LearningCatalogs
            .Include(c => c.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);
        catalog.Courses.Should().HaveCount(2);
    }
}
