using Diwink.Extensions.EntityFrameworkCore.Exceptions;
using FluentAssertions;

namespace Diwink.Extensions.EntityFrameworkCore.Tests.Unit.GraphDiff;

public class ExceptionValidationTests
{
    [Fact]
    public void UnsupportedNavigationMutatedException_rejects_blank_relationship_path()
    {
        var act = () => new UnsupportedNavigationMutatedException("   ", "OneToMany");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("relationshipPath");
    }

    [Fact]
    public void PartialMutationNotAllowedException_rejects_blank_unsupported_branch()
    {
        var act = () => new PartialMutationNotAllowedException("Course.Tags", " ");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("unsupportedBranch");
    }

    [Fact]
    public void UnloadedNavigationMutationException_rejects_blank_navigation_name()
    {
        var act = () => new UnloadedNavigationMutationException("Course.Tags", " ");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("navigationName");
    }
}
