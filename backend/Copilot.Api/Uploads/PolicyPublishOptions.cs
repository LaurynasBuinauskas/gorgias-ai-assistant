namespace Copilot.Api.Uploads;

/// <summary>
/// How the API reaches the publish workflow. The token is a fine-grained GitHub token with
/// actions-write on this repository only, stored in Key Vault; while it is unset every
/// publish attempt answers 503 and says so — uploads keep working either way.
/// </summary>
public sealed class PolicyPublishOptions
{
    public const string SectionName = "PolicyPublish";

    public string GitHubToken { get; set; } = "";

    public string Repository { get; set; } = "LaurynasBuinauskas/gorgias-ai-assistant";

    public string WorkflowFile { get; set; } = "publish-policy.yml";

    public string GitRef { get; set; } = "main";

    public string VersionsContainer { get; set; } = "knowledge-versions";
}
