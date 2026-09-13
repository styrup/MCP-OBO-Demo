using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using System.Text.Json;

namespace McpDummyServer.Services;

/// <summary>
/// Same On-Behalf-Of pattern as <see cref="GraphOboService"/>, but pointed at
/// a custom-developed downstream API instead of Microsoft Graph - this is
/// the shape you'd copy when your MCP server needs to call a service *you*
/// built, rather than a Microsoft first-party API.
///
/// The bundled <c>DownstreamSampleApi</c> project is a minimal example of
/// such a service: its own Entra ID app registration exposes an API/scope,
/// and it validates incoming tokens the same way McpDummyServer does. The
/// only difference from calling Microsoft Graph is *which* app registration
/// and scope you exchange the token for - the OBO mechanics are identical.
/// </summary>
public class CustomApiOboService
{
    private readonly EntraIdOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private IConfidentialClientApplication? _confidentialClientApplication;

    public CustomApiOboService(IOptions<EntraIdOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Exchanges the caller's inbound MCP access token for a token scoped to
    /// your own downstream API (On-Behalf-Of / RFC 8693 token exchange), and
    /// calls that API's <c>/api/profile</c> endpoint with it.
    /// </summary>
    /// <param name="inboundUserAccessToken">
    /// The access token the MCP client sent to this server (the one
    /// validated by JwtBearer/ASP.NET Core authentication).
    /// </param>
    public async Task<string> CallProfileEndpointAsync(string inboundUserAccessToken, CancellationToken cancellationToken)
    {
        if (!_options.SupportsCustomApiOnBehalfOf)
        {
            return "Custom API On-Behalf-Of is not configured: set EntraId:ClientId, EntraId:ClientSecret, " +
                   "EntraId:CustomApiScope and EntraId:CustomApiBaseUrl (and grant the server app registration " +
                   "the custom API's delegated permission with admin consent). See README.md, section " +
                   "'Kalde din egen downstream-service (custom API)'.";
        }

        var app = GetOrCreateConfidentialClientApplication();

        AuthenticationResult result;
        try
        {
            // Identical mechanics to the Graph OBO call - only the requested
            // scope and the API called afterwards differ.
            result = await app.AcquireTokenOnBehalfOf(
                    [_options.CustomApiScope!],
                    new UserAssertion(inboundUserAccessToken))
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException ex)
        {
            // Typically means the delegated permission for the custom API
            // hasn't been granted admin consent yet on the server's app
            // registration, or the custom API hasn't exposed that scope.
            return $"Could not obtain a token for the custom API on behalf of the user: {ex.Message} " +
                   "Make sure the server's app registration has been granted the custom API's delegated " +
                   "scope with admin consent.";
        }
        catch (MsalServiceException ex)
        {
            return $"On-Behalf-Of token exchange for the custom API failed: {ex.Message}";
        }

        using var apiClient = _httpClientFactory.CreateClient("CustomApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/profile");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);

        using var response = await apiClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"Custom API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : body;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : "(unknown)";

        return $"{message} (name: {name})";
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
