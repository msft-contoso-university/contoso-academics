using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Contoso.Academics;

public sealed class IdentitySettings
{
    public string TenantId { get; set; } = "";
    public string Audience { get; set; } = "";
}

public static class CatalogAuthorization
{
    public const string Read = "catalog-read";
    public const string Lookup = "course-lookup";
    public const string Write = "catalog-write";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Read, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => CanReadDelegated(context.User)));
        options.AddPolicy(Lookup, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            CanReadDelegated(context.User) ||
            IsApplication(context.User) && context.User.IsInRole("Academics.Lookup")));
        options.AddPolicy(Write, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            !IsApplication(context.User) && HasScope(context.User, "academics.write") &&
            context.User.IsInRole("Registrar")));
    }

    private static bool IsApplication(ClaimsPrincipal user) => user.HasClaim("idtyp", "app");

    private static bool CanReadDelegated(ClaimsPrincipal user) =>
        !IsApplication(user) && HasScope(user, "academics.read") &&
        (user.IsInRole("Reader") || user.IsInRole("Registrar"));

    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scp").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
