using System.Runtime.CompilerServices;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Aurora DSQL runs in UTC, and some spec tests mix client-side <c>DateTime.Today</c> with the
/// server's <c>now()</c>. On a host whose local date differs from UTC (e.g. UTC+3 just after
/// midnight), those tests fail by one day. Pin the process to UTC so the suites are deterministic.
/// </summary>
internal static class UtcTimeZone
{
    [ModuleInitializer]
    internal static void UseUtc()
    {
        Environment.SetEnvironmentVariable("TZ", "UTC");
        TimeZoneInfo.ClearCachedData();
    }
}
