using System;
using System.Net;

namespace SubscriptionLibraries.Core.Services;

public sealed class CatalogHttpException : Exception
{
    public CatalogHttpException(
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
public sealed class CatalogDataException : Exception
{
    public CatalogDataException(string message)
        : base(message)
    {
    }

    public CatalogDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
