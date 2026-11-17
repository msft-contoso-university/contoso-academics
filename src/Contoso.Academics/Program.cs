using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Contoso.Academics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;

var migrate = args.Contains("--migrate", StringComparer.Ordinal);
var seed = args.Contains("--seed-demo", StringComparer.Ordinal);
if (seed && !migrate)
    throw new ArgumentException("--seed-demo requires the explicit --migrate command.");

var builder = WebApplication.CreateBuilder(
    args.Where(a => a is not "--migrate" and not "--seed-demo").ToArray());
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseConflictHandler>();
builder.Services.AddOptions<IdentitySettings>().BindConfiguration("AzureAd")
    .Validate(o => Guid.TryParse(o.TenantId, out var tenant) && tenant != Guid.Empty,
        "AzureAd:TenantId must be a nonempty tenant GUID.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience),
        "AzureAd:Audience is required.")
    .ValidateOnStart();
builder.Services.AddDbContext<AcademicsDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connection = configuration.GetConnectionString("Academics")
        ?? throw new InvalidOperationException("ConnectionStrings:Academics is required.");
    options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3).CommandTimeout(30));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<IdentitySettings>>((options, settings) =>
    {
        options.Authority = $"https://login.microsoftonline.com/{settings.Value.TenantId}/v2.0";
        options.Audience = settings.Value.Audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            NameClaimType = "name",
            RoleClaimType = "roles",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(CatalogAuthorization.Configure);
builder.Services.AddHealthChecks().AddCheck<DatabaseReadiness>("sql", tags: ["ready"]);

if (!migrate && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    var connection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
        ?? throw new InvalidOperationException("APPLICATIONINSIGHTS_CONNECTION_STRING is required.");
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("contoso-academics"))
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = connection;
            options.Credential = new DefaultAzureCredential();
        });
}

var app = builder.Build();

if (migrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
    await database.Database.MigrateAsync();
    if (seed)
        await DemoSeed.ApplyAsync(database, CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();
app.MapControllers();
await app.RunAsync();

public partial class Program;
