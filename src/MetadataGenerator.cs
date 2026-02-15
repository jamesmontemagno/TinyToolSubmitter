using GitHub.Copilot.SDK;
using System.Text;
using System.Text.Json;

namespace TinyToolSubmitter;

/// <summary>
/// Holds the metadata needed to submit a tool to Tiny Tool Town.
/// </summary>
public record ToolMetadata
{
    public string Name { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Description { get; set; } = "";
    public string GitHubUrl { get; set; } = "";
    public string? WebsiteUrl { get; set; }
    public string Author { get; set; } = "";
    public string AuthorGitHub { get; set; } = "";
    public string Tags { get; set; } = "";
    public string? Language { get; set; }
    public string? License { get; set; }
}

/// <summary>
/// Uses the Copilot SDK to analyze a README and extract tool metadata.
/// </summary>
public static class MetadataGenerator
{
    public static async Task<ToolMetadata?> GenerateAsync(
        CopilotSession session, string readmeContent, string repoName)
    {
                var prompt = $@"You extract metadata from a GitHub README for Tiny Tool Town.

Return ONLY valid JSON (no markdown, no code fences) with this exact schema:
{{
    ""name"": ""string"",
    ""tagline"": ""string <= 100 chars"",
    ""description"": ""string, 2-4 sentences"",
    ""tags"": [""lowercase-tag"", ""another-tag"", ""3-6 tags total""]
}}

Rules:
- Name must match the tool name from README; do not invent a different product.
- Tagline must be concise and clear.
- Description should be enthusiastic but honest.
- Tags must be lowercase, relevant, and contain 3-6 entries.

Repository name: {repoName}

README content:
{readmeContent}";

        var raw = await SendPromptAsync(session, prompt);
        var metadata = ParseResponse(raw);
        if (metadata == null)
            return null;

        NormalizeGeneratedFields(metadata);

        if (HasGeneratedFieldIssues(metadata, out var issues))
        {
            var repairPrompt = $"""
                Fix this metadata JSON so it satisfies ALL constraints.
                Return ONLY valid JSON with keys: name, tagline, description, tags.

                Constraints:
                - name: non-empty
                - tagline: non-empty and <= 100 characters
                - description: non-empty, 2-4 sentences
                - tags: array of 3-6 lowercase, relevant tags

                Issues found:
                {string.Join("; ", issues)}

                Current JSON:
                {JsonSerializer.Serialize(new
                {
                    name = metadata.Name,
                    tagline = metadata.Tagline,
                    description = metadata.Description,
                    tags = metadata.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                })}
                """;

            var repairedRaw = await SendPromptAsync(session, repairPrompt);
            var repaired = ParseResponse(repairedRaw);
            if (repaired != null)
            {
                NormalizeGeneratedFields(repaired);
                if (!HasGeneratedFieldIssues(repaired, out _))
                    metadata = repaired;
            }
        }

        ApplyFallbacks(metadata, repoName);
        NormalizeGeneratedFields(metadata);

        return metadata;
    }

    private static async Task<string> SendPromptAsync(CopilotSession session, string prompt)
    {
        var result = new StringBuilder();
        var done = new TaskCompletionSource();

        var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    result.Append(delta.Data.DeltaContent);
                    break;
                case AssistantMessageEvent msg:
                    if (result.Length == 0)
                        result.Append(msg.Data.Content);
                    break;
                case SessionIdleEvent:
                    done.TrySetResult();
                    break;
                case SessionErrorEvent err:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [AI error: {err.Data.Message}]");
                    Console.ResetColor();
                    done.TrySetResult();
                    break;
            }
        });

        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt });
            await done.Task;
        }
        finally
        {
            subscription.Dispose();
        }

        return result.ToString().Trim();
    }

    private static ToolMetadata? ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var json = ExtractJsonObject(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagsValue = "";
            if (TryGetProperty(root, "tags", out var tagsElement))
            {
                if (tagsElement.ValueKind == JsonValueKind.Array)
                {
                    tagsValue = string.Join(", ", tagsElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))!);
                }
                else if (tagsElement.ValueKind == JsonValueKind.String)
                {
                    tagsValue = tagsElement.GetString() ?? "";
                }
            }

            return new ToolMetadata
            {
                Name = GetString(root, "name"),
                Tagline = GetString(root, "tagline"),
                Description = GetString(root, "description"),
                Tags = tagsValue
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start
            ? raw[start..(end + 1)]
            : raw;
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void NormalizeGeneratedFields(ToolMetadata metadata)
    {
        metadata.Name = metadata.Name.Trim();
        metadata.Tagline = metadata.Tagline.Trim();
        metadata.Description = metadata.Description.Trim();

        var tags = metadata.Tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant())
            .Distinct()
            .ToList();

        metadata.Tags = string.Join(", ", tags);
    }

    private static bool HasGeneratedFieldIssues(ToolMetadata metadata, out List<string> issues)
    {
        issues = [];

        if (string.IsNullOrWhiteSpace(metadata.Name))
            issues.Add("name is missing");

        if (string.IsNullOrWhiteSpace(metadata.Tagline))
            issues.Add("tagline is missing");
        else if (metadata.Tagline.Length > 100)
            issues.Add("tagline exceeds 100 characters");

        if (string.IsNullOrWhiteSpace(metadata.Description))
            issues.Add("description is missing");

        var tags = metadata.Tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (tags.Count < 3 || tags.Count > 6)
            issues.Add("tags must contain 3 to 6 entries");

        if (tags.Any(t => t != t.ToLowerInvariant()))
            issues.Add("tags must be lowercase");

        return issues.Count > 0;
    }

    private static void ApplyFallbacks(ToolMetadata metadata, string repoName)
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
            metadata.Name = repoName;

        if (string.IsNullOrWhiteSpace(metadata.Tagline))
            metadata.Tagline = $"A tiny tool called {repoName}.";

        if (metadata.Tagline.Length > 100)
            metadata.Tagline = metadata.Tagline[..100].TrimEnd();

        if (string.IsNullOrWhiteSpace(metadata.Description))
            metadata.Description = $"{repoName} is a helpful open source tool from this repository.";

        if (string.IsNullOrWhiteSpace(metadata.Tags))
            metadata.Tags = "cli, developer-tools, productivity";

    }
}
