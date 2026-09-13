namespace McpDummyServer;

/// <summary>
/// Configuration for protecting the HTTP MCP endpoint with Microsoft Entra ID
/// (OAuth 2.0 / OpenID Connect), following the design described in
/// https://github.com/merill/mcp-entra-design (RFC 9728 Protected Resource
/// Metadata, RFC 8414 Authorization Server Metadata, RFC 8707 Resource
/// Indicators - Entra ID doesn't support Dynamic Client Registration, so the
/// client app must be pre-registered, see README.md).
///
/// Bound from the "EntraId" section of appsettings.json / environment
/// variables / user-secrets. Leave TenantId/Audience empty to run the HTTP
/// endpoint without authentication (useful for local learning/dev only).
/// </summary>
public class EntraIdOptions
{
    public const string SectionName = "EntraId";

    /// <summary>Directory (tenant) ID of the Entra ID tenant, e.g. a GUID or "contoso.onmicrosoft.com".</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Application (client) ID of the app registration that represents this
    /// MCP server. Required in addition to <see cref="Audience"/> when the
    /// server itself calls a downstream API on behalf of the signed-in user
    /// (On-Behalf-Of flow) - see <c>Services/GraphOboService.cs</c>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client secret for the server's app registration. Only needed for the
    /// On-Behalf-Of downstream API demo; never required for plain token
    /// validation. Keep this out of source control - use user-secrets or
    /// environment variables, never appsettings.json, in real projects.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Scope requested on the downstream API (Microsoft Graph) when
    /// exchanging the caller's token via the On-Behalf-Of flow.
    /// </summary>
    public string DownstreamScope { get; set; } = "https://graph.microsoft.com/User.Read";

    /// <summary>
    /// Scope requested on your own downstream API (DownstreamSampleApi, or
    /// any other custom-developed service) when exchanging the caller's
    /// token via the On-Behalf-Of flow, e.g. "api://{downstream-api-client-id}/access_as_user".
    /// Leave empty to skip this demo (see <see cref="SupportsCustomApiOnBehalfOf"/>).
    /// </summary>
    public string? CustomApiScope { get; set; }

    /// <summary>
    /// Base URL of your own downstream API, e.g. "https://localhost:7128/"
    /// for the bundled DownstreamSampleApi sample running locally.
    /// </summary>
    public string? CustomApiBaseUrl { get; set; }

    /// <summary>Expected audience of the access token: the Application ID URI (e.g.
    /// "api://{server-app-client-id}") or client ID of the app registration
    /// that represents this MCP server ("Expose an API").
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>Scope clients must request/hold to call the MCP tools, e.g. "mcp.tools".</summary>
    public string Scope { get; set; } = "mcp.tools";

    /// <summary>True once both TenantId and Audience have been provided.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(Audience);

    /// <summary>
    /// True once ClientId and ClientSecret are also available, i.e. enough to
    /// perform an On-Behalf-Of token exchange with a downstream API.
    /// </summary>
    public bool SupportsOnBehalfOf => IsConfigured
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// True once everything needed to perform an On-Behalf-Of exchange
    /// against your own custom downstream API is available: the base
    /// requirements for <see cref="SupportsOnBehalfOf"/>, plus
    /// <see cref="CustomApiScope"/> and <see cref="CustomApiBaseUrl"/>.
    /// </summary>
    public bool SupportsCustomApiOnBehalfOf => SupportsOnBehalfOf
        && !string.IsNullOrWhiteSpace(CustomApiScope)
        && !string.IsNullOrWhiteSpace(CustomApiBaseUrl);
}
