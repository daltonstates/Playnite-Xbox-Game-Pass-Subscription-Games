using System;

namespace SubscriptionLibraries.Core.Models;

[Flags]
public enum SubscriptionPlatforms
{
    None = 0,
    WindowsPc = 1,
    XboxConsole = 2
}

public static class SubscriptionPlatformNames
{
    public static string Format(SubscriptionPlatforms platforms) => platforms switch
    {
        SubscriptionPlatforms.WindowsPc => "Windows PC",
        SubscriptionPlatforms.XboxConsole => "Xbox console",
        SubscriptionPlatforms.WindowsPc | SubscriptionPlatforms.XboxConsole =>
            "Windows PC + Xbox console",
        _ => string.Empty
    };
}
