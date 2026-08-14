using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Uploads;

/// <summary>
/// Dispatches the publish workflow over the GitHub REST API. GitHub is a dependency for
/// *publishing* only, by design — drafting never touches this path.
/// </summary>
public sealed class GitHubWorkflowTrigger(
    IHttpClientFactory httpClientFactory,
    IOptions<PolicyPublishOptions> options) : IPublishTrigger
{
    private readonly PolicyPublishOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GitHubToken);

    public async Task TriggerAsync(
        string publishId,
        IReadOnlyList<string> blobs,
        string publishedBy,
        string mode,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(GitHubWorkflowTrigger));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.github.com/repos/{_options.Repository}/actions/workflows/"
            + $"{_options.WorkflowFile}/dispatches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.GitHubToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("gorgias-copilot", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Content = JsonContent.Create(new
        {
            @ref = _options.GitRef,
            inputs = new
            {
                publishId,
                blobs = System.Text.Json.JsonSerializer.Serialize(blobs),
                publishedBy,
                mode,
            },
        });

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"GitHub refused the workflow dispatch: {(int)response.StatusCode} {body[..Math.Min(body.Length, 300)]}");
        }
    }
}
