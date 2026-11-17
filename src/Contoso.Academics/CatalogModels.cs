using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace Contoso.Academics;

public sealed class Instructor
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateOnly HireDate { get; set; }
}

public sealed class Department
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Budget { get; set; }
    public DateOnly StartDate { get; set; }
    public int? InstructorId { get; set; }
    public Instructor? Instructor { get; set; }
}

public sealed class Course
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public int Credits { get; set; }
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
}

public sealed class DemoSeedHistory
{
    public required string Id { get; set; }
}

public sealed record InstructorInput(
    [Required, StringLength(50, MinimumLength = 1)] string FirstName,
    [Required, StringLength(50, MinimumLength = 1)] string LastName,
    DateOnly HireDate,
    [Range(1, int.MaxValue)] int? Id = null);

public sealed record DepartmentInput(
    [Required, StringLength(50, MinimumLength = 3)] string Name,
    [Range(typeof(decimal), "-922337203685477.5808", "922337203685477.5807",
        ParseLimitsInInvariantCulture = true)] decimal Budget,
    DateOnly StartDate,
    [Range(1, int.MaxValue)] int? InstructorId = null,
    [Range(1, int.MaxValue)] int? Id = null);

public sealed record CourseInput(
    [Required, StringLength(50, MinimumLength = 3)] string Title,
    [Range(0, 5)] int Credits,
    [Range(1, int.MaxValue)] int DepartmentId,
    [Range(1, int.MaxValue)] int? Id = null);

public sealed record InstructorView(int Id, string FirstName, string LastName, DateOnly HireDate);
public sealed record DepartmentSummary(int Id, string Name);
public sealed record DepartmentView(
    int Id, string Name, decimal Budget, DateOnly StartDate, int? InstructorId, InstructorView? Instructor);
public sealed record CourseView(
    int Id, string Title, int Credits, int DepartmentId, DepartmentSummary Department);

public static class CatalogProjection
{
    public static readonly Expression<Func<Instructor, InstructorView>> Instructor =
        value => new(value.Id, value.FirstName, value.LastName, value.HireDate);
    public static readonly Expression<Func<Department, DepartmentView>> Department =
        value => new(value.Id, value.Name, value.Budget, value.StartDate, value.InstructorId,
            value.Instructor == null ? null : new InstructorView(
                value.Instructor.Id, value.Instructor.FirstName, value.Instructor.LastName,
                value.Instructor.HireDate));
    public static readonly Expression<Func<Course, CourseView>> Course =
        value => new(value.Id, value.Title, value.Credits, value.DepartmentId,
            new DepartmentSummary(value.Department.Id, value.Department.Name));
}
