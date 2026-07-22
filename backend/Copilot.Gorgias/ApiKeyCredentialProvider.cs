using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Copilot.Gorgias;

public sealed class ApiKeyCredentialProvider(IOptions<GorgiasOptions> options) : IGorgiasCredentialProvider
{
    public AuthenticationHeaderValue CreateAuthenticationHeader()
    {
        var settings = options.Value;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Email}:{settings.ApiKey}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
