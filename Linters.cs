// <copyright file="Linters.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;

namespace LintmaxCs;

/// <summary>Runs every applicable non-C# file-type linter over the target tree.</summary>
internal static class Linters
{
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
        yield return ("editorconfig-checker", ["."]);
        yield return ("typos", fix ? [".", "--write-changes"] : ["."]);
        yield return ("dprint", [fix ? "fmt" : "check", "--config", dprintConfig]);
        var shell = ShellFiles(root);
        var conditional = new (bool Active, string Exe, string[] Args)[]
        {
            (shell.Length > 0, "shfmt", fix ? ["-w", "."] : ["-d", "."]),
            (shell.Length > 0, "shellcheck", shell),
            (
                HasFiles(root, "*.ps1"),
                "pwsh",
                ["-NoProfile", "-Command", "Invoke-ScriptAnalyzer -Path . -Recurse -EnableExit"]
            ),
            (
                HasFiles(root, "*.xaml"),
                "dotnet",
                fix ? ["xstyler", "-r", "-d", "."] : ["xstyler", "-r", "-d", ".", "--passive"]
            ),
            (Directory.Exists(Path.Combine(root, ".github", "workflows")), "actionlint", []),
        };
        foreach (var (_, exe, args) in conditional.Where(static c => c.Active))
        {
            yield return (exe, args);
        }
    }

    private static string[] ShellFiles(string root)
    {
        return
        [
            .. Directory
                .EnumerateFiles(root, "*.sh", SearchOption.AllDirectories)
                .Where(static f =>
                    !f.Contains("/obj/", StringComparison.Ordinal)
                    && !f.Contains("/bin/", StringComparison.Ordinal)
                ),
        ];
    }

    private static bool HasFiles(string root, string pattern)
    {
        return Directory
            .EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Any(static f =>
                !f.Contains("/obj/", StringComparison.Ordinal)
                && !f.Contains("/bin/", StringComparison.Ordinal)
            );
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
            var psi = new ProcessStartInfo(exe)
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
                var output =
                    await p.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false)
                    + await p.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
                await p.WaitForExitAsync(token).ConfigureAwait(false);
                return (p.ExitCode, output);
            }
            catch (Exception e)
                when (e
                        is System.ComponentModel.Win32Exception
                            or InvalidOperationException
                            or IOException
                )
            {
                return (-1, e.ToString());
            }
        }
    }
}
