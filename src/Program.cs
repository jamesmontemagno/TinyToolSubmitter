using System.Diagnostics;
using GitHub.Copilot.SDK;
using Spectre.Console;
using TinyToolSubmitter;

// --- Parse CLI flags ---
var flagArgs = args.Where(a => a.StartsWith("--") || a.StartsWith("-")).ToList();
var positionalArgs = args.Where(a => !a.StartsWith("-")).ToList();

var headless = flagArgs.Contains("--headless");

string? flagReadmePath = null;
string? flagModelName = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--readme") flagReadmePath = args[i + 1];
    if (args[i] == "--model") flagModelName = args[i + 1];
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Tiny Tool Town — Submission Helper 🏘️📋             ║");
Console.WriteLine("║       Powered by GitHub Copilot SDK                        ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// Resolve the repo directory
var repoDir = positionalArgs.Count > 0
    ? Path.GetFullPath(positionalArgs[0])
    : Directory.GetCurrentDirectory();

if (!Directory.Exists(repoDir))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ Directory not found: {repoDir}");
    Console.ResetColor();
    return 1;
}

Console.WriteLine($"📂 Repository: {repoDir}");

// --- Step 1: Find README ---
string readmePath;
if (!string.IsNullOrEmpty(flagReadmePath))
{
    readmePath = Path.GetFullPath(flagReadmePath);
}
else
{
    var detected = RepoDetector.FindReadme(repoDir);
    if (detected != null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"📄 Found README: {Path.GetRelativePath(repoDir, detected)}");
        Console.ResetColor();

        if (headless)
        {
            readmePath = detected;
        }
        else
        {
            var useDetected = AnsiConsole.Confirm("Use this README?", true);
            readmePath = useDetected
                ? detected
                : PromptForReadmePath(repoDir);
        }
    }
    else if (!headless)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("📄 No README found automatically. Let's pick one.");
        Console.ResetColor();
        readmePath = PromptForReadmePath(repoDir);
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("❌ No README found and running in headless mode. Use --readme <path>.");
        Console.ResetColor();
        return 1;
    }
}

if (!File.Exists(readmePath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ README not found: {readmePath}");
    Console.ResetColor();
    return 1;
}

var readmeContent = await File.ReadAllTextAsync(readmePath);
if (readmeContent.Length > 4000)
    readmeContent = readmeContent[..4000] + "\n\n[truncated]";

Console.WriteLine($"📏 README length: {readmeContent.Length} chars");

// --- Step 2: Detect repo metadata ---
Console.WriteLine("\n🔍 Detecting repository metadata...");

var githubUrl = RepoDetector.DetectGitHubUrl(repoDir);
var license = RepoDetector.DetectLicense(repoDir);
var language = RepoDetector.DetectLanguage(repoDir);
var authorName = RepoDetector.DetectGitUserName(repoDir);
var authorGitHub = RepoDetector.DetectGitHubUsername(githubUrl);
var repoName = githubUrl != null
    ? new Uri(githubUrl).AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? Path.GetFileName(repoDir)
    : Path.GetFileName(repoDir);

Console.ForegroundColor = ConsoleColor.DarkGray;
if (githubUrl != null) Console.WriteLine($"   GitHub URL: {githubUrl}");
if (license != null) Console.WriteLine($"   License:    {license}");
if (language != null) Console.WriteLine($"   Language:   {language}");
if (authorName != null) Console.WriteLine($"   Author:     {authorName}");
if (authorGitHub != null) Console.WriteLine($"   Username:   {authorGitHub}");
Console.ResetColor();

// --- Step 3: Use Copilot to generate metadata ---
Console.WriteLine("\n🤖 Starting Copilot to analyze your README...");

CopilotClient? client = null;
CopilotSession? session = null;

try
{
    client = new CopilotClient();
    await client.StartAsync();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✅ Copilot client started");
    Console.ResetColor();

    // Model selection
    string selectedModel;
    if (flagModelName != null)
    {
        selectedModel = flagModelName;
    }
    else if (headless)
    {
        selectedModel = "gpt-5-mini";
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("   Fetching available models...");
        Console.ResetColor();

        var models = await client.ListModelsAsync();
        if (models != null && models.Count > 0)
        {
            var defaultIndex = models.FindIndex(m =>
                m.Id.Equals("gpt-5-mini", StringComparison.OrdinalIgnoreCase));
            if (defaultIndex < 0) defaultIndex = 0;

            var orderedModels = models
                .Select((model, index) => new { Model = model, IsDefault = index == defaultIndex })
                .OrderByDescending(item => item.IsDefault)
                .ToList();

            var labels = orderedModels
                .Select(item => item.IsDefault ? $"{item.Model.Name} (recommended)" : item.Model.Name)
                .ToList();

            var selectedLabel = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]🤖 Select a model[/]")
                    .PageSize(10)
                    .AddChoices(labels));

            var selectedIndex = labels.FindIndex(label => label == selectedLabel);
            selectedModel = orderedModels[selectedIndex].Model.Id;
        }
        else
        {
            selectedModel = "gpt-5-mini";
        }
    }

    Console.WriteLine($"🤖 Using model: {selectedModel}\n");

    session = await client.CreateSessionAsync(new SessionConfig
    {
        Model = selectedModel,
        Streaming = true
    });

    ToolMetadata? metadata;
    if (headless)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("   Analyzing README with AI...");
        Console.ResetColor();
        metadata = await MetadataGenerator.GenerateAsync(session, readmeContent, repoName);
    }
    else
    {
        metadata = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[grey]Analyzing README with AI...[/]", async _ =>
            {
                return await MetadataGenerator.GenerateAsync(session, readmeContent, repoName);
            });
    }

    if (metadata == null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("❌ Failed to generate metadata from README.");
        Console.ResetColor();
        return 1;
    }

    // Fill in detected values
    metadata.GitHubUrl = githubUrl ?? "";
    metadata.Language = language;
    metadata.License = license;
    metadata.Author = authorName ?? "";
    metadata.AuthorGitHub = authorGitHub ?? "";

    // --- Step 4: Review metadata ---
    AnsiConsole.Write(new Rule("[cyan]📋 Generated Submission Metadata[/]"));
    RenderMetadataTable(metadata);

    if (!headless)
    {
        while (true)
        {
            var fieldChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]✏️  Edit metadata fields[/] (choose [green]Done[/] to continue)")
                    .PageSize(12)
                    .AddChoices([
                        "Done",
                        "name",
                        "tagline",
                        "description",
                        "github_url",
                        "website",
                        "author",
                        "author_github",
                        "tags",
                        "language",
                        "license"
                    ]));

            if (fieldChoice == "Done")
                break;

            var currentValue = GetFieldValue(metadata, fieldChoice);
            var newValue = AnsiConsole.Prompt(
                new TextPrompt<string>($"New value for [green]{fieldChoice}[/]")
                    .DefaultValue(currentValue));

            SetFieldValue(metadata, fieldChoice, newValue.Trim());

            AnsiConsole.MarkupLine($"[green]✅ Updated {fieldChoice}[/]");
            RenderMetadataTable(metadata);
        }
    }

    // --- Step 5: Build and open the URL ---
    var issueUrl = IssueUrlBuilder.Build(metadata);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n═══════════════════════════════════════════════");
    Console.WriteLine("  🚀 Ready to Submit!");
    Console.WriteLine("═══════════════════════════════════════════════");
    Console.ResetColor();

    Console.WriteLine("\n📋 Open this URL to submit your tool to Tiny Tool Town:\n");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(issueUrl);
    Console.ResetColor();

    // Try to open the URL in the default browser
    if (!headless)
    {
        Console.Write("\n🌐 Open in browser? (Y/n): ");
        var openChoice = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(openChoice) || openChoice == "y" || openChoice == "yes")
        {
            OpenUrl(issueUrl);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Opened in browser!");
            Console.ResetColor();
        }
    }
    else
    {
        OpenUrl(issueUrl);
    }

    Console.WriteLine("\n🏘️  Thanks for submitting to Tiny Tool Town!");
    return 0;
}
catch (OperationCanceledException)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n⚠️ Cancelled by user.");
    Console.ResetColor();
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n❌ Fatal error: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    Console.ResetColor();
    return 1;
}
finally
{
    if (session != null) await session.DisposeAsync();
    if (client != null) await client.DisposeAsync();
    Console.WriteLine("\n🛑 Done.");
}

static void RenderMetadataTable(ToolMetadata metadata)
{
    var table = new Table()
        .RoundedBorder()
        .AddColumn("[yellow]Field[/]")
        .AddColumn("[yellow]Value[/]");

    table.AddRow("Tool Name", metadata.Name);
    table.AddRow("Tagline", metadata.Tagline);
    table.AddRow("Description", metadata.Description);
    table.AddRow("GitHub URL", metadata.GitHubUrl);
    table.AddRow("Website", metadata.WebsiteUrl ?? "(none)");
    table.AddRow("Author", metadata.Author);
    table.AddRow("GitHub User", metadata.AuthorGitHub);
    table.AddRow("Tags", metadata.Tags);
    table.AddRow("Language", metadata.Language ?? "(not detected)");
    table.AddRow("License", metadata.License ?? "(not detected)");

    AnsiConsole.Write(table);
}

static void OpenUrl(string url)
{
    try
    {
        if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { UseShellExecute = false, CreateNoWindow = true });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
    }
    catch
    {
        // Silently fail — URL is already printed
    }
}

static string PromptForReadmePath(string repoDir)
{
    var currentDir = repoDir;

    while (true)
    {
        var relativeCurrentDir = Path.GetRelativePath(repoDir, currentDir);
        var currentDirDisplay = string.IsNullOrEmpty(relativeCurrentDir) ? "." : relativeCurrentDir;

        var readmeCandidates = Directory
            .EnumerateFiles(currentDir)
            .Where(IsReadmeCandidate)
            .OrderBy(Path.GetFileName)
            .ToList();

        var subDirectories = Directory
            .EnumerateDirectories(currentDir)
            .Where(d => !string.Equals(Path.GetFileName(d), ".git", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName)
            .ToList();

        var prompt = new SelectionPrompt<string>();
        prompt.Title($"Select README in [green]{currentDirDisplay}[/] or browse:");
        prompt.PageSize(15);
        prompt.AddChoice("✍ Enter full path manually");
        prompt.AddChoices(readmeCandidates.Select(f => $"📄 {Path.GetFileName(f)}"));
        prompt.AddChoices(subDirectories.Select(d => $"📁 {Path.GetFileName(d)}"));

        var parentDirectory = Directory.GetParent(currentDir);
        if (parentDirectory != null)
            prompt.AddChoice("↩ Go up");

        prompt.AddChoice("❌ Cancel");

        var choice = AnsiConsole.Prompt(prompt);

        if (choice.StartsWith("📄 ", StringComparison.Ordinal))
        {
            var fileName = choice[(choice.IndexOf(' ') + 1)..].Trim();
            return Path.Combine(currentDir, fileName);
        }

        if (choice.StartsWith("📁 ", StringComparison.Ordinal))
        {
            var dirName = choice[(choice.IndexOf(' ') + 1)..].Trim();
            currentDir = Path.Combine(currentDir, dirName);
            continue;
        }

        if (choice == "↩ Go up")
        {
            if (parentDirectory != null)
                currentDir = parentDirectory.FullName;
            continue;
        }

        if (choice == "✍ Enter full path manually")
        {
            var input = AnsiConsole.Prompt(
                new TextPrompt<string>("README path")
                    .PromptStyle("green")
                    .DefaultValue(Path.Combine(repoDir, "README.md"))
                    .Validate(path =>
                        File.Exists(path)
                            ? ValidationResult.Success()
                            : ValidationResult.Error("README file does not exist.")));

            return Path.GetFullPath(input);
        }

        if (choice == "❌ Cancel")
            throw new OperationCanceledException("README selection cancelled.");
    }
}

static bool IsReadmeCandidate(string filePath)
{
    var fileName = Path.GetFileName(filePath);
    return fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("README", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("README.rst", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("README.txt", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("README.", StringComparison.OrdinalIgnoreCase);
}

static string GetFieldValue(ToolMetadata metadata, string field) => field switch
{
    "name" => metadata.Name,
    "tagline" => metadata.Tagline,
    "description" => metadata.Description,
    "github_url" => metadata.GitHubUrl,
    "website" => metadata.WebsiteUrl ?? string.Empty,
    "author" => metadata.Author,
    "author_github" => metadata.AuthorGitHub,
    "tags" => metadata.Tags,
    "language" => metadata.Language ?? string.Empty,
    "license" => metadata.License ?? string.Empty,
    _ => string.Empty
};

static void SetFieldValue(ToolMetadata metadata, string field, string value)
{
    switch (field)
    {
        case "name": metadata.Name = value; break;
        case "tagline": metadata.Tagline = value; break;
        case "description": metadata.Description = value; break;
        case "github_url": metadata.GitHubUrl = value; break;
        case "website": metadata.WebsiteUrl = value; break;
        case "author": metadata.Author = value; break;
        case "author_github": metadata.AuthorGitHub = value; break;
        case "tags": metadata.Tags = value; break;
        case "language": metadata.Language = value; break;
        case "license": metadata.License = value; break;
    }
}
