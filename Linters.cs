// <copyright file="Linters.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Text;
using System.Text.RegularExpressions;

namespace LintmaxCs;

/// <summary>Runs every applicable non-C# file-type linter over the target tree.</summary>
internal static class Linters
{
    private const string ConfigFlag = "--config";
    private const string AssetsFolder = "assets";

    private static readonly string[] ShellCheckFlags =
    [
        "--enable=all",
        "--severity=style",
    ];
    private static readonly string[] HadolintFlags =
    [
        "--strict-labels",
        "--failure-threshold",
        "style",
    ];
    private static readonly string[] WebExt =
    [
        ".js",
        ".mjs",
        ".cjs",
        ".jsx",
        ".ts",
        ".tsx",
        ".css",
        ".scss",
        ".html",
        ".graphql",
    ];
    private static readonly string[] DprintExtensions =
    [
        ".json",
        ".jsonc",
        ".toml",
        ".md",
        ".markdown",
        ".yml",
        ".yaml",
        ".css",
        ".scss",
        ".sass",
        ".less",
        ".html",
        ".htm",
        ".vue",
        ".svelte",
        ".astro",
        ".graphql",
        ".gql",
        ".sql",
    ];

    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, AssetsFolder);

    private static string GitleaksConfig =>
        Path.Combine(AppContext.BaseDirectory, AssetsFolder, "gitleaks.toml");

    private static string MarkdownConfig =>
        Path.Combine(AppContext.BaseDirectory, AssetsFolder, ".markdownlint.jsonc");
    private static string YamlConfig =>
        Path.Combine(AppContext.BaseDirectory, AssetsFolder, ".yamllint.yaml");

    /// <summary>Runs the read-only file-type gate; prints findings; returns true when all pass.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="dprintConfig">Path to the bundled dprint config.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when every applicable linter passes.</returns>
    internal static async Task<bool> CheckAsync(string root, string dprintConfig, CancellationToken token)
    {
        var files = await FilesAsync(root, token).ConfigureAwait(false);
        var mirror = await VersionControl.MirrorAsync(root, files, token).ConfigureAwait(false);
        try
        {
            var tasks = new List<Task<(int Code, string Output)>>();
            foreach (var (exe, args) in Applicable(mirror, dprintConfig, fix: false))
            {
                tasks.Add(Proc.RunAsync(exe, args, mirror, token));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var ok = true;
            foreach (var (code, output) in results)
            {
                if (code is not 0)
                {
                    ok = false;
                    await Console.Error.WriteLineAsync(
                        output.Replace(mirror, root, StringComparison.Ordinal).Trim().AsMemory(), token
                    ).ConfigureAwait(false);
                }
            }

            return ok;
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    /// <summary>Runs the write-mode formatters for every applicable file type.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="dprintConfig">Path to the bundled dprint config.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    internal static async Task FixAsync(string root, string dprintConfig, CancellationToken token)
    {
        var files = (await FilesAsync(root, token).ConfigureAwait(false)).ToArray();
        var mirror = await VersionControl.MirrorAsync(root, files, token).ConfigureAwait(false);
        try
        {
            foreach (var (exe, args) in Applicable(mirror, dprintConfig, fix: true))
            {
                _ = await Proc.RunAsync(exe, args, mirror, token).ConfigureAwait(false);
            }

            foreach (var file in files)
            {
                var source = Path.Combine(mirror, Path.GetRelativePath(root, file));
                if (File.Exists(source))
                {
                    File.Copy(source, file, overwrite: true);
                }
            }
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    private static IEnumerable<(string Exe, string[] Args)> Applicable(
        string root,
        string dprintConfig,
        bool fix
    )
    {
        var ignored = Ignored(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !ignored(f)).ToArray();
        if (files.Length is 0)
        {
            yield break;
        }

        // reason: gitleaks walks directories; root is a mirror containing only eligible files.
        yield return ("gitleaks", ["dir", "--max-archive-depth", "100", ConfigFlag, GitleaksConfig, root]);
        yield return ("editorconfig-checker", files);
        yield return ("typos", fix ? ["--write-changes", .. files] : files);
        var formatted = files.Where(IsDprintFile).ToArray();
        if (formatted.Length > 0)
        {
            yield return (
                "dprint",
                [fix ? "fmt" : "check", "--allow-no-files", ConfigFlag, dprintConfig, .. formatted]
            );
        }

        foreach (var tool in FileTypeTools(root, files, fix))
        {
            yield return tool;
        }
    }

    /// <summary>Passes the repository's actionlint config explicitly, since a mirror without .git cannot locate it.</summary>
    /// <param name="root">Directory the workflows are listed from.</param>
    /// <param name="workflows">Workflow files to check.</param>
    /// <returns>The actionlint arguments.</returns>
    private static string[] ActionlintArgs(string root, string[] workflows)
    {
        var config = Path.Combine(root, ".github", "actionlint.yaml");
        return File.Exists(config) ? ["-config-file", config, .. workflows] : workflows;
    }

    private static IEnumerable<(string Exe, string[] Args)> FileTypeTools(
        string root,
        string[] files,
        bool fix
    )
    {
        var web = files
            .Where(static f => WebExt.Contains(Path.GetExtension(f), StringComparer.Ordinal))
            .ToArray();
        var markdown = WithExtension(files, ".md");
        var yaml = files
            .Where(static f => string.Equals(Path.GetExtension(f), ".yml", StringComparison.Ordinal)
                || string.Equals(Path.GetExtension(f), ".yaml", StringComparison.Ordinal)
            )
            .ToArray();
        var workflows = WorkflowFiles(root, yaml);
        var toml = WithExtension(files, ".toml");
        var docker = files
            .Where(static f => Path.GetFileName(f).StartsWith("Dockerfile", StringComparison.Ordinal))
            .ToArray();
        var conditional = ScriptTools(files, fix).Concat(new (bool Active, string Exe, string[] Args)[]
        {
            (workflows.Length > 0, "actionlint", ActionlintArgs(root, workflows)),
            (
                markdown.Length > 0,
                "markdownlint-cli2",
                fix
                    ? [ConfigFlag, MarkdownConfig, "--fix", .. markdown]
                    : [ConfigFlag, MarkdownConfig, .. markdown]
            ),
            (yaml.Length > 0, "yamllint", ["-c", YamlConfig, "--strict", .. yaml]),
            (toml.Length > 0, "taplo", ["lint", .. toml]),
            (docker.Length > 0, "hadolint", [.. HadolintFlags, .. docker]),
            (
                web.Length > 0,
                "biome",
                fix
                    ? ["check", "--write", "--config-path", AssetsDir, .. web]
                    : ["check", "--config-path", AssetsDir, .. web]
            ),
        });
        foreach (var (_, exe, args) in conditional.Where(static c => c.Active))
        {
            yield return (exe, args);
        }
    }

    private static (bool Active, string Exe, string[] Args)[] ScriptTools(string[] files, bool fix)
    {
        var shell = WithExtension(files, ".sh");
        var xaml = WithExtension(files, ".xaml");
        var powershell = WithExtension(files, ".ps1");
        return
        [
            (
                shell.Length > 0,
                "shfmt",
                [fix ? "-w" : "-d", "-s", "-ci", "-bn", "-sr", "-i", "2", .. shell]
            ),
            (shell.Length > 0, "shellcheck", [.. ShellCheckFlags, .. shell]),
            (powershell.Length > 0, "pwsh", PowerShellArgs(powershell)),
            (xaml.Length > 0, "xstyler", fix ? ["-f", .. xaml] : ["-f", .. xaml, "--passive"]),
        ];
    }

    private static string[] WorkflowFiles(string root, IEnumerable<string> yaml) =>
        [.. yaml.Where(f => Path.GetRelativePath(root, f)
            .Replace('\\', '/')
            .StartsWith(".github/workflows/", StringComparison.Ordinal)
        )];

    private static string[] PowerShellArgs(string[] powershell) =>
        [
            "-NoProfile",
            "-Command",
            "$findings = @("
                + string.Join("; ", powershell.Select(static f =>
                    "Invoke-ScriptAnalyzer -Path '"
                    + f.Replace("'", "''", StringComparison.Ordinal)
                    + "' -Severity Error,Warning,Information"
                ))
                + "); $findings; if ($findings.Count -gt 0) { exit 1 }",
        ];

    private static string[] WithExtension(IEnumerable<string> files, string extension) =>
        [.. files.Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.Ordinal))];

    private static bool IsDprintFile(string file) =>
        DprintExtensions.Contains(Path.GetExtension(file), StringComparer.Ordinal)
        || Path.GetFileName(file).StartsWith("Dockerfile", StringComparison.Ordinal);

    private static async Task<IEnumerable<string>> FilesAsync(string root, CancellationToken token)
    {
        var ignored = Ignored(root);
        var files = await VersionControl.FilesAsync(root, "*", token).ConfigureAwait(false);
        return files.Where(f => !ignored(f));
    }

    /// <summary>Builds a predicate skipping generated/VCS paths plus the consumer's .lintmaxignore globs.</summary>
    /// <param name="root">Target directory.</param>
    /// <returns>A predicate true for paths to skip.</returns>
    private static Func<string, bool> Ignored(string root)
    {
        var globs = IgnoreGlobs(root);
        if (globs.Count is 0)
        {
            return PathUtil.IsExcluded;
        }

        var regexes = globs
            .Select(static g => new Regex(
                GlobToRegex(g),
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)
            ))
            .ToArray();
        return f =>
        {
            if (PathUtil.IsExcluded(f))
            {
                return true;
            }

            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            return Array.Exists(regexes, r => r.IsMatch(rel));
        };
    }

    /// <summary>Translates a gitignore-style glob (supporting * and **) to an anchored regex.</summary>
    /// <param name="glob">The glob pattern.</param>
    /// <returns>A regex string matching the path or anything beneath it.</returns>
    private static string GlobToRegex(string glob)
    {
        const string RegexSpecial = @"\.()+|^$[]{}";
        const int DoubleStarLength = 2;
        var sb = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            var c = glob[i];
            if (c is '*' && i + 1 < glob.Length && glob[i + 1] is '*')
            {
                _ = sb.Append(".*");
                i += DoubleStarLength;
                if (i < glob.Length && glob[i] is '/')
                {
                    i++;
                }
            }
            else if (c is '*')
            {
                _ = sb.Append("[^/]*");
                i++;
            }
            else if (c is '?')
            {
                _ = sb.Append('.');
                i++;
            }
            else
            {
                if (RegexSpecial.Contains(c, StringComparison.Ordinal))
                {
                    _ = sb.Append('\\');
                }

                _ = sb.Append(c);
                i++;
            }
        }

        return sb.Append("(/.*)?$").ToString();
    }

    /// <summary>Reads consumer generated-path excludes from .lintmaxignore (one glob per line).</summary>
    /// <param name="root">Target directory.</param>
    /// <returns>The exclude globs (empty when absent).</returns>
    private static IReadOnlyList<string> IgnoreGlobs(string root)
    {
        var file = Path.Combine(root, ".lintmaxignore");
        return !File.Exists(file)
            ? []
            :
            [
                .. File.ReadLines(file)
                    .Select(static l => l.Trim())
                    .Where(static l => l.Length > 0 && !l.StartsWith('#')),
            ];
    }
}
