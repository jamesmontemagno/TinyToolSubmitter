using System.Diagnostics;

namespace TinyToolSubmitter;

/// <summary>
/// Finds the README file in a repository and detects the GitHub remote URL.
/// </summary>
public static class RepoDetector
{
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
            if (content.Contains("MIT License", StringComparison.OrdinalIgnoreCase))
                return "MIT";
            if (content.Contains("Apache License", StringComparison.OrdinalIgnoreCase))
                return "Apache-2.0";
            if (content.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.OrdinalIgnoreCase))
            {
                if (content.Contains("Version 3", StringComparison.OrdinalIgnoreCase))
                    return "GPL-3.0";
                if (content.Contains("Version 2", StringComparison.OrdinalIgnoreCase))
                    return "GPL-2.0";
            }
            if (content.Contains("BSD 2-Clause", StringComparison.OrdinalIgnoreCase))
                return "BSD-2-Clause";
            if (content.Contains("BSD 3-Clause", StringComparison.OrdinalIgnoreCase))
                return "BSD-3-Clause";
            if (content.Contains("Mozilla Public License", StringComparison.OrdinalIgnoreCase))
                return "MPL-2.0";
            if (content.Contains("ISC License", StringComparison.OrdinalIgnoreCase))
                return "ISC";
            if (content.Contains("The Unlicense", StringComparison.OrdinalIgnoreCase))
                return "Unlicense";

            return "Unknown";
        }
        return null;
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
