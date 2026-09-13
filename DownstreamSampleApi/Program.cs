using DownstreamSampleApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var entraId = builder.Configuration.GetSection(DownstreamApiOptions.SectionName).Get<DownstreamApiOptions>()
    ?? new DownstreamApiOptions();

if (entraId.IsConfigured)
{
    var authority = $"https://login.microsoftonline.com/{entraId.TenantId}/v2.0";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = entraId.Audience;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = authority,
                ValidAudience = entraId.Audience,
                NameClaimType = "name",
                RoleClaimType = "roles",
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.Error.WriteLine($"DownstreamSampleApi: token validation failed: {context.Exception.Message}");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var name = context.Principal?.Identity?.Name
                        ?? context.Principal?.FindFirstValue("preferred_username")
                        ?? "unknown";
                    Console.Error.WriteLine($"DownstreamSampleApi: token validated for {name}");
                    return Task.CompletedTask;
                }
            };
        });
    builder.Services.AddAuthorization();
}
else
{
    Console.Error.WriteLine(
        "WARNING: EntraId:TenantId / EntraId:Audience are not configured in DownstreamSampleApi - " +
        "this API is running WITHOUT authentication. See McpDummyServer/README.md to set it up. " +
        "Do not run unauthenticated like this outside local learning/dev.");
}

var app = builder.Build();

if (entraId.IsConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/", () => "DownstreamSampleApi is running. See McpDummyServer/README.md.");

// This is the endpoint McpDummyServer's CustomApiOboService calls with an
// On-Behalf-Of-exchanged access token. Because that token was minted for
// *this* API's own audience/scope (not the MCP server's), it proves the
// full identity chain: user -> MCP client -> MCP server -> this API, all
// still carrying the original user's identity end-to-end.
var profileEndpoint = app.MapGet("/api/profile", (ClaimsPrincipal user) =>
{
    var name = user.Identity?.Name
        ?? user.FindFirstValue("preferred_username")
        ?? user.FindFirstValue("name")
        ?? "(unknown)";
    var oid = user.FindFirstValue("oid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "(unknown)";
    var scopes = user.FindFirstValue("scp") ?? "(none)";
    var appId = user.FindFirstValue("azp") ?? user.FindFirstValue("appid") ?? "(unknown)";

    return Results.Ok(new
    {
        message = "Hello from DownstreamSampleApi! This response proves the caller's identity " +
                   "made it all the way from the user through the MCP server via On-Behalf-Of.",
        name,
        objectId = oid,
        scopes,
        calledViaAppId = appId,
    });
});

if (entraId.IsConfigured)
{
    profileEndpoint.RequireAuthorization();
}

app.Run();
