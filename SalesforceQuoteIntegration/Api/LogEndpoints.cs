using Microsoft.EntityFrameworkCore;
using SalesforceQuoteIntegration.Data;
using SalesforceQuoteIntegration.Models;
using SalesforceQuoteIntegration.Models.Api;

namespace SalesforceQuoteIntegration.Api;

public static class LogEndpoints
{
    public static void MapLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/logs")
            .WithTags("Logs");

        // GET /api/logs
        group.MapGet("/", async (
            IDbContextFactory<AppDbContext> dbFactory,
            string?   level    = null,
            DateTime? from     = null,
            DateTime? to       = null,
            string?   search   = null,
            int       page     = 1,
            int       pageSize = 50) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var query = db.ApplicationLogs.AsQueryable();

            if (!string.IsNullOrEmpty(level))
                query = query.Where(l => l.Level == level);

            if (from.HasValue)
                query = query.Where(l => l.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(l => l.Timestamp <= to.Value);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(l => l.Message != null && l.Message.Contains(search));

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new PagedResult<ApplicationLogs>
            {
                Items      = items,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            });
        })
        .WithName("GetLogs")
        .WithSummary("Get paginated application logs with optional filters");

        // GET /api/logs/errors
        group.MapGet("/errors", async (
            IDbContextFactory<AppDbContext> dbFactory,
            DateTime? from     = null,
            int       page     = 1,
            int       pageSize = 50) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.ApplicationLogs
                .Where(l => l.Level == "Error" || l.Level == "Fatal");

            if (from.HasValue)
                query = query.Where(l => l.Timestamp >= from.Value);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new PagedResult<ApplicationLogs>
            {
                Items      = items,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            });
        })
        .WithName("GetErrorLogs")
        .WithSummary("Get only Error and Fatal level logs");

        // GET /api/logs/stats
        group.MapGet("/stats", async (
            IDbContextFactory<AppDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var byLevel  = await db.ApplicationLogs
                .GroupBy(l => l.Level)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToListAsync();

            var last1hr   = await db.ApplicationLogs.CountAsync(l => l.Timestamp >= DateTime.UtcNow.AddHours(-1));
            var last24hrs = await db.ApplicationLogs.CountAsync(l => l.Timestamp >= DateTime.UtcNow.AddHours(-24));

            return Results.Ok(new
            {
                ByLevel     = byLevel,
                Last1Hour   = last1hr,
                Last24Hours = last24hrs
            });
        })
        .WithName("GetLogStats")
        .WithSummary("Get log statistics broken down by level");
    }
}
