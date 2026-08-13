namespace Copilot.Api.Uploads;

public static class PolicyUploadExtensions
{
    public static IServiceCollection AddPolicyUploads(this IServiceCollection services)
    {
        // Validated on first use rather than at startup: a dev machine without storage
        // configured must still run the API for draft work; the admin surface then fails
        // with this message instead of a null-reference deep in the SDK.
        services.AddOptions<PolicyUploadOptions>()
            .BindConfiguration(PolicyUploadOptions.SectionName)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                "PolicyUploads:ConnectionString is not configured. Set it from the Key Vault "
                + "secret 'storage-connection'.");

        services.AddSingleton<IPolicyDraftStore, BlobPolicyDraftStore>();
        return services;
    }
}
