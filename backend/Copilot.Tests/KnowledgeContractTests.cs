using System.Reflection;
using Copilot.Knowledge;

namespace Copilot.Tests;

/// <summary>
/// The storage choice is meant to stay reversible: pgvector was the original plan and Azure AI
/// Search replaced it, so nothing above <see cref="IKnowledgeStore"/> may learn which one is
/// behind it. That is easy to state and easy to erode one convenient parameter at a time,
/// so it is asserted rather than trusted.
/// </summary>
public sealed class KnowledgeContractTests
{
    private static readonly string[] s_vendorNamespaces = ["Azure", "Microsoft.Azure", "OpenAI"];

    [Fact]
    public void NoVendorSdkTypeAppearsInTheStoreContract()
    {
        var surface = new List<Type>();

        foreach (var method in typeof(IKnowledgeStore).GetMethods())
        {
            surface.Add(method.ReturnType);
            surface.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        foreach (var type in new[] { typeof(KnowledgeQuery), typeof(KnowledgeChunk) })
        {
            surface.AddRange(type.GetProperties().Select(p => p.PropertyType));
        }

        var leaked = surface
            .SelectMany(Unwrap)
            .Where(t => s_vendorNamespaces.Any(ns =>
                t.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true))
            .Select(t => t.FullName)
            .Distinct()
            .ToArray();

        Assert.True(leaked.Length == 0,
            $"Vendor SDK types reached the knowledge contract: {string.Join(", ", leaked)}");
    }

    [Fact]
    public void RetrievalIsCancellable()
    {
        // Every async public API flows a CancellationToken; a draft abandoned mid-stream must
        // not leave a retrieval running against a paid service.
        var method = typeof(IKnowledgeStore).GetMethod(nameof(IKnowledgeStore.RetrieveAsync));

        Assert.NotNull(method);
        Assert.Contains(method!.GetParameters(), p => p.ParameterType == typeof(CancellationToken));
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments().SelectMany(Unwrap))
        {
            yield return argument;
        }
    }
}
