using System.Text.RegularExpressions;

namespace Copilot.Api.Uploads;

/// <summary>
/// Boundary validation for client uploads. Everything here is about structure — content
/// checks (banned codes, PII, chunk sanity) run in the ingest pipeline where the parsed
/// text exists. Returns the reason a request is refused, in words the uploader can act on,
/// or null when it is acceptable.
/// </summary>
public static partial class PolicyUploadValidator
{
    private static readonly string[] s_allowedExtensions = [".md", ".docx"];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,58}[a-z0-9]$")]
    private static partial Regex TopicShape();

    public static string? Validate(
        string fileName,
        long sizeBytes,
        string market,
        string topic,
        string uploadedBy,
        PolicyUploadOptions options)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!s_allowedExtensions.Contains(extension))
        {
            // PDFs are refused on purpose: the client's PDFs strip diacritics, a recorded
            // source defect. Refusing loudly beats ingesting silently broken text.
            return $"Only .md and .docx files are accepted, not '{extension}'. "
                   + "PDFs lose accented characters and cannot be used.";
        }

        if (fileName != Path.GetFileName(fileName) || fileName.Contains(".."))
        {
            return "The file name must be a plain name without any path.";
        }

        if (sizeBytes <= 0)
        {
            return "The file is empty.";
        }

        if (sizeBytes > options.MaxFileBytes)
        {
            return $"The file is {sizeBytes:N0} bytes; the limit is {options.MaxFileBytes:N0}. "
                   + "Split very large documents by topic.";
        }

        if (!options.Markets.Contains(market, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{market}' is not a known market. Use one of: "
                   + string.Join(", ", options.Markets) + ".";
        }

        if (!TopicShape().IsMatch(topic))
        {
            return "The topic must be lowercase letters, digits and hyphens, "
                   + "like 'shipping-and-returns'.";
        }

        if (string.IsNullOrWhiteSpace(uploadedBy) || uploadedBy.Trim().Length > 100)
        {
            return "Say who is uploading (uploadedBy), so changes are attributable.";
        }

        return null;
    }
}
