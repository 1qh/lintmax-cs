// <copyright file="Evolve.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;

namespace LintmaxCs;

/// <summary>Self-evolving behaviors: CI detection, self-update, dependency-staleness advisory.</summary>
internal static class Evolve
{
    private const string SkipStaleness = "LINTMAX_SKIP_STALENESS";

    /// <summary>Gets a value indicating whether the tool runs under CI.</summary>
    internal static bool IsCi => Flag("CI") || Flag("GITHUB_ACTIONS");

    /// <summary>Gets a value indicating whether the green-cache is bypassed.</summary>
    internal static bool NoCache => Flag("LINTMAX_NO_CACHE");

    /// <summary>Gets a value indicating whether per-phase timing prints.</summary>
    internal static bool Timing => Flag("LINTMAX_TIMING");

    /// <summary>Updates the tool itself to latest under CI; no-op locally.</summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    internal static async Task SelfUpdateAsync(CancellationToken token)
    {
        if (!IsCi)
        {
            return;
        }

        _ = await ShAsync("dotnet", "tool update -g lintmax-cs", token).ConfigureAwait(false);
    }

    /// <summary>Prints an advisory when analyzer packages lag latest; never fails the gate.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    internal static async Task StalenessAdvisoryAsync(string root, CancellationToken token)
    {
        if (Flag(SkipStaleness))
        {
            return;
        }

        var (_, listed) = await ShAsync(
                "dotnet",
                $"list \"{root}\" package --outdated --highest-minor",
                token
            )
            .ConfigureAwait(false);
        var lines = listed
            .Split('\n')
            .Where(static l =>
                l.Contains("> ", StringComparison.Ordinal)
                && l.Contains("Analyzer", StringComparison.OrdinalIgnoreCase)
            );
        foreach (var line in lines)
        {
            await Console
                .Error.WriteLineAsync($"stale: {line.Trim()}".AsMemory(), token)
                .ConfigureAwait(false);
        }
    }

    private static bool Flag(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrEmpty(value) && !string.Equals(value, "0", StringComparison.Ordinal);
    }

    private static Task<(int Code, string Output)> ShAsync(
        string exe,
        string args,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(exe);
        return CoreAsync();

        async Task<(int Code, string Output)> CoreAsync()
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            try
            {
                using var p = Process.Start(psi)!;
                var output = await p.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
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
