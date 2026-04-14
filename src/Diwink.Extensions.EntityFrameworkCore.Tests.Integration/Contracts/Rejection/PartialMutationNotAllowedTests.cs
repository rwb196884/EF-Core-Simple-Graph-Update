using Diwink.Extensions.EntityFrameworkCore.Exceptions;
using Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;
using Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Integration.Contracts.Rejection;

/// <summary>
/// Integration tests proving all-or-nothing rejection semantics (FR-017).
/// When a graph contains both supported and unsupported mutations, the entire
/// operation is rejected — no supported mutations are applied.
/// </summary>
public class PartialMutationNotAllowedTests : IntegrationTestBase
{
    public PartialMutationNotAllowedTests(SqlServerContainerFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var ctx = CreateContext();
        await SeedData.SeedFullScenarioAsync(ctx);
    }

    [Fact]
    public async Task Mixed_supported_and_unsupported_mutations_rejects_entire_operation()
    {
        // Arrange — use Course as root with:
        //   Course.Catalog (ManyToOne, unsupported) — loaded and mutated
        //   Course.Tags (pure M:M, supported) — loaded and mutated
        await using var ctx = CreateContext();
        var existing = await ctx.Courses
            .Include(c => c.Catalog)
            .Include(c => c.Tags)
            .FirstAsync(c => c.Id == SeedData.Course1Id);

        var originalTitle = existing.Title;

        // Updated graph mutates both:
        // - Scalar change on Course (supported)
        // - Catalog name change (ManyToOne mutation, unsupported)
        // - Tags unchanged (supported, no mutation)
        var updated = new Course
        {
            Id = SeedData.Course1Id,
            CatalogId = SeedData.CatalogId,
            Title = "Should NOT be applied",
            Code = existing.Code,
            Catalog = new LearningCatalog
            {
                Id = SeedData.CatalogId,
                Name = "Renamed Catalog"
            },
            Tags = existing.Tags.Select(t => new TopicTag
            {
                Id = t.Id,
                Label = t.Label
            }).ToList()
        };

        // Act & Assert — entire operation rejected
        var act = () => ctx.UpdateGraph(existing, updated);
        act.Should().Throw<GraphUpdateException>();

        // Verify no mutations were applied (all-or-nothing)
        await using var verifyCtx = CreateContext();
        var result = await verifyCtx.Courses
            .FirstAsync(c => c.Id == SeedData.Course1Id);
        result.Title.Should().Be(originalTitle,
            "scalar updates must NOT be applied when unsupported mutation causes rejection");
    }
}
