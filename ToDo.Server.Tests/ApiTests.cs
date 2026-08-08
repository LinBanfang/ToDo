using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using ToDo.Sync;
using Xunit;

namespace ToDo.Server.Tests;

/// <summary>
/// End-to-end HTTP tests through the real pipeline (auth via X-Sync-Key, JSON wire
/// format, incremental cursor) using WebApplicationFactory over a temp SQLite file.
/// </summary>
public sealed class ApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "todo-sync-api-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
                // SCREAMING_SNAKE spelling to exercise the env-var config path that
                // production uses (SYNC_KEY=... via systemd EnvironmentFile).
                b.UseSetting("SYNC_KEY", "test-key");
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();   // pooled connections otherwise keep the file locked
        File.Delete(_dbPath);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private async Task<HttpResponseMessage> PostSync(long since, string key, List<SyncChange>? changes = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/sync")
        {
            Content = new StringContent(JsonSerializer.Serialize(new SyncRequest { DeviceId = "d", Since = since, Changes = changes }, WebJson), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Sync-Key", key);
        return await _factory.CreateClient().SendAsync(req);
    }

    [Fact]
    public async Task Healthz_ReturnsOk() =>
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/healthz")).StatusCode);

    [Fact]
    public async Task Sync_WithoutKey_Returns401()
    {
        var res = await _factory.CreateClient().PostAsync("/api/sync", JsonContent.Create(new SyncRequest { DeviceId = "d" }));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Sync_WithWrongKey_Returns401()
    {
        var res = await PostSync(0, "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Sync_WithCorrectKey_AcceptsAndReturnsChanges()
    {
        var res = await PostSync(0, "test-key", new()
        {
            new SyncChange { Type = "task", Id = "t1", ModifiedAt = 100, Payload = "{\"title\":\"x\"}" },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<SyncResponse>(WebJson);
        Assert.Equal(1, body!.ServerSeq);
        Assert.Equal("t1", Assert.Single(body.Changes!).Id);
    }

    [Fact]
    public async Task Sync_TwoDevices_IncrementalPull()
    {
        async Task<SyncResponse> Push(string device, long since, List<SyncChange>? changes = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/sync")
            {
                Content = new StringContent(JsonSerializer.Serialize(new SyncRequest { DeviceId = device, Since = since, Changes = changes }, WebJson), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Sync-Key", "test-key");
            var r = await _factory.CreateClient().SendAsync(req);
            return (await r.Content.ReadFromJsonAsync<SyncResponse>(WebJson))!;
        }

        // device A pushes task t1 (seq 1)
        var a1 = await Push("a", 0, new() { new() { Type = "task", Id = "t1", ModifiedAt = 100, Payload = "a" } });
        Assert.Equal(1, a1.ServerSeq);

        // device B, fresh (since 0), pushes its own t2 and sees both tasks
        var b1 = await Push("b", 0, new() { new() { Type = "task", Id = "t2", ModifiedAt = 100, Payload = "b" } });
        Assert.Equal(2, b1.ServerSeq);
        Assert.Equal(2, b1.Changes!.Count);

        // device A pulls incrementally — sees only t2
        var a2 = await Push("a", a1.ServerSeq);
        Assert.Equal(2, a2.ServerSeq);
        Assert.Equal("t2", Assert.Single(a2.Changes!).Id);
    }
}
