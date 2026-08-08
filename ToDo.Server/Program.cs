using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ToDo.Server;
using ToDo.Sync;

var builder = WebApplication.CreateBuilder(args);

var connString = ResolveConfig(builder.Configuration, "ConnectionStrings:Default")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "";
builder.Services.AddDbContext<SyncDbContext>(opt => opt.UseSqlite(connString));
builder.Services.AddScoped<SyncStore>();

// camelCase, case-insensitive wire envelope (entity payloads stay opaque strings).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

// Ensure the data file's directory exists, then create tables and enable WAL.
// WAL mode is stored in the database file itself, so setting it once at startup is enough.
using (var scope = app.Services.CreateScope())
{
    var dataSource = new SqliteConnectionStringBuilder(connString).DataSource;
    var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    db.Database.EnsureCreated();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/sync", async (HttpRequest request, SyncStore store, IConfiguration config) =>
{
    var expected = ResolveConfig(config, "SyncKey") ?? "";
    var provided = request.Headers["X-Sync-Key"].ToString();
    if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(provided, expected))
        return Results.Unauthorized();

    var body = await request.ReadFromJsonAsync<SyncRequest>();
    if (body == null) return Results.BadRequest();

    var result = store.Merge(body.Changes ?? new List<SyncChange>(), body.Since);
    return Results.Ok(new SyncResponse { ServerSeq = result.ServerSeq, Changes = result.Changes });
});

app.Run();

// Config keys in appsettings.json are PascalCase ("SyncKey"), while the deployed
// environment conventionally exports them in SCREAMING_SNAKE ("SYNC_KEY"). Hosts
// can match keys case-sensitively, so resolve by a case-insensitive scan and
// treat empty values as unset (appsettings ships "SyncKey": "").
static string? ResolveConfig(IConfiguration config, string name)
{
    foreach (var kv in config.AsEnumerable())
        if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kv.Value))
            return kv.Value;
    return null;
}

// Fixed-time comparison so the shared sync key isn't leaked via timing.
static bool FixedTimeEquals(string? a, string? b)
{
    var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a ?? ""));
    var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b ?? ""));
    return CryptographicOperations.FixedTimeEquals(ha, hb);
}

// WebApplicationFactory<Program> target for integration tests.
public partial class Program { }
