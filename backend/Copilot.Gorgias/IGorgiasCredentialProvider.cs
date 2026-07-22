using System.Net.Http.Headers;

namespace Copilot.Gorgias;

/// <summary>
/// Seam for Gorgias authentication. The private-app model uses a Basic header from an
/// API key; a future public-app OAuth2 flow replaces only this implementation.
/// </summary>
public interface IGorgiasCredentialProvider
{
    AuthenticationHeaderValue CreateAuthenticationHeader();
}
