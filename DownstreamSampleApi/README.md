# DownstreamSampleApi

Et minimalt ASP.NET Core-API, der demonstrerer "jeres eget udviklede API" i
On-Behalf-Of (OBO)-mønstret beskrevet i
[merill/mcp-entra-design](https://github.com/merill/mcp-entra-design). Det
bruges som modpart, når `McpDummyServer`'s `call_custom_api`-tool viser,
hvordan man kalder en anden service end Microsoft Graph på vegne af den
indloggede bruger.

Se den fulde forklaring, arkitektur-diagram og trin-for-trin opsætning
(app-registrering, scope, admin consent, konfiguration) i
[`McpDummyServer/README.md`](../McpDummyServer/README.md), afsnittet
**"Kalde din egen downstream-service (custom API)"**.

## Kør

```powershell
dotnet run
```

Lytter som standard på `https://localhost:7128` (se
`Properties/launchSettings.json`).

## Endpoints

| Endpoint       | Beskrivelse |
|----------------|-------------|
| `GET /`        | Simpel status-besked, ingen auth krævet. |
| `GET /api/profile` | Kræver et gyldigt Entra ID access token (når `EntraId:TenantId`/`EntraId:Audience` er sat i `appsettings.json`/user-secrets). Returnerer navn, object-ID og scopes udtrukket fra tokenets claims - som bevis på at brugerens identitet er bevaret hele vejen fra MCP-klienten, gennem `McpDummyServer`, og ind i dette API via On-Behalf-Of. |

Uden `EntraId`-konfiguration kører API'et uafbeskyttet (praktisk til lokal
læring), og skriver en advarsel til stderr ved opstart - samme
graceful-degradation-princip som i `McpDummyServer`.
