// <copyright file="Gate.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;

namespace LintmaxCs;

/// <summary>Runs the configless max-strict gate against the current directory.</summary>
internal static class Gate
{
    private const string Dotnet = "dotnet";

    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>Runs the gate, optionally autofixing first.</summary>
    /// <param name="fix">When true, formats and autofixes before gating.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Zero when clean, one otherwise.</returns>
    internal static async Task<int> RunAsync(bool fix, CancellationToken token)
    {
        await Evolve.SelfUpdateAsync(token).ConfigureAwait(false);
        var root = Directory.GetCurrentDirectory();
        var props = Path.Combine(AssetsDir, "inject.props");
        if (!File.Exists(props))
        {
            await Console
                .Error.WriteLineAsync($"lintmax-cs: assets missing: {props}".AsMemory(), token)
                .ConfigureAwait(false);
            return 1;
        }

        if (fix)
        {
            await AutofixAsync(root, props, token).ConfigureAwait(false);
        }

        var (treeHash, hit) = await CacheStateAsync(root, fix, token).ConfigureAwait(false);
        if (hit)
        {
            if (!await StalenessGateAsync(root, token).ConfigureAwait(false))
            {
                return 1;
            }

            await Console.Out.WriteLineAsync("ok (cached)".AsMemory(), token).ConfigureAwait(false);
            return 0;
        }

        var watch = Evolve.Timing ? Stopwatch.StartNew() : null;
        var passed = await RunLintersAsync(root, props, token).ConfigureAwait(false);
        if (watch is not null)
        {
            await Console
                .Error.WriteLineAsync(
                    $"timing: {watch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}ms".AsMemory(),
                    token
                )
                .ConfigureAwait(false);
        }

        if (passed)
        {
            await File.WriteAllTextAsync(Cache.GreenPath(root), treeHash, token).ConfigureAwait(false);
            if (!await StalenessGateAsync(root, token).ConfigureAwait(false))
            {
                return 1;
            }

            await Console.Out.WriteLineAsync("ok".AsMemory(), token).ConfigureAwait(false);
            return 0;
        }

        return 1;
    }

    /// <summary>Fails the gate when any direct dependency lags latest (Latest-only enforcement).</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when every dependency is current; false (with stderr detail) when any is stale.</returns>
    private static async Task<bool> StalenessGateAsync(string root, CancellationToken token)
    {
        var stale = await Evolve.StalenessAsync(root, token).ConfigureAwait(false);
        if (stale.Count is 0)
        {
            return true;
        }

        foreach (var s in stale)
        {
            await Console.Error.WriteLineAsync($"stale: {s}".AsMemory(), token).ConfigureAwait(false);
        }

        var count = stale.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Console
            .Error.WriteLineAsync(
                $"{count} direct dep(s) behind latest; bump to latest or set LINTMAX_SKIP_STALENESS=1".AsMemory(),
                token
            )
            .ConfigureAwait(false);
        return false;
    }

    private static async Task<(string TreeHash, bool Hit)> CacheStateAsync(
        string root,
        bool fix,
        CancellationToken token
    )
    {
        var cfgBytes = await File.ReadAllBytesAsync(
                Path.Combine(AssetsDir, "lintmax.globalconfig"),
                token
            )
            .ConfigureAwait(false);
        var cfgHash =
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cfgBytes))
            + ThisAssembly.Version;
        var treeHash = await Cache.TreeHashAsync(root, cfgHash, token).ConfigureAwait(false);
        var hit =
            !fix
            && !Evolve.NoCache
            && string.Equals(
                await Cache.LastGreenAsync(root, token).ConfigureAwait(false),
                treeHash,
                StringComparison.Ordinal
            );
        return (treeHash, hit);
    }

    /// <summary>Runs the C# gate plus the file-type linters, printing any findings.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="props">Path to the injected props.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when every linter passes.</returns>
    private static async Task<(int Code, bool NoDeprecated)> CSharpGateAsync(
        string root,
        string props,
        CancellationToken token
    )
    {
        var hasProject = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Any(static f =>
                (
                    f.EndsWith(".csproj", StringComparison.Ordinal)
                    || f.EndsWith(".sln", StringComparison.Ordinal)
                ) && !PathUtil.IsExcluded(f)
            );
        if (!hasProject)
        {
            return (0, true);
        }

        var (buildCode, buildOutput) = await ShAsync(
                Dotnet,
                $"build -c Release -p:CustomBeforeMicrosoftCommonProps=\"{props}\" -warnaserror",
                token
            )
            .ConfigureAwait(false);
        foreach (
            var line in buildOutput
                .Split('\n')
                .Where(static l => l.Contains(": error ", StringComparison.Ordinal))
        )
        {
            await Console.Error.WriteLineAsync(line.Trim().AsMemory(), token).ConfigureAwait(false);
        }

        var (_, deprecatedOut) = await ShAsync(
                Dotnet,
                "list package --deprecated --format json",
                token
            )
            .ConfigureAwait(false);
        var noDeprecated = !deprecatedOut.Contains(
            "\"deprecationReasons\"",
            StringComparison.Ordinal
        );
        if (!noDeprecated)
        {
            await Console
                .Error.WriteLineAsync("deprecated NuGet package(s) present".AsMemory(), token)
                .ConfigureAwait(false);
        }

        return (buildCode, noDeprecated);
    }

    private static async Task<bool> RunLintersAsync(
        string root,
        string props,
        CancellationToken token
    )
    {
        var cfg = Path.Combine(AssetsDir, "dprint.json");
        await Dprint.BumpAsync(cfg, token).ConfigureAwait(false);
        var (code, noDeprecated) = await CSharpGateAsync(root, props, token).ConfigureAwait(false);

        var fileTypesOk = await Linters.CheckAsync(root, cfg, token).ConfigureAwait(false);
        var offenders = await Transform.OffendersAsync(root, token).ConfigureAwait(false);
        var badXml = await Transform.MalformedXmlAsync(root, token).ConfigureAwait(false);
        foreach (var f in badXml)
        {
            await Console
                .Error.WriteLineAsync($"{f}: malformed XML".AsMemory(), token)
                .ConfigureAwait(false);
        }
        foreach (var f in offenders)
        {
            await Console
                .Error.WriteLineAsync($"{f}: strippable comment (run fix)".AsMemory(), token)
                .ConfigureAwait(false);
        }

        var buildClean = code is 0 && fileTypesOk && noDeprecated;
        return buildClean && offenders.Count is 0 && badXml.Count is 0;
    }

    /// <summary>Applies safe then build-verified fixers, reverting any that break the build.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="props">Path to the injected props.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    private static async Task AutofixAsync(string root, string props, CancellationToken token)
    {
        var dbp = Path.Combine(root, "Directory.Build.props");
        var had = File.Exists(dbp);
        var bak = dbp + ".lintmaxbak";
        if (had)
        {
            File.Move(dbp, bak, overwrite: true);
        }

        await File.WriteAllTextAsync(
                dbp,
                $"<Project><Import Project=\"{props}\" /></Project>",
                token
            )
            .ConfigureAwait(false);
        try
        {
            _ = await Transform.StripAsync(root, token).ConfigureAwait(false);
            _ = await ShAsync(Dotnet, "restore", token).ConfigureAwait(false);
            _ = await ShAsync("csharpier", "format .", token).ConfigureAwait(false);
            var dprintCfg = Path.Combine(AssetsDir, "dprint.json");
            await Dprint.BumpAsync(dprintCfg, token).ConfigureAwait(false);
            await Linters.FixAsync(root, dprintCfg, token).ConfigureAwait(false);
            _ = await ShAsync("git", "add -A", token).ConfigureAwait(false);
            foreach (
                var fx in new[]
                {
                    "format style --severity info",
                    "format analyzers --severity info",
                }
            )
            {
                _ = await ShAsync(Dotnet, fx, token).ConfigureAwait(false);
                if (
                    (await ShAsync(Dotnet, "build -c Release", token).ConfigureAwait(false)).Code
                    is 0
                )
                {
                    _ = await ShAsync("git", "add -A", token).ConfigureAwait(false);
                }
                else
                {
                    _ = await ShAsync("git", "checkout -- .", token).ConfigureAwait(false);
                    await Console
                        .Error.WriteLineAsync(
                            $"lintmax-cs: '{fx}' broke the build; skipped.".AsMemory(),
                            token
                        )
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            File.Delete(dbp);
            if (had)
            {
                File.Move(bak, dbp, overwrite: true);
            }
        }
    }

    private static async Task<(int Code, string Output)> ShAsync(
        string exe,
        string args,
        CancellationToken token
    )
    {
        var psi = new ProcessStartInfo(Proc.ResolveExe(exe), args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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
