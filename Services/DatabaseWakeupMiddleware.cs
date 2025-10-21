// Services/DatabaseWakeupMiddleware.cs
using Microsoft.Data.SqlClient;

public class DatabaseWakeupMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DatabaseWakeupMiddleware> _logger;
    private readonly string _connectionString;
    private static DateTime _lastWakeupAttempt = DateTime.MinValue;
    private static readonly TimeSpan WakeupRetryInterval = TimeSpan.FromMinutes(5);

    public DatabaseWakeupMiddleware(RequestDelegate next, ILogger<DatabaseWakeupMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _connectionString = config.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only attempt wakeup on Identity routes (login, register, etc)
        if (IsIdentityRoute(context.Request.Path))
        {
            await EnsureDatabaseIsAwakeAsync();
        }

        await _next(context);
    }

    private async Task EnsureDatabaseIsAwakeAsync()
    {
        // Avoid hammering the database with constant wakeup attempts
        if (DateTime.UtcNow - _lastWakeupAttempt < WakeupRetryInterval && _lastWakeupAttempt != DateTime.MinValue)
        {
            return;
        }

        _lastWakeupAttempt = DateTime.UtcNow;

        try
        {
            var csb = new SqlConnectionStringBuilder(_connectionString)
            {
                ConnectTimeout = 5
            };

            using (var connection = new SqlConnection(csb.ConnectionString))
            {
                await connection.OpenAsync();
                
                // Simple query to verify database is responsive
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = 5;
                    await cmd.ExecuteScalarAsync();
                }

                _logger.LogInformation("Database is awake and responsive.");
            }
        }
        catch (SqlException ex) when (ex.Number == -2 || ex.Number == -1) // Timeout or connection refused
        {
            _logger.LogWarning($"Database is paused or unreachable. Error: {ex.Message}");
            // Don't throw - let the page handle it naturally when Identity attempts to access the DB
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during database wakeup check.");
        }
    }

    private bool IsIdentityRoute(PathString path)
    {
        var pathStr = path.Value?.ToLower() ?? string.Empty;
        return pathStr.Contains("/identity/account/login") ||
               pathStr.Contains("/identity/account/register") ||
               pathStr.Contains("/identity/account/confirmemail") ||
               pathStr.Contains("/identity/account/resendconfirmationemail") ||
               pathStr.Contains("/identity/account/forgotpassword") ||
               pathStr.Contains("/identity/account/resetpassword");
    }
}