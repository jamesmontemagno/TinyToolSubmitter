using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TinyToolSubmitter;

/// <summary>
/// Finds the README file in a repository and detects the GitHub remote URL.
/// </summary>
public static class RepoDetector
{
    private static readonly string[] WebsiteHintKeywords =
    [
        "demo", "try", "website", "live", "app", "preview", "play"
    ];

    private static readonly string[] ReadmeNames =
    [
        "README.md", "readme.md", "Readme.md",
        "README", "readme",
        "README.rst", "readme.rst",
        "README.txt", "readme.txt"
    ];

    /// <summary>
    /// Finds a README file in the given directory.
    /// </summary>
    public static string? FindReadme(string directory)
    {
        foreach (var name in ReadmeNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>
    /// Detects the GitHub repository URL from git remotes.
    /// Returns a URL like https://github.com/owner/repo
    /// </summary>
    public static string? DetectGitHubUrl(string directory)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote get-url origin",
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            return NormalizeGitUrl(output);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects the license file in the repo and tries to identify the license type.
    /// </summary>
    public static string? DetectLicense(string directory)
    {
        var licenseFiles = new[] { "LICENSE", "LICENSE.md", "LICENSE.txt", "LICENCE", "LICENCE.md", "LICENCE.txt" };
        foreach (var name in licenseFiles)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            var detectedFromLicenseFile = DetectLicenseFromText(content);
            if (detectedFromLicenseFile != null)
                return detectedFromLicenseFile;

            return "Unknown";
        }

        // Fallback: some projects only mention license in README.
        var readmePath = FindReadme(directory);
        if (!string.IsNullOrWhiteSpace(readmePath) && File.Exists(readmePath))
        {
            var readmeContent = File.ReadAllText(readmePath);
            var detectedFromReadme = DetectLicenseFromText(readmeContent);
            if (detectedFromReadme != null)
                return detectedFromReadme;
        }

        return null;
    }

    /// <summary>
    /// Detects a likely public website/demo URL from README markdown.
    /// </summary>
    public static string? DetectWebsiteOrDemoUrl(string readmeContent, string? githubUrl)
    {
        if (string.IsNullOrWhiteSpace(readmeContent))
            return null;

        var githubRepoUri = TryCreateUri(githubUrl);
        var candidates = new List<(string Url, int Score, int Order)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (Match match in Regex.Matches(readmeContent, @"\[(?<label>[^\]]+)\]\((?<url>https?://[^\s)]+)\)", RegexOptions.IgnoreCase))
        {
            var label = match.Groups["label"].Value;
            var rawUrl = match.Groups["url"].Value;
            if (TryBuildWebsiteCandidate(rawUrl, label, githubRepoUri, out var normalized, out var score) && seen.Add(normalized))
                candidates.Add((normalized, score + 2, order++));
        }

        foreach (Match match in Regex.Matches(readmeContent, @"https?://[^\s<>()]+", RegexOptions.IgnoreCase))
        {
            var rawUrl = match.Value;
            if (TryBuildWebsiteCandidate(rawUrl, null, githubRepoUri, out var normalized, out var score) && seen.Add(normalized))
                candidates.Add((normalized, score, order++));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Order)
            .Select(c => c.Url)
            .FirstOrDefault();
    }

    private static string? DetectLicenseFromText(string content)
    {
        if (content.Contains("MIT License", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("licensed under the mit", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("license: mit", StringComparison.OrdinalIgnoreCase))
            return "MIT";

        if (content.Contains("Apache License", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Apache-2.0", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("license: apache", StringComparison.OrdinalIgnoreCase))
            return "Apache-2.0";

        if (content.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("GPL-3.0", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("GPLv3", StringComparison.OrdinalIgnoreCase))
            return "GPL-3.0";

        if (content.Contains("GPL-2.0", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("GPLv2", StringComparison.OrdinalIgnoreCase))
            return "GPL-2.0";

        if (content.Contains("BSD 2-Clause", StringComparison.OrdinalIgnoreCase))
            return "BSD-2-Clause";

        if (content.Contains("BSD 3-Clause", StringComparison.OrdinalIgnoreCase))
            return "BSD-3-Clause";

        if (content.Contains("Mozilla Public License", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("MPL-2.0", StringComparison.OrdinalIgnoreCase))
            return "MPL-2.0";

        if (content.Contains("ISC License", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("license: isc", StringComparison.OrdinalIgnoreCase))
            return "ISC";

        if (content.Contains("The Unlicense", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("unlicense", StringComparison.OrdinalIgnoreCase))
            return "Unlicense";

        return null;
    }

    private static bool TryBuildWebsiteCandidate(
        string rawUrl,
        string? label,
        Uri? githubRepoUri,
        out string normalizedUrl,
        out int score)
    {
        normalizedUrl = "";
        score = 0;

        var cleaned = rawUrl.Trim().TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '"', '\'');
        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host.ToLowerInvariant();
        if (host is "github.com" or "raw.githubusercontent.com" or "img.shields.io")
            return false;

        var lowerPath = uri.AbsolutePath.ToLowerInvariant();
        if (lowerPath.EndsWith(".png") || lowerPath.EndsWith(".jpg") || lowerPath.EndsWith(".jpeg") ||
            lowerPath.EndsWith(".gif") || lowerPath.EndsWith(".svg") || lowerPath.EndsWith(".webp"))
            return false;

        if (githubRepoUri != null &&
            uri.Host.Equals(githubRepoUri.Host, StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith(githubRepoUri.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            return false;

        var hintText = $"{label} {uri.Host} {uri.AbsolutePath}";
        score += WebsiteHintKeywords.Count(keyword => hintText.Contains(keyword, StringComparison.OrdinalIgnoreCase)) * 3;

        if (host.Contains("vercel.app") || host.Contains("netlify.app") || host.Contains("pages.dev") || host.Contains("github.io"))
            score += 2;

        normalizedUrl = uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    private static Uri? TryCreateUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    /// <summary>
    /// Detects the primary programming language by scanning file extensions.
    /// </summary>
    public static string? DetectLanguage(string directory)
    {
        var extensionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "C#",
            [".fs"] = "F#",
            [".vb"] = "VB.NET",
            [".py"] = "Python",
            [".rs"] = "Rust",
            [".go"] = "Go",
            [".ts"] = "TypeScript",
            [".js"] = "JavaScript",
            [".java"] = "Java",
            [".kt"] = "Kotlin",
            [".swift"] = "Swift",
            [".rb"] = "Ruby",
            [".cpp"] = "C++",
            [".c"] = "C",
            [".zig"] = "Zig",
            [".lua"] = "Lua",
            [".php"] = "PHP",
            [".dart"] = "Dart",
            [".ex"] = "Elixir",
            [".exs"] = "Elixir",
            [".hs"] = "Haskell",
            [".scala"] = "Scala",
            [".r"] = "R",
            [".jl"] = "Julia",
            [".pl"] = "Perl",
            [".sh"] = "Shell",
            [".ps1"] = "PowerShell",
        };

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 5
            }))
            {
                // Skip common non-source directories
                if (file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}target{Path.DirectorySeparatorChar}"))
                    continue;

                var ext = Path.GetExtension(file);
                if (extensionMap.TryGetValue(ext, out var lang))
                {
                    counts.TryGetValue(lang, out var count);
                    counts[lang] = count + 1;
                }
            }
        }
        catch
        {
            // Ignore enumeration errors
        }

        return counts.Count > 0
            ? counts.OrderByDescending(kv => kv.Value).First().Key
            : null;
    }

    /// <summary>
    /// Detects the git user name from git config.
    /// </summary>
    public static string? DetectGitUserName(string directory)
    {
        return RunGit(directory, "config user.name");
    }

    /// <summary>
    /// Detects the GitHub username from the remote URL.
    /// </summary>
    public static string? DetectGitHubUsername(string? githubUrl)
    {
        if (string.IsNullOrEmpty(githubUrl)) return null;
        try
        {
            var uri = new Uri(githubUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            return segments.Length >= 1 ? segments[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeGitUrl(string url)
    {
        // SSH: git@github.com:owner/repo.git -> https://github.com/owner/repo
        if (url.StartsWith("git@github.com:"))
        {
            var path = url["git@github.com:".Length..].TrimEnd('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                path = path[..^4];
            return $"https://github.com/{path}";
        }

        // HTTPS: https://github.com/owner/repo.git -> https://github.com/owner/repo
        if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = url.TrimEnd('/');
            if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4];
            return trimmed;
        }

        return url;
    }

    private static string? RunGit(string directory, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
