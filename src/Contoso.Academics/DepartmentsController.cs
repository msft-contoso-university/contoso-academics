using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Contoso.Academics;

[ApiController, Route("api/v1/departments")]
public sealed class DepartmentsController(AcademicsDbContext database) : ControllerBase
{
    [HttpGet, Authorize(Policy = CatalogAuthorization.Read)]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var count = await database.Departments.CountAsync(cancellationToken);
        var departments = await database.Departments.AsNoTracking().OrderBy(d => d.Id)
            .Skip((int)Math.Min((long)(page - 1) * 50, int.MaxValue)).Take(50)
            .Select(CatalogProjection.Department).ToListAsync(cancellationToken);
        return Ok(new { Departments = departments, Count = count, Pages = (int)Math.Ceiling(count / 50d), CurrentPage = page });
    }

    [HttpGet("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Read)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var department = await database.Departments.AsNoTracking().Where(d => d.Id == id)
            .Select(CatalogProjection.Department).SingleOrDefaultAsync(cancellationToken);
        return department is null ? NotFound() : Ok(department);
    }

    [HttpPost, Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Create(DepartmentInput input, CancellationToken cancellationToken)
    {
        if (input.Id is not null)
            return Problem(statusCode: 400, title: "IDs are allocated by the service.");
        if (input.InstructorId is not null &&
            !await database.Instructors.AnyAsync(i => i.Id == input.InstructorId, cancellationToken))
            return Problem(statusCode: 400, title: "The referenced instructor does not exist.");
        var department = new Department
        {
            Name = input.Name, Budget = input.Budget, StartDate = input.StartDate, InstructorId = input.InstructorId
        };
        database.Departments.Add(department);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = department.Id }, await database.Departments
            .Where(d => d.Id == department.Id).Select(CatalogProjection.Department).SingleAsync(cancellationToken));
    }

    [HttpPut("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Update(int id, DepartmentInput input, CancellationToken cancellationToken)
    {
        if (input.Id is not null && input.Id != id)
            return Problem(statusCode: 400, title: "The route and body IDs must match.");
        var department = await database.Departments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (department is null)
            return NotFound();
        if (input.InstructorId is not null &&
            !await database.Instructors.AnyAsync(i => i.Id == input.InstructorId, cancellationToken))
            return Problem(statusCode: 400, title: "The referenced instructor does not exist.");
        department.Name = input.Name;
        department.Budget = input.Budget;
        department.StartDate = input.StartDate;
        department.InstructorId = input.InstructorId;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int:min(1)}"), Authorize(Policy = CatalogAuthorization.Write)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var department = await database.Departments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (department is null)
            return NotFound();
        if (await database.Courses.AnyAsync(c => c.DepartmentId == id, cancellationToken))
            return Problem(statusCode: 409, title: "Courses still reference this department.");
        database.Departments.Remove(department);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
