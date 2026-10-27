using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AuthServer.Host.Startup;

/// <summary>Readiness: PostgreSQL answers within two seconds. Never throws.</summary>
internal sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("AuthServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("ConnectionStrings:AuthServer is not configured.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Budget);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(deadline.Token);

            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(deadline.Token);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL did not answer within {Budget.TotalSeconds:0} s.");
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or ArgumentException)
        {
            // The message names the cause, never the connection string.
            return HealthCheckResult.Unhealthy(exception.GetType().Name + ": " + exception.Message);
        }
    }
}
