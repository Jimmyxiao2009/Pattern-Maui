using System.Text.Json;
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

/// <summary>
/// Small persistence boundary for MAUI. Non-secret preferences use the platform
/// preferences store; the API key is kept in SecureStorage and never included in
/// the JSON settings blob or normal diagnostics.
/// </summary>
public sealed class AppSettingsStore
{
    private const string ProfileKey = "pattern.runtime.profile";
    private const string ApiKeyKey = "pattern.runtime.api-key";
    private const string ConversationKey = "pattern.conversation.history";

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
}
