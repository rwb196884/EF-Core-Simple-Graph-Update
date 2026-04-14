using Diwink.Extensions.EntityFrameworkCore.TestModel;
using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Contracts.CycleDetection;

/// <summary>
/// Integration tests verifying that bidirectional M:M cycles complete against
/// a real SQL Server database. Uses the LearningCatalog aggregate root where
/// Course ↔ TopicTag creates a natural cycle path via shared skip navigations.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CycleDetectionContractTests : IntegrationTestBase
{
    public CycleDetectionContractTests(SqlServerContainerFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Bidirectional_m2m_cycle_completes_with_correct_state()
    {
        // Arrange — seed full scenario: Course1 has [Tag1, Tag2], Course2 has [Tag2, Tag3]
        await using var seedCtx = CreateContext();
        await SeedData.SeedFullScenarioAsync(seedCtx);

        await using var ctx = CreateContext();
        // Load creates cycle: Catalog → Course → Tag → Course (same instances)
        var existing = await ctx.LearningCatalogs
            .Include(c => c.Courses)
                .ThenInclude(c => c.Tags)
                    .ThenInclude(t => t.Courses)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        // Build detached graph with bidirectional references matching tracked state
        var updatedTag1 = new TopicTag { Id = SeedData.Tag1Id, Label = "Updated Architecture" };
        var updatedTag2 = new TopicTag { Id = SeedData.Tag2Id, Label = "Updated Testing" };
        var updatedTag3 = new TopicTag { Id = SeedData.Tag3Id, Label = "Security" };

        var updatedCourse1 = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = "Updated Design",
            Code = "SD-101",
            Tags = [updatedTag1, updatedTag2]
        };

        var updatedCourse2 = new Course
        {
            Id = SeedData.Course2Id,
            CatalogId = SeedData.CatalogId,
            Title = "Updated Security",
            Code = "SP-201",
            Tags = [updatedTag2, updatedTag3]
        };

        // Set inverse collections to match tracked state (prevents false link removal)
        updatedTag1.Courses = [updatedCourse1];
        updatedTag2.Courses = [updatedCourse1, updatedCourse2];
        updatedTag3.Courses = [updatedCourse2];

        var updatedCatalog = new LearningCatalog
        {
            Id = SeedData.CatalogId,
            Name = "Updated Engineering Fundamentals",
            Courses = [updatedCourse1, updatedCourse2]
        };

        // Act — would cause StackOverflowException without cycle detection
        ctx.UpdateGraph(existing, updatedCatalog);
        await ctx.SaveChangesAsync();

        // Assert
        await using var verifyCtx = CreateContext();
        var result = await verifyCtx.LearningCatalogs
            .Include(c => c.Courses)
                .ThenInclude(c => c.Tags)
            .FirstAsync(c => c.Id == SeedData.CatalogId);

        result.Name.Should().Be("Updated Engineering Fundamentals");
        result.Courses.Should().HaveCount(2);

        var course1 = result.Courses.Single(c => c.Id == SeedData.Course1Id);
        course1.Title.Should().Be("Updated Design");
        course1.Tags.Should().HaveCount(2);

        var course2 = result.Courses.Single(c => c.Id == SeedData.Course2Id);
        course2.Title.Should().Be("Updated Security");
        course2.Tags.Should().HaveCount(2);

        // Tags updated via recursive M:M traversal
        var tag1 = await verifyCtx.TopicTags.FirstAsync(t => t.Id == SeedData.Tag1Id);
        tag1.Label.Should().Be("Updated Architecture");

        var tag2 = await verifyCtx.TopicTags.FirstAsync(t => t.Id == SeedData.Tag2Id);
        tag2.Label.Should().Be("Updated Testing");

        // Tag3 kept original value
        var tag3 = await verifyCtx.TopicTags.FirstAsync(t => t.Id == SeedData.Tag3Id);
        tag3.Label.Should().Be("Security");
    }
}
