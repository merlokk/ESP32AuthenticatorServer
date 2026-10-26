# The backend image. Build context: backend/.
#   docker build -t authserver-backend:dev --build-arg VERSION=0.1.0 \
#     --build-arg COMMIT=$(git rev-parse HEAD) backend
#   docker run --rm -p 8080:8080 -e ConnectionStrings__AuthServer="..." authserver-backend:dev

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so the layer survives source-only changes.
COPY NuGet.config Directory.Build.props Directory.Packages.props AuthServer.slnx ./
COPY src/AuthServer.Host/AuthServer.Host.csproj src/AuthServer.Host/
COPY src/AuthServer.Shared/AuthServer.Shared.csproj src/AuthServer.Shared/
RUN dotnet restore src/AuthServer.Host/AuthServer.Host.csproj

COPY src/ src/
# No .git in the context, so the commit comes in as an argument (GET /api/version shows it).
ARG VERSION=0.1.0
ARG COMMIT=
RUN dotnet publish src/AuthServer.Host/AuthServer.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    -p:Version=${VERSION} \
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
