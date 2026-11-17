using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contoso.Academics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Contoso.Academics.Tests;

[TestClass]
public sealed class CatalogTests
{
    private AcademicsApplication application = null!;
    private HttpClient client = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        application = new AcademicsApplication();
        client = application.CreateClient();
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
        if (application.UsesSqlServer)
            await database.Database.MigrateAsync();
        else
            await database.Database.EnsureCreatedAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (application.UsesSqlServer)
        {
            await using var scope = application.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AcademicsDbContext>().Database.EnsureDeletedAsync();
        }
        client.Dispose();
        await application.DisposeAsync();
    }

    private void Authenticate(string? scope = "academics.read academics.write", string roles = "Registrar",
        string? identityType = null) =>
        client.DefaultRequestHeaders.Authorization = new("Bearer", application.Token(scope, roles, identityType));

    private async Task<T> Create<T>(string resource, object body) where T : class
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/{resource}", body);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsNotNull(response.Headers.Location);
        await ExportResponse(typeof(T).Name[..^4], await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new AssertFailedException("No created resource.");
    }

    private static async Task ExportResponse(string schema, string response)
    {
        var directory = Environment.GetEnvironmentVariable("ACADEMICS_CONTRACT_SAMPLES_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        using var document = JsonDocument.Parse(response);
        var sample = JsonSerializer.Serialize(new { schema, body = document.RootElement });
        await File.WriteAllTextAsync(Path.Combine(directory, $"{schema}-{Guid.NewGuid():N}.json"), sample);
    }

    private async Task<(InstructorView Instructor, DepartmentView Department, CourseView Course)> CreateCatalog()
    {
        Authenticate();
        var instructor = await Create<InstructorView>("instructors",
            new InstructorInput("Demo", "Instructor", new DateOnly(2020, 1, 1)));
        var department = await Create<DepartmentView>("departments",
            new DepartmentInput("Computing", 250000.25m, new DateOnly(2020, 1, 1), instructor.Id));
        var course = await Create<CourseView>("courses", new CourseInput("Distributed systems", 4, department.Id));
        return (instructor, department, course);
    }

    [TestMethod]
    public async Task AnonymousRequestsCannotReadOrMutateCatalog()
    {
        using var read = await client.GetAsync("/api/v1/courses");
        using var write = await client.PostAsJsonAsync("/api/v1/instructors",
            new InstructorInput("Demo", "Instructor", new DateOnly(2020, 1, 1)));
        Assert.AreEqual(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [TestMethod]
    [DataRow("academics.read", "Reader", null, 200)]
    [DataRow("academics.read", "Registrar", null, 200)]
    [DataRow("academics.read.extra", "Reader", null, 403)]
    [DataRow(null, "Registrar", null, 403)]
    [DataRow("academics.write", "Reader", null, 403)]
    [DataRow("academics.read", "Academics.Lookup", null, 403)]
    [DataRow(null, "Academics.Lookup", "app", 403)]
    [DataRow(null, "Academics.Lookup", null, 403)]
    [DataRow("academics.read academics.write", "Registrar", "app", 403)]
    public async Task ReadsRequireExactDelegatedScopeAndRoleOrExplicitApplicationLookup(
        string? scope, string roles, string? identityType, int expected)
    {
        Authenticate(scope, roles, identityType);
        using var response = await client.GetAsync("/api/v1/courses");
        Assert.AreEqual((HttpStatusCode)expected, response.StatusCode);
    }

    [TestMethod]
    [DataRow("academics.write", "Registrar", null, 201)]
    [DataRow("academics.write", "Reader", null, 403)]
    [DataRow("academics.read", "Registrar", null, 403)]
    [DataRow(null, "Registrar", null, 403)]
    [DataRow("academics.write", "Academics.Lookup,Registrar", "app", 403)]
    public async Task WritesAreDelegatedRegistrarOnly(string? scope, string roles, string? identityType, int expected)
    {
        Authenticate(scope, roles, identityType);
        using var response = await client.PostAsJsonAsync("/api/v1/instructors",
            new InstructorInput("Demo", "Instructor", new DateOnly(2020, 1, 1)));
        Assert.AreEqual((HttpStatusCode)expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task WrongIssuerAudienceExpiryAndSignatureAreRejected()
    {
        var valid = application.Token();
        var parts = valid.Split('.');
        var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1;
        parts[2] = Base64UrlEncoder.Encode(signature);
        foreach (var token in new[]
        {
            application.Token(issuer: "https://untrusted.invalid/v2.0"),
            application.Token(audience: "api://another-service"),
            application.Token(expired: true),
            application.Token(signed: false),
            string.Join('.', parts)
        })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.GetAsync("/api/v1/courses");
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [TestMethod]
    public async Task ApplicationLookupCannotEnumerateCatalogOrReadInstructorAndBudgetData()
    {
        var (instructor, department, course) = await CreateCatalog();
        Authenticate(null, "Academics.Lookup", "app");
        using var lookup = await client.GetAsync($"/api/v1/courses/{course.Id}");
        Assert.AreEqual(HttpStatusCode.OK, lookup.StatusCode);
        using var unknown = await client.GetAsync("/api/v1/courses/2147483647");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        foreach (var resource in new[] { "courses", "departments", "instructors",
                     $"departments/{department.Id}", $"instructors/{instructor.Id}" })
        {
            using var forbidden = await client.GetAsync($"/api/v1/{resource}");
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
    }

    [TestMethod]
    public async Task CatalogCrudPreservesRelationshipsAndRejectsReferencedDeletes()
    {
        var (instructor, department, course) = await CreateCatalog();
        Assert.AreEqual(instructor.Id, department.Instructor?.Id);
        Assert.AreEqual(department.Name, course.Department.Name);
        Assert.AreEqual(250000.25m, department.Budget);
        foreach (var (resource, schema) in new[]
        {
            ("courses", "CoursePage"), ("departments", "DepartmentPage"), ("instructors", "InstructorPage")
        })
        {
            using var response = await client.GetAsync($"/api/v1/{resource}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var page = JsonDocument.Parse(json);
            Assert.AreEqual(1, page.RootElement.GetProperty(resource).GetArrayLength());
            Assert.AreEqual(1, page.RootElement.GetProperty("count").GetInt32());
            Assert.AreEqual(1, page.RootElement.GetProperty("pages").GetInt32());
            Assert.AreEqual(1, page.RootElement.GetProperty("currentPage").GetInt32());
            await ExportResponse(schema, json);
        }
        using var instructorConflict = await client.DeleteAsync($"/api/v1/instructors/{instructor.Id}");
        using var departmentConflict = await client.DeleteAsync($"/api/v1/departments/{department.Id}");
        Assert.AreEqual(HttpStatusCode.Conflict, instructorConflict.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, departmentConflict.StatusCode);

        using var courseUpdate = await client.PutAsJsonAsync($"/api/v1/courses/{course.Id}",
            new CourseInput("Updated systems", 5, department.Id, course.Id));
        using var departmentUpdate = await client.PutAsJsonAsync($"/api/v1/departments/{department.Id}",
            new DepartmentInput("Computer science", 300000m, department.StartDate, instructor.Id, department.Id));
        using var instructorUpdate = await client.PutAsJsonAsync($"/api/v1/instructors/{instructor.Id}",
            new InstructorInput("Updated", "Instructor", instructor.HireDate, instructor.Id));
        Assert.AreEqual(HttpStatusCode.NoContent, courseUpdate.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, departmentUpdate.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, instructorUpdate.StatusCode);
        var changed = await client.GetFromJsonAsync<CourseView>($"/api/v1/courses/{course.Id}");
        Assert.IsNotNull(changed);
        Assert.AreEqual("Updated systems", changed.Title);
        Assert.AreEqual("Computer science", changed.Department.Name);
        var changedDepartment = await client.GetFromJsonAsync<DepartmentView>($"/api/v1/departments/{department.Id}");
        Assert.AreEqual("Updated", changedDepartment?.Instructor?.FirstName);

        foreach (var path in new[] { $"courses/{course.Id}", $"departments/{department.Id}", $"instructors/{instructor.Id}" })
        {
            using var deletion = await client.DeleteAsync($"/api/v1/{path}");
            using var missing = await client.GetAsync($"/api/v1/{path}");
            Assert.AreEqual(HttpStatusCode.NoContent, deletion.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [TestMethod]
    public async Task ValidationRejectsMissingFieldsBadReferencesAndRouteBodyMismatchWithoutWriting()
    {
        var (_, department, course) = await CreateCatalog();
        var invalidBodies = new object[]
        {
            new { title = "No credits", departmentId = department.Id },
            new CourseInput("ab", 4, department.Id),
            new CourseInput("Too many credits", 6, department.Id),
            new CourseInput("Unknown department", 3, int.MaxValue),
            new CourseInput("Client allocated ID", 3, department.Id, 999)
        };
        foreach (var body in invalidBodies)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/courses", body);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        using var badReference = await client.PostAsJsonAsync("/api/v1/departments",
            new DepartmentInput("Unknown instructor", 0m, department.StartDate, int.MaxValue));
        Assert.AreEqual(HttpStatusCode.BadRequest, badReference.StatusCode);
        using var mismatch = await client.PutAsJsonAsync($"/api/v1/courses/{course.Id}",
            new CourseInput("Wrong target", 3, department.Id, int.MaxValue));
        Assert.AreEqual(HttpStatusCode.BadRequest, mismatch.StatusCode);
        var unchanged = await client.GetFromJsonAsync<CourseView>($"/api/v1/courses/{course.Id}");
        Assert.AreEqual(course.Title, unchanged?.Title);
        await using var scope = application.Services.CreateAsyncScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<AcademicsDbContext>().Courses.CountAsync());
    }

    [TestMethod]
    public async Task PaginationKeepsLegacyDefaultAndBoundsLargeAndNegativePages()
    {
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
            database.Instructors.AddRange(Enumerable.Range(1, 51).Select(i => new Instructor
            {
                FirstName = $"Demo{i:D2}",
                LastName = "Instructor",
                HireDate = new DateOnly(2020, 1, 1)
            }));
            await database.SaveChangesAsync();
        }
        Authenticate("academics.read", "Reader");
        foreach (var page in new[] { "-5", "1", "2", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        {
            using var response = await client.GetAsync($"/api/v1/instructors?page={page}");
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await ExportResponse("InstructorPage", body.RootElement.GetRawText());
            Assert.AreEqual(51, body.RootElement.GetProperty("count").GetInt32());
            Assert.AreEqual(2, body.RootElement.GetProperty("pages").GetInt32());
            var length = body.RootElement.GetProperty("instructors").GetArrayLength();
            Assert.AreEqual(page switch { "-5" or "1" => 50, "2" => 1, _ => 0 }, length);
            Assert.AreEqual(page == "-5" ? 1 : int.Parse(page, System.Globalization.CultureInfo.InvariantCulture),
                body.RootElement.GetProperty("currentPage").GetInt32());
        }
    }

    [TestMethod]
    public async Task DatabaseEnforcesReferencesIndependentlyOfHttpValidation()
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
        database.Courses.Add(new Course { Title = "Orphan course", Credits = 3, DepartmentId = int.MaxValue });
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ExplicitDemoSeedIsRepeatableAndRefusesExistingUnmarkedData()
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
        await DemoSeed.ApplyAsync(database, CancellationToken.None);
        await DemoSeed.ApplyAsync(database, CancellationToken.None);
        Assert.AreEqual(2, await database.Courses.CountAsync());
        Assert.AreEqual(1, await database.Departments.CountAsync());
        Assert.AreEqual(1, await database.Instructors.CountAsync());
        await database.DemoSeedHistory.ExecuteDeleteAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => DemoSeed.ApplyAsync(database, CancellationToken.None));
        Assert.AreEqual(2, await database.Courses.CountAsync());
    }

    [TestMethod]
    public async Task HealthDoesNotRequireAuthenticationAndUnmigratedSchemaIsNotReady()
    {
        using var live = await client.GetAsync("/health/live");
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
        using var ready = await client.GetAsync("/health/ready");
        Assert.AreEqual(application.UsesSqlServer ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.IsFalse((await ready.Content.ReadAsStringAsync()).Contains("ConnectionStrings", StringComparison.Ordinal));
    }
}
