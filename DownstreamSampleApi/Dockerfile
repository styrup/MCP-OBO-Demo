FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DownstreamSampleApi.csproj ./
RUN dotnet restore DownstreamSampleApi.csproj

COPY . .
RUN dotnet publish DownstreamSampleApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "DownstreamSampleApi.dll"]
