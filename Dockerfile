# ==============================================================================
# Stage 1: Build React + Vite SPA (Node 20 Alpine)
# ==============================================================================
FROM node:20-alpine AS ui-build
WORKDIR /workspace/ui

# Install dependencies with lockfile consistency
COPY ui/package.json ui/package-lock.json* ./
RUN npm ci

# Copy UI source files and produce optimized production bundle
COPY ui/ ./
RUN npm run build

# ==============================================================================
# Stage 2: Build & Publish .NET Web API with Embedded Static UI
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

# Copy project files first for optimal NuGet layer caching
COPY src/ZatcaBridge.Domain/ZatcaBridge.Domain.csproj src/ZatcaBridge.Domain/
COPY src/ZatcaBridge.Application/ZatcaBridge.Application.csproj src/ZatcaBridge.Application/
COPY src/ZatcaBridge.Infrastructure/ZatcaBridge.Infrastructure.csproj src/ZatcaBridge.Infrastructure/
COPY src/ZatcaBridge.Api/ZatcaBridge.Api.csproj src/ZatcaBridge.Api/
RUN dotnet restore src/ZatcaBridge.Api/ZatcaBridge.Api.csproj

# Copy C# source code
COPY src/ src/

# Overlay freshly built frontend assets into wwwroot
COPY --from=ui-build /workspace/src/ZatcaBridge.Api/wwwroot/ src/ZatcaBridge.Api/wwwroot/

# Publish Release build
RUN dotnet publish src/ZatcaBridge.Api/ZatcaBridge.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ==============================================================================
# Stage 3: Lightweight & Hardened ASP.NET Core Runtime
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

EXPOSE 8080

# Production environment configurations
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__DefaultConnection="Data Source=/app/data/zatca_bridge.db" \
    DATA_PROTECTION_KEYS_PATH="/app/keys"

# Prepare persistent volume folders and assign ownership to built-in non-root app user ($APP_UID)
RUN mkdir -p /app/data /app/keys && chown -R $APP_UID:$APP_UID /app

# Copy compiled application
COPY --from=dotnet-build --chown=$APP_UID:$APP_UID /app/publish .

# Run as non-root user for container security
USER $APP_UID

VOLUME ["/app/data", "/app/keys"]

ENTRYPOINT ["dotnet", "ZatcaBridge.Api.dll"]
