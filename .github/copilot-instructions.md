# Copilot instructions for this repo

This is a learning/reference repo for MCP (Model Context Protocol) server development
in .NET, built incrementally to demonstrate: basic tools, dual transports (stdio + HTTP),
Entra ID OAuth protection, and the On-Behalf-Of (OBO) pattern for calling downstream APIs.
There is no solution (`.sln`) file - the two projects are independent and each built/run
from its own folder.

## Projects

- `McpDummyServer/` - the MCP server itself.
- `DownstreamSampleApi/` - a minimal, separate ASP.NET Core API used only to demonstrate
  calling a *custom-developed* downstream service via OBO (as opposed to Microsoft Graph).
  It has its own Entra ID app registration, independent of the MCP server's.

## Build & run

No test suite or linter is configured in this repo - there's nothing to run beyond build.

```powershell
cd McpDummyServer
dotnet build                      # 0 errors/0 warnings is the expected baseline
dotnet run                        # stdio transport (for VS Code / Claude Desktop clients)
dotnet run -- --http              # HTTP transport, default http://localhost:5000/mcp
$env:ASPNETCORE_URLS = "http://localhost:5123"; dotnet run -- --http   # custom port
```

```powershell
cd DownstreamSampleApi
dotnet build
dotnet run                        # listens on https://localhost:7128 by default (see Properties/launchSettings.json)
```

Manual protocol testing (no automated tests exist): send JSON-RPC lines over stdio, or use
curl against the HTTP endpoint. Streamable HTTP responses come back as Server-Sent Events,
so requests need `Accept: application/json, text/event-stream`:

```powershell
curl -X POST http://localhost:5123/mcp `
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" `
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

`npx @modelcontextprotocol/inspector dotnet run --project .` also works for interactive testing.

## Architecture (McpDummyServer)

- **Dual transport, one tool set**: `Program.cs` `Main` branches on the `--http` CLI arg.
  `RunStdioAsync` uses `Host.CreateApplicationBuilder`; `RunHttpAsync` uses
  `WebApplication.CreateBuilder` + `.WithHttpTransport()` + `app.MapMcp("/mcp")`. Both call
  `.WithToolsFromAssembly()` on the same assembly, so tool code is transport-agnostic and
  never needs transport-specific branching itself.
- **Tool auto-discovery is the extensibility mechanism**: any class marked
  `[McpServerToolType]` with `[McpServerTool]`-attributed methods is found automatically.
  Adding a capability means adding a class under `Tools/` (or `Resources/`/`Prompts/` +
  `.WithResourcesFromAssembly()`/`.WithPromptsFromAssembly()`) - never touching `Program.cs`.
- **Tools can be static or instance classes.** Instance classes (e.g. `GraphProfileTool`,
  `CustomApiTool`) get constructor parameters resolved from DI exactly like ASP.NET Core
  controllers - register the dependency in `builder.Services` and take it as a constructor
  parameter.
- **stdio logging must go to stderr**, never stdout - stdout carries JSON-RPC messages and
  any stray log line breaks the protocol. See the `LogToStandardErrorThreshold` setup in
  `RunStdioAsync`.
- **Entra ID OAuth is optional and self-detecting**: `EntraIdOptions.IsConfigured` is true
  once `EntraId:TenantId`/`EntraId:Audience` are set. If unset, the HTTP endpoint runs
  unauthenticated with a stderr warning (intentional, for local learning) - if set,
  JwtBearer + `.AddMcp()` (RFC 9728 Protected Resource Metadata at
  `/.well-known/oauth-protected-resource`) protect `/mcp`.
- **On-Behalf-Of (OBO) tools follow a strict template** (see `GraphOboService`/
  `GraphProfileTool` and `CustomApiOboService`/`CustomApiTool`): the tool pulls the inbound
  bearer token from `IHttpContextAccessor`, hands it to a singleton `*OboService` that uses
  MSAL's `AcquireTokenOnBehalfOf` to exchange it for a downstream-scoped token, then calls
  the downstream API with *that* token - never the server's own app identity. Copy this
  pair of files when adding a new downstream-service integration.
- **Graceful degradation is a deliberate design principle** applied everywhere
  auth-related: missing config, missing bearer token, or missing OBO setup returns a plain
  explanatory string from the tool instead of throwing, so the learning experience never
  hard-crashes. Preserve this behavior when adding new auth-dependent tools.

## Conventions

- Tool method descriptions and parameter descriptions use `[Description("...")]` - these
  strings are shown to MCP clients/LLMs, so keep them accurate when changing behavior.
- Config surfaces (`EntraIdOptions`, `DownstreamApiOptions`) expose computed `bool`
  properties (`IsConfigured`, `SupportsOnBehalfOf`, `SupportsCustomApiOnBehalfOf`) rather
  than scattering null-checks - reuse/extend these rather than re-deriving the checks
  inline.
- Secrets (`ClientSecret`, tenant/client IDs) belong in `dotnet user-secrets` or environment
  variables (`EntraId__TenantId` style), never committed to `appsettings.json`.
- `README.md` (in `McpDummyServer/`) is the primary user-facing doc and is kept in sync with
  every feature addition (transports, OAuth setup, OBO patterns, VS Code testing) - update it
  alongside code changes rather than treating docs as a separate pass.
