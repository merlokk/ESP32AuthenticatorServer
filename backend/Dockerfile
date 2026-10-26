# The backend image. Build context: backend/.
#   docker build -t authserver-backend:dev --build-arg VERSION=0.1.0 backend
#   docker run --rm -p 8080:8080 -e ConnectionStrings__AuthServer="..." authserver-backend:dev

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so the layer survives source-only changes.
COPY NuGet.config Directory.Build.props Directory.Packages.props AuthServer.slnx ./
COPY src/AuthServer.Host/AuthServer.Host.csproj src/AuthServer.Host/
COPY src/AuthServer.Shared/AuthServer.Shared.csproj src/AuthServer.Shared/
RUN dotnet restore src/AuthServer.Host/AuthServer.Host.csproj

COPY src/ src/
ARG VERSION=0.0.0
RUN dotnet publish src/AuthServer.Host/AuthServer.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    -p:Version=${VERSION} \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Overrides appsettings.json's localhost URL; non-root, so a port above 1024.
ENV Urls=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "AuthServer.Host.dll"]
