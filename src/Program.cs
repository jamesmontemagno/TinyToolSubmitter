using System.Diagnostics;
using System.Runtime.InteropServices;
using GitHub.Copilot.SDK;
using Spectre.Console;
using TinyToolSubmitter;

const string RecommendedModelId = "claude-haiku-4.5";

// --- Parse CLI flags ---
var flagArgs = args.Where(a => a.StartsWith("--") || a.StartsWith("-")).ToList();
var positionalArgs = args.Where(a => !a.StartsWith("-")).ToList();

var headless = flagArgs.Contains("--headless");
var verbose = flagArgs.Contains("--verbose");

string? flagReadmePath = null;
string? flagModelName = null;
string? flagCliPath = null;
string? flagThemeName = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--readme") flagReadmePath = args[i + 1];
    if (args[i] == "--model") flagModelName = args[i + 1];
    if (args[i] == "--cli-path") flagCliPath = args[i + 1];
    if (args[i] == "--theme") flagThemeName = args[i + 1];
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Tiny Tool Town — Submission Helper 🏘️📋                 ║");
Console.WriteLine("║       Powered by GitHub Copilot SDK                          ║");
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

var fullReadmeContent = await File.ReadAllTextAsync(readmePath);
var readmeContent = fullReadmeContent;
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
var websiteUrl = RepoDetector.DetectWebsiteOrDemoUrl(fullReadmeContent, githubUrl);
var repoName = githubUrl != null
    ? new Uri(githubUrl).AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? Path.GetFileName(repoDir)
    : Path.GetFileName(repoDir);

Console.ForegroundColor = ConsoleColor.DarkGray;
if (githubUrl != null) Console.WriteLine($"   GitHub URL: {githubUrl}");
if (license != null) Console.WriteLine($"   License:    {license}");
if (language != null) Console.WriteLine($"   Language:   {language}");
if (authorName != null) Console.WriteLine($"   Author:     {authorName}");
if (authorGitHub != null) Console.WriteLine($"   Username:   {authorGitHub}");
if (websiteUrl != null) Console.WriteLine($"   Website:    {websiteUrl}");
Console.ResetColor();

// --- Step 3: Use Copilot to generate metadata ---
Console.WriteLine("\n🤖 Starting Copilot to analyze your README...");

CopilotClient? client = null;
CopilotSession? session = null;

try
{
    var cliLaunch = ResolveCliLaunch(flagCliPath);

    if (verbose)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"   Copilot CLI path: {cliLaunch.DisplayPath} ({cliLaunch.Source})");
        Console.ResetColor();
    }

    if (!TryRunCopilotProcess(cliLaunch, out var versionStdOut, out var versionStdErr, out var versionExitCode, "--version"))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Failed to execute GitHub Copilot CLI at '{cliLaunch.DisplayPath}'.");
        Console.ResetColor();
        WriteCliResolutionGuidance(cliLaunch.Source);
        return 1;
    }

    if (versionExitCode != 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ GitHub Copilot CLI returned a non-zero exit code for '--version' ({versionExitCode}).");
        Console.ResetColor();
        if (!string.IsNullOrWhiteSpace(versionStdErr))
            Console.WriteLine($"   {versionStdErr}");
        return 1;
    }

    if (verbose)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"   copilot --version: {versionStdOut}");
        Console.ResetColor();

        if (TryRunCopilotProcess(cliLaunch, out var authStdOut, out var authStdErr, out var authExitCode, "auth", "status"))
        {
            var authText = !string.IsNullOrWhiteSpace(authStdOut) ? authStdOut : authStdErr;
            Console.ForegroundColor = authExitCode == 0 ? ConsoleColor.DarkGray : ConsoleColor.Yellow;
            Console.WriteLine($"   copilot auth status (exit {authExitCode}): {authText}");
            Console.ResetColor();
        }
    }

    client = new CopilotClient(new CopilotClientOptions
    {
        CliPath = cliLaunch.CliPath,
        CliArgs = cliLaunch.CliArgs
    });

    try
    {
        await client.StartAsync();
    }
    catch (Exception ex) when (IsCopilotCliMissingError(ex))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ GitHub Copilot CLI was not found at '{cliLaunch.DisplayPath}'.");
        Console.ResetColor();
        WriteCliResolutionGuidance(cliLaunch.Source);
        Console.WriteLine("   Then run this tool again.");
        return 1;
    }
    catch (Exception ex) when (IsCopilotConnectionLostError(ex))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("❌ Could not connect to GitHub Copilot CLI.");
        Console.ResetColor();
        Console.WriteLine("   The Copilot CLI process started but disconnected before initialization completed.");
        Console.WriteLine("   Try: `copilot --version` and `copilot auth status` (or `copilot auth login` if needed). ");
        Console.WriteLine("   Run with `--verbose` for startup diagnostics.");
        if (cliLaunch.Source is "argument" or "environment")
            Console.WriteLine("   Also verify the configured Copilot CLI path points to the official Copilot CLI binary.");
        return 1;
    }

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
        selectedModel = RecommendedModelId;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("   Fetching available models...");
        Console.ResetColor();

        var models = await client.ListModelsAsync();
        if (models != null && models.Count > 0)
        {
            var defaultIndex = -1;
            for (var i = 0; i < models.Count; i++)
            {
                if (models[i].Id.Equals(RecommendedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    defaultIndex = i;
                    break;
                }
            }
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
            selectedModel = RecommendedModelId;
        }
    }

    Console.WriteLine($"🤖 Using model: {selectedModel}\n");

    session = await client.CreateSessionAsync(new SessionConfig
    {
        Model = selectedModel,
        Streaming = true,
        OnPermissionRequest = PermissionHandler.ApproveAll
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
    metadata.WebsiteUrl ??= websiteUrl;
    metadata.Theme = NormalizeThemeSelection(metadata.Theme);

    if (!string.IsNullOrWhiteSpace(flagThemeName))
    {
        if (!TryParseThemeFlagValue(flagThemeName, out var parsedTheme))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Invalid theme '{flagThemeName}'.");
            Console.ResetColor();
            Console.WriteLine($"   Valid values: {string.Join(", ", GetThemeFlagValues())}");
            return 1;
        }

        metadata.Theme = parsedTheme;
    }

    // --- Step 4: Review metadata ---
    AnsiConsole.Write(new Rule("[cyan]📋 Generated Submission Metadata[/]"));
    RenderMetadataTable(metadata);

    if (!headless)
    {
        RenderThemePaletteTable();

        if (AnsiConsole.Confirm("Select a page theme now?", false))
        {
            metadata.Theme = PromptForTheme(metadata.Theme);
            AnsiConsole.MarkupLine($"[green]✅ Updated theme to {DisplayTheme(metadata.Theme)}[/]");
            RenderMetadataTable(metadata);
        }

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
                        "license",
                        "theme"
                    ]));

            if (fieldChoice == "Done")
                break;

            if (fieldChoice == "theme")
            {
                RenderThemePaletteTable();
                metadata.Theme = PromptForTheme(metadata.Theme);
                AnsiConsole.MarkupLine($"[green]✅ Updated theme to {DisplayTheme(metadata.Theme)}[/]");
                RenderMetadataTable(metadata);
                continue;
            }

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
    if (session != null)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch
        {
        }
    }

    if (client != null)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch
        {
        }
    }

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
    table.AddRow("Theme", DisplayTheme(metadata.Theme));

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
    "theme" => DisplayTheme(metadata.Theme),
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
        case "theme": metadata.Theme = NormalizeThemeSelection(value); break;
    }
}

static void RenderThemePaletteTable()
{
    var palettes = GetThemePalettes();

    var table = new Table()
        .RoundedBorder()
        .AddColumn("[yellow]Theme[/]")
        .AddColumn("[yellow]Preview[/]")
        .AddColumn("[yellow]Palette[/]");

    table.AddRow("None (site default)", "(uses Tiny Tool Town default)", "");

    foreach (var (theme, colors) in palettes)
    {
        var swatches = string.Join(" ", colors.Select(color => $"[{color}]■[/]"));
        table.AddRow(theme, swatches, string.Join(", ", colors));
    }

    AnsiConsole.Write(new Rule("[cyan]🎨 Theme Preview[/]"));
    AnsiConsole.Write(table);
}

static string? PromptForTheme(string? currentTheme)
{
    var options = GetThemeOptions();
    var defaultChoice = string.IsNullOrWhiteSpace(currentTheme)
        ? "None (site default)"
        : currentTheme;

    if (!options.Contains(defaultChoice, StringComparer.OrdinalIgnoreCase))
        defaultChoice = "None (site default)";

    var orderedOptions = options
        .OrderByDescending(option => option.Equals(defaultChoice, StringComparison.OrdinalIgnoreCase))
        .ToList();

    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[yellow]🎨 Select page theme[/]")
            .PageSize(15)
            .AddChoices(orderedOptions));

    return NormalizeThemeSelection(selected);
}

static string DisplayTheme(string? theme)
{
    return string.IsNullOrWhiteSpace(theme)
        ? "None (site default)"
        : theme;
}

static string? NormalizeThemeSelection(string? selection)
{
    if (string.IsNullOrWhiteSpace(selection))
        return null;

    var trimmed = selection.Trim();
    if (trimmed.Equals("None (site default)", StringComparison.OrdinalIgnoreCase))
        return null;

    var allowed = GetThemeOptions()
        .Where(option => !option.Equals("None (site default)", StringComparison.OrdinalIgnoreCase));

    return allowed.FirstOrDefault(option => option.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
}

static IReadOnlyList<string> GetThemeOptions()
{
    return
    [
        "None (site default)",
        "terminal",
        "neon",
        "minimal",
        "pastel",
        "matrix",
        "sunset",
        "ocean",
        "forest",
        "candy",
        "synthwave",
        "newspaper",
        "retro"
    ];
}

static IReadOnlyList<string> GetThemeFlagValues()
{
    return
    [
        "none",
        "terminal",
        "neon",
        "minimal",
        "pastel",
        "matrix",
        "sunset",
        "ocean",
        "forest",
        "candy",
        "synthwave",
        "newspaper",
        "retro"
    ];
}

static bool TryParseThemeFlagValue(string value, out string? theme)
{
    var trimmed = value.Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
    {
        theme = null;
        return false;
    }

    if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("default", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("site-default", StringComparison.OrdinalIgnoreCase))
    {
        theme = null;
        return true;
    }

    var allowed = GetThemeOptions()
        .Where(option => !option.Equals("None (site default)", StringComparison.OrdinalIgnoreCase));

    var match = allowed.FirstOrDefault(option => option.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    if (match == null)
    {
        theme = null;
        return false;
    }

    theme = match;
    return true;
}

static IReadOnlyList<(string Theme, string[] Colors)> GetThemePalettes()
{
    return
    [
        ("terminal", ["#0A0A0A", "#111111", "#00FF41", "#39FF14"]),
        ("neon", ["#0D0221", "#150535", "#FF2A6D", "#05D9E8"]),
        ("minimal", ["#FAFAFA", "#FFFFFF", "#333333", "#555555"]),
        ("pastel", ["#FEF6F9", "#FFFFFF", "#E8829A", "#82B4E8"]),
        ("matrix", ["#000800", "#001200", "#00FF00", "#00CC00"]),
        ("sunset", ["#1A0A2E", "#251244", "#FF6B35", "#FF9F1C"]),
        ("ocean", ["#0A1628", "#0F2035", "#00B4D8", "#0096C7"]),
        ("forest", ["#1A2416", "#243020", "#82B74B", "#C4A35A"]),
        ("candy", ["#FF69B4", "#FF91CB", "#FFFF00", "#00FFCC"]),
        ("synthwave", ["#1A1033", "#241546", "#FF71CE", "#01CDFE"]),
        ("newspaper", ["#F2EFE6", "#FFFDF7", "#B91C1C", "#1A1A1A"]),
        ("retro", ["#1A1200", "#2A1F00", "#FFB000", "#FF8C00"])
    ];
}

static bool IsCopilotCliMissingError(Exception ex)
{
    var current = ex;
    while (current != null)
    {
        if (current.Message.Contains("Copilot CLI not found", StringComparison.OrdinalIgnoreCase) ||
            current.Message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
            current.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
            return true;

        current = current.InnerException!;
    }

    return false;
}

static bool IsCopilotConnectionLostError(Exception ex)
{
    var current = ex;
    while (current != null)
    {
        if (current.Message.Contains("Communication error with Copilot CLI", StringComparison.OrdinalIgnoreCase) ||
            current.Message.Contains("JSON-RPC connection with the remote party was lost", StringComparison.OrdinalIgnoreCase))
            return true;

        current = current.InnerException!;
    }

    return false;
}

static bool TryRunProcess(string fileName, string arguments, out string standardOutput, out string standardError, out int exitCode)
{
    try
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            standardOutput = "";
            standardError = "";
            exitCode = -1;
            return false;
        }

        standardOutput = process.StandardOutput.ReadToEnd().Trim();
        standardError = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        exitCode = process.ExitCode;
        return true;
    }
    catch
    {
        standardOutput = "";
        standardError = "";
        exitCode = -1;
        return false;
    }
}

static bool TryRunCopilotProcess(CliLaunchConfig cliLaunch, out string standardOutput, out string standardError, out int exitCode, params string[] arguments)
{
    try
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = cliLaunch.CliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (cliLaunch.CliArgs != null)
        {
            foreach (var cliArg in cliLaunch.CliArgs)
                processStartInfo.ArgumentList.Add(cliArg);
        }

        foreach (var argument in arguments)
            processStartInfo.ArgumentList.Add(argument);

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            standardOutput = "";
            standardError = "";
            exitCode = -1;
            return false;
        }

        standardOutput = process.StandardOutput.ReadToEnd().Trim();
        standardError = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        exitCode = process.ExitCode;
        return true;
    }
    catch
    {
        standardOutput = "";
        standardError = "";
        exitCode = -1;
        return false;
    }
}

static CliLaunchConfig ResolveCliLaunch(string? explicitCliPath)
{
    if (!string.IsNullOrWhiteSpace(explicitCliPath))
        return CreateCliLaunchConfig(explicitCliPath.Trim(), "argument");

    var environmentCliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
    if (!string.IsNullOrWhiteSpace(environmentCliPath))
        return CreateCliLaunchConfig(environmentCliPath.Trim(), "environment");

    if (TryFindUserInstalledCopilotCli() is { } userInstalledCli)
        return CreateCliLaunchConfig(userInstalledCli.Path, userInstalledCli.Source);

    var bundledCli = TryFindBundledCopilotCli();
    if (!string.IsNullOrWhiteSpace(bundledCli))
        return CreateCliLaunchConfig(bundledCli, "bundled");

    return CreateCliLaunchConfig("copilot", "default");
}

static CliLaunchConfig CreateCliLaunchConfig(string discoveredPath, string source)
{
    if (OperatingSystem.IsWindows())
    {
        var extension = Path.GetExtension(discoveredPath);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var commandShell = Environment.GetEnvironmentVariable("ComSpec");
            return new CliLaunchConfig(
                string.IsNullOrWhiteSpace(commandShell) ? "cmd.exe" : commandShell,
                ["/d", "/s", "/c", $"\"{discoveredPath}\""],
                discoveredPath,
                source);
        }

        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return new CliLaunchConfig(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", discoveredPath],
                discoveredPath,
                source);
        }
    }

    return new CliLaunchConfig(discoveredPath, null, discoveredPath, source);
}

static CliCandidate? TryFindUserInstalledCopilotCli()
{
    var detectedCli = TryFindCopilotCliOnPath();
    if (!string.IsNullOrWhiteSpace(detectedCli))
        return new CliCandidate(detectedCli, "PATH");

    foreach (var candidate in GetCopilotCliInstallCandidates())
    {
        if (File.Exists(candidate.Path))
            return candidate;
    }

    return null;
}

static string? TryFindCopilotCliOnPath()
{
    if (OperatingSystem.IsWindows())
    {
        if (TryRunProcess("where", "copilot", out var whereStdOut, out _, out var whereExitCode) && whereExitCode == 0)
        {
            return SelectPreferredCliPath(whereStdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));
        }
    }
    else
    {
        if (TryRunProcess("which", "copilot", out var whichStdOut, out _, out var whichExitCode) && whichExitCode == 0)
        {
            return SelectPreferredCliPath(whichStdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));
        }
    }

    return null;
}

static IEnumerable<CliCandidate> GetCopilotCliInstallCandidates()
{
    var candidates = new List<CliCandidate>();
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var seen = new HashSet<string>(comparer);

    void AddFileCandidate(string? candidatePath, string source)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return;

        var fullPath = Path.GetFullPath(candidatePath);
        if (seen.Add(fullPath))
            candidates.Add(new CliCandidate(fullPath, source));
    }

    if (OperatingSystem.IsWindows())
    {
        void AddWindowsCommandCandidates(string? directoryPath, string source)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            AddFileCandidate(Path.Combine(directoryPath, "copilot.exe"), source);
            AddFileCandidate(Path.Combine(directoryPath, "copilot.cmd"), source);
        }

        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var npmPrefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX");
        var pnpmHome = Environment.GetEnvironmentVariable("PNPM_HOME");
        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddWindowsCommandCandidates(Path.Combine(applicationData, "npm"), "AppData npm");
        AddWindowsCommandCandidates(Path.Combine(localApplicationData, "npm"), "LocalAppData npm");
        AddWindowsCommandCandidates(npmPrefix, "NPM_CONFIG_PREFIX");
        AddWindowsCommandCandidates(pnpmHome, "PNPM_HOME");
        AddWindowsCommandCandidates(Path.Combine(localApplicationData, "pnpm"), "LocalAppData pnpm");
        AddWindowsCommandCandidates(Path.Combine(localApplicationData, "Volta", "bin"), "Volta");
        AddWindowsCommandCandidates(Path.Combine(userProfile, "scoop", "shims"), "Scoop");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
            AddFileCandidate(Path.Combine(chocolateyInstall, "bin", "copilot.exe"), "Chocolatey");
        AddFileCandidate(Path.Combine(programFiles, "GitHub Copilot CLI", "copilot.exe"), "Program Files");
        AddFileCandidate(Path.Combine(programFiles, "GitHub Copilot", "copilot.exe"), "Program Files");
        AddFileCandidate(Path.Combine(programFilesX86, "GitHub Copilot CLI", "copilot.exe"), "Program Files (x86)");
        AddFileCandidate(Path.Combine(programFilesX86, "GitHub Copilot", "copilot.exe"), "Program Files (x86)");
    }
    else
    {
        void AddUnixCommandCandidate(string? directoryPath, string source)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return;

            AddFileCandidate(Path.Combine(directoryPath, "copilot"), source);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var npmPrefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX");
        var pnpmHome = Environment.GetEnvironmentVariable("PNPM_HOME");

        AddUnixCommandCandidate(Path.Combine(userProfile, ".npm-global", "bin"), "~/.npm-global/bin");
        AddUnixCommandCandidate(Path.Combine(userProfile, ".local", "bin"), "~/.local/bin");
        AddUnixCommandCandidate(Path.Combine(userProfile, ".volta", "bin"), "~/.volta/bin");
        if (!string.IsNullOrWhiteSpace(npmPrefix))
            AddUnixCommandCandidate(Path.Combine(npmPrefix, "bin"), "NPM_CONFIG_PREFIX");
        AddUnixCommandCandidate(pnpmHome, "PNPM_HOME");
        AddUnixCommandCandidate("/usr/local/bin", "/usr/local/bin");
        AddUnixCommandCandidate("/opt/homebrew/bin", "/opt/homebrew/bin");
        AddUnixCommandCandidate("/usr/bin", "/usr/bin");
        AddUnixCommandCandidate("/snap/bin", "/snap/bin");
    }

    return candidates;
}

static string? SelectPreferredCliPath(IEnumerable<string> candidatePaths)
{
    return candidatePaths
        .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        .OrderBy(GetCliPathPriority)
        .FirstOrDefault();
}

static int GetCliPathPriority(string candidatePath)
{
    if (!OperatingSystem.IsWindows())
        return 0;

    var extension = Path.GetExtension(candidatePath);
    return extension.ToLowerInvariant() switch
    {
        ".exe" => 0,
        ".cmd" => 1,
        ".bat" => 2,
        ".ps1" => 3,
        "" => 4,
        _ => 5
    };
}

static string? TryFindBundledCopilotCli()
{
    var binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var candidateRoots = new List<string> { Path.GetFullPath(AppContext.BaseDirectory) };
    var baseDirectoryParent = Directory.GetParent(Path.GetFullPath(AppContext.BaseDirectory));
    if (baseDirectoryParent != null)
        candidateRoots.Add(baseDirectoryParent.FullName);

    foreach (var root in candidateRoots.Distinct(comparer))
    {
        foreach (var runtimeId in GetBundledRuntimeIdentifiers())
        {
            var candidatePath = Path.Combine(root, "runtimes", runtimeId, "native", binaryName);
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        var runtimesDirectory = Path.Combine(root, "runtimes");
        if (!Directory.Exists(runtimesDirectory))
            continue;

        var discoveredPath = Directory.EnumerateFiles(runtimesDirectory, binaryName, SearchOption.AllDirectories)
            .OrderBy(GetCliPathPriority)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(discoveredPath))
            return discoveredPath;
    }

    return null;
}

static IReadOnlyList<string> GetBundledRuntimeIdentifiers()
{
    var os = OperatingSystem.IsWindows()
        ? "win"
        : OperatingSystem.IsMacOS()
            ? "osx"
            : "linux";

    var primaryArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    var secondaryArchitecture = primaryArchitecture == "arm64" ? "x64" : "arm64";

    return
    [
        $"{os}-{primaryArchitecture}",
        $"{os}-{secondaryArchitecture}"
    ];
}

static void WriteCliResolutionGuidance(string source)
{
    switch (source)
    {
        case "argument":
            Console.WriteLine("   Verify the provided --cli-path value points to a valid Copilot CLI executable.");
            break;
        case "environment":
            Console.WriteLine("   Verify COPILOT_CLI_PATH points to a valid Copilot CLI executable.");
            break;
        case "bundled":
            Console.WriteLine("   Rebuild or reinstall the tool so the SDK-bundled Copilot CLI files are restored.");
            break;
        default:
            Console.WriteLine("   Install GitHub Copilot CLI, set COPILOT_CLI_PATH, or pass --cli-path <path>.");
            break;
    }
}

readonly record struct CliLaunchConfig(string CliPath, string[]? CliArgs, string DisplayPath, string Source);

readonly record struct CliCandidate(string Path, string Source);
