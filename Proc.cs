// <copyright file="Proc.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
using System.Text;

namespace LintmaxCs;

/// <summary>Resolves and runs tools across platforms.</summary>
internal static class Proc
{
    /// <summary>Resolves <paramref name="exe"/> against PATH, honoring Windows PATHEXT (.cmd/.bat/.exe).</summary>
    /// <param name="exe">Bare executable name or path.</param>
    /// <returns>A full path when found; otherwise the original name.</returns>
    internal static string ResolveExe(string exe)
    {
        ArgumentNullException.ThrowIfNull(exe);
        if (Path.IsPathRooted(exe) || exe.Contains('/', StringComparison.Ordinal))
        {
            return exe;
        }

        var pathExt = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
            )
            : [string.Empty];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries
        );
        foreach (var dir in dirs)
        {
            foreach (var ext in pathExt)
            {
                var candidate = Path.Combine(dir, exe + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return exe;
    }

    /// <summary>Runs a tool and combines its output streams.</summary>
    /// <param name="exe">Executable name or path.</param>
    /// <param name="args">Tool arguments.</param>
    /// <param name="cwd">Working directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The exit code and combined output.</returns>
    internal static async Task<(int Code, string Output)> RunAsync(
        string exe,
        string[] args,
        string cwd,
        CancellationToken token
    )
    {
        var (code, stdout, stderr) = await RunSeparatedAsync(exe, args, cwd, token).ConfigureAwait(false);
        return (code, stdout + stderr);
    }

    /// <summary>Runs a tool while keeping its output streams separate for parsing.</summary>
    /// <param name="exe">Executable name or path.</param>
    /// <param name="args">Tool arguments.</param>
    /// <param name="cwd">Working directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The exit code, standard output and standard error.</returns>
    internal static async Task<(int Code, string Stdout, string Stderr)> RunSeparatedAsync(
        string exe,
        string[] args,
        string cwd,
        CancellationToken token
    )
    {
        var psi = new ProcessStartInfo(ResolveExe(exe))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
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
            return (p.ExitCode, stdout, stderr);
        }
        catch (Exception e)
            when (e
                    is System.ComponentModel.Win32Exception
                        or InvalidOperationException
                        or IOException
            )
        {
            return (-1, string.Empty, $"lintmax-cs: required tool '{exe}' not found or failed: {e.Message}");
        }
    }
}
