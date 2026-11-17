using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Academics;

[ApiController, Route("api/v1/instructors")]
public sealed class InstructorsController(AcademicsDbContext database) : ControllerBase
{
    [HttpGet, Authorize(Policy = CatalogAuthorization.Read)]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var count = await database.Instructors.CountAsync(cancellationToken);
        var instructors = await database.Instructors.AsNoTracking().OrderBy(i => i.Id)
            .Skip((int)Math.Min((long)(page - 1) * 50, int.MaxValue)).Take(50)
            .Select(CatalogProjection.Instructor).ToListAsync(cancellationToken);
        return Ok(new { Instructors = instructors, Count = count, Pages = (int)Math.Ceiling(count / 50d), CurrentPage = page });
    }

    [HttpGet("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Read)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var instructor = await database.Instructors.AsNoTracking().Where(i => i.Id == id)
            .Select(CatalogProjection.Instructor).SingleOrDefaultAsync(cancellationToken);
        return instructor is null ? NotFound() : Ok(instructor);
    }

    [HttpPost, Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Create(InstructorInput input, CancellationToken cancellationToken)
    {
        if (input.Id is not null)
            return Problem(statusCode: 400, title: "IDs are allocated by the service.");
        var instructor = new Instructor
        {
            FirstName = input.FirstName, LastName = input.LastName, HireDate = input.HireDate
        };
        database.Instructors.Add(instructor);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = instructor.Id },
            new InstructorView(instructor.Id, instructor.FirstName, instructor.LastName, instructor.HireDate));
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Update(int id, InstructorInput input, CancellationToken cancellationToken)
    {
        if (input.Id is not null && input.Id != id)
            return Problem(statusCode: 400, title: "The route and body IDs must match.");
        var instructor = await database.Instructors.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (instructor is null)
            return NotFound();
        instructor.FirstName = input.FirstName;
        instructor.LastName = input.LastName;
        instructor.HireDate = input.HireDate;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var instructor = await database.Instructors.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (instructor is null)
            return NotFound();
        if (await database.Departments.AnyAsync(d => d.InstructorId == id, cancellationToken))
            return Problem(statusCode: 409, title: "Departments still reference this instructor.");
        database.Instructors.Remove(instructor);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
