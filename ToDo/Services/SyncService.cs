using System.Net.Http;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ToDo.Sync;

namespace ToDo.Services;

/// <summary>Sync engine states shown in the settings page.</summary>
public enum SyncStatus
{
    Disabled,        // user turned sync off
    NotConfigured,   // enabled but server URL / key empty
    Syncing,         // a round-trip is in flight
    Online,          // last round-trip succeeded
    Offline,         // last round-trip failed (network/server) or key rejected
    VersionMismatch, // server answered but speaks an incompatible protocol version
}

/// <summary>
/// Background sync engine for the WPF app. Triggered on startup, on window focus and
/// every 60s. All LiteDB access is marshalled onto the app's single DB (UI) thread via
/// the dispatcher; only the HTTP round-trip happens off it — keeping the app's
/// single-threaded LiteDB model intact (ADR-002 style Refresh for the UI).
/// </summary>
public partial class SyncService : ObservableObject, IDisposable
{
    private readonly DatabaseService _db;
    private readonly Dispatcher? _dispatcher;
    private readonly HttpClient _http;
    private DispatcherTimer? _timer;
    private int _inFlight;          // re-entrancy guard (0 = idle, 1 = running)
    private bool _bootstrapped;     // outbox seeded from existing data (first sync only)
    private bool _authFailed;       // last failure was a 401, shown distinctly
    private Action? _onSynced;      // app hook: full UI refresh after changes land

    [ObservableProperty]
    private SyncStatus _status = SyncStatus.Disabled;

    /// <summary>Fired whenever the status text changes, so the settings section can refresh.</summary>
    public event Action? StatusChanged;

    public SyncService(DatabaseService db, Dispatcher? dispatcher, HttpMessageHandler? handler = null)
    {
        _db = db;
        _dispatcher = dispatcher;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);

        // A device id is minted once and kept forever (server treats it as the cursor owner).
        if (string.IsNullOrEmpty(SettingsService.Current.DeviceId))
        {
            SettingsService.Current.DeviceId = Guid.NewGuid().ToString("N");
            SettingsService.Save();
        }
    }

    /// <summary>Wires the post-sync UI refresh (LoadAll + rebuild active tasks).</summary>
    public void SetRefreshAction(Action? onSynced) => _onSynced = onSynced;

    /// <summary>Starts the 60s timer and kicks off the first sync. Tests call
    /// <see cref="SyncOnceAsync"/> directly and never reach this.</summary>
    public void Start()
    {
        if (_dispatcher == null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Trigger();
        _timer.Start();
        Trigger();
    }

    /// <summary>Fire-and-forget entry point for the timer, window focus and the "Sync now" button.</summary>
    public void Trigger() => _ = SyncOnceAsync();

    /// <summary>Re-evaluates the status without a network round-trip (e.g. after toggling
    /// sync off in settings).</summary>
    public void RefreshStatus()
    {
        var s = SettingsService.Current;
        if (!s.SyncEnabled) SetStatus(SyncStatus.Disabled);
        else if (string.IsNullOrWhiteSpace(s.SyncServerUrl) || string.IsNullOrWhiteSpace(s.SyncKey)) SetStatus(SyncStatus.NotConfigured);
        else if (Status == SyncStatus.Disabled || Status == SyncStatus.NotConfigured) SetStatus(SyncStatus.Offline);
    }

    /// <summary>
    /// One sync round-trip: pull the outbox, push it, apply the server's reply, clear the
    /// pushed events, persist the cursor. Safe to call repeatedly — a re-entrancy guard
    /// drops overlapping runs.
    /// </summary>
    public async Task SyncOnceAsync()
    {
        var settings = SettingsService.Current;
        if (!settings.SyncEnabled) { SetStatus(SyncStatus.Disabled); return; }
        if (string.IsNullOrWhiteSpace(settings.SyncServerUrl) || string.IsNullOrWhiteSpace(settings.SyncKey))
        { SetStatus(SyncStatus.NotConfigured); return; }

        if (Interlocked.Exchange(ref _inFlight, 1) != 0)
        {
            DiagnosticLog.Info("sync", "overlapping round-trip dropped (previous still in flight)");
            return;
        }
        var startedAt = Environment.TickCount64;
        try
        {
            SetStatus(SyncStatus.Syncing);
            _authFailed = false;

            // First sync ever: seed the outbox with the current state of every syncable
            // entity so pre-existing data uploads instead of staying only on this device.
            if (settings.LastSyncServerSeq == 0 && !_bootstrapped)
            {
                DiagnosticLog.Info("sync", "first sync: seeding outbox from existing data");
                await RunOnDbThread(() => _db.BootstrapSync());
                _bootstrapped = true;
            }

            // Snapshot the outbox and cursor on the DB thread (single-threaded LiteDB).
            var (events, since) = await RunOnDbThread(() =>
            {
                var evs = _db.Tracker.AllPending().ToList();
                return (evs, SettingsService.Current.LastSyncServerSeq);
            });
            DiagnosticLog.Info("sync", $"round-trip start: device={settings.DeviceId} since={since} pending={events.Count}");

            var response = await RoundTripAsync(events, since);

            // Server answered but speaks an incompatible protocol: refuse to apply its
            // reply (it may serialize entities differently) and flag the mismatch instead.
            if (response.ProtocolVersion != SyncProtocol.Version)
            {
                DiagnosticLog.Error("sync", $"protocol mismatch: server={response.ProtocolVersion}, client={SyncProtocol.Version}; reply refused");
                await SetStatusAfterSpin(SyncStatus.VersionMismatch, startedAt);
                return;
            }

            // A server whose sequence went backwards was wiped or swapped out: its seq
            // restarts at 0, our cursor no longer matches, and the (already-run) bootstrap
            // guard would silently stop this device from ever re-uploading. Re-seed the
            // outbox from the full local state and push everything again to restore the
            // mirror, so a server reset no longer means silent data loss.
            if (response.ServerSeq < since)
            {
                DiagnosticLog.Warn("sync",
                    $"server sequence reset detected ({since} → {response.ServerSeq}), re-uploading full local state");
                await RunOnDbThread(() => _db.BootstrapSync());
                events = (await RunOnDbThread(() => _db.Tracker.AllPending().ToList()))!;
                response = await RoundTripAsync(events, 0);
                if (response.ProtocolVersion != SyncProtocol.Version)
                {
                    DiagnosticLog.Error("sync", $"protocol mismatch after reset re-upload: server={response.ProtocolVersion}, client={SyncProtocol.Version}; reply refused");
                    await SetStatusAfterSpin(SyncStatus.VersionMismatch, startedAt);
                    return;
                }
            }

            // Apply the reply (LWW, My Day preserved), clear what was pushed, persist the
            // new cursor — all on the DB thread, then let the app rebuild its UI.
            await RunOnDbThread(() =>
            {
                _db.ApplySync(response.Changes ?? new List<SyncChange>());
                _db.Tracker.ClearPushed(events);
                SettingsService.Current.LastSyncServerSeq = response.ServerSeq;
                SettingsService.Current.LastSyncTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SettingsService.Save();
                _onSynced?.Invoke();
            });
            DiagnosticLog.Info("sync", $"round-trip ok: applied {response.Changes?.Count ?? 0} change(s), server seq {response.ServerSeq}");

            await SetStatusAfterSpin(SyncStatus.Online, startedAt);
        }
        catch (SyncAuthException ex)
        {
            DiagnosticLog.Error("sync", $"sync key rejected (HTTP 401): {ex.Message}");
            await SetStatusAfterSpin(SyncStatus.Offline, startedAt, authFailed: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("sync", $"round-trip failed: {ErrorDetail(ex)}");
            await SetStatusAfterSpin(SyncStatus.Offline, startedAt);
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    // ─── Status text & colour for the settings page ────────────────
    // Mirrors FluentColors.xaml semantic brushes (theme-neutral hex values, kept in sync
    // by hand): Online = AccentGreen, Syncing = AccentBlue, error = AccentRed, idle = TextDisabled.
    private static readonly Brush OnlineBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
    private static readonly Brush SyncingBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
    private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0x9F, 0x9D));

    public string StatusText => Status switch
    {
        SyncStatus.Syncing => Loc.SyncStatusSyncing,
        SyncStatus.Online => $"{Loc.SyncStatusOnline} · {LastSyncText}",
        SyncStatus.Offline when _authFailed => Loc.SyncStatusAuthFailed,
        SyncStatus.Offline => Loc.SyncStatusOffline,
        SyncStatus.VersionMismatch => Loc.SyncStatusVersionMismatch,
        SyncStatus.NotConfigured => Loc.SyncStatusNotConfigured,
        _ => Loc.SyncStatusDisabled,
    };

    /// <summary>Colour the status line by state: green online, blue syncing, red on
    /// version mismatch / rejected key, gray when offline or sync is off.</summary>
    public Brush StatusBrush => Status switch
    {
        SyncStatus.Online => OnlineBrush,
        SyncStatus.Syncing => SyncingBrush,
        SyncStatus.VersionMismatch => ErrorBrush,
        SyncStatus.Offline when _authFailed => ErrorBrush,
        _ => IdleBrush,
    };

    public string LastSyncText
    {
        get
        {
            var ts = SettingsService.Current.LastSyncTime;
            if (ts == 0) return Loc.SyncNever;
            return $"{Loc.SyncLastSynced} {Loc.ReminderTime(DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime)}";
        }
    }

    private void SetStatus(SyncStatus status, bool authFailed = false)
    {
        _authFailed = authFailed;
        Status = status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        StatusChanged?.Invoke();
    }

    /// <summary>Longest time the "syncing" indicator is allowed to stay up on a fast
    /// round-trip, so the spinner is actually visible even when the request fails or
    /// completes in milliseconds (e.g. connection refused or version mismatch).</summary>
    private const long MinSpinMs = 700;

    /// <summary>Sets the terminal status, but only after the syncing indicator has been
    /// visible for at least <see cref="MinSpinMs"/>. Skipped headless (no dispatcher) —
    /// there is no UI to show it, and tests call SyncOnceAsync directly.</summary>
    private async Task SetStatusAfterSpin(SyncStatus status, long startedAt, bool authFailed = false)
    {
        if (_dispatcher != null)
        {
            var remaining = MinSpinMs - (Environment.TickCount64 - startedAt);
            if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining));
        }
        SetStatus(status, authFailed);
    }

    /// <summary>Unwraps to the innermost exception so the log shows the real cause
    /// (e.g. "connection refused") instead of an HttpRequestException wrapper.</summary>
    private static string ErrorDetail(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null) current = current.InnerException;
        return $"{current.GetType().Name}: {current.Message}";
    }

    /// <summary>Builds a request from an outbox snapshot and performs the HTTP round-trip
    /// — the only part of a sync that touches the network (kept off the DB thread).</summary>
    private async Task<SyncResponse> RoundTripAsync(List<SyncEvent> events, long since)
    {
        var request = new SyncRequest
        {
            DeviceId = SettingsService.Current.DeviceId,
            Since = since,
            Changes = events.Select(e => new SyncChange
            {
                Type = e.EntityType,
                Id = e.EntityId,
                ModifiedAt = e.ModifiedAt,
                Deleted = e.Deleted,
                Payload = e.PayloadJson,
            }).ToList(),
        };
        return await BuildClient().SyncAsync(request);
    }

    /// <summary>The server URL/key can be edited in settings between syncs, so the client
    /// is rebuilt per round-trip (the shared HttpClient keeps its connection pool).</summary>
    private SyncHttpClient BuildClient()
    {
        var s = SettingsService.Current;
        return new SyncHttpClient(s.SyncServerUrl, s.SyncKey, _http);
    }

    private Task RunOnDbThread(Action action)
    {
        if (_dispatcher == null) { action(); return Task.CompletedTask; }
        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> RunOnDbThread<T>(Func<T> func)
    {
        if (_dispatcher == null) return Task.FromResult(func());
        return _dispatcher.InvokeAsync(func).Task;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _http.Dispose();
    }
}
