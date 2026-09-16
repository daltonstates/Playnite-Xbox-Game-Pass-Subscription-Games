using System;
using Playnite.SDK;
using SubscriptionLibraries.Core.Services;

namespace SubscriptionLibraries.Services;

internal sealed class PlayniteSubscriptionLogger : ISubscriptionLogger
{
    private readonly ILogger logger;

    public PlayniteSubscriptionLogger(ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Debug(string message) => logger.Debug(message);

    public void Info(string message) => logger.Info(message);

    public void Warn(string message) => logger.Warn(message);

    public void Warn(Exception exception, string message) => logger.Warn(exception, message);

    public void Error(Exception exception, string message) => logger.Error(exception, message);
}
