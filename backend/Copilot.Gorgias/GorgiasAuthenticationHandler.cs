namespace Copilot.Gorgias;

/// <summary>Attaches the Gorgias credentials to every outgoing request of the typed client.</summary>
public sealed class GorgiasAuthenticationHandler(IGorgiasCredentialProvider credentialProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = credentialProvider.CreateAuthenticationHeader();
        return base.SendAsync(request, cancellationToken);
    }
}
