using System;

namespace SubscriptionLibraries.Core.Models;

[Flags]
public enum XboxConsoleGenerations
{
    None = 0,
    XboxOne = 1,
    SeriesXorS = 2,
    Both = XboxOne | SeriesXorS
}
