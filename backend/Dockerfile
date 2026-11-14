# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY NexusBank.Api/NexusBank.Api.csproj NexusBank.Api/
COPY NexusBank.Application/NexusBank.Application.csproj NexusBank.Application/
COPY NexusBank.Domain/NexusBank.Domain.csproj NexusBank.Domain/
COPY NexusBank.Infrastructure/NexusBank.Infrastructure.csproj NexusBank.Infrastructure/
COPY NexusBank.IntegrationServices/NexusBank.IntegrationServices.csproj NexusBank.IntegrationServices/
COPY NexusBank.Workers/NexusBank.Workers.csproj NexusBank.Workers/
COPY NexusBank.Shared/NexusBank.Shared.csproj NexusBank.Shared/
RUN dotnet restore NexusBank.Api/NexusBank.Api.csproj

COPY . .
RUN dotnet publish NexusBank.Api/NexusBank.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl backs the container healthcheck and lets operators probe /health directly.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

COPY --from=build /app/publish .

EXPOSE 8080

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "NexusBank.Api.dll"]
