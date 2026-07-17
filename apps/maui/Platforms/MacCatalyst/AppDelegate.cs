using Foundation;
using ObjCRuntime;
using Pattern.Maui.Services;
using UIKit;
using UserNotifications;

namespace Pattern.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        _ = UNUserNotificationCenter.Current.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
        return base.FinishedLaunching(application, launchOptions);
    }

    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);
        if (builder.System != UIMenuSystem.MainSystem) return;
        var command = UIKeyCommand.Create(new Foundation.NSString("p"), UIKeyModifierFlags.Command | UIKeyModifierFlags.Alternate, new Selector("patternQuickChat:"));
        var menu = UIMenu.Create("Pattern", new UIMenuElement[] { command });
        builder.InsertSiblingMenuAfter(menu, UIMenuIdentifier.Help.ToString());
    }

    [Export("patternQuickChat:")]
    public void PatternQuickChat(UIKeyCommand command) => MacCatalystMenuService.RequestQuickChat();
}
