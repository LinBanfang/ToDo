using System.Net.Http;
using System.Threading;
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

        if (Interlocked.Exchange(ref _inFlight, 1) != 0) return;
        try
        {
            SetStatus(SyncStatus.Syncing);
            _authFailed = false;

            // First sync ever: seed the outbox with the current state of every syncable
            // entity so pre-existing data uploads instead of staying only on this device.
            if (settings.LastSyncServerSeq == 0 && !_bootstrapped)
            {
                await RunOnDbThread(() => _db.BootstrapSync());
                _bootstrapped = true;
            }

            // Snapshot the outbox and cursor on the DB thread (single-threaded LiteDB).
            var (events, since) = await RunOnDbThread(() =>
            {
                var evs = _db.Tracker.AllPending().ToList();
                return (evs, SettingsService.Current.LastSyncServerSeq);
            });

            var request = new SyncRequest
            {
                DeviceId = settings.DeviceId,
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

            var response = await BuildClient().SyncAsync(request);

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

            SetStatus(SyncStatus.Online);
        }
        catch (SyncAuthException)
        {
            SetStatus(SyncStatus.Offline, authFailed: true);
        }
        catch (Exception)
        {
            SetStatus(SyncStatus.Offline);
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    // ─── Status text for the settings page ────────────────
    public string StatusText => Status switch
    {
        SyncStatus.Syncing => Loc.SyncStatusSyncing,
        SyncStatus.Online => $"{Loc.SyncStatusOnline} · {LastSyncText}",
        SyncStatus.Offline when _authFailed => Loc.SyncStatusAuthFailed,
        SyncStatus.Offline => Loc.SyncStatusOffline,
        SyncStatus.NotConfigured => Loc.SyncStatusNotConfigured,
        _ => Loc.SyncStatusDisabled,
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
        StatusChanged?.Invoke();
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
