namespace SubscriptionLibraries.Core.Models;

public sealed class SubscriptionService
{
    public static readonly SubscriptionService PcGamePass = new("pc-game-pass", "PC Game Pass");

    public SubscriptionService(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }
}
