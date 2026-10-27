# The backend image. Build context: backend/.
#   docker build -t authserver-backend:dev --build-arg VERSION=$(git describe --tags --match=* --long) \
#     --build-arg COMMIT=$(git rev-parse HEAD) backend
#   docker run --rm -p 8080:8080 -e ConnectionStrings__AuthServer="..." authserver-backend:dev

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so the layer survives source-only changes.
COPY NuGet.config Directory.Build.props Directory.Packages.props AuthServer.slnx ./
COPY src/AuthServer.Host/AuthServer.Host.csproj src/AuthServer.Host/
COPY src/AuthServer.Shared/AuthServer.Shared.csproj src/AuthServer.Shared/
COPY src/Modules/Identity/AuthServer.Identity/AuthServer.Identity.csproj src/Modules/Identity/AuthServer.Identity/
COPY src/Modules/Identity/AuthServer.Identity.Contracts/AuthServer.Identity.Contracts.csproj src/Modules/Identity/AuthServer.Identity.Contracts/
COPY src/Modules/Sync/AuthServer.Sync/AuthServer.Sync.csproj src/Modules/Sync/AuthServer.Sync/
COPY src/Modules/Firmware/AuthServer.Firmware/AuthServer.Firmware.csproj src/Modules/Firmware/AuthServer.Firmware/
COPY src/Modules/Firmware/AuthServer.Firmware.Contracts/AuthServer.Firmware.Contracts.csproj src/Modules/Firmware/AuthServer.Firmware.Contracts/
RUN dotnet restore src/AuthServer.Host/AuthServer.Host.csproj

COPY src/ src/
# No .git in the context, so version (git describe) and commit come in as arguments
# (GET /api/version shows them). An empty VERSION leaves 0.0.0: a bare -p:Version= would fail.
ARG VERSION=
ARG COMMIT=
RUN dotnet publish src/AuthServer.Host/AuthServer.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    ${VERSION:+-p:Version=$VERSION} \
    -p:SourceRevisionId=${COMMIT} \
    -p:UseAppHost=false

# Chiseled: no shell, no package manager, non-root by default. No ICU (globalization invariant)
# and no tzdata: the backend works in UTC. Probes come from outside (no curl inside).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app ./

# The base image listens on ASPNETCORE_HTTP_PORTS=8080 (non-root, so above 1024); dotnet run uses
# launchSettings.json's 5080 instead.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "AuthServer.Host.dll"]
