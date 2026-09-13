using McpDummyServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using System.Security.Claims;

namespace McpDummyServer;

class Program
{
    static async Task Main(string[] args)
    {
        // Two transports, one tool set: run with `dotnet run` for stdio (used by
        // local clients like VS Code / Claude Desktop) or `dotnet run -- --http`
        // to expose the same server over HTTP (streamable HTTP transport) so it
        // can be reached remotely, e.g. http://localhost:5000/mcp. The HTTP
        // transport can optionally be protected with Entra ID OAuth - see
        // README.md for how to set that up.
        if (args.Contains("--http"))
        {
            await RunHttpAsync(args);
        }
        else
        {
            await RunStdioAsync(args);
        }
    }

    static async Task RunStdioAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // MCP uses stdout for JSON-RPC messages, so all logging must go to stderr.
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        RegisterOnBehalfOfServices(builder.Services, builder.Configuration);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            // Auto-discovers every class marked [McpServerToolType] in this assembly.
            // To add new capabilities over time, just add a new class under Tools/ (or
            // Resources/ / Prompts/) - no changes needed here.
            .WithToolsFromAssembly();

        var host = builder.Build();
        await host.RunAsync();
    }

    static async Task RunHttpAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var entraId = builder.Configuration.GetSection(EntraIdOptions.SectionName).Get<EntraIdOptions>()
            ?? new EntraIdOptions();
        var mcpServerUrl = builder.Configuration["Mcp:ServerUrl"] ?? "http://localhost:5000/mcp";

        if (entraId.IsConfigured)
        {
            ConfigureEntraIdAuthentication(builder, entraId, mcpServerUrl);
        }
        else
        {
            Console.Error.WriteLine(
                "WARNING: EntraId:TenantId / EntraId:Audience are not configured - the HTTP MCP " +
                "endpoint is running WITHOUT authentication. See README.md to set up Entra ID. " +
                "Do not run unauthenticated like this outside local learning/dev.");
        }

        RegisterOnBehalfOfServices(builder.Services, builder.Configuration);

        builder.Services
            .AddMcpServer()
            // Same tools as stdio - transport-agnostic, discovered from the assembly.
            .WithToolsFromAssembly()
            .WithHttpTransport();

        var app = builder.Build();

        if (entraId.IsConfigured)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        var mcpEndpoint = app.MapMcp("/mcp");
        if (entraId.IsConfigured)
        {
            // Requires a valid, audience-matching Entra ID access token (see
            // ConfigureEntraIdAuthentication). Unauthenticated requests get a 401
            // with a WWW-Authenticate header pointing clients to
            // /.well-known/oauth-protected-resource for discovery (RFC 9728).
            mcpEndpoint.RequireAuthorization();
        }

        await app.RunAsync();
    }

    /// <summary>
    /// Protects the MCP HTTP endpoint with Microsoft Entra ID: validates
    /// incoming JWT access tokens against the tenant's OpenID Connect
    /// metadata, and publishes OAuth Protected Resource Metadata (RFC 9728)
    /// at /.well-known/oauth-protected-resource so MCP clients can discover
    /// how to authenticate. See README.md for the Entra ID app registration
    /// steps this configuration expects.
    /// </summary>
    static void ConfigureEntraIdAuthentication(WebApplicationBuilder builder, EntraIdOptions entraId, string mcpServerUrl)
    {
        var authority = $"https://login.microsoftonline.com/{entraId.TenantId}/v2.0";

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            // Entra ID publishes its Authorization Server Metadata (RFC 8414) /
            // OpenID Connect discovery document at {authority}/.well-known/openid-configuration,
            // which JwtBearer uses automatically to fetch signing keys, issuer, etc.
            options.Authority = authority;
            options.Audience = entraId.Audience;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = authority,
                ValidAudience = entraId.Audience, // RFC 8707 resource/audience binding
                NameClaimType = "name",
                RoleClaimType = "roles",
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.Error.WriteLine($"Entra ID token validation failed: {context.Exception.Message}");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var name = context.Principal?.Identity?.Name
                        ?? context.Principal?.FindFirstValue("preferred_username")
                        ?? "unknown";
                    Console.Error.WriteLine($"Entra ID token validated for: {name}");
                    return Task.CompletedTask;
                },
                OnChallenge = _ =>
                {
                    Console.Error.WriteLine("Challenging client to authenticate with Entra ID");
                    return Task.CompletedTask;
                }
            };
        })
        .AddMcp(options =>
        {
            // Published at {mcpServerUrl-root}/.well-known/oauth-protected-resource
            options.ResourceMetadata = new()
            {
                Resource = mcpServerUrl,
                AuthorizationServers = { authority },
                ScopesSupported = [entraId.Scope],
            };
        });

        builder.Services.AddAuthorization();
    }

    /// <summary>
    /// Registers what's needed for GraphProfileTool and CustomApiTool to
    /// demonstrate calling a downstream API (Microsoft Graph, and your own
    /// custom-developed service respectively) on behalf of the signed-in
    /// user via the On-Behalf-Of flow (see Services/GraphOboService.cs and
    /// Services/CustomApiOboService.cs). Registered for both transports so
    /// tool discovery/DI resolution works everywhere; the tools themselves
    /// explain that they only function over authenticated HTTP, where
    /// there's an inbound user token to exchange.
    /// </summary>
    static void RegisterOnBehalfOfServices(IServiceCollection services, IConfiguration configuration)
    {
        var entraId = configuration.GetSection(EntraIdOptions.SectionName).Get<EntraIdOptions>()
            ?? new EntraIdOptions();

        services.Configure<EntraIdOptions>(configuration.GetSection(EntraIdOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddHttpClient("MicrosoftGraph", client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        });
        services.AddSingleton<GraphOboService>();

        // Base address for your own downstream API - falls back to the
        // bundled DownstreamSampleApi's default local dev URL if not
        // configured, so the client can still be constructed (the tool
        // itself checks SupportsCustomApiOnBehalfOf before using it).
        services.AddHttpClient("CustomApi", client =>
        {
            client.BaseAddress = new Uri(entraId.CustomApiBaseUrl ?? "https://localhost:7128/");
        });
        services.AddSingleton<CustomApiOboService>();
    }
}
