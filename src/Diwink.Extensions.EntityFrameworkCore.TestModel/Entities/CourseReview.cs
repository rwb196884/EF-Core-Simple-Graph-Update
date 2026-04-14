namespace Diwink.Extensions.EntityFrameworkCore.TestModel.Entities;

public class CourseReview
{
    public Guid Id { get; set; }
    public Guid? CourseId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;

    public Course? Course { get; set; }
}
