using System.Diagnostics;

namespace Copilot.Tests.Integration;

/// <summary>
/// Credentials for tests that hit the real Search index and the real embedding API.
///
/// Read from the environment first, then from Key Vault via the Azure CLI, so a developer
/// with `az login` needs no setup and nothing is ever committed. When neither is available the
/// tests <b>skip with a reason</b> rather than passing — a green run that silently exercised
/// nothing is worse than a red one.
/// </summary>
public static class KnowledgeTestEnvironment
{
    private const string Vault = "gorgias-assistant-kv";

    public static string SearchEndpoint => "https://gorgias-assistant-search.search.windows.net";

    public static string IndexName => "knowledge-v1";

    public static string? SearchKey { get; } = Resolve("SEARCH_ADMIN_KEY", "search-adminkey");

    public static string? OpenAiKey { get; } = Resolve("OPENAI_API_KEY", "openai-apikey");

    public static bool IsAvailable => SearchKey is not null && OpenAiKey is not null;

    public static string SkipReason =>
        "Needs the Search and OpenAI keys. Run 'az login' with get access to " +
        $"{Vault}, or set SEARCH_ADMIN_KEY and OPENAI_API_KEY.";

    private static string? Resolve(string variable, string secretName)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var cli = FindAzureCli();
        if (cli is null)
        {
            return null;
        }

        try
        {
            var start = new ProcessStartInfo(cli)
            {
                Arguments = $"keyvault secret show --vault-name {Vault} --name {secretName} " +
                            "--query value -o tsv",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(milliseconds: 30_000);
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            // Not logged in, or the CLI failed. Both mean "skip", not "fail".
            return null;
        }
    }

    /// <summary>
    /// On Windows the Azure CLI is a .cmd shim, which CreateProcess will not resolve from a
    /// bare name — the tests would skip for the wrong reason and look like they had run.
    /// </summary>
    private static string? FindAzureCli()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "az.cmd", "az.bat", "az.exe" } : ["az"];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return (from directory in directories
                from name in names
                let candidate = Path.Combine(directory.Trim(), name)
                where File.Exists(candidate)
                select candidate).FirstOrDefault();
    }
}

/// <summary>A test that needs live credentials; skipped with a reason when they are absent.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!KnowledgeTestEnvironment.IsAvailable)
        {
            Skip = KnowledgeTestEnvironment.SkipReason;
        }
    }
}
