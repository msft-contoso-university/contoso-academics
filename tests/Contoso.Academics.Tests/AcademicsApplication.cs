using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Contoso.Academics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Contoso.Academics.Tests;

internal sealed class AcademicsApplication : WebApplicationFactory<Program>
{
    public const string Tenant = "11111111-1111-1111-1111-111111111111";
    public const string Issuer = $"https://login.microsoftonline.com/{Tenant}/v2.0";
    public const string Audience = "api://academics-tests";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly SqliteConnection? sqlite;
    private readonly string connection;
    public bool UsesSqlServer { get; }

    public AcademicsApplication()
    {
        var sql = Environment.GetEnvironmentVariable("ACADEMICS_TEST_SQL_CONNECTION");
        UsesSqlServer = !string.IsNullOrWhiteSpace(sql);
        if (UsesSqlServer)
        {
            var settings = new SqlConnectionStringBuilder(sql)
            {
                InitialCatalog = $"AcademicsTest_{Guid.NewGuid():N}"
            };
            if (settings.DataSource is not ("127.0.0.1,1433" or "localhost,1433"))
                throw new InvalidOperationException("Integration tests accept only the disposable loopback SQL container.");
            connection = settings.ConnectionString;
        }
        else
        {
            sqlite = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            sqlite.Open();
            connection = sqlite.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = Tenant,
                ["AzureAd:Audience"] = Audience,
                ["ConnectionStrings:Academics"] = connection
            }));
        builder.ConfigureTestServices(services =>
        {
            if (sqlite is not null)
            {
                services.RemoveAll<DbContextOptions<AcademicsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AcademicsDbContext>>();
                services.AddDbContext<AcademicsDbContext>(options => options.UseSqlite(sqlite));
            }
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(SigningKey());
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        });
    }

    private RsaSecurityKey SigningKey() => new(rsa) { KeyId = "academics-test-key" };

    public string Token(string? scope = "academics.read academics.write", string roles = "Registrar",
        string? identityType = null, string audience = Audience, string issuer = Issuer, bool expired = false,
        bool signed = true)
    {
        var claims = roles.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(role => new Claim("roles", role)).ToList();
        if (scope is not null)
            claims.Add(new Claim("scp", scope));
        if (identityType is not null)
            claims.Add(new Claim("idtyp", identityType));
        var token = new JwtSecurityToken(issuer, audience, claims,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(expired ? -10 : 10),
            signed ? new SigningCredentials(SigningKey(), SecurityAlgorithms.RsaSha256) : null);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            sqlite?.Dispose();
            rsa.Dispose();
        }
    }
}
