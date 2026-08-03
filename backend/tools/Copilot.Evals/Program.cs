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

// Tracks production, which has run exemplars at 3 since 2026-08-03. It is deliberately not
// zero: a default run that tested a configuration nobody serves would report class J as not
// exercised and say nothing about the corpus actually in use. Keep this in step with
// `TicketTopK` in `appsettings.json`; pass `--ticket-topk 0` to see behaviour without them.
var ticketTopK = int.TryParse(Argument("--ticket-topk"), out var parsed) ? parsed : 3;
var draftsPath = Argument("--drafts");
var root = AppContext.BaseDirectory;

// Sweeping the whole corpus is a different job from running cases, so it exits here rather
// than bolting a second meaning onto the eval report.
if (Environment.GetCommandLineArgs().Contains("--sweep-corpus"))
{
    var sweepKey = Secret("search-adminkey");
    if (sweepKey is null)
    {
        Console.Error.WriteLine("Could not read the search key. Run 'az login'.");
        return 1;
    }

    var ticketIndex = Argument("--index") ?? "tickets-v2";
    Console.WriteLine($"Sweeping every document in {ticketIndex}");

    var sweep = await CorpusSweep.RunAsync(
        new Uri("https://gorgias-assistant-search.search.windows.net"),
        ticketIndex,
        sweepKey,
        CancellationToken.None);

    var sweepReport = CorpusSweep.Render(sweep);
    File.WriteAllText(outputPath, sweepReport);
    Console.WriteLine(sweepReport);
    Console.WriteLine($"Report written to {outputPath}");
    return sweep.Findings.Count == 0 ? 0 : 1;
}

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

var runner = new EvalRunner(
    chatClient, store, new RetrievalOptions { TicketTopK = ticketTopK }, new DraftingOptions());
Console.WriteLine($"Ticket exemplars: {(ticketTopK > 0 ? $"top {ticketTopK}" : "off")}");
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

var (markdown, blockingFailure) = Report.Render(results, ticketTopK);
File.WriteAllText(outputPath, markdown);

if (draftsPath is not null)
{
    // Machine-readable so two runs can be diffed without a human reading 98 drafts.
    File.WriteAllText(draftsPath, System.Text.Json.JsonSerializer.Serialize(
        results.Select(r => new
        {
            id = r.Case.Id,
            className = r.Case.Class,
            outcome = r.Outcome.Outcome,
            body = r.Outcome.Body,
            citations = r.Outcome.Citations.Select(c => c.SourcePath).ToArray(),
            passed = r.Passed,
            // Carried so a judge comparing two runs can assess whether a draft answers the
            // question, not merely whether it reads well.
            question = CustomerQuestion(Path.Combine(fixtures, r.Case.Fixture)),
        }),
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Drafts written to {draftsPath}");
}

Console.WriteLine();
Console.WriteLine($"{results.Count(r => r.Passed)}/{results.Count} case(s) passed. "
                  + $"Report written to {outputPath}");
Console.WriteLine(blockingFailure
    ? "FAIL: a release-blocking class is below threshold."
    : "PASS: every release-blocking class met its threshold.");

if (ticketTopK <= 0 && cases.Any(c => string.Equals(c.Class, "exemplar", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine(
        "NOTE: class J was not exercised — exemplars are off. Re-run with --ticket-topk 3 "
        + "to test the ticket corpus.");
}

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

static string CustomerQuestion(string fixturePath)
{
    if (!File.Exists(fixturePath))
    {
        return "";
    }

    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixturePath));
    var root = document.RootElement;
    var subject = root.TryGetProperty("subject", out var s) ? s.GetString() : "";

    var text = "";
    if (root.TryGetProperty("messages", out var messages))
    {
        foreach (var message in messages.EnumerateArray())
        {
            var fromAgent = message.TryGetProperty("fromAgent", out var a) && a.GetBoolean();
            var note = message.TryGetProperty("isInternalNote", out var n) && n.GetBoolean();
            if (!fromAgent && !note && message.TryGetProperty("text", out var t))
            {
                text = t.GetString() ?? "";
            }
        }
    }

    return $"Subject: {subject}\n{text}".Trim();
}
