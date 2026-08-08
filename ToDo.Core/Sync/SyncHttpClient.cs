using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ToDo.Sync;

/// <summary>
/// POSTs <see cref="SyncRequest"/> to the self-hosted sync server and parses the
/// reply. Lives in ToDo.Core so the MAUI Android client reuses it unchanged.
/// The wire format is camelCase with case-insensitive envelope keys (matching the
/// server's ConfigureHttpJsonOptions); entity payloads stay opaque JSON strings.
/// </summary>
public class SyncHttpClient
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _syncKey;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public SyncHttpClient(string serverUrl, string syncKey, HttpClient? http = null)
    {
        _endpoint = serverUrl.TrimEnd('/') + "/api/sync";
        _syncKey = syncKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, WebJson),
                Encoding.UTF8,
                "application/json"),
        };
        req.Headers.Add("X-Sync-Key", _syncKey);

        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new SyncAuthException();
        res.EnsureSuccessStatusCode();

        return await res.Content.ReadFromJsonAsync<SyncResponse>(WebJson, ct)
            ?? throw new SyncException("Sync server returned an empty response.");
    }
}

/// <summary>Base for sync transport failures.</summary>
public class SyncException : Exception
{
    public SyncException(string message) : base(message) { }
    public SyncException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The server rejected X-Sync-Key (HTTP 401) — the key is wrong or missing.</summary>
public class SyncAuthException : SyncException
{
    public SyncAuthException() : base("Sync key rejected (HTTP 401).") { }
}
