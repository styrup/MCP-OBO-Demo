using McpDummyServer.Services;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpDummyServer.Tools;

/// <summary>
/// Demonstrates calling a downstream service (Microsoft Graph) on behalf of
/// the signed-in user via the On-Behalf-Of flow, as described in
/// https://github.com/merill/mcp-entra-design. Only meaningful when running
/// over HTTP with Entra ID authentication enabled (see README.md) - the tool
/// needs the caller's inbound access token to exchange it for a
/// downstream-scoped token, so it doesn't apply to stdio or unauthenticated
/// HTTP mode.
///
/// This is an instance (non-static) tool type, unlike EchoTool/CalculatorTool:
/// the MCP SDK resolves constructor parameters from the DI container, which
/// is how tools get access to services like this one, IHttpClientFactory, a
/// database context, etc.
/// </summary>
[McpServerToolType]
public sealed class GraphProfileTool
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GraphOboService _graphOboService;

    public GraphProfileTool(IHttpContextAccessor httpContextAccessor, GraphOboService graphOboService)
    {
        _httpContextAccessor = httpContextAccessor;
        _graphOboService = graphOboService;
    }

    [McpServerTool(Name = "whoami_graph"),
     Description("Calls Microsoft Graph's /me endpoint on behalf of the signed-in MCP caller (On-Behalf-Of flow) and returns their profile.")]
    public async Task<string> WhoAmIAsync(CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return "This tool requires the HTTP transport with Entra ID authentication " +
                   "(run with 'dotnet run -- --http' and configure EntraId - see README.md). " +
                   "It has no meaning over stdio, since there's no signed-in user token to exchange.";
        }

        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return "No inbound bearer token found on the request - this tool only works when the " +
                   "HTTP MCP endpoint is protected with Entra ID authentication (see README.md).";
        }

        var inboundToken = authHeader["Bearer ".Length..];
        return await _graphOboService.GetMyProfileAsync(inboundToken, cancellationToken);
    }
}
