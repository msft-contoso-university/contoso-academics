using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Academics;

[ApiController, Route("api/v1/courses")]
public sealed class CoursesController(AcademicsDbContext database) : ControllerBase
{
    [HttpGet, Authorize(Policy = CatalogAuthorization.Read)]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var count = await database.Courses.CountAsync(cancellationToken);
        var courses = await database.Courses.AsNoTracking().OrderBy(c => c.Id)
            .Skip((int)Math.Min((long)(page - 1) * 50, int.MaxValue)).Take(50)
            .Select(CatalogProjection.Course).ToListAsync(cancellationToken);
        return Ok(new { Courses = courses, Count = count, Pages = (int)Math.Ceiling(count / 50d), CurrentPage = page });
    }

    [HttpGet("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Lookup)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var course = await database.Courses.AsNoTracking().Where(c => c.Id == id)
            .Select(CatalogProjection.Course).SingleOrDefaultAsync(cancellationToken);
        return course is null ? NotFound() : Ok(course);
    }

    [HttpPost, Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Create(CourseInput input, CancellationToken cancellationToken)
    {
        if (input.Id is not null)
            return Problem(statusCode: 400, title: "IDs are allocated by the service.");
        if (!await database.Departments.AnyAsync(d => d.Id == input.DepartmentId, cancellationToken))
            return Problem(statusCode: 400, title: "The referenced department does not exist.");
        var course = new Course { Title = input.Title, Credits = input.Credits, DepartmentId = input.DepartmentId };
        database.Courses.Add(course);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = course.Id }, await database.Courses
            .Where(c => c.Id == course.Id).Select(CatalogProjection.Course).SingleAsync(cancellationToken));
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Update(int id, CourseInput input, CancellationToken cancellationToken)
    {
        if (input.Id is not null && input.Id != id)
            return Problem(statusCode: 400, title: "The route and body IDs must match.");
        var course = await database.Courses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null)
            return NotFound();
        if (!await database.Departments.AnyAsync(d => d.Id == input.DepartmentId, cancellationToken))
            return Problem(statusCode: 400, title: "The referenced department does not exist.");
        course.Title = input.Title;
        course.Credits = input.Credits;
        course.DepartmentId = input.DepartmentId;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var course = await database.Courses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null)
            return NotFound();
        database.Courses.Remove(course);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
