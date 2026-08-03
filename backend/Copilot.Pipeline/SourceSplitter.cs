using System.Text;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// The delimiter as the model actually writes it, which is not always as it was asked.
    ///
    /// It was matched literally until a draft came back with `---\nSOURCES---`, the dashes and
    /// the word split across a line break. An exact match missed it, so the whole block stayed
    /// in the reply, no citation resolved, and the failure reported itself as "the model never
    /// emitted a sources block" — which was untrue and sent the investigation the wrong way.
    /// The agent would have seen a draft ending in `---SOURCES--- P2 P4` and no sources listed.
    ///
    /// Whitespace and a varying number of dashes are therefore tolerated. The model is being
    /// asked for a marker, not a checksum.
    /// </summary>
    private static readonly Regex s_delimiter = new(
        @"-{2,}\s*SOURCES\s*-{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Characters held back while streaming so a delimiter arriving in pieces is still
    /// recognised. Comfortably longer than the delimiter and any whitespace inside it.
    /// </summary>
    private const int HoldBack = 32;

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

        var marker = s_delimiter.Match(_pending);
        if (marker.Success)
        {
            var emit = _pending[..marker.Index];
            _sources.Append(_pending[(marker.Index + marker.Length)..]);
            _pending = "";
            _inSources = true;
            _body.Append(emit);
            return emit;
        }

        // Keep back enough to recognise a delimiter split across updates.
        var safe = Math.Max(0, _pending.Length - HoldBack);
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
    /// Whether the model ever emitted the delimiter. Distinguishes "wrote no sources at all"
    /// from "wrote sources that resolved to nothing" — which look identical from the citation
    /// list and need opposite fixes.
    /// </summary>
    public bool EmittedSourcesBlock => _inSources;

    /// <summary>
    /// Labels the model cited that matched nothing retrieved, populated by
    /// <see cref="ResolveCitations"/>. An internal label is expected here and is not a defect;
    /// anything else means the model named a source it was never shown.
    /// </summary>
    public IReadOnlyList<string> UnresolvedLabels => _unresolved;

    private readonly List<string> _unresolved = [];

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
            if (!seen.Add(label))
            {
                continue;
            }

            if (citable.TryGetValue(label, out var chunk))
            {
                citations.Add(new DraftCitation(label, chunk.Id, chunk.SourcePath, chunk.Market));
            }
            else
            {
                _unresolved.Add(label);
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
