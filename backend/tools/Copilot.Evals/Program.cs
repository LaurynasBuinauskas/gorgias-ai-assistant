using System.Diagnostics;
using Copilot.Evals;
using Copilot.Knowledge;
using Copilot.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var caseFilter = Argument("--class");
var outputPath = Argument("--out") ?? "eval-report.md";
var root = AppContext.BaseDirectory;

var cases = LoadCases(Path.Combine(root, "cases"), caseFilter);
if (cases.Count == 0)
{
    Console.Error.WriteLine("No cases found. Expected YAML under cases/.");
    return 1;
}

Console.WriteLine($"Running {cases.Count} case(s)"
                  + (caseFilter is null ? "" : $" in class '{caseFilter}'"));

var openAiKey = Secret("openai-apikey");
var searchKey = Secret("search-adminkey");
if (openAiKey is null || searchKey is null)
{
    Console.Error.WriteLine(
        "Could not read secrets. Run 'az login' with access to gorgias-assistant-kv, "
        + "or set OPENAI_API_KEY and SEARCH_ADMIN_KEY.");
    return 1;
}

var chatClient = new OpenAIClient(openAiKey)
    .GetChatClient("gpt-4.1-mini-2025-04-14")
    .AsIChatClient();

var embeddings = new OpenAIClient(openAiKey)
    .GetEmbeddingClient("text-embedding-3-small")
    .AsIEmbeddingGenerator(1536);

var store = new AzureSearchKnowledgeStore(
    Options.Create(new KnowledgeOptions
    {
        Endpoint = "https://gorgias-assistant-search.search.windows.net",
        IndexName = "knowledge-v1",
        ApiKey = searchKey,
    }),
    embeddings,
    new RetrievalHealth(),
    NullLogger<AzureSearchKnowledgeStore>.Instance);

var runner = new EvalRunner(chatClient, store, new RetrievalOptions(), new DraftingOptions());
var fixtures = Path.Combine(root, "fixtures", "tickets");

var results = new List<CaseResult>();
foreach (var testCase in cases)
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        var result = await runner.RunAsync(testCase, fixtures, CancellationToken.None);
        results.Add(result);
        Console.WriteLine($"  {(result.Passed ? "pass" : "FAIL")}  {testCase.Id} "
                          + $"({stopwatch.ElapsedMilliseconds} ms)");
    }
    catch (Exception error)
    {
        // A case that throws is a failure, not a crash — one broken fixture must not hide the
        // results of every other case in the run.
        results.Add(new CaseResult(
            testCase,
            new DraftOutcome { Outcome = "error", Body = error.Message },
            [new AssertionResult("ran", false, error.Message)]));
        Console.WriteLine($"  ERROR {testCase.Id}: {error.Message}");
    }
}

var (markdown, blockingFailure) = Report.Render(results);
File.WriteAllText(outputPath, markdown);

Console.WriteLine();
Console.WriteLine($"{results.Count(r => r.Passed)}/{results.Count} case(s) passed. "
                  + $"Report written to {outputPath}");
Console.WriteLine(blockingFailure
    ? "FAIL: a release-blocking class is below threshold."
    : "PASS: every release-blocking class met its threshold.");

return blockingFailure ? 1 : 0;

static string? Argument(string name)
{
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static List<EvalCase> LoadCases(string directory, string? classFilter)
{
    if (!Directory.Exists(directory))
    {
        return [];
    }

    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    var cases = new List<EvalCase>();
    foreach (var path in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories))
    {
        EvalCase? testCase;
        try
        {
            testCase = deserializer.Deserialize<EvalCase>(File.ReadAllText(path));
        }
        catch (Exception error)
        {
            // Name the file. An unattributed parser error mid-suite is a poor trade for the
            // one-line try that turns it into "this file, this line".
            throw new InvalidOperationException(
                $"Could not parse case file {path}: {error.Message}", error);
        }

        if (testCase is null)
        {
            continue;
        }

        testCase.SourcePath = path;
        if (classFilter is null || string.Equals(testCase.Class, classFilter, StringComparison.OrdinalIgnoreCase))
        {
            cases.Add(testCase);
        }
    }

    return [.. cases.OrderBy(c => c.Class).ThenBy(c => c.Id)];
}

static string? Secret(string name)
{
    var fromEnvironment = Environment.GetEnvironmentVariable(
        name == "openai-apikey" ? "OPENAI_API_KEY" : "SEARCH_ADMIN_KEY");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

    // On Windows the Azure CLI is a .cmd shim, which CreateProcess will not resolve by name.
    var names = OperatingSystem.IsWindows() ? new[] { "az.cmd", "az.bat", "az.exe" } : ["az"];
    var cli = (from directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                   .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
               from candidate in names
               let full = Path.Combine(directory.Trim(), candidate)
               where File.Exists(full)
               select full).FirstOrDefault();

    if (cli is null)
    {
        return null;
    }

    using var process = Process.Start(new ProcessStartInfo(cli)
    {
        Arguments = $"keyvault secret show --vault-name gorgias-assistant-kv --name {name} "
                    + "--query value -o tsv",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    if (process is null)
    {
        return null;
    }

    var value = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit(30_000);
    return process.ExitCode == 0 && value.Length > 0 ? value : null;
}
