using AuthServer.Host.Startup;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;

// The tag that separates readiness from liveness: liveness runs no checks at all.
const string ReadyTag = "ready";

var builder = WebApplication.CreateBuilder(args);

// Host-wide composition lives here and only here (architecture.md §4.5): a module contributes its
// endpoints, its DbContext and its own registrations through AddXxxModule(), never a global default.
//
// FastEndpoints, Swagger, JwtBearer and TickerQ are wired with the first module: FastEndpoints
// refuses to start with no endpoint to discover.

// The clock, once, for every module: no DateTime.UtcNow in testable code.
builder.Services.AddSingleton(TimeProvider.System);

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

// Readiness gains the PostgreSQL check (tagged ReadyTag) with the first module's DbContext.
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseForwardedHeaders();

app.MapPrometheusScrapingEndpoint();

// Which build is answering. Anonymous: the version is already in the image tag.
app.MapGet("/api/version", () => TypedResults.Ok(new VersionResponse(ProductVersion.Value)))
    .WithName("GetVersion")
    .WithTags("Host")
    .AllowAnonymous();

// Liveness: the process answers. No checks, so a database outage never restarts the pod.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// Readiness, and /health for a person: the checks this instance needs to serve.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
    .AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
    .AllowAnonymous();

app.Run();
