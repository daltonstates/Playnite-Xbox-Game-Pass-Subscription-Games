using System;
using System.Threading;
using System.Threading.Tasks;

namespace SubscriptionLibraries.Core.Services;

public interface IHttpClientService
{
    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default);
}
