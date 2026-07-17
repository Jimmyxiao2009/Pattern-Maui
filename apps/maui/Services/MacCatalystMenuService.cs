namespace Pattern.Maui.Services;

/// <summary>Shared callback for the Catalyst menu command; the actual UIMenu is installed by AppDelegate.</summary>
public static class MacCatalystMenuService
{
    public static event Action? QuickChatRequested;
    public static void RequestQuickChat() => QuickChatRequested?.Invoke();
}
