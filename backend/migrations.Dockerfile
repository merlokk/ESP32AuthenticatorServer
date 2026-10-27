# The migrations image: one EF Core migration bundle per module schema and a runner that applies
# them in order (architecture.md §6). Build context: backend/.
#   docker build -f backend/migrations.Dockerfile -t authserver-migrations:dev backend
#   docker run --rm -e ConnectionStrings__AuthServer="Host=...;Database=authserver;..." authserver-migrations:dev
#
# It runs as a Kubernetes Job (compose: a service the backend waits for) before the backend rolls,
# never as an initContainer (one per replica) and never inside the backend at startup.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The tool's major is EF Core's.
RUN dotnet tool install --global dotnet-ef --version 10.0.14
ENV PATH="/root/.dotnet/tools:${PATH}"

COPY NuGet.config Directory.Build.props Directory.Packages.props ./
COPY src/AuthServer.Host/AuthServer.Host.csproj src/AuthServer.Host/
COPY src/AuthServer.Shared/AuthServer.Shared.csproj src/AuthServer.Shared/
COPY src/Modules/Identity/AuthServer.Identity/AuthServer.Identity.csproj src/Modules/Identity/AuthServer.Identity/
COPY src/Modules/Identity/AuthServer.Identity.Contracts/AuthServer.Identity.Contracts.csproj src/Modules/Identity/AuthServer.Identity.Contracts/
COPY src/Modules/Sync/AuthServer.Sync/AuthServer.Sync.csproj src/Modules/Sync/AuthServer.Sync/
# The bundle is published for --target-runtime, so the RID-specific assets must exist too.
RUN dotnet restore src/AuthServer.Host/AuthServer.Host.csproj \
 && dotnet restore src/AuthServer.Host/AuthServer.Host.csproj --runtime linux-x64

COPY src/ src/

# dotnet ef builds the host's service provider to reach a DbContext; it connects to nothing. The
# .invalid host (RFC 6761) never resolves, so the placeholder cannot reach anything real.
ENV ConnectionStrings__AuthServer="Host=migrations-build.invalid;Database=authserver;Username=none;Password=none"

# One bundle per module (one DbContext and one history table per schema), plus the idempotent SQL
# for review: docker run --rm --entrypoint cat authserver-migrations:TAG /sql/identity.sql
# Framework-dependent: the runtime image carries ASP.NET Core once for all bundles.
RUN set -eu; \
    mkdir -p /migrations /sql; \
    for spec in \
        "identity:src/Modules/Identity/AuthServer.Identity:IdentityDbContext" \
        "sync:src/Modules/Sync/AuthServer.Sync:SyncDbContext" \
    ; do \
        schema="${spec%%:*}"; rest="${spec#*:}"; \
        project="${rest%%:*}"; context="${rest#*:}"; \
        dotnet ef migrations bundle --project "${project}" --startup-project src/AuthServer.Host \
            --context "${context}" --configuration Release --target-runtime linux-x64 \
            --output "/migrations/${schema}/efbundle" --force; \
        dotnet ef migrations script --idempotent --project "${project}" --startup-project src/AuthServer.Host \
            --context "${context}" --configuration Release --output "/sql/${schema}.sql"; \
    done

# Not chiseled: the runner is a shell script. aspnet, not runtime: a bundle builds the host's
# service provider and needs Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Npgsql probes GSSAPI on connect; without the library a successful Job logs "Error: ...".
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /migrations /migrations
COPY --from=build /sql /sql
COPY apply-migrations.sh /usr/local/bin/apply-migrations.sh
RUN chmod +x /usr/local/bin/apply-migrations.sh

USER $APP_UID
ENTRYPOINT ["/usr/local/bin/apply-migrations.sh"]
