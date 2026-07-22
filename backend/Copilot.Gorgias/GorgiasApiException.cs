using System.Net;

namespace Copilot.Gorgias;

public sealed class GorgiasApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
