// <copyright file="Linters.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
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
        "--external-sources",
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
    internal static Task<bool> CheckAsync(string root, string dprintConfig, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(root);
        return CoreAsync();

        async Task<bool> CoreAsync()
        {
            var tasks = Applicable(root, dprintConfig, fix: false)
                .Select(job => ShAsync(job.Exe, job.Args, root, token));
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var ok = true;
            foreach (var (code, output) in results)
            {
                if (code is not 0)
                {
                    ok = false;
                    await Console
                        .Error.WriteLineAsync(output.Trim().AsMemory(), token)
                        .ConfigureAwait(false);
                }
            }

            return ok;
        }
    }

    /// <summary>Runs the write-mode formatters for every applicable file type.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="dprintConfig">Path to the bundled dprint config.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    internal static Task FixAsync(string root, string dprintConfig, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(root);
        return CoreAsync();

        async Task CoreAsync()
        {
            foreach (var (exe, args) in Applicable(root, dprintConfig, fix: true))
            {
                _ = await ShAsync(exe, args, root, token).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<(string Exe, string[] Args)> Applicable(
        string root,
        string dprintConfig,
        bool fix
    )
    {
        var ignore = IgnoreGlobs(root);
        yield return ("editorconfig-checker", EditorconfigArgs(ignore));
        yield return ("gitleaks", GitleaksArgs());
        yield return ("typos", TyposArgs(ignore, fix));
        yield return ("dprint", DprintArgs(dprintConfig, ignore, fix));
        var shell = ShellFiles(root);
        var xaml = XamlFiles(root);
        var web = WebFileList(root);
        var conditional = new (bool Active, string Exe, string[] Args)[]
        {
            (
                shell.Length > 0,
                "shfmt",
                fix
                    ? ["-w", "-s", "-ci", "-bn", "-sr", "-i", "2", "."]
                    : ["-d", "-s", "-ci", "-bn", "-sr", "-i", "2", "."]
            ),
            (shell.Length > 0, "shellcheck", [.. ShellCheckFlags, .. shell]),
            (
                HasFiles(root, "*.ps1"),
                "pwsh",
                [
                    "-NoProfile",
                    "-Command",
                    "Invoke-ScriptAnalyzer -Path . -Recurse -EnableExit -Severity Error,Warning,Information",
                ]
            ),
            (
                xaml.Length > 0,
                "xstyler",
                fix ? ["-f", .. xaml] : ["-f", .. xaml, "--passive"]
            ),
            (Directory.Exists(Path.Combine(root, ".github", "workflows")), "actionlint", []),
            (
                HasFiles(root, "*.md"),
                "markdownlint-cli2",
                fix
                    ? [ConfigFlag, MarkdownConfig, "--fix", "**/*.md", .. NegationGlobs(ignore)]
                    : [ConfigFlag, MarkdownConfig, "**/*.md", .. NegationGlobs(ignore)]
            ),
            (
                HasFiles(root, "*.yml") || HasFiles(root, "*.yaml"),
                "yamllint",
                ["-c", YamlConfig, "--strict", .. YamlFiles(root)]
            ),
            (HasFiles(root, "*.toml"), "taplo", ["lint"]),
            (DockerFiles(root).Length > 0, "hadolint", [.. HadolintFlags, .. DockerFiles(root)]),
            (
                web.Length > 0,
                "biome",
                fix
                    ? ["check", "--write", "--config-path", AssetsDir, .. web]
                    : ["check", "--config-path", AssetsDir, .. web]
            ),
        };
        foreach (var (_, exe, args) in conditional.Where(static c => c.Active))
        {
            yield return (exe, args);
        }
    }

    private static string[] WebFileList(string root) =>
        [
            .. Files(root)
                .Where(static f => WebExt.Contains(Path.GetExtension(f), StringComparer.Ordinal)),
        ];

    private static string[] DockerFiles(string root)
    {
        return
        [
            .. Files(root)
                .Where(static f =>
                    Path.GetFileName(f).StartsWith("Dockerfile", StringComparison.Ordinal)
                ),
        ];
    }

    private static IEnumerable<string> Files(string root)
    {
        var ignored = Ignored(root);
        return Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => !ignored(f));
    }

    private static string[] ShellFiles(string root)
    {
        var ignored = Ignored(root);
        return
        [
            .. Directory
                .EnumerateFiles(root, "*.sh", SearchOption.AllDirectories)
                .Where(f => !ignored(f)),
        ];
    }

    private static string[] YamlFiles(string root)
    {
        var ignored = Ignored(root);
        return
        [
            .. Directory
                .EnumerateFiles(root, "*.yml", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
                .Where(f => !ignored(f)),
        ];
    }

    private static string[] GitleaksArgs() =>
        ["dir", "--max-archive-depth", "100", ConfigFlag, GitleaksConfig, "."];

    private static string[] XamlFiles(string root)
    {
        var ignored = Ignored(root);
        return
        [
            .. Directory
                .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !ignored(f)),
        ];
    }

    private static string[] DprintArgs(
        string dprintConfig,
        IReadOnlyCollection<string> ignore,
        bool fix
    )
    {
        var args = new List<string>
        {
            fix ? "fmt" : "check",
            "--allow-no-files",
            ConfigFlag,
            dprintConfig,
        };

        // reason: dprint --excludes is variadic — repeating the flag errors "cannot be used multiple times"; pass it once with all globs.
        if (ignore.Count > 0)
        {
            args.Add("--excludes");
            args.AddRange(ignore);
        }

        return [.. args];
    }

    private static string[] TyposArgs(IEnumerable<string> ignore, bool fix)
    {
        var args = new List<string> { "." };
        if (fix)
        {
            args.Add("--write-changes");
        }

        foreach (var glob in ignore)
        {
            args.Add("--exclude");
            args.Add(glob);
        }

        return [.. args];
    }

    private static string[] EditorconfigArgs(IReadOnlyCollection<string> ignore)
    {
        return ignore.Count is 0
            ? ["."]
            : ["-exclude", string.Join('|', ignore.Select(GlobToRegex)), "."];
    }

    private static string[] NegationGlobs(IEnumerable<string> ignore) =>
        [.. ignore.Select(static g => "!" + g)];

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

    private static bool HasFiles(string root, string pattern)
    {
        var ignored = Ignored(root);
        return Directory
            .EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Any(f => !ignored(f));
    }

    private static Task<(int Code, string Output)> ShAsync(
        string exe,
        string[] args,
        string cwd,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(exe);
        ArgumentNullException.ThrowIfNull(args);
        return CoreAsync();

        async Task<(int Code, string Output)> CoreAsync()
        {
            var psi = new ProcessStartInfo(Proc.ResolveExe(exe))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = cwd,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            try
            {
                using var p = Process.Start(psi)!;

                // reason: drain stdout+stderr concurrently — a sequential ReadToEnd deadlocks when the child fills the second pipe's buffer before the first stream closes.
                var outTask = p.StandardOutput.ReadToEndAsync(token);
                var errTask = p.StandardError.ReadToEndAsync(token);
                var stdout = await outTask.ConfigureAwait(false);
                var stderr = await errTask.ConfigureAwait(false);
                await p.WaitForExitAsync(token).ConfigureAwait(false);
                return (p.ExitCode, stdout + stderr);
            }
            catch (Exception e)
                when (e
                        is System.ComponentModel.Win32Exception
                            or InvalidOperationException
                            or IOException
                )
            {
                return (-1, $"lintmax-cs: required tool '{exe}' not found or failed: {e.Message}");
            }
        }
    }
}
