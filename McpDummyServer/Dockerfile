FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY McpDummyServer.csproj ./
RUN dotnet restore McpDummyServer.csproj

COPY . .
RUN dotnet publish McpDummyServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
# Always run with the HTTP transport in the container - stdio only makes sense
# for local clients launching the process directly.
ENTRYPOINT ["dotnet", "McpDummyServer.dll", "--http"]
