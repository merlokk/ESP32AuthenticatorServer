using AuthServer.Host.Startup;
using AuthServer.Identity;
using AuthServer.Sync;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;

// The tag that separates readiness from liveness: liveness runs no checks at all.
const string ReadyTag = "ready";

var builder = WebApplication.CreateBuilder(args);

// Host-wide composition lives here and only here (architecture.md §4.5): a module contributes its
// endpoints, its DbContext and its own registrations through AddXxxModule(), never a global default.
// TickerQ comes with the first job.

builder.Services.AddFastEndpoints(options => options.Assemblies = [typeof(IdentityModule).Assembly, typeof(SyncModule).Assembly]);

builder.Services.SwaggerDocument(options =>
{
    options.DocumentSettings = settings =>
    {
        settings.Title = "AuthServer";
        settings.Version = "v1";
        settings.SchemaSettings.SchemaProcessors.Add(new RequireNonNullablePropertiesProcessor());
    };
    options.ShortSchemaNames = true;
});

// The clock, once, for every module: no DateTime.UtcNow in testable code.
builder.Services.AddSingleton(TimeProvider.System);

// One bearer scheme (the default) and the policies; Identity's cookies are its own schemes.
builder.Services.AddBearerAuthentication(builder.Configuration);

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddSyncModule(builder.Configuration);

// Behind an ingress the public host and scheme arrive in these headers. Pod addresses are dynamic,
// so no fixed proxy list; the backend is reachable only inside the cluster.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// The framework's own meters; no instrumentation package needed. Modules add theirs with AddMeter.
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel")
    .AddPrometheusExporter());

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: [ReadyTag]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(config => config.Endpoints.RoutePrefix = "api");
app.UseSwaggerGen();

app.MapPrometheusScrapingEndpoint();

// Which build is answering. Anonymous: the version is already in the image tag. A minimal-API route,
// not a slice: it is the one endpoint whose job is to answer when composition has gone wrong.
app.MapGet("/api/version", () => TypedResults.Ok(new VersionResponse(ProductVersion.Value, ProductVersion.Commit)))
    .WithName("GetVersion")
    .WithTags("Host")
    .AllowAnonymous();

// Liveness: the process answers. No checks, so a database outage never restarts the pod.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous()
    .ExcludeFromDescription();

// Readiness, and /health for a person: the checks this instance needs to serve.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
    .AllowAnonymous()
    .ExcludeFromDescription();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();
