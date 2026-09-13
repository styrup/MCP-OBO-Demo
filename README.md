# mcp-dummy

Et lille test-/læringsprojekt til at forstå MCP (Model Context Protocol)
udvikling i .NET - bygget til gradvist at demonstrere flere features oven på
en simpel start.

## Projekter

- **[`McpDummyServer/`](McpDummyServer/README.md)** - selve MCP-serveren.
  Understøtter både stdio- og HTTP-transport, kan beskyttes med OAuth via
  Microsoft Entra ID, og demonstrerer On-Behalf-Of-kald til downstream-
  services (Microsoft Graph og en selvudviklet API). Se
  [`McpDummyServer/README.md`](McpDummyServer/README.md) for alle detaljer:
  hvordan man kører, tester og sætter Entra ID/OAuth op, samt hvordan man
  tester med VS Code.
- **[`DownstreamSampleApi/`](DownstreamSampleApi/README.md)** - et minimalt,
  separat API der bruges som eksempel på "jeres eget udviklede API", som
  MCP-serveren kalder via On-Behalf-Of. Se
  [`DownstreamSampleApi/README.md`](DownstreamSampleApi/README.md) for
  detaljer.

## Hurtig start

```powershell
cd McpDummyServer
dotnet run              # stdio-transport (til fx VS Code / Claude Desktop)
dotnet run -- --http    # HTTP-transport på http://localhost:5000/mcp
```

Se [`McpDummyServer/README.md`](McpDummyServer/README.md) for arkitektur,
alle tools, OAuth-opsætning og OBO-mønstre.
