using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;

namespace Pattern.Maui.Services;

public sealed record RelaySettings(string Url, string Username, string Password, string ChannelKey, string DeviceId)
{
    public bool IsConfigured => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https");
}

public sealed record RelayEnvelope(string Id, string From, string Role, string Type, long Ts, string Body, bool Encrypted = true);
public sealed record RelayStatus(bool Configured, bool Online, int OutboxCount, DateTimeOffset? LastSyncAt, string? Error);

/// <summary>
/// Android's native relay client. It mirrors the sidecar relay wire format so the
/// APK can exchange encrypted messages without embedding Node or opening localhost.
/// The outbox and cursor are persisted and every sync is idempotent.
/// </summary>
public sealed class RelayService
{
    private const string UrlKey = "pattern.relay.url";
    private const string UsernameKey = "pattern.relay.username";
    private const string DeviceKey = "pattern.relay.device";
    private const string ChannelKey = "pattern.relay.channel";
    private const string PasswordKey = "pattern.relay.password";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _seen = [];
    private List<RelayEnvelope> _outbox = [];
    private RelaySettings _settings = new("", "", "", "", Guid.NewGuid().ToString("N"));
    private DateTimeOffset? _lastSync;
    private string? _lastError;
    private bool _online;

    public RelayStatus Status => new(_settings.IsConfigured, _online, _outbox.Count, _lastSync, _lastError);

    public async Task InitializeAsync()
    {
        _settings = await LoadSettingsAsync();
        LoadLocalState();
    }

    public RelaySettings CurrentSettings => _settings;

    public async Task<bool> ConsumePendingPairingAsync()
    {
        var raw = Preferences.Default.Get("pattern.pending.pairing", string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            await SaveSettingsAsync(ParsePairingCode(raw));
            Preferences.Default.Remove("pattern.pending.pairing");
            return true;
        }
        catch { return false; }
    }

    public async Task SaveSettingsAsync(RelaySettings settings)
    {
        _settings = settings with
        {
            Url = settings.Url.Trim(),
            Username = settings.Username.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(settings.DeviceId) ? Guid.NewGuid().ToString("N") : settings.DeviceId.Trim(),
            ChannelKey = string.IsNullOrWhiteSpace(settings.ChannelKey) ? NewSecret() : settings.ChannelKey.Trim(),
        };
        Preferences.Default.Set(UrlKey, _settings.Url);
        Preferences.Default.Set(UsernameKey, _settings.Username);
        Preferences.Default.Set(DeviceKey, _settings.DeviceId);
        try
        {
            await SecureStorage.Default.SetAsync(ChannelKey, _settings.ChannelKey);
            await SecureStorage.Default.SetAsync(PasswordKey, _settings.Password);
        }
        catch { /* leave the relay disabled if the platform vault is unavailable */ }
    }

    public static RelaySettings ParsePairingCode(string raw)
    {
        var value = raw.Trim();
        var encoded = value.StartsWith("pattern://pair?data=", StringComparison.OrdinalIgnoreCase)
            ? value["pattern://pair?data=".Length..]
            : value;
        encoded = Uri.UnescapeDataString(encoded);
        var json = Encoding.UTF8.GetString(FromBase64Url(encoded));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidOperationException("不支持的配对码版本。");
        var url = root.GetProperty("webdavUrl").GetString() ?? "";
        var key = root.GetProperty("channelKey").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("配对码缺少 WebDAV 或频道密钥。");
        return new RelaySettings(url, root.TryGetProperty("username", out var user) ? user.GetString() ?? "" : "", root.TryGetProperty("password", out var pass) ? pass.GetString() ?? "" : "", key, Guid.NewGuid().ToString("N"));
    }

    public async Task PublishChatAsync(string body, CancellationToken cancellationToken = default)
    {
        var env = new RelayEnvelope(Guid.NewGuid().ToString("N")[..26], _settings.DeviceId, "user", "chat", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await EnsureDirectoriesAsync(cancellationToken);
                await PutAsync($"mailbox/{env.Id}.json", JsonSerializer.Serialize(env with { Body = Encrypt(env.Body, _settings.ChannelKey) }), cancellationToken);
                _online = true;
                _lastError = null;
            }
            catch (Exception error)
            {
                _outbox.Add(env);
                SaveLocalState();
                _online = false;
                _lastError = error.Message;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<RelayEnvelope>> SyncAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_settings.IsConfigured)
            {
                _online = false;
                _lastError = "尚未配置 WebDAV relay";
                return [];
            }
            await EnsureDirectoriesAsync(cancellationToken);
            foreach (var queued in _outbox.ToArray())
            {
                try
                {
                    await PutAsync($"mailbox/{queued.Id}.json", JsonSerializer.Serialize(queued with { Body = Encrypt(queued.Body, _settings.ChannelKey) }), cancellationToken);
                    _outbox.Remove(queued);
                }
                catch { /* retain for next retry */ }
            }
            var names = await ListNamesAsync(cancellationToken);
            var incoming = new List<RelayEnvelope>();
            foreach (var name in names)
            {
                try
                {
                    var raw = await GetAsync($"mailbox/{name}", cancellationToken);
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var env = JsonSerializer.Deserialize<RelayEnvelope>(raw);
                    if (env is null || env.From == _settings.DeviceId || _seen.Contains(env.Id)) continue;
                    var decoded = env.Encrypted ? env with { Body = Decrypt(env.Body, _settings.ChannelKey) } : env;
                    _seen.Add(env.Id);
                    incoming.Add(decoded);
                }
                catch { /* ignore malformed or concurrently deleted files */ }
            }
            await PutAsync($"cursors/{_settings.DeviceId}.json", JsonSerializer.Serialize(new { deviceId = _settings.DeviceId, updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }), cancellationToken);
            _online = true;
            _lastError = null;
            _lastSync = DateTimeOffset.UtcNow;
            SaveLocalState();
            return incoming.OrderBy(item => item.Ts).ThenBy(item => item.Id).ToArray();
        }
        catch (Exception error)
        {
            _online = false;
            _lastError = error.Message;
            SaveLocalState();
            return [];
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureDirectoriesAsync(CancellationToken cancellationToken)
    {
        foreach (var part in new[] { "", "devices", "mailbox", "cursors", "state" })
        {
            try { await RequestAsync(HttpMethod.Put, part, null, cancellationToken, "MKCOL"); }
            catch { /* existing collections and providers without MKCOL are fine */ }
        }
    }

    private async Task<List<string>> ListNamesAsync(CancellationToken cancellationToken)
    {
        using var response = await RequestAsync(new HttpMethod("PROPFIND"), "mailbox/", "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/></d:prop></d:propfind>", cancellationToken, contentType: "application/xml");
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return Regex.Matches(xml, "<[^>]*:?href[^>]*>([^<]+)</", RegexOptions.IgnoreCase)
            .Select(match => Uri.UnescapeDataString(match.Groups[1].Value).Trim().Split('/').LastOrDefault() ?? "")
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task PutAsync(string path, string body, CancellationToken cancellationToken) =>
        await RequestAsync(HttpMethod.Put, path, body, cancellationToken);

    private async Task<string?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await RequestAsync(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken, string? overrideMethod = null, bool allowNotFound = false, string contentType = "application/json")
    {
        if (!_settings.IsConfigured) throw new InvalidOperationException("WebDAV relay 未配置。");
        var baseUrl = _settings.Url.TrimEnd('/') + "/pattern/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(overrideMethod is null ? method : new HttpMethod(overrideMethod), baseUrl);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.Username}:{_settings.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Add("Depth", "1");
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, contentType);
        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && !(allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound))
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException($"WebDAV {method} {path} 返回 {(int)status}");
        }
        return response;
    }

    private string StatePath => Path.Combine(FileSystem.AppDataDirectory, "pattern-relay-state.json");

    private void LoadLocalState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
            foreach (var id in doc.RootElement.GetProperty("seen").EnumerateArray()) _seen.Add(id.GetString() ?? "");
            _outbox = doc.RootElement.GetProperty("outbox").Deserialize<List<RelayEnvelope>>() ?? [];
        }
        catch { _seen.Clear(); _outbox = []; }
    }

    private void SaveLocalState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new { seen = _seen.TakeLast(5000), outbox = _outbox }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* persistence errors must not crash the app */ }
    }

    private async Task<RelaySettings> LoadSettingsAsync()
    {
        var url = Preferences.Default.Get(UrlKey, "");
        var username = Preferences.Default.Get(UsernameKey, "");
        var device = Preferences.Default.Get(DeviceKey, Guid.NewGuid().ToString("N"));
        var channel = "";
        var password = "";
        try
        {
            channel = await SecureStorage.Default.GetAsync(ChannelKey) ?? "";
            password = await SecureStorage.Default.GetAsync(PasswordKey) ?? "";
        }
        catch { }
        return new RelaySettings(url, username, password, channel, device);
    }

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Encrypt(string plain, string secret)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var iv = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[Encoding.UTF8.GetByteCount(plain)];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, Encoding.UTF8.GetBytes(plain), cipher, tag);
        return ToBase64Url(iv.Concat(tag).Concat(cipher).ToArray());
    }

    private static string Decrypt(string payload, string secret)
    {
        var value = FromBase64Url(payload);
        if (value.Length < 28) throw new InvalidOperationException("无效的 relay 密文");
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var plain = new byte[value.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(value[..12], value[28..], value[12..28], plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static string ToBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
