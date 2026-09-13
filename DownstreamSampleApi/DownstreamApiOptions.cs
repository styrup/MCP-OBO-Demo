namespace DownstreamSampleApi;

/// <summary>
/// Configuration for this sample's own Entra ID app registration - the
/// "downstream service" that McpDummyServer calls On-Behalf-Of the signed-in
/// user (see McpDummyServer's README.md, section "Kalde din egen downstream-
/// service (custom API)"). This is deliberately a *separate* app registration
/// from the MCP server's, mirroring a real-world setup where the MCP server
/// and the API it calls are different applications with different owners.
/// </summary>
public class DownstreamApiOptions
{
    public const string SectionName = "EntraId";

    /// <summary>Directory (tenant) ID of the Entra ID tenant.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Expected audience of incoming access tokens: the Application ID URI
    /// (e.g. "api://{this-api-client-id}") exposed by this API's own app
    /// registration ("Expose an API").
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>True once both TenantId and Audience have been provided.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(Audience);
}
