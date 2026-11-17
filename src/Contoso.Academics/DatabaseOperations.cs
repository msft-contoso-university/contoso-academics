using System.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.Academics;

public sealed class DatabaseReadiness(AcademicsDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
            return HealthCheckResult.Unhealthy("SQL is unavailable.");
        if (!database.Database.GetMigrations().Any() ||
            (await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            return HealthCheckResult.Unhealthy("Database migrations are pending.");
        return HealthCheckResult.Healthy();
    }
}

public sealed class DatabaseConflictHandler(
    ILogger<DatabaseConflictHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException &&
            exception is not DbUpdateException { InnerException: SqlException { Number: 547 or 2601 or 2627 } })
            return false;
        logger.LogWarning(exception, "Catalog persistence conflict; trace {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The change conflicts with the current catalog state.",
                Extensions = { ["traceId"] = context.TraceIdentifier }
            }
        });
        return true;
    }
}

public static class DemoSeed
{
    public static async Task ApplyAsync(AcademicsDbContext database, CancellationToken cancellationToken)
    {
        var strategy = database.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            const string version = "catalog-demo-v1";
            if (await database.DemoSeedHistory.AnyAsync(h => h.Id == version, cancellationToken))
                return;
            if (await database.Instructors.AnyAsync(cancellationToken) ||
                await database.Departments.AnyAsync(cancellationToken) ||
                await database.Courses.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Demo seeding refuses an existing, unmarked catalog.");

            var instructor = new Instructor
            {
                FirstName = "Demo", LastName = "Instructor", HireDate = new DateOnly(2020, 1, 1)
            };
            var department = new Department
            {
                Name = "Computing", Budget = 100000m, StartDate = new DateOnly(2020, 1, 1),
                Instructor = instructor
            };
            database.Courses.Add(new Course { Title = "Distributed systems", Credits = 4, Department = department });
            database.Courses.Add(new Course { Title = "Software engineering", Credits = 3, Department = department });
            database.DemoSeedHistory.Add(new DemoSeedHistory { Id = version });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
