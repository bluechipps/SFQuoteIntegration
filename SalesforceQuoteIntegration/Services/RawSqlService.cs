using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SalesforceQuoteIntegration.Data;

namespace SalesforceQuoteIntegration.Services;

/// <summary>
/// Provides raw SQL execution methods against the same database connection
/// used by AppDbContext. Equivalent to the classic SqlClient methods:
/// ExecuteNonQuery, ExecuteScalar, ExecuteReader, and a typed ExecuteQuery.
///
/// Non-stored-procedure methods accept a plain SQL string — embed your values
/// directly using a C# interpolated string:
///
///     var count = await _rawSql.ExecuteScalarAsync&lt;int&gt;(
///         $"SELECT COUNT(*) FROM Orders WHERE QuoteId = '{quoteId}'");
///
/// Stored procedure methods accept SqlParameter arrays for named parameter passing.
/// </summary>
public class RawSqlService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public RawSqlService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    // -------------------------------------------------------------------------
    // Helpers — open a connection from the shared DbContext
    // -------------------------------------------------------------------------

    private async Task<(AppDbContext db, SqlConnection conn)> OpenConnectionAsync()
    {
        var db   = await _dbContextFactory.CreateDbContextAsync();
        var conn = (SqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync();
        return (db, conn);
    }

    // -------------------------------------------------------------------------
    // ExecuteNonQuery — INSERT, UPDATE, DELETE
    // Returns the number of rows affected.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a SQL statement that does not return rows (INSERT, UPDATE, DELETE).
    /// Returns the number of rows affected, same as SqlCommand.ExecuteNonQuery().
    /// Embed values directly in the SQL string using a C# interpolated string.
    /// </summary>
    /// <example>
    /// int rows = await _rawSql.ExecuteNonQueryAsync(
    ///     $"UPDATE Orders SET Status = 'Processed' WHERE QuoteId = '{quoteId}'");
    ///
    /// int rows = await _rawSql.ExecuteNonQueryAsync(
    ///     $"INSERT INTO ProcessingQueue (QuoteId, Status, CreatedAt) VALUES ('{quoteId}', 'Pending', '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}')");
    /// </example>
    public async Task<int> ExecuteNonQueryAsync(string sql)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd = new SqlCommand(sql, conn);

        try
        {
            var rows = await cmd.ExecuteNonQueryAsync();
            Log.Debug($"ExecuteNonQuery affected {rows} row(s) | SQL: {sql}");
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteNonQuery failed | SQL: {sql}");
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // ExecuteNonQuery — Stored Procedure (uses SqlParameter)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure that does not return rows.
    /// Returns the number of rows affected.
    /// </summary>
    /// <example>
    /// int rows = await _rawSql.ExecuteStoredProcedureNonQueryAsync(
    ///     "dbo.usp_ProcessQuote",
    ///     new SqlParameter("@QuoteId",  quoteId),
    ///     new SqlParameter("@Status",   "Processed"));
    /// </example>
    public async Task<int> ExecuteStoredProcedureNonQueryAsync(string procedureName, params SqlParameter[] parameters)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd = new SqlCommand(procedureName, conn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        cmd.Parameters.AddRange(parameters);

        try
        {
            var rows = await cmd.ExecuteNonQueryAsync();
            Log.Debug($"ExecuteStoredProcedureNonQuery '{procedureName}' affected {rows} row(s)");
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteStoredProcedureNonQuery failed | Procedure: {procedureName}");
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // ExecuteScalar — returns a single value (COUNT, MAX, a specific column, etc.)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a SQL query and returns the first column of the first row.
    /// Returns default(T) if the result is null, same as SqlCommand.ExecuteScalar().
    /// Embed values directly in the SQL string using a C# interpolated string.
    /// </summary>
    /// <example>
    /// int count = await _rawSql.ExecuteScalarAsync&lt;int&gt;(
    ///     $"SELECT COUNT(*) FROM Orders WHERE QuoteId = '{quoteId}'");
    ///
    /// string? name = await _rawSql.ExecuteScalarAsync&lt;string?&gt;(
    ///     $"SELECT TOP 1 CustomerName FROM Orders WHERE AccountId = '{accountId}'");
    ///
    /// bool exists = await _rawSql.ExecuteScalarAsync&lt;int&gt;(
    ///     $"SELECT COUNT(*) FROM Orders WHERE QuoteId = '{quoteId}'") > 0;
    ///
    /// decimal total = await _rawSql.ExecuteScalarAsync&lt;decimal&gt;(
    ///     $"SELECT SUM(Amount) FROM OrderLines WHERE OrderId = {orderId}") ?? 0m;
    /// </example>
    public async Task<T?> ExecuteScalarAsync<T>(string sql)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd = new SqlCommand(sql, conn);

        try
        {
            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return default;

            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteScalar failed | SQL: {sql}");
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // ExecuteScalar — Stored Procedure (uses SqlParameter)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure and returns the first column of the first row.
    /// </summary>
    /// <example>
    /// int orderId = await _rawSql.ExecuteStoredProcedureScalarAsync&lt;int&gt;(
    ///     "dbo.usp_GetOrderIdForQuote",
    ///     new SqlParameter("@QuoteId", quoteId));
    /// </example>
    public async Task<T?> ExecuteStoredProcedureScalarAsync<T>(string procedureName, params SqlParameter[] parameters)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd = new SqlCommand(procedureName, conn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        cmd.Parameters.AddRange(parameters);

        try
        {
            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return default;

            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteStoredProcedureScalar failed | Procedure: {procedureName}");
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // ExecuteReader — reads rows into a List<Dictionary<string, object?>>
    // Each dictionary represents one row, keyed by column name.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a SQL query and returns all rows as a list of dictionaries.
    /// Each dictionary maps column name to value, equivalent to iterating a SqlDataReader.
    /// Returns an empty list if no rows are found.
    /// Embed values directly in the SQL string using a C# interpolated string.
    /// </summary>
    /// <example>
    /// var rows = await _rawSql.ExecuteReaderAsync(
    ///     $"SELECT OrderId, CustomerName, Total FROM Orders WHERE QuoteId = '{quoteId}'");
    ///
    /// foreach (var row in rows)
    /// {
    ///     var orderId      = row["OrderId"];
    ///     var customerName = row["CustomerName"]?.ToString();
    ///     var total        = row["Total"] is decimal d ? d : 0m;
    /// }
    /// </example>
    public async Task<List<Dictionary<string, object?>>> ExecuteReaderAsync(string sql)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd     = new SqlCommand(sql, conn);
        var             results = new List<Dictionary<string, object?>>();

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }

            //Log.Debug($"ExecuteReader returned {results.Count} row(s) | SQL: {sql}");
            return results;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteReader failed | SQL: {sql}");
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // ExecuteReader — Stored Procedure (uses SqlParameter)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure and returns all rows as a list of dictionaries.
    /// </summary>
    /// <example>
    /// var rows = await _rawSql.ExecuteStoredProcedureReaderAsync(
    ///     "dbo.usp_GetOrdersForQuote",
    ///     new SqlParameter("@QuoteId",  quoteId),
    ///     new SqlParameter("@MaxRows",  100));
    /// </example>
    public async Task<List<Dictionary<string, object?>>> ExecuteStoredProcedureReaderAsync(
        string procedureName, params SqlParameter[] parameters)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd = new SqlCommand(procedureName, conn)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        cmd.Parameters.AddRange(parameters);

        var results = new List<Dictionary<string, object?>>();

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }

            Log.Debug($"ExecuteStoredProcedureReader '{procedureName}' returned {results.Count} row(s)");
            return results;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteStoredProcedureReader failed | Procedure: {procedureName}");
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // ExecuteQuery<T> — typed version of ExecuteReader
    // Maps rows to a strongly typed class by matching column names to property
    // names (case-insensitive).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a SQL query and maps each row to a strongly typed object T.
    /// Column names are matched to property names case-insensitively.
    /// Properties with no matching column are left at their default value.
    /// Embed values directly in the SQL string using a C# interpolated string.
    /// </summary>
    /// <example>
    /// public class OrderSummary
    /// {
    ///     public int     OrderId      { get; set; }
    ///     public string  CustomerName { get; set; } = string.Empty;
    ///     public decimal Total        { get; set; }
    /// }
    ///
    /// var orders = await _rawSql.ExecuteQueryAsync&lt;OrderSummary&gt;(
    ///     $"SELECT OrderId, CustomerName, Total FROM Orders WHERE QuoteId = '{quoteId}'");
    ///
    /// foreach (var order in orders)
    ///     Log.Information($"Order {order.OrderId} — {order.CustomerName} — {order.Total:C}");
    /// </example>
    public async Task<List<T>> ExecuteQueryAsync<T>(string sql) where T : new()
    {
        var rows    = await ExecuteReaderAsync(sql);
        var results = new List<T>();
        var props   = typeof(T).GetProperties();

        foreach (var row in rows)
        {
            var obj = new T();

            foreach (var prop in props)
            {
                var match = row.Keys.FirstOrDefault(k =>
                    string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));

                if (match == null || row[match] == null)
                    continue;

                try
                {
                    var value = Convert.ChangeType(row[match], prop.PropertyType);
                    prop.SetValue(obj, value);
                }
                catch
                {
                    // Skip columns that can't be converted — leave property at default
                }
            }

            results.Add(obj);
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // ExecuteQuery<T> — Stored Procedure (uses SqlParameter)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure and maps each row to a strongly typed object T.
    /// </summary>
    /// <example>
    /// var orders = await _rawSql.ExecuteStoredProcedureQueryAsync&lt;OrderSummary&gt;(
    ///     "dbo.usp_GetOrdersForQuote",
    ///     new SqlParameter("@QuoteId", quoteId));
    /// </example>
    public async Task<List<T>> ExecuteStoredProcedureQueryAsync<T>(
        string procedureName, params SqlParameter[] parameters)
        where T : new()
    {
        var rows    = await ExecuteStoredProcedureReaderAsync(procedureName, parameters);
        var results = new List<T>();
        var props   = typeof(T).GetProperties();

        foreach (var row in rows)
        {
            var obj = new T();

            foreach (var prop in props)
            {
                var match = row.Keys.FirstOrDefault(k =>
                    string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));

                if (match == null || row[match] == null)
                    continue;

                try
                {
                    var value = Convert.ChangeType(row[match], prop.PropertyType);
                    prop.SetValue(obj, value);
                }
                catch
                {
                    // Skip columns that can't be converted
                }
            }

            results.Add(obj);
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // ExecuteReaderCallbackAsync — row-by-row processing for large result sets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a SQL query and invokes a callback for each row as it is read.
    /// Use this for large result sets to avoid loading all rows into memory at once.
    /// Embed values directly in the SQL string using a C# interpolated string.
    /// </summary>
    /// <example>
    /// await _rawSql.ExecuteReaderCallbackAsync(
    ///     $"SELECT OrderId, Total FROM Orders WHERE Status = 'Pending' AND BranchId = {branchId}",
    ///     async row =>
    ///     {
    ///         var orderId = (int)row["OrderId"]!;
    ///         await ProcessOrderAsync(orderId);
    ///     });
    /// </example>
    public async Task ExecuteReaderCallbackAsync(
        string sql,
        Func<Dictionary<string, object?>, Task> rowCallback)
    {
        var (db, conn) = await OpenConnectionAsync();
        await using var _ = db;
        await using var __ = conn;

        await using var cmd      = new SqlCommand(sql, conn);
        int             rowCount = 0;

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                await rowCallback(row);
                rowCount++;
            }

            Log.Debug($"ExecuteReaderCallback processed {rowCount} row(s) | SQL: {sql}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ExecuteReaderCallback failed | SQL: {sql}");
            throw;
        }
    }
}
