using ModelContextProtocol.Server;
using Npgsql;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<PostgresqlTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-postgresql" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class PostgresqlTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's PostgreSQL configuration status.")]
    public static object Config() => new
    {
        hasConnectionString = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING"))
    };

    [McpServerTool(ReadOnly = true)]
    [Description("List PostgreSQL databases visible to the configured login.")]
    public static Task<object[]> ListDatabases(string? connectionString = null) =>
        Query(connectionString, "select datname as database_name from pg_database where datistemplate = false order by datname", 200);

    [McpServerTool(ReadOnly = true)]
    [Description("List schemas in the current PostgreSQL database.")]
    public static Task<object[]> ListSchemas(string? connectionString = null) =>
        Query(connectionString, "select schema_name from information_schema.schemata order by schema_name", 500);

    [McpServerTool(ReadOnly = true)]
    [Description("List tables in the current PostgreSQL database.")]
    public static Task<object[]> ListTables(string? connectionString = null, string? schema = null)
    {
        const string sql = """
            select table_schema, table_name, table_type
            from information_schema.tables
            where (@schema is null or table_schema = @schema)
              and table_schema not in ('pg_catalog', 'information_schema')
            order by table_schema, table_name
            """;
        return Query(connectionString, sql, 1000, new NpgsqlParameter("schema", (object?)schema ?? DBNull.Value));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Describe columns for a PostgreSQL table.")]
    public static Task<object[]> DescribeTable(string tableName, string schema = "public", string? connectionString = null)
    {
        const string sql = """
            select ordinal_position, column_name, data_type, udt_name, is_nullable, column_default
            from information_schema.columns
            where table_schema = @schema and table_name = @table
            order by ordinal_position
            """;
        return Query(connectionString, sql, 1000, new NpgsqlParameter("schema", schema), new NpgsqlParameter("table", tableName));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute a read-only PostgreSQL query and return rows.")]
    public static Task<object[]> ExecuteReadQuery(string sql, string? connectionString = null, int maxRows = 200)
    {
        Guard.RequireReadQuery(sql);
        return Query(connectionString, sql, Math.Clamp(maxRows, 1, 5000));
    }

    [McpServerTool]
    [Description("Execute a PostgreSQL non-query command.")]
    public static async Task<object> ExecuteNonQuery(string sql, string? connectionString = null, int timeoutSeconds = 30)
    {
        Guard.RequireSqlWrites();
        await using var connection = new NpgsqlConnection(Guard.ConnectionString(connectionString));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = Math.Clamp(timeoutSeconds, 1, 300) };
        var affected = await command.ExecuteNonQueryAsync();
        return new { rowsAffected = affected };
    }

    private static async Task<object[]> Query(string? connectionString, string sql, int maxRows, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(Guard.ConnectionString(connectionString));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (rows.Count < maxRows && await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows.Cast<object>().ToArray();
    }
}

internal static class Guard
{
    public static string ConnectionString(string? provided)
    {
        var value = string.IsNullOrWhiteSpace(provided)
            ? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            : provided;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Provide connectionString or set POSTGRES_CONNECTION_STRING.");
        return value;
    }

    public static void RequireReadQuery(string sql)
    {
        var trimmed = sql.TrimStart();
        var allowed = new[] { "select", "with", "show", "explain" };
        if (!allowed.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Read query must start with SELECT, WITH, SHOW, or EXPLAIN. Use ExecuteNonQuery for writes.");
    }

    public static void RequireSqlWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_POSTGRES_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("PostgreSQL write tools are disabled because MCP_ENABLE_POSTGRES_WRITES=false.");
    }
}

