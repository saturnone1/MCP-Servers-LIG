using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<MssqlTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-mssql" }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class MssqlTools
{
    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's SQL Server configuration status.")]
    public static object Config() => new
    {
        hasConnectionString = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"))
    };

    [McpServerTool(ReadOnly = true)]
    [Description("List databases visible to the configured SQL Server login.")]
    public static Task<object[]> ListDatabases(string? connectionString = null) =>
        Query(connectionString, "select name, database_id, create_date from sys.databases order by name", 200);

    [McpServerTool(ReadOnly = true)]
    [Description("List schemas in the current database.")]
    public static Task<object[]> ListSchemas(string? connectionString = null) =>
        Query(connectionString, "select name, schema_id from sys.schemas order by name", 200);

    [McpServerTool(ReadOnly = true)]
    [Description("List tables in the current database.")]
    public static Task<object[]> ListTables(string? connectionString = null, string? schema = null)
    {
        const string sql = """
            select s.name as schema_name, t.name as table_name, t.create_date, t.modify_date
            from sys.tables t
            join sys.schemas s on s.schema_id = t.schema_id
            where (@schema is null or s.name = @schema)
            order by s.name, t.name
            """;
        return Query(connectionString, sql, 1000, new SqlParameter("@schema", (object?)schema ?? DBNull.Value));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Describe columns for a table in the current database.")]
    public static Task<object[]> DescribeTable(string tableName, string schema = "dbo", string? connectionString = null)
    {
        const string sql = """
            select c.column_id, c.name as column_name, ty.name as data_type, c.max_length, c.precision, c.scale, c.is_nullable
            from sys.columns c
            join sys.tables t on t.object_id = c.object_id
            join sys.schemas s on s.schema_id = t.schema_id
            join sys.types ty on ty.user_type_id = c.user_type_id
            where s.name = @schema and t.name = @table
            order by c.column_id
            """;
        return Query(connectionString, sql, 1000, new SqlParameter("@schema", schema), new SqlParameter("@table", tableName));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute a read-only SELECT/WITH query and return rows.")]
    public static Task<object[]> ExecuteReadQuery(string sql, string? connectionString = null, int maxRows = 200)
    {
        Guard.RequireReadQuery(sql);
        return Query(connectionString, sql, Math.Clamp(maxRows, 1, 5000));
    }

    [McpServerTool]
    [Description("Execute a non-query SQL command.")]
    public static async Task<object> ExecuteNonQuery(string sql, string? connectionString = null, int timeoutSeconds = 30)
    {
        Guard.RequireSqlWrites();
        await using var connection = new SqlConnection(Guard.ConnectionString(connectionString));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = Math.Clamp(timeoutSeconds, 1, 300) };
        var affected = await command.ExecuteNonQueryAsync();
        return new { rowsAffected = affected };
    }

    private static async Task<object[]> Query(string? connectionString, string sql, int maxRows, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(Guard.ConnectionString(connectionString));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
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
            ? Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING")
            : provided;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Provide connectionString or set MSSQL_CONNECTION_STRING.");
        return value;
    }

    public static void RequireReadQuery(string sql)
    {
        var trimmed = sql.TrimStart();
        if (!trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("with", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Read query must start with SELECT or WITH. Use ExecuteNonQuery for writes.");
    }

    public static void RequireSqlWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_SQL_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("SQL write tools are disabled because MCP_ENABLE_SQL_WRITES=false.");
    }
}
