using System.Net;

namespace ContextGuard.Core;

// Shared validation policy for workflows and transports; no HTTP or OS side effects.
public static class ModelEndpointPolicy
{
    public static Uri Validate(string endpoint, bool localOnly)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new SafeOperationException("InvalidEndpoint");
        bool loopback = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
        if (localOnly && !loopback) throw new SafeOperationException("LocalEndpointOnly");
        if (!localOnly && uri.Scheme != "https" && !loopback)
            throw new SafeOperationException("HttpsRequired");
        return uri;
    }
}
