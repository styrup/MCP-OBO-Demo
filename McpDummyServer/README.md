# McpDummyServer

En lille MCP (Model Context Protocol) server til at lære MCP-udvikling i .NET.
Bygget med det officielle [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) SDK
oven på `Microsoft.Extensions.Hosting`.

## Kør serveren

Serveren understøtter to transports, med samme tools bag begge:

**Stdio** (default) – til lokale MCP-klienter som VS Code / Claude Desktop:

```powershell
dotnet run
```

Kommunikationen foregår via stdin/stdout med JSON-RPC. Al logging sendes til
**stderr**, så det ikke forstyrrer JSON-RPC-protokollen på stdout.

**HTTP** – til fjern-/netværksadgang, via `--http`-flaget:

```powershell
dotnet run -- --http
```

Serveren lytter som standard på `http://localhost:5000` (ASP.NET Core's
default), og MCP-endpointet er `http://localhost:5000/mcp`. Du kan ændre
adressen med `ASPNETCORE_URLS`, fx:

```powershell
$env:ASPNETCORE_URLS = "http://localhost:5123"
dotnet run -- --http
```

Test endpointet direkte med curl (Streamable HTTP transport - svar kommer som
Server-Sent-Events):

```powershell
curl -X POST http://localhost:5123/mcp `
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" `
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## Arkitektur

```
Program.cs          -> Host + DI-opsætning. Registrerer MCP-serveren og
                        vælger transport (stdio eller HTTP via --http-flag).
                        Begge dele bruger WithToolsFromAssembly() til at
                        scanne assemblien for tools - selve tool-koden er
                        transport-agnostisk og delt mellem de to.
                        I HTTP-mode konfigureres desuden Entra ID OAuth-
                        beskyttelse hvis EntraId:TenantId/Audience er sat.
EntraIdOptions.cs    -> Konfigurationsklasse for Entra ID-beskyttelsen
                        (TenantId, Audience, Scope) og for On-Behalf-Of-kald
                        til downstream-services (ClientId, ClientSecret,
                        DownstreamScope til Graph, CustomApiScope/
                        CustomApiBaseUrl til jeres egen service). Bundet fra
                        appsettings.json / miljøvariabler / user-secrets.
appsettings.json     -> Konfiguration, bl.a. "EntraId"- og "Mcp"-sektionerne.
Services/            -> Almindelige DI-registrerede services med logik der
                        ikke hører hjemme direkte i et tool.
  GraphOboService.cs -> Demonstrerer at kalde en downstream-service (Microsoft
                        Graph) på vegne af den indloggede bruger via
                        On-Behalf-Of-flowet (RFC 8693 token exchange).
  CustomApiOboService.cs -> Samme mønster, men mod jeres egen udviklede
                        service (den medfølgende DownstreamSampleApi) -
                        skabelonen til at kalde et internt/custom API.
Tools/               -> Ét tool-type (klasse) per emne. Hver public metode
                        med [McpServerTool] bliver automatisk et kaldbart
                        MCP-tool - ingen manuel registrering nødvendig.
  EchoTool.cs        -> Simpelt eksempel: ekko af tekst.
  CalculatorTool.cs  -> Eksempel med flere relaterede tools + fejlhåndtering.
  GraphProfileTool.cs -> Instans-tool (ikke static) der via DI får adgang til
                        GraphOboService og HTTP-requestens token, og viser
                        On-Behalf-Of-mønstret i praksis.
  CustomApiTool.cs   -> Samme mønster som GraphProfileTool, men kalder
                        CustomApiOboService / DownstreamSampleApi.
```

`DownstreamSampleApi/` er et separat, minimalt ASP.NET Core-projekt ved siden
af `McpDummyServer/` (egen `.csproj`, egen Entra ID-app-registrering). Det
repræsenterer "jeres eget interne API", som MCP-serveren kalder via
On-Behalf-Of - se afsnittet "Kalde din egen downstream-service" nedenfor.

Dette mønster (attribute-baseret discovery) er valgt specifikt, så
arkitekturen skalerer uden ændringer i `Program.cs`:

- **Nyt tool** → tilføj en ny public static metode med `[McpServerTool]` i en
  eksisterende `[McpServerToolType]`-klasse, eller opret en helt ny klasse i
  `Tools/`. Den bliver fundet automatisk ved næste build/run.
- **Resources** (statisk/dynamisk data klienten kan læse) → opret en
  `Resources/`-mappe med klasser markeret `[McpServerResourceType]` og kald
  `.WithResourcesFromAssembly()` i `Program.cs`.
- **Prompts** (genbrugelige prompt-skabeloner) → tilsvarende med
  `Prompts/` og `[McpServerPromptType]` + `.WithPromptsFromAssembly()`.
- **Dependency injection**: tools kan tage constructor- eller
  metode-parametre som bliver resolvet fra DI-containeren (fx en
  `HttpClient` eller din egen service) - registrer dem bare i
  `builder.Services` som normalt. `GraphProfileTool` er et konkret eksempel:
  den er en almindelig (ikke-static) klasse, og MCP SDK'et resolver dens
  constructor-parametre (`IHttpContextAccessor`, `GraphOboService`) fra DI,
  ligesom en controller i ASP.NET Core.
- **State/services**: hvis et tool skal bruge delt tilstand eller kalde eksterne
  systemer, læg logikken i en almindelig service-klasse registreret i DI, og
  lad tool-metoden være en tynd wrapper omkring den (se `GraphOboService`).

## Nuværende tools

| Tool           | Beskrivelse                                  |
|----------------|-----------------------------------------------|
| `echo`         | Ekkoer en besked tilbage, evt. i store bogstaver |
| `add`          | Lægger to tal sammen                          |
| `divide`       | Dividerer to tal, kaster fejl ved division med 0 |
| `whoami_graph` | Kalder Microsoft Graph `/me` på vegne af den indloggede bruger (On-Behalf-Of) - kræver HTTP + Entra ID, se nedenfor |
| `call_custom_api` | Kalder jeres egen downstream-service (`DownstreamSampleApi`) på vegne af den indloggede bruger (On-Behalf-Of) - kræver HTTP + Entra ID, se nedenfor |

## Test manuelt

Du kan teste serveren uden en rigtig MCP-klient ved at sende JSON-RPC-linjer
direkte til stdin, fx via et lille script eller værktøjer som
[MCP Inspector](https://github.com/modelcontextprotocol/inspector):

```
npx @modelcontextprotocol/inspector dotnet run --project .
```

## OAuth-beskyttelse af HTTP-endpointet med Microsoft Entra ID

`--http`-transporten kan beskyttes med OAuth 2.0 via Microsoft Entra ID, efter
mønstret beskrevet i [merill/mcp-entra-design](https://github.com/merill/mcp-entra-design)
(session om at bygge enterprise-MCP'er på Entra). Konkret implementerer
serveren:

- **RFC 9728 – OAuth 2.0 Protected Resource Metadata**: serveren udstiller
  `/.well-known/oauth-protected-resource`, så MCP-klienter automatisk kan
  opdage hvilken authorization server og hvilke scopes der kræves.
- **RFC 8414 – Authorization Server Metadata**: dækkes af Entra ID's egen
  OIDC-discovery-dokument (`{authority}/.well-known/openid-configuration`),
  som JWT-valideringen bruger automatisk til at hente signing keys m.m.
- **RFC 8707 – Resource Indicators**: access token skal have `aud` (audience)
  der matcher denne servers App ID URI - forhindrer at et token udstedt til
  en anden API kan genbruges her.
- Dynamic Client Registration (RFC 7591) understøttes **ikke** af Entra ID -
  klient-appen skal derfor forhåndsregistreres (se trin 2 nedenfor).

Uden konfiguration kører HTTP-endpointet **uden** autentificering (praktisk
til lokal læring), og serveren skriver en advarsel til stderr ved opstart.
Så snart `EntraId:TenantId` og `EntraId:Audience` er sat, kræver
`/mcp`-endpointet et gyldigt Entra ID access token.

### Sådan sætter du Entra ID op

**Trin 1 - Registrér en app for selve MCP-serveren ("Expose an API")**

1. Gå til [Microsoft Entra admin center](https://entra.microsoft.com) →
   **App registrations** → **New registration**.
2. Navn: fx `McpDummyServer`. Lad redirect URI stå tom (serveren modtager
   ingen redirects). Klik **Register**.
3. Noter **Application (client) ID** og **Directory (tenant) ID** fra
   oversigtssiden.
4. Gå til **Expose an API**:
   - Klik **Add** ud for "Application ID URI" og accepter forslaget
     (`api://<client-id>`), eller sæt dit eget. Dette er værdien du skal
     bruge som `EntraId:Audience`.
   - Klik **Add a scope**: Scope name `mcp.tools`, Who can consent
     `Admins and users`, giv en kort admin/user consent-beskrivelse (fx
     "Allow calling MCP tools on this server"). Gem.

**Trin 2 - Registrér en app for MCP-klienten (fx VS Code, Claude Desktop,
eller dit eget testscript)**

1. **App registrations** → **New registration**. Navn: fx
   `McpDummyServer Client`. Vælg passende redirect URI-type (fx "Public
   client/native" med `http://localhost` for CLI/desktop-flows, eller "Web"
   hvis klienten er en webapp).
2. Under **API permissions** → **Add a permission** → **My APIs** → vælg
   `McpDummyServer` → marker `mcp.tools`-scopet → **Add permissions**.
3. Hvis scopet kræver admin consent, klik **Grant admin consent**.
4. Klienten bruger nu client-app'ens ID til at hente et access token (via
   Authorization Code + PKCE, device code, eller en anden passende OAuth
   2.0-flow) med scope `api://<server-client-id>/mcp.tools`, og sender det
   som `Authorization: Bearer <token>` til MCP-serveren.

**Trin 3 - Konfigurér serveren**

Sæt `EntraId:TenantId` og `EntraId:Audience` (App ID URI fra trin 1), enten i
`appsettings.json`, som miljøvariabler, eller (anbefalet lokalt) via
`dotnet user-secrets`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "EntraId:TenantId" "<din-tenant-id>"
dotnet user-secrets set "EntraId:Audience" "api://<server-client-id>"
```

Eller via miljøvariabler (nyttigt i containere/CI):

```powershell
$env:EntraId__TenantId = "<din-tenant-id>"
$env:EntraId__Audience = "api://<server-client-id>"
```

Sæt også `Mcp:ServerUrl` til den fulde, offentligt tilgængelige URL til
`/mcp`-endpointet (skal matche præcis, jf. RFC 8707 audience-binding), fx
`https://mit-domæne.example/mcp`.

**Trin 4 - Kør og test**

```powershell
dotnet run -- --http
```

Uden token: `POST /mcp` giver `401 Unauthorized` med en
`WWW-Authenticate`-header, der peger på
`/.well-known/oauth-protected-resource` - det er sådan en MCP-klient
automatisk opdager hvor den skal logge ind. Med et gyldigt Entra ID-token i
`Authorization: Bearer <token>`-headeren giver kaldene normale MCP-svar.

## Teste med VS Code (HTTP + OAuth)

Ja, det er muligt - VS Code's MCP-understøttelse (Copilot Chat / agent-tilstand)
kan bruges som en rigtig, OAuth-beskyttet MCP-klient mod denne server. VS Code
forstår automatisk MCP'ens auth-flow: den opdager
`/.well-known/oauth-protected-resource` (RFC 9728), åbner en browser til login
hos Entra ID, håndterer consent, og vedhæfter derefter
`Authorization: Bearer <token>` på alle efterfølgende kald - helt uden at du
selv skal håndtere tokens.

### Client-ID: brug VS Code's egen, allerede registrerede app (anbefalet)

Da Entra ID ikke understøtter Dynamic Client Registration (se afsnittet
ovenfor), skal enhver OAuth-klient bruge et forudregistreret client-ID. Det
gode ved VS Code er, at Microsoft allerede har forudregistreret en
multi-tenant "public client"-app til netop dette formål, som er tilgængelig i
alle tenants uden yderligere opsætning:

```
VS Code client ID: aebc6443-996d-45c2-90f0-388ff96faa56
```

Det betyder, at du normalt **ikke** behøver at registrere en separat
klient-app (som beskrevet i "Trin 2" ovenfor) bare for at teste med VS Code -
du kan pege direkte på VS Code's egen app-ID. Hvis din organisation kræver
tættere styring/audit af hvilke klienter der må tilgå API'et (fx blokerer
"unmanaged" apps, eller kræver at alle klienter er eksplicit godkendt), så
registrér i stedet din egen klient-app efter "Trin 2" og brug dens client-ID
i stedet.

### Opsætning

**Trin 1 - Start serveren med Entra ID konfigureret**

Følg "Sådan sætter du Entra ID op" ovenfor (`EntraId:TenantId`,
`EntraId:Audience`, evt. `EntraId:ClientId`/`ClientSecret` hvis du også vil
teste `whoami_graph`/`call_custom_api`), og kør:

```powershell
dotnet run -- --http
```

Serveren lytter som standard på `http://localhost:5000/mcp`. Sørg for at
`Mcp:ServerUrl` i `appsettings.json` matcher præcis den URL du rent faktisk
bruger (inkl. port), da RFC 8707-audience-bindingen er følsom over for
uoverensstemmelser.

**Trin 2 - Opret `.vscode/mcp.json` i dit workspace**

```json
{
  "servers": {
    "mcpDummyServer": {
      "type": "http",
      "url": "http://localhost:5000/mcp",
      "oauth": {
        "clientId": "aebc6443-996d-45c2-90f0-388ff96faa56"
      }
    }
  }
}
```

Du kan også oprette den via Command Palette (`Ctrl+Shift+P`) →
**MCP: Add Server** → vælg "HTTP" → indsæt URL'en, og tilføj bagefter
`oauth`-objektet manuelt i den genererede fil (feltet understøttes ikke i den
guidede flow, men læses fint fra filen).

**Trin 3 - Start og godkend serveren i VS Code**

1. Kør **MCP: List Servers** fra Command Palette, vælg `mcpDummyServer`, og
   start den. VS Code beder først om at bekræfte, at du stoler på serveren.
2. Ved første kald til et tool åbner VS Code en browser til Entra ID-login.
   Log ind, og accepter consent-skærmen ("Visual Studio Code vil have adgang
   til McpDummyServer med scope mcp.tools" el. lign.).
3. VS Code gemmer token'et (og fornyer det automatisk med refresh token) og
   genbruger det til efterfølgende kald.

**Trin 4 - Test**

Åbn Copilot Chat i agent-tilstand og bed den bruge et af tools'ene, fx:

```
Brug add-tool'et til at lægge 21 og 21 sammen
```

eller, hvis du også har sat OBO-delen op:

```
Kald whoami_graph og fortæl mig hvad den svarer
```

Du kan se de tilgængelige tools og deres status under **MCP: List Servers**,
og nulstille cachede tools/godkendelser med **MCP: Reset Cached Tools**
henholdsvis **MCP: Reset Trust**, hvis noget opfører sig underligt efter en
kodeændring.

### Fejlfinding

- **"AADSTS65001: The user or administrator has not consented..."** - scopet
  `mcp.tools` på din API-app-registrering har sandsynligvis "Who can
  consent" sat til kun Admins. Sæt det til "Admins and users" (se "Trin 1" i
  Entra ID-afsnittet), eller bed en admin om at give consent for hele
  tenanten.
- **Login-loop / forkert tenant** - hvis du er logget ind med en anden
  Microsoft-konto i browseren end den, der hører til din test-tenant, kan
  login fejle stille. Prøv en inprivate/incognito-browser-session ved første
  login.
- **401 bliver ved med at komme, selv efter login** - tjek at
  `EntraId:Audience` i `appsettings.json` er præcis App ID URI'en fra "Expose
  an API" (inkl. `api://`-præfiks), og at `Mcp:ServerUrl` matcher den URL VS
  Code rent faktisk forbinder til.
- **Ændret tools-liste vises ikke** - kør **MCP: Reset Cached Tools**.
- Server-siden logger altid til stderr ved auth-fejl/succes
  (`Entra ID token validation failed/validated for: ...`), så terminalen hvor
  du kører `dotnet run -- --http` er et godt sted at se hvad der reelt sker.

## Kalde en downstream-service (On-Behalf-Of)

`mcp-entra-design` beskriver også hvad man gør, når MCP-serveren selv skal
kalde en anden service (fx Microsoft Graph, eller jeres eget interne API) på
vegne af brugeren, i stedet for kun at validere det indkommende token. Deres
reference-eksempel er Microsoft Graph MCP Server, som er en ren "delegated
proxy":

```
Bruger (logget ind)
  -> MCP-klient (fx VS Code)
       -> MCP-server (validerer det indkommende token) <- det vi allerede har
            -> Downstream-API (kaldes med et token udstedt via
               On-Behalf-Of / RFC 8693 token exchange, så
               downstream-API'et ser BRUGERENS identitet - ikke
               serverens egen)
```

Det vigtige princip: MCP-serveren bruger **ikke** sin egen app-identitet til
at kalde downstream-API'et (det ville være "app-only"-adgang og omgå
brugerens faktiske rettigheder). I stedet udveksles brugerens token for et
nyt token, scoped til downstream-API'et, mens brugeren forbliver "subject".

Dette er implementeret i løsningen som en selvstændig demo:

- **`Services/GraphOboService.cs`** bruger MSAL.NET
  (`IConfidentialClientApplication.AcquireTokenOnBehalfOf`) til at udveksle
  det indkommende token for et Microsoft Graph-token, og kalder derefter
  `https://graph.microsoft.com/v1.0/me` med det.
- **`Tools/GraphProfileTool.cs`** er tool'et (`whoami_graph`) der henter det
  indkommende token fra HTTP-requestens Authorization-header (via
  `IHttpContextAccessor`) og sender det videre til `GraphOboService`.
- Vi bruger Microsoft Graph's `/me`-endpoint fordi *alle* Entra ID-tenants har
  det tilgængeligt uden ekstra opsætning - men mønstret er identisk uanset
  hvilket downstream-API I reelt vil kalde fra jeres egne tools.

### Sådan sætter du On-Behalf-Of op

Dette bygger oven på Entra ID-opsætningen ovenfor (samme app-registrering fra
"Trin 1").

1. **Tilføj et client secret** til serverens app-registrering: **App
   registrations** → `McpDummyServer` → **Certificates & secrets** → **New
   client secret**. Kopiér værdien med det samme (den vises kun én gang).
2. **Giv serveren delegeret adgang til Microsoft Graph**: samme
   app-registrering → **API permissions** → **Add a permission** →
   **Microsoft Graph** → **Delegated permissions** → vælg `User.Read` →
   **Add permissions**. Klik derefter **Grant admin consent**.
3. **Konfigurér serveren** med de ekstra felter (samme metode som
   `TenantId`/`Audience` - user-secrets anbefales, da `ClientSecret` er
   hemmeligt):

   ```powershell
   dotnet user-secrets set "EntraId:ClientId" "<server-app-client-id>"
   dotnet user-secrets set "EntraId:ClientSecret" "<client-secret-værdi>"
   ```

   `EntraId:DownstreamScope` defaulter til
   `https://graph.microsoft.com/User.Read` og behøver normalt ikke ændres.

4. **Test**: kald `whoami_graph` som et MCP-tool (via en rigtig MCP-klient
   med et Entra ID-token, eller direkte med curl og en Authorization-header
   med et access token). Serveren udveksler dit token og returnerer dit
   Graph-navn/UPN/mail.

Uden `ClientId`/`ClientSecret` konfigureret svarer `whoami_graph` med en
forklarende fejlbesked i stedet for at fejle uventet - ligesom resten af
løsningen er den fejlende sti eksplicit og informativ, så det er tydeligt
hvad der mangler.

## Kalde din egen downstream-service (custom API)

Ovenstående demo bruger Microsoft Graph, fordi det er tilgængeligt i alle
tenants uden ekstra opsætning. Men i praksis vil jeres MCP-tools ofte skulle
kalde jeres **egen** udviklede service i stedet - fx et internt API bag
Entra ID. Løsningen indeholder derfor et konkret, kørende eksempel på præcis
det: et separat lille API-projekt, `DownstreamSampleApi/`, som MCP-serveren
kalder via samme On-Behalf-Of-mønster.

```
Bruger (logget ind)
  -> MCP-klient
       -> McpDummyServer (validerer indkommende token)
            -> On-Behalf-Of: udveksler token til DownstreamSampleApi's scope
                 -> DownstreamSampleApi (GET /api/profile)
                    - egen Entra ID app-registrering
                    - validerer selv det modtagne token (samme måde som
                      McpDummyServer gør det for MCP-klienter)
                    - returnerer brugerens navn/oid/scopes som bevis på at
                      identiteten er bevaret hele vejen igennem
```

Det er bevidst to **forskellige** app-registreringer (én for MCP-serveren, én
for `DownstreamSampleApi`), ligesom i et rigtigt setup hvor MCP-serveren og
det API den kalder typisk ejes/driftes hver for sig.

Implementeringen følger nøjagtig samme opskrift som Graph-eksemplet, bare med
et andet scope og en anden baseadresse:

- **`Services/CustomApiOboService.cs`** udveksler det indkommende token til et
  token scoped til `DownstreamSampleApi` (via `EntraId:CustomApiScope`), og
  kalder derefter `GET /api/profile` på `EntraId:CustomApiBaseUrl` med det.
- **`Tools/CustomApiTool.cs`** er tool'et (`call_custom_api`), der - ligesom
  `GraphProfileTool` - henter det indkommende token fra HTTP-requestens
  Authorization-header og sender det videre.
- **`DownstreamSampleApi/`** er selve "jeres egen service": et minimalt
  ASP.NET Core-projekt med sin egen `Program.cs`, der validerer indkommende
  Entra ID-tokens (samme JwtBearer-opsætning som McpDummyServer) og
  eksponerer `GET /api/profile`, som returnerer den kaldende brugers
  identitet udtrukket fra tokenets claims.

### Sådan sætter du det op

**Trin 1 - Registrér en app for `DownstreamSampleApi`**

Samme fremgangsmåde som "Trin 1" i Entra ID-afsnittet ovenfor, men for en ny,
separat app:

1. **App registrations** → **New registration**. Navn: fx
   `DownstreamSampleApi`. Ingen redirect URI nødvendig.
2. Noter **Application (client) ID**.
3. **Expose an API** → **Add** for Application ID URI (accepter forslaget,
   `api://<downstream-api-client-id>`) → **Add a scope**: navn
   `access_as_user`, "Who can consent" = `Admins and users`, udfyld
   consent-teksterne. Gem. Denne fulde scope-streng
   (`api://<downstream-api-client-id>/access_as_user`) er værdien til
   `EntraId:CustomApiScope` på **MCP-serveren**.

**Trin 2 - Giv MCP-serverens app-registrering adgang til `DownstreamSampleApi`**

1. Gå til `McpDummyServer`-app-registreringen (samme som bruges til OAuth-
   beskyttelsen ovenfor) → **API permissions** → **Add a permission** →
   **My APIs** → vælg `DownstreamSampleApi` → marker `access_as_user` →
   **Add permissions**.
2. Klik **Grant admin consent** (nødvendigt for at On-Behalf-Of-udvekslingen
   kan lykkes uden en ekstra brugerinteraktion).

**Trin 3 - Konfigurér og kør `DownstreamSampleApi`**

Den bruger samme `EntraId:TenantId`/`EntraId:Audience`-mønster som
McpDummyServer, men peger på sin *egen* app:

```powershell
cd DownstreamSampleApi
dotnet user-secrets init
dotnet user-secrets set "EntraId:TenantId" "<din-tenant-id>"
dotnet user-secrets set "EntraId:Audience" "api://<downstream-api-client-id>"
dotnet run
```

Den lytter som standard på `https://localhost:7128` (se
`Properties/launchSettings.json`).

**Trin 4 - Konfigurér `McpDummyServer` til at kalde den**

```powershell
cd McpDummyServer
dotnet user-secrets set "EntraId:CustomApiScope" "api://<downstream-api-client-id>/access_as_user"
dotnet user-secrets set "EntraId:CustomApiBaseUrl" "https://localhost:7128/"
```

(Kør denne samtidig med Entra ID- og OBO-opsætningen fra afsnittene
ovenfor - `ClientId`/`ClientSecret` er fælles for begge OBO-demoer.)

**Trin 5 - Test**

Start begge servere (`DownstreamSampleApi` og `McpDummyServer -- --http`), og
kald `call_custom_api` som MCP-tool med et gyldigt Entra ID-token. MCP-
serveren udveksler dit token til et token for `DownstreamSampleApi`, kalder
`/api/profile`, og du får din identitet retur - denne gang bekræftet af *jeres
egen* service, ikke Microsoft Graph.

Uden konfiguration (`CustomApiScope`/`CustomApiBaseUrl`) svarer
`call_custom_api` med en forklarende fejlbesked, ligesom `whoami_graph` gør
det for Graph-demoen.

## Næste skridt (idéer til udvidelse)

- Tilføj et resource, der eksponerer en simpel liste/fil.
- Tilføj en prompt-skabelon.
- Udvid `DownstreamSampleApi` med flere endpoints/rigtig forretningslogik, og
  tilføj tilsvarende tools der kalder dem via `CustomApiOboService`-mønstret.
