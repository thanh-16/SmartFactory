# ------------------------------------------------------------------------------
# STAGE 1: Build & Publish (.NET 10 SDK)
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Cache restore layer for fast builds
COPY ["SmartFactory.Api/SmartFactory.Api.csproj", "SmartFactory.Api/"]
RUN dotnet restore "SmartFactory.Api/SmartFactory.Api.csproj"

# Copy remaining source code and publish release build
COPY SmartFactory.Api/ SmartFactory.Api/
WORKDIR "/src/SmartFactory.Api"
RUN dotnet publish "SmartFactory.Api.csproj" -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ------------------------------------------------------------------------------
# STAGE 2: Runtime (.NET 10 ASP.NET Non-Root)
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install curl for container HEALTHCHECK
USER root
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Ensure directories for SQLite WAL data and uploads exist with non-root ownership
RUN mkdir -p /app/data /app/wwwroot/uploads/defects && \
    chown -R $APP_UID:$APP_UID /app

# Copy compiled application and static assets (wwwroot)
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# Environment Configuration
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# Switch to non-root user
USER $APP_UID

# Container Healthcheck probe
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SmartFactory.Api.dll"]
