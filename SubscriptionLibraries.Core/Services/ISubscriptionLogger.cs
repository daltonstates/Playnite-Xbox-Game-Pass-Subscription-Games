using System;

namespace SubscriptionLibraries.Core.Services;

public interface ISubscriptionLogger
{
    void Debug(string message);

    void Info(string message);

    void Warn(string message);

    void Warn(Exception exception, string message);

    void Error(Exception exception, string message);
}
public sealed class NullSubscriptionLogger : ISubscriptionLogger
{
    public static readonly NullSubscriptionLogger Instance = new();

    private NullSubscriptionLogger()
    {
    }

    public void Debug(string message)
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Warn(Exception exception, string message)
    {
    }

    public void Error(Exception exception, string message)
    {
    }
}
