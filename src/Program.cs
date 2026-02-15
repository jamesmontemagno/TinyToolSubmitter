using System.Diagnostics;
using GitHub.Copilot.SDK;
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
        Console.WriteLine($"📄 Found README: {Path.GetFileName(detected)}");
        Console.ResetColor();
        readmePath = detected;
    }
    else if (!headless)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("📄 No README found. Enter path to README: ");
        Console.ResetColor();
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input) || !File.Exists(input))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ Invalid path. Exiting.");
            Console.ResetColor();
            return 1;
        }
        readmePath = Path.GetFullPath(input);
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

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n🤖 Select a model:");
            Console.ResetColor();

            for (int i = 0; i < models.Count; i++)
            {
                var isDefault = i == defaultIndex ? " (default)" : "";
                Console.WriteLine($"   {i + 1}. {models[i].Name}{isDefault}");
            }

            Console.Write($"\nEnter choice (1-{models.Count}) [default: {defaultIndex + 1}]: ");
            var modelChoice = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(modelChoice) || !int.TryParse(modelChoice, out var mIdx) || mIdx < 1 || mIdx > models.Count)
                mIdx = defaultIndex + 1;

            selectedModel = models[mIdx - 1].Id;
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

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("   Analyzing README with AI...");
    Console.ResetColor();

    var metadata = await MetadataGenerator.GenerateAsync(session, readmeContent, repoName);

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
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n═══════════════════════════════════════════════");
    Console.WriteLine("  📋 Generated Submission Metadata");
    Console.WriteLine("═══════════════════════════════════════════════");
    Console.ResetColor();

    DisplayField("Tool Name", metadata.Name);
    DisplayField("Tagline", metadata.Tagline);
    DisplayField("Description", metadata.Description);
    DisplayField("GitHub URL", metadata.GitHubUrl);
    DisplayField("Website", metadata.WebsiteUrl ?? "(none)");
    DisplayField("Author", metadata.Author);
    DisplayField("GitHub User", metadata.AuthorGitHub);
    DisplayField("Tags", metadata.Tags);
    DisplayField("Language", metadata.Language ?? "(not detected)");
    DisplayField("License", metadata.License ?? "(not detected)");

    Console.WriteLine();

    if (!headless)
    {
        // Allow editing
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("✏️  Would you like to edit any fields before submitting?");
        Console.ResetColor();
        Console.WriteLine("   Press Enter to accept, or type a field name to edit");
        Console.WriteLine("   (name, tagline, description, github_url, website, author, author_github, tags, language, license)");
        Console.WriteLine();

        while (true)
        {
            Console.Write("   Edit field (or Enter to continue): ");
            var field = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(field)) break;

            Console.Write("   New value: ");
            var newValue = Console.ReadLine()?.Trim() ?? "";

            switch (field)
            {
                case "name": metadata.Name = newValue; break;
                case "tagline": metadata.Tagline = newValue; break;
                case "description": metadata.Description = newValue; break;
                case "github_url": metadata.GitHubUrl = newValue; break;
                case "website": metadata.WebsiteUrl = newValue; break;
                case "author": metadata.Author = newValue; break;
                case "author_github": metadata.AuthorGitHub = newValue; break;
                case "tags": metadata.Tags = newValue; break;
                case "language": metadata.Language = newValue; break;
                case "license": metadata.License = newValue; break;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"   Unknown field: {field}");
                    Console.ResetColor();
                    continue;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   ✅ Updated {field}");
            Console.ResetColor();
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

static void DisplayField(string label, string value)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write($"   {label,-14} ");
    Console.ResetColor();
    Console.WriteLine(value);
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
