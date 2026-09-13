using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using System.Text.Json;

namespace McpDummyServer.Services;

/// <summary>
/// Demonstrates the pattern an MCP server needs when it has to call a
/// downstream service on behalf of the signed-in user, as described in
/// https://github.com/merill/mcp-entra-design (section "Microsoft Graph MCP
/// Server" - the Graph MCP Server is a pure delegated proxy):
///
///   User (signed in)
///     -> MCP client (e.g. VS Code)
///          -> MCP server (validates the inbound token)  &lt;-- this service starts here
///               -> downstream API, called with a token obtained via
///                  OAuth 2.0 Token Exchange / On-Behalf-Of (RFC 8693),
///                  so the downstream API sees the *user's* identity, not
///                  the MCP server's.
///
/// The MCP server never uses its own app identity to call the downstream
/// API (that would be "app-only" access and bypass the user's actual
/// permissions) - it exchanges the caller's access token for a new token,
/// scoped to the downstream API, while preserving the user as the subject.
///
/// This demo calls Microsoft Graph's `/me` endpoint because every Entra ID
/// tenant has it available out of the box, so it's easy to try without
/// standing up a custom downstream API. The same pattern applies verbatim to
/// any other downstream API/service you'd call from a tool.
/// </summary>
public class GraphOboService
{
    private readonly EntraIdOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private IConfidentialClientApplication? _confidentialClientApplication;

    public GraphOboService(IOptions<EntraIdOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Exchanges the caller's inbound MCP access token for a Microsoft Graph
    /// token (On-Behalf-Of / RFC 8693 token exchange) and fetches the
    /// signed-in user's profile from Graph with it.
    /// </summary>
    /// <param name="inboundUserAccessToken">
    /// The access token the MCP client sent to this server (the one
    /// validated by JwtBearer/ASP.NET Core authentication).
    /// </param>
    public async Task<string> GetMyProfileAsync(string inboundUserAccessToken, CancellationToken cancellationToken)
    {
        if (!_options.SupportsOnBehalfOf)
        {
            return "On-Behalf-Of is not configured: set EntraId:ClientId and EntraId:ClientSecret " +
                   "(and grant the server app registration the Microsoft Graph 'User.Read' delegated " +
                   "permission with admin consent). See README.md, section 'Kalde en downstream-service'.";
        }

        var app = GetOrCreateConfidentialClientApplication();

        AuthenticationResult result;
        try
        {
            // This is the actual On-Behalf-Of call: MSAL exchanges
            // inboundUserAccessToken for a *new* access token, scoped to
            // Microsoft Graph, still representing the same user.
            result = await app.AcquireTokenOnBehalfOf(
                    [_options.DownstreamScope],
                    new UserAssertion(inboundUserAccessToken))
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException ex)
        {
            // Typically means the delegated Graph permission hasn't been
            // granted admin consent yet on the server's app registration.
            return $"Could not obtain a Microsoft Graph token on behalf of the user: {ex.Message} " +
                   "Make sure the server's app registration has the Graph 'User.Read' delegated " +
                   "permission with admin consent granted.";
        }
        catch (MsalServiceException ex)
        {
            return $"On-Behalf-Of token exchange failed: {ex.Message}";
        }

        using var graphClient = _httpClientFactory.CreateClient("MicrosoftGraph");
        using var request = new HttpRequestMessage(HttpMethod.Get, "me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);

        using var response = await graphClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"Microsoft Graph returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var displayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() : "(unknown)";
        var upn = root.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : "(unknown)";
        var mail = root.TryGetProperty("mail", out var m) ? m.GetString() : null;

        return $"Signed in as: {displayName} ({upn}){(mail is null ? "" : $", mail: {mail}")}";
    }

    private IConfidentialClientApplication GetOrCreateConfidentialClientApplication()
    {
        return _confidentialClientApplication ??= ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{_options.TenantId}/v2.0")
            .Build();
    }
}
