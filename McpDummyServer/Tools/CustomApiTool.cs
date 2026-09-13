using McpDummyServer.Services;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpDummyServer.Tools;

/// <summary>
/// Demonstrates calling a *custom-developed* downstream service (the bundled
/// DownstreamSampleApi project) on behalf of the signed-in user via the
/// On-Behalf-Of flow - the same pattern as GraphProfileTool, but pointed at
/// your own API instead of Microsoft Graph. Copy this tool + CustomApiOboService
/// as the starting point when you want an MCP tool to call a real service
/// you built. Only meaningful over HTTP with Entra ID authentication enabled
/// (see README.md).
/// </summary>
[McpServerToolType]
public sealed class CustomApiTool
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CustomApiOboService _customApiOboService;

    public CustomApiTool(IHttpContextAccessor httpContextAccessor, CustomApiOboService customApiOboService)
    {
        _httpContextAccessor = httpContextAccessor;
        _customApiOboService = customApiOboService;
    }

    [McpServerTool(Name = "call_custom_api"),
     Description("Calls your own downstream API (DownstreamSampleApi's /api/profile) on behalf of the signed-in MCP caller (On-Behalf-Of flow).")]
    public async Task<string> CallCustomApiAsync(CancellationToken cancellationToken)
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
        return await _customApiOboService.CallProfileEndpointAsync(inboundToken, cancellationToken);
    }
}
