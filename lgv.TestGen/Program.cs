using System.Text;

// ──────────────────────────────────────────────────────────────
// lgv.TestGen — generates realistic log files for testing lgv
//
// Creates two directories next to the exe and streams log lines
// at ~5 lines/second. Every 30 seconds the current file is
// closed and a new one opened (simulating log rotation).
// ──────────────────────────────────────────────────────────────

const string Dir1 = "WebService";
const string Dir2 = "BackgroundWorker";
const int LinesPerSecond = 5;
const int RotationIntervalSeconds = 30;

var baseDir = AppContext.BaseDirectory;
Directory.CreateDirectory(Path.Combine(baseDir, Dir1));
Directory.CreateDirectory(Path.Combine(baseDir, Dir2));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("lgv.TestGen started.");
Console.WriteLine($"  Log dir 1: {Path.Combine(baseDir, Dir1)}");
Console.WriteLine($"  Log dir 2: {Path.Combine(baseDir, Dir2)}");
Console.WriteLine($"  Rate: {LinesPerSecond} lines/sec, rotation every {RotationIntervalSeconds}s");
Console.WriteLine("  Press Ctrl+C to stop.");
Console.WriteLine();

var t1 = RunLogger(Dir1, "WebService",       WebServiceMessages(), cts.Token);
var t2 = RunLogger(Dir2, "BackgroundWorker", WorkerMessages(),     cts.Token);

await Task.WhenAll(t1, t2);
Console.WriteLine("Stopped.");

// ── Log writer loop ──────────────────────────────────────────

static async Task RunLogger(
    string dirName,
    string serviceName,
    IEnumerable<(string level, Func<string> message)> messages,
    CancellationToken ct)
{
    var baseDir = AppContext.BaseDirectory;
    var dir = Path.Combine(baseDir, dirName);
    var delay = TimeSpan.FromMilliseconds(1000.0 / LinesPerSecond);
    var rotationInterval = TimeSpan.FromSeconds(RotationIntervalSeconds);
    var messageList = messages.ToList();
    var rng = new Random();
    int msgIndex = 0;

    while (!ct.IsCancellationRequested)
    {
        var filePath = Path.Combine(dir, $"{serviceName}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Console.WriteLine($"[{serviceName}] Opening {Path.GetFileName(filePath)}");

        await using var writer = new StreamWriter(
            new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            Encoding.UTF8,
            bufferSize: 1);

        writer.AutoFlush = true;
        var rotateAt = DateTime.UtcNow + rotationInterval;

        while (!ct.IsCancellationRequested && DateTime.UtcNow < rotateAt)
        {
            var (level, msgFn) = messageList[msgIndex % messageList.Count];
            msgIndex++;

            // Inject occasional random level overrides for variety
            var effectiveLevel = rng.Next(100) switch
            {
                < 2 => "ERR",
                < 5 => "WRN",
                _   => level
            };

            var line = FormatLine(effectiveLevel, serviceName, msgFn());
            await writer.WriteLineAsync(line);

            // Occasionally append a stack trace after errors
            if (effectiveLevel == "ERR" && rng.Next(3) == 0)
            {
                await writer.WriteLineAsync($"   at {serviceName}.Core.Handler.ProcessAsync(Request req) in Handler.cs:line {rng.Next(10, 300)}");
                await writer.WriteLineAsync($"   at {serviceName}.Infrastructure.Pipeline.InvokeAsync(Context ctx) in Pipeline.cs:line {rng.Next(10, 150)}");
            }

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }

        Console.WriteLine($"[{serviceName}] Rotating — closing {Path.GetFileName(filePath)}");
    }
}

static string FormatLine(string level, string service, string message)
{
    var ts  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    var tag = level switch
    {
        "ERR" => "[ERR]",
        "WRN" => "[WRN]",
        "DBG" => "[DBG]",
        "TRC" => "[TRC]",
        _     => "[INF]",
    };
    return $"{ts} {tag} [{service}] {message}";
}

// ── Message pools ────────────────────────────────────────────

static IEnumerable<(string, Func<string>)> WebServiceMessages()
{
    var rng     = new Random();
    var methods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" };
    var paths   = new[]
    {
        "/api/users", "/api/orders", "/api/products", "/api/auth/token",
        "/api/reports", "/api/health", "/api/settings", "/api/events",
    };
    var statusOk  = new[] { 200, 201, 204 };
    var status4xx = new[] { 400, 401, 403, 404, 422 };
    var status5xx = new[] { 500, 502, 503 };

    return
    [
        ("INF", () =>
        {
            var method = methods[rng.Next(methods.Length)];
            var path   = paths[rng.Next(paths.Length)];
            var status = statusOk[rng.Next(statusOk.Length)];
            var ms     = rng.Next(5, 280);
            var reqId  = Guid.NewGuid().ToString()[..8];
            return $"{method} {path} → {status} ({ms}ms) req={reqId}";
        }),
        ("INF", () =>
        {
            var path  = paths[rng.Next(paths.Length)];
            var count = rng.Next(1, 200);
            return $"Cache hit for {path}, returned {count} items";
        }),
        ("INF", () => $"Active sessions: {rng.Next(10, 5000)}"),
        ("INF", () => $"JWT validated for user={Guid.NewGuid().ToString()[..8]}, expires in {rng.Next(5, 60)}min"),
        ("INF", () => $"Rate limiter: {rng.Next(0, 95)}% of quota used for client {rng.Next(1000, 9999)}"),
        ("INF", () => $"DB query completed in {rng.Next(1, 120)}ms, rows={rng.Next(0, 10000)}"),
        ("INF", () => $"Response serialised: {rng.Next(200, 80000)} bytes"),
        ("WRN", () =>
        {
            var path   = paths[rng.Next(paths.Length)];
            var status = status4xx[rng.Next(status4xx.Length)];
            var ms     = rng.Next(10, 500);
            return $"Client error on {path} → {status} ({ms}ms)";
        }),
        ("WRN", () => $"Slow query detected: {rng.Next(500, 3000)}ms — consider adding index on orders.created_at"),
        ("WRN", () => $"Retry attempt {rng.Next(1, 3)}/3 for downstream service (upstream timeout)"),
        ("WRN", () => $"Memory pressure: heap {rng.Next(70, 95)}% of limit"),
        ("ERR", () =>
        {
            var path   = paths[rng.Next(paths.Length)];
            var status = status5xx[rng.Next(status5xx.Length)];
            return $"Unhandled exception on {path} → {status}: System.TimeoutException: The operation timed out after 30000ms";
        }),
        ("ERR", () => "NpgsqlException: Connection pool exhausted (max=100) — all connections busy"),
        ("DBG", () => $"Middleware pipeline: {rng.Next(3, 8)} handlers, elapsed {rng.Next(0, 5)}ms"),
        ("DBG", () => $"DI scope created for request {Guid.NewGuid().ToString()[..8]}"),
        ("TRC", () => $"Header X-Request-Id: {Guid.NewGuid()}"),
    ];
}

static IEnumerable<(string, Func<string>)> WorkerMessages()
{
    var rng      = new Random();
    var jobTypes = new[] { "ReportGeneration", "EmailDispatch", "DataSync", "ImageResize", "AuditExport", "CacheWarmup" };
    var queues   = new[] { "high-priority", "default", "bulk", "maintenance" };

    return
    [
        ("INF", () =>
        {
            var job   = jobTypes[rng.Next(jobTypes.Length)];
            var jobId = Guid.NewGuid().ToString()[..12];
            return $"Job started: {job} id={jobId} queue={queues[rng.Next(queues.Length)]}";
        }),
        ("INF", () =>
        {
            var job = jobTypes[rng.Next(jobTypes.Length)];
            var ms  = rng.Next(200, 15000);
            return $"Job completed: {job} in {ms}ms";
        }),
        ("INF", () => $"Queue depth: high={rng.Next(0, 5)} default={rng.Next(0, 30)} bulk={rng.Next(0, 200)}"),
        ("INF", () => $"Processed {rng.Next(1, 500)} records in batch #{rng.Next(1000, 9999)}"),
        ("INF", () => $"Worker heartbeat — uptime {rng.Next(1, 72)}h {rng.Next(0, 59)}m"),
        ("INF", () => $"Scheduled job {jobTypes[rng.Next(jobTypes.Length)]} enqueued, next run in {rng.Next(1, 60)}min"),
        ("INF", () => $"S3 upload complete: reports/{DateTime.Today:yyyy/MM}/export-{rng.Next(10000, 99999)}.csv ({rng.Next(1, 500)}KB)"),
        ("INF", () => $"Email dispatched to {rng.Next(1, 2000)} recipients, bounced={rng.Next(0, 5)}"),
        ("WRN", () =>
        {
            var job = jobTypes[rng.Next(jobTypes.Length)];
            return $"Job {job} exceeded soft time limit ({rng.Next(30, 120)}s), still running";
        }),
        ("WRN", () => $"Dead-letter queue has {rng.Next(1, 20)} messages — manual review needed"),
        ("WRN", () => $"External API rate limit approaching: {rng.Next(80, 99)}% of 1000 req/min"),
        ("WRN", () => $"Disk usage at {rng.Next(75, 95)}% — log rotation may be required soon"),
        ("ERR", () =>
        {
            var job = jobTypes[rng.Next(jobTypes.Length)];
            return $"Job {job} failed after {rng.Next(1, 3)} retries: System.InvalidOperationException: Downstream service returned 503";
        }),
        ("ERR", () => "SMTP relay rejected message: 550 5.1.1 The email account does not exist"),
        ("DBG", () => $"Dequeued message id={Guid.NewGuid().ToString()[..8]} attempt={rng.Next(1, 3)}"),
        ("DBG", () => $"Lock acquired for resource 'job:{rng.Next(100, 999)}', TTL=30s"),
        ("TRC", () => $"Serialising payload: {rng.Next(100, 50000)} bytes"),
    ];
}
