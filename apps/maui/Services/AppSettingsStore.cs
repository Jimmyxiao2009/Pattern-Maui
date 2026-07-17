using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Storage;

namespace Pattern.Maui.Services;

public sealed record RuntimeProfile(
    string Provider,
    string Endpoint,
    string Model,
    string Persona,
    string UserName,
    bool ProactiveEnabled,
    bool ProactivePaused,
    int BedtimeHour);

public sealed record ConversationSession(string Id, string Title, List<ChatTurn> Messages, bool Archived, DateTimeOffset UpdatedAt);

public sealed record RelayBackupInfo(string Url, string Username, string DeviceId);

/// <summary>
/// Portable, non-secret client backup. API keys and relay credentials are
/// deliberately excluded; they stay in SecureStorage and must be re-entered or
/// paired again on a new device.
/// </summary>
public sealed record AppSettingsBackup(
    int Version,
    DateTimeOffset ExportedAt,
    RuntimeProfile Profile,
    List<ConversationSession> Sessions,
    string ConversationHistory,
    RelayBackupInfo? Relay);

/// <summary>
/// Small persistence boundary for MAUI. Non-secret preferences use the platform
/// preferences store; the API key is kept in SecureStorage and never included in
/// the JSON settings blob or normal diagnostics.
/// </summary>
public sealed class AppSettingsStore
{
    public const int CurrentBackupVersion = 1;
    private const string ProfileKey = "pattern.runtime.profile";
    private const string ApiKeyKey = "pattern.runtime.api-key";
    private const string ConversationKey = "pattern.conversation.history";
    private const string SessionsKey = "pattern.conversation.sessions";

    public RuntimeProfile LoadProfile()
    {
        var raw = Preferences.Default.Get(ProfileKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return new RuntimeProfile(
                "openai-compatible",
                Environment.GetEnvironmentVariable("PATTERN_ENDPOINT") ?? "https://api.openai.com/v1",
                Environment.GetEnvironmentVariable("PATTERN_MODEL") ?? "gpt-4o-mini",
                "You are Pattern, a helpful personal AI companion.",
                "User",
                false,
                false,
                23);
        try
        {
            return JsonSerializer.Deserialize<RuntimeProfile>(raw) ?? throw new JsonException();
        }
        catch
        {
            Preferences.Default.Remove(ProfileKey);
            return new RuntimeProfile("openai-compatible", "https://api.openai.com/v1", "gpt-4o-mini", "You are Pattern, a helpful personal AI companion.", "User", false, false, 23);
        }
    }

    public void SaveProfile(RuntimeProfile profile) =>
        Preferences.Default.Set(ProfileKey, JsonSerializer.Serialize(profile));

    public async Task<string> LoadApiKeyAsync()
    {
        try { return await SecureStorage.Default.GetAsync(ApiKeyKey) ?? string.Empty; }
        catch { return string.Empty; }
    }

    public async Task SaveApiKeyAsync(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) SecureStorage.Default.Remove(ApiKeyKey);
            else await SecureStorage.Default.SetAsync(ApiKeyKey, value);
        }
        catch
        {
            // Some desktop debug hosts do not provide a secure vault. Do not fall
            // back to plaintext; the user can still provide PATTERN_API_KEY.
        }
    }

    public string LoadConversationHistory() => Preferences.Default.Get(ConversationKey, string.Empty);

    public void SaveConversationHistory(string value) => Preferences.Default.Set(ConversationKey, value);

    public void ClearConversationHistory() => Preferences.Default.Remove(ConversationKey);

    public List<ConversationSession> LoadConversationSessions()
    {
        var raw = Preferences.Default.Get(SessionsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try { return JsonSerializer.Deserialize<List<ConversationSession>>(raw) ?? []; }
        catch { Preferences.Default.Remove(SessionsKey); return []; }
    }

    public void SaveConversationSessions(IEnumerable<ConversationSession> sessions) =>
        Preferences.Default.Set(SessionsKey, JsonSerializer.Serialize(sessions));

    public AppSettingsBackup CreateBackup(RelaySettings? relay = null) => new(
        CurrentBackupVersion,
        DateTimeOffset.UtcNow,
        LoadProfile(),
        LoadConversationSessions(),
        LoadConversationHistory(),
        relay is null ? null : new RelayBackupInfo(relay.Url, relay.Username, relay.DeviceId));

    public void RestoreBackup(AppSettingsBackup backup)
    {
        if (backup.Version != CurrentBackupVersion)
            throw new InvalidOperationException($"不支持的备份版本：{backup.Version}");
        SaveProfile(backup.Profile);
        SaveConversationSessions(backup.Sessions ?? []);
        SaveConversationHistory(backup.ConversationHistory ?? string.Empty);
    }

    /// <summary>
    /// Parses and migrates the original unversioned export shape as well as the
    /// current version. Keeping migration here means future schema changes do
    /// not require changing the page-level import flow.
    /// </summary>
    public static AppSettingsBackup ParseBackup(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("备份不是有效 JSON。");
        var version = node["version"]?.GetValue<int>() ?? 0;
        if (version == 0)
        {
            node["version"] = CurrentBackupVersion;
            node["exportedAt"] ??= DateTimeOffset.UtcNow.ToString("O");
            node["profile"] ??= node["runtimeProfile"]?.DeepClone() ?? JsonSerializer.SerializeToNode(new RuntimeProfile(
                "openai-compatible", "https://api.openai.com/v1", "gpt-4o-mini",
                "You are Pattern, a helpful personal AI companion.", "User", false, false, 23));
            node["sessions"] ??= new JsonArray();
            node["conversationHistory"] ??= node["history"]?.GetValue<string>() ?? string.Empty;
        }
        if (version > CurrentBackupVersion)
            throw new InvalidOperationException($"备份版本 {version} 高于当前客户端支持的版本。");
        var backup = node.Deserialize<AppSettingsBackup>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (backup is null || backup.Profile is null || backup.Sessions is null)
            throw new InvalidOperationException("备份缺少必要的配置或会话数据。");
        return backup with { Version = CurrentBackupVersion, ConversationHistory = backup.ConversationHistory ?? string.Empty };
    }
}
