using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OscCommittee.Web.Services.Grading;

/// <summary>
/// PLAN §10.3 secret redaction. Only the outgoing prompt is redacted; the stored bundle and the
/// board's copy keep the real values.
/// </summary>
public static partial class SecretRedactor
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Replaces secret-looking JSON values inside every <c>appsettings*.json</c> section.</summary>
    public static string RedactBundle(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)
            || !markdown.Contains("appsettings", StringComparison.OrdinalIgnoreCase))
        {
            return markdown;
        }

        var lines = markdown.Split('\n');
        var output = new List<string>(lines.Length);
        var block = new List<string>();
        string? sectionPath = null;
        var inFence = false;

        foreach (var line in lines)
        {
            if (!inFence)
            {
                if (line.StartsWith("## /", StringComparison.Ordinal))
                {
                    sectionPath = line[4..].Trim();
                }

                if (line.StartsWith("````", StringComparison.Ordinal))
                {
                    inFence = true;
                    block.Clear();
                }

                output.Add(line);
                continue;
            }

            if (line == "````")
            {
                if (IsAppSettings(sectionPath))
                {
                    var content = string.Join('\n', block);
                    var redacted = TryRedactJson(content, out var json)
                        ? json
                        : RedactLines(content);

                    output.AddRange(redacted.Split('\n'));
                }
                else
                {
                    output.AddRange(block);
                }

                output.Add(line);
                inFence = false;
                continue;
            }

            block.Add(line);
        }

        // A truncated bundle can end mid-fence; keep whatever is left rather than dropping it.
        if (inFence && block.Count > 0)
        {
            output.AddRange(block);
        }

        return string.Join('\n', output);
    }

    /// <summary>Redacts a standalone JSON document; used by the tests and the fallback path.</summary>
    public static string RedactJson(string json) =>
        TryRedactJson(json, out var redacted) ? redacted : RedactLines(json);

    private static bool IsAppSettings(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var leaf = path[(path.LastIndexOf('/') + 1)..];
        return leaf.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
               && leaf.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRedactJson(string json, out string redacted)
    {
        redacted = json;

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return false;
            }

            RedactNode(node, insideConnectionStrings: false);
            redacted = node.ToJsonString(WriteOptions);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static void RedactNode(JsonNode node, bool insideConnectionStrings)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToList())
            {
                var value = obj[key];
                var connectionStrings =
                    insideConnectionStrings
                    || key.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase);

                if ((connectionStrings || SensitiveKey().IsMatch(key)) && value is JsonValue)
                {
                    obj[key] = "***";
                }
                else if (value is not null)
                {
                    RedactNode(value, connectionStrings);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    RedactNode(item, insideConnectionStrings);
                }
            }
        }
    }

    /// <summary>Line-based fallback for JSON that is truncated or otherwise not parseable.</summary>
    private static string RedactLines(string text)
    {
        var lines = text.Split('\n');
        var insideConnectionStrings = false;
        var depth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (!insideConnectionStrings
                && line.Contains("\"ConnectionStrings\"", StringComparison.OrdinalIgnoreCase))
            {
                insideConnectionStrings = true;
            }

            lines[index] = KeyValue().Replace(line, match =>
            {
                var key = match.Groups["key"].Value;
                return insideConnectionStrings || SensitiveKey().IsMatch(key)
                    ? $"\"{key}\": \"***\""
                    : match.Value;
            });

            if (!insideConnectionStrings)
            {
                continue;
            }

            depth += line.Count(character => character == '{');
            depth -= line.Count(character => character == '}');

            if (depth <= 0)
            {
                insideConnectionStrings = false;
            }
        }

        return string.Join('\n', lines);
    }

    [GeneratedRegex("password|secret|key|token|connectionstring", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKey();

    [GeneratedRegex(@"""(?<key>(?:[^""\\]|\\.)*)""\s*:\s*""(?:[^""\\]|\\.)*""", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValue();
}