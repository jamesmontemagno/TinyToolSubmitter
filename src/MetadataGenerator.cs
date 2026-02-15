using GitHub.Copilot.SDK;

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
        var prompt = $"""
            You are an assistant that extracts metadata from a GitHub repository README for submission 
            to "Tiny Tool Town" — a curated collection of small, delightful open source tools.

            Analyze the following README and extract/generate the metadata below.
            Respond EXACTLY in this format (one field per line, field name followed by colon and value):

            NAME: The tool's name (concise, title-case)
            TAGLINE: A short, fun one-line description of what the tool does (under 100 chars)
            DESCRIPTION: A 2-4 sentence enthusiastic description of what it does, why it's great, and why it's delightful. Be conversational and fun.
            TAGS: Comma-separated lowercase tags relevant to the tool (3-6 tags, e.g. cli, windows, productivity)

            Rules:
            - NAME should be the actual tool name from the README, not made up
            - TAGLINE should be catchy and concise — think app store subtitle
            - DESCRIPTION should be enthusiastic but honest about what the tool does
            - TAGS should be relevant lowercase keywords, comma-separated
            - Do NOT wrap values in quotes
            - Each field must be on a single line

            Repository name: {repoName}

            README content:
            {readmeContent}
            """;

        var result = new System.Text.StringBuilder();
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

        var raw = result.ToString().Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return ParseResponse(raw);
    }

    private static ToolMetadata ParseResponse(string raw)
    {
        var metadata = new ToolMetadata();
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("NAME:", StringComparison.OrdinalIgnoreCase))
                metadata.Name = trimmed["NAME:".Length..].Trim();
            else if (trimmed.StartsWith("TAGLINE:", StringComparison.OrdinalIgnoreCase))
                metadata.Tagline = trimmed["TAGLINE:".Length..].Trim();
            else if (trimmed.StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase))
                metadata.Description = trimmed["DESCRIPTION:".Length..].Trim();
            else if (trimmed.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase))
                metadata.Tags = trimmed["TAGS:".Length..].Trim();
        }

        return metadata;
    }
}
