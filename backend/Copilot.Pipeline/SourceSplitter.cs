using System.Text;
using Copilot.Domain;
using Copilot.Knowledge;

namespace Copilot.Pipeline;

/// <summary>
/// Separates the customer-facing reply from the source labels that follow it.
///
/// Streaming makes this less trivial than a string split: the delimiter can arrive spread
/// across several updates, so emitting eagerly would leak a partial "---SOU" into the reply an
/// agent copies. This holds back a delimiter-length tail until it knows the text is safe.
/// </summary>
public sealed class SourceSplitter
{
    private readonly StringBuilder _body = new();
    private readonly StringBuilder _sources = new();
    private string _pending = "";
    private bool _inSources;

    /// <summary>Feeds one update and returns reply text that is safe to emit now.</summary>
    public string Push(string text)
    {
        if (_inSources)
        {
            _sources.Append(text);
            return "";
        }

        _pending += text;

        var marker = _pending.IndexOf(DraftPrompt.SourcesDelimiter, StringComparison.Ordinal);
        if (marker >= 0)
        {
            var emit = _pending[..marker];
            _sources.Append(_pending[(marker + DraftPrompt.SourcesDelimiter.Length)..]);
            _pending = "";
            _inSources = true;
            _body.Append(emit);
            return emit;
        }

        // Keep back enough to recognise a delimiter split across updates.
        var safe = Math.Max(0, _pending.Length - DraftPrompt.SourcesDelimiter.Length);
        var ready = _pending[..safe];
        _pending = _pending[safe..];
        _body.Append(ready);
        return ready;
    }

    /// <summary>Flushes whatever was held back. Call once the stream ends.</summary>
    public string Complete()
    {
        if (_inSources || _pending.Length == 0)
        {
            return "";
        }

        var remainder = _pending;
        _pending = "";
        _body.Append(remainder);
        return remainder;
    }

    public string Body => _body.ToString().TrimEnd();

    /// <summary>
    /// Resolves emitted labels against what was actually retrieved. Labels that match nothing
    /// are dropped rather than reported: a citation naming a source that was never shown is
    /// not evidence of grounding, and internal labels are deliberately unresolvable.
    /// </summary>
    public IReadOnlyList<DraftCitation> ResolveCitations(
        IReadOnlyDictionary<string, KnowledgeChunk> citable)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var citations = new List<DraftCitation>();

        foreach (var token in _sources.ToString()
                     .Split(['\n', '\r', ',', ' ', '\t', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            var label = token.Trim().TrimEnd('.', ';');
            if (seen.Add(label) && citable.TryGetValue(label, out var chunk))
            {
                citations.Add(new DraftCitation(label, chunk.Id, chunk.SourcePath, chunk.Market));
            }
        }

        return citations;
    }
}

/// <summary>Splits a complete, non-streamed response in one call.</summary>
public static class SourceSplitterExtensions
{
    public static SourceSplitter SplitAll(string text)
    {
        var splitter = new SourceSplitter();
        splitter.Push(text);
        splitter.Complete();
        return splitter;
    }
}
