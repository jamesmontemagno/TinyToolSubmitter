namespace TinyToolSubmitter;

/// <summary>
/// Builds a pre-filled GitHub issue URL for the Tiny Tool Town submission template.
/// </summary>
public static class IssueUrlBuilder
{
    private const string BaseUrl = "https://github.com/shanselman/TinyToolTown/issues/new";
    private const string Template = "submit-tool.yml";

    /// <summary>
    /// Builds a URL that opens a new GitHub issue with the submit-tool.yml template
    /// pre-filled with the provided metadata.
    /// </summary>
    public static string Build(ToolMetadata metadata)
    {
        var parameters = new List<string>
        {
            $"template={Uri.EscapeDataString(Template)}",
            $"title={Uri.EscapeDataString($"[Tool] {metadata.Name}")}",
            $"labels={Uri.EscapeDataString("new-tool")}",
            $"name={Uri.EscapeDataString(metadata.Name)}",
            $"tagline={Uri.EscapeDataString(metadata.Tagline)}",
            $"description={Uri.EscapeDataString(metadata.Description)}",
            $"github_url={Uri.EscapeDataString(metadata.GitHubUrl)}",
            $"author={Uri.EscapeDataString(metadata.Author)}",
            $"author_github={Uri.EscapeDataString(metadata.AuthorGitHub)}",
            $"tags={Uri.EscapeDataString(metadata.Tags)}"
        };

        if (!string.IsNullOrWhiteSpace(metadata.WebsiteUrl))
            parameters.Add($"website_url={Uri.EscapeDataString(metadata.WebsiteUrl)}");

        if (!string.IsNullOrWhiteSpace(metadata.Language))
            parameters.Add($"language={Uri.EscapeDataString(metadata.Language)}");

        if (!string.IsNullOrWhiteSpace(metadata.License))
            parameters.Add($"license={Uri.EscapeDataString(metadata.License)}");

        return $"{BaseUrl}?{string.Join("&", parameters)}";
    }
}
