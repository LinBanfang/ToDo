using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the sync engine end-to-end without a network: a stub HttpMessageHandler
/// answers with canned SyncResponses. Covers bootstrap gating, outbox flush, cursor
/// persistence, LWW application (My Day preserved) and the Offline/Online status map.
/// </summary>
[Collection("settings-shared")]   // serialized with SettingsServiceTests — SettingsService is a shared static
public sealed class SyncServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public SyncServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
        SettingsService.UseDirectory(_dir);
        var s = SettingsService.Current;
        s.SyncEnabled = true;
        s.SyncServerUrl = "http://localhost:5080";
        s.SyncKey = "test-key";
        s.DeviceId = "test-device";
        s.LastSyncServerSeq = 0;
        s.LastSyncTime = 0;
        SettingsService.Save();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Captures the outgoing SyncRequest so tests can inspect what would be pushed.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int Calls;
        public SyncRequest? LastRequest;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            LastRequest = JsonSerializer.Deserialize<SyncRequest>(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), WebJson);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, SyncResponse body)
    {
        // Tests that don't care about the protocol version get a matching one by default.
        if (body.ProtocolVersion == 0) body.ProtocolVersion = SyncProtocol.Version;
        return new(code)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, WebJson), System.Text.Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task Disabled_SkipsNetwork_AndSetsDisabled()
    {
        SettingsService.Current.SyncEnabled = false;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse()));
        var svc = new SyncService(_db, null, handler);

        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.Disabled, svc.Status);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, SettingsService.Current.LastSyncServerSeq);
    }

    [Fact]
    public async Task MissingUrlOrKey_SetsNotConfigured_NoRequest()
    {
        SettingsService.Current.SyncServerUrl = "";
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse()));
        var svc = new SyncService(_db, null, handler);

        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.NotConfigured, svc.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task FirstSync_BootstrapsOutbox_Pushes_AppliesRemote_AndPersistsCursor()
    {
        // Pre-existing data: outbox is cleared so only BootstrapSync can seed it.
        _db.Tasks.Insert(new TaskItem { Id = "t-local", Title = "mine", ListId = "list-tasks" });
        _db.Tracker.Clear();

        var remote = new SyncResponse
        {
            ServerSeq = 5,
            Changes = new() { SyncEntitySerializer.ToChange(new TaskItem { Id = "t-remote", Title = "remote", ListId = "list-tasks" })! },
        };
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, remote));
        var svc = new SyncService(_db, null, handler);

        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.Online, svc.Status);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("test-device", handler.LastRequest!.DeviceId);
        Assert.Equal(0, handler.LastRequest.Since);
        Assert.Contains(handler.LastRequest.Changes!, c => c.Id == "t-local");   // bootstrap pushed it

        Assert.Empty(_db.Tracker.AllPending());                                  // outbox drained
        Assert.Equal(5, SettingsService.Current.LastSyncServerSeq);              // cursor persisted
        Assert.True(SettingsService.Current.LastSyncTime > 0);
        Assert.NotNull(_db.Tasks.FindById("t-remote"));                          // remote change applied
    }

    [Fact]
    public async Task SubsequentSync_SendsOnlyNewDelta()
    {
        SettingsService.Current.LastSyncServerSeq = 9;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse { ServerSeq = 9 }));
        var svc = new SyncService(_db, null, handler);
        await svc.SyncOnceAsync();   // baseline: nothing pending

        Assert.Equal(9, handler.LastRequest!.Since);
        Assert.Empty(handler.LastRequest.Changes!);

        _db.Tasks.Insert(new TaskItem { Id = "t-new", Title = "n", ListId = "list-tasks" });
        await svc.SyncOnceAsync();

        var change = Assert.Single(handler.LastRequest!.Changes!);
        Assert.Equal("t-new", change.Id);
    }

    [Fact]
    public async Task ServerReset_DetectsSeqRollback_ReBootstrapsAndReUploads()
    {
        // A fully-synced device: cursor is ahead of an empty outbox.
        SettingsService.Current.LastSyncServerSeq = 5;
        var local = new TaskItem { Id = "t-keep", Title = "mine", ListId = "list-tasks" };
        _db.Tasks.Insert(local);
        _db.Tracker.Clear();

        SyncRequest? first = null;
        var handler = new StubHandler(req =>
        {
            var body = JsonSerializer.Deserialize<SyncRequest>(
                req.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), WebJson);
            if (first == null)
            {
                first = body; // a wiped server answers with seq 0 and no data
                return JsonResponse(HttpStatusCode.OK, new SyncResponse { ServerSeq = 0 });
            }
            // The restored server echoes the re-uploaded entities.
            var changes = body!.Changes ?? new List<SyncChange>();
            return JsonResponse(HttpStatusCode.OK, new SyncResponse { ServerSeq = changes.Count, Changes = changes });
        });

        var svc = new SyncService(_db, null, handler);
        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.Online, svc.Status);
        Assert.Equal(2, handler.Calls);                          // reset caused a second round-trip
        Assert.Equal(5, first!.Since);                           // first attempt used the stale cursor
        Assert.Equal(0, handler.LastRequest!.Since);             // re-upload pulled from seq 0
        Assert.Contains(handler.LastRequest.Changes!, c => c.Id == "t-keep"); // local data re-uploaded

        Assert.Empty(_db.Tracker.AllPending());                  // outbox drained again
        Assert.Equal(1, SettingsService.Current.LastSyncServerSeq); // cursor = seq after re-upload
    }

    [Fact]
    public async Task KeyRejected_SetsOffline_WithAuthStatusText()
    {
        SettingsService.Current.LastSyncServerSeq = 3;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var svc = new SyncService(_db, null, handler);

        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.Offline, svc.Status);
        Assert.Equal(Loc.SyncStatusAuthFailed, svc.StatusText);
    }

    [Fact]
    public async Task ServerError_SetsOffline()
    {
        SettingsService.Current.LastSyncServerSeq = 3;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var svc = new SyncService(_db, null, handler);

        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.Offline, svc.Status);
        Assert.Equal(Loc.SyncStatusOffline, svc.StatusText);
    }

    [Fact]
    public async Task MyDayFields_SurviveRemoteApply()
    {
        _db.Tracker.Clear();
        var local = new TaskItem { Id = "t1", Title = "mine", ListId = "list-tasks", IsMyDay = true, MyDayOrder = 7 };
        _db.Tasks.Insert(local);
        SettingsService.Current.LastSyncServerSeq = 2;

        // The server returns a newer snapshot (payload never carries My Day).
        var remote = SyncEntitySerializer.ToChange(new TaskItem { Id = "t1", Title = "mine", ListId = "list-tasks" })!;
        remote.ModifiedAt = local.ModifiedAt + 5000;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse { ServerSeq = 3, Changes = new() { remote } }));

        var svc = new SyncService(_db, null, handler);
        await svc.SyncOnceAsync();

        var after = _db.Tasks.FindById("t1");
        Assert.NotNull(after);
        Assert.True(after.IsMyDay);      // per-device state survives the remote overwrite
        Assert.Equal(7, after.MyDayOrder);
    }

    [Fact]
    public async Task RemoteTombstone_RemovesLocalEntity()
    {
        _db.Tracker.Clear();
        var doomed = new TaskItem { Id = "t-rm", Title = "bye", ListId = "list-tasks" };
        _db.Tasks.Insert(doomed);
        SettingsService.Current.LastSyncServerSeq = 2;

        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse
        {
            ServerSeq = 3,
            Changes = new() { new SyncChange { Type = SyncEntityTypes.Task, Id = "t-rm", ModifiedAt = doomed.ModifiedAt + 5000, Deleted = true } },
        }));

        var svc = new SyncService(_db, null, handler);
        await svc.SyncOnceAsync();

        Assert.Null(_db.Tasks.FindById("t-rm"));
        Assert.Empty(_db.Tracker.AllPending());   // tombstone push cleared after apply
    }

    [Fact]
    public async Task VersionMismatch_SetsVersionMismatch_DoesNotApplyOrMoveCursor()
    {
        _db.Tracker.Clear();
        _db.Tasks.Insert(new TaskItem { Id = "t-keep", Title = "keep", ListId = "list-tasks" });
        SettingsService.Current.LastSyncServerSeq = 2;

        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse
        {
            ServerSeq = 99,
            ProtocolVersion = SyncProtocol.Version + 1,   // an incompatible server
            Changes = new() { new SyncChange { Type = SyncEntityTypes.Task, Id = "t-remote", ModifiedAt = 1, Deleted = false, Payload = "{\"Id\":\"t-remote\",\"Title\":\"x\",\"ListId\":\"list-tasks\"}" } },
        }));

        var svc = new SyncService(_db, null, handler);
        await svc.SyncOnceAsync();

        Assert.Equal(SyncStatus.VersionMismatch, svc.Status);
        Assert.Equal(Loc.SyncStatusVersionMismatch, svc.StatusText);
        Assert.Equal(2, SettingsService.Current.LastSyncServerSeq);          // cursor untouched
        Assert.Equal(0, SettingsService.Current.LastSyncTime);               // never "synced"
        Assert.Null(_db.Tasks.FindById("t-remote"));                         // reply refused
        Assert.Single(_db.Tracker.AllPending());                             // outbox still pending
    }

    [Fact]
    public async Task AuthFailure_LogsErrorLineToDiagnosticLog()
    {
        SettingsService.Current.LastSyncServerSeq = 3;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var svc = new SyncService(_db, null, handler);

        using var sink = new LogSink();
        await svc.SyncOnceAsync();

        Assert.Contains(sink.Lines, l => l.Level == "ERROR" && l.Module == "sync" && l.Message.Contains("401"));
    }

    [Fact]
    public async Task SuccessfulRoundTrip_LogsStartAndSummary()
    {
        SettingsService.Current.LastSyncServerSeq = 0;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new SyncResponse { ServerSeq = 0 }));
        var svc = new SyncService(_db, null, handler);

        using var sink = new LogSink();
        await svc.SyncOnceAsync();

        // The full flow is wired to DiagnosticLog: outbound snapshot + inbound summary.
        Assert.Contains(sink.Lines, l => l.Level == "INFO" && l.Module == "sync"
            && l.Message.StartsWith("round-trip start:"));
        Assert.Contains(sink.Lines, l => l.Level == "INFO" && l.Module == "sync"
            && l.Message.StartsWith("round-trip ok:"));
    }

    /// <summary>Captures every DiagnosticLog line written while active (test seam).</summary>
    private sealed class LogSink : IDisposable
    {
        public List<(string Level, string Module, string Message)> Lines { get; } = new();

        private readonly Action<string, string, string> _handler;

        public LogSink()
        {
            _handler = (level, module, message) => Lines.Add((level, module, message));
            DiagnosticLog.Written += _handler;
        }

        public void Dispose() => DiagnosticLog.Written -= _handler;
    }
}
