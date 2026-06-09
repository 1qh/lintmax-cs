// <copyright file="Gate.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.ComponentModel;
using System.Diagnostics;

namespace LintmaxCs;

/// <summary>Runs the configless max-strict gate against the current directory.</summary>
internal static class Gate
{
    private const string Dotnet = "dotnet";

    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>Runs the gate, optionally autofixing first.</summary>
    /// <param name="fix">When true, formats and autofixes before gating.</param>
    /// <returns>Zero when clean, one otherwise.</returns>
    internal static async Task<int> RunAsync(bool fix)
    {
        var root = Directory.GetCurrentDirectory();
        var props = Path.Combine(AssetsDir, "inject.props");
        if (!File.Exists(props))
        {
            await Console.Error.WriteLineAsync($"lintmax-cs: assets missing: {props}").ConfigureAwait(false);
            return 1;
        }

        if (fix)
        {
            await AutofixAsync(root, props).ConfigureAwait(false);
        }

        var cfgHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(
                Path.Combine(AssetsDir, "lintmax.globalconfig")).ConfigureAwait(false)));
        var treeHash = await Cache.TreeHashAsync(root, cfgHash).ConfigureAwait(false);
        if (!fix && string.Equals(await Cache.LastGreenAsync(root).ConfigureAwait(false), treeHash, StringComparison.Ordinal))
        {
            await Console.Out.WriteLineAsync("ok (cached)").ConfigureAwait(false);
            return 0;
        }

        var (code, output) = await ShAsync(
            Dotnet,
            $"build -c Release -p:CustomBeforeMicrosoftCommonProps=\"{props}\" -warnaserror").ConfigureAwait(false);
        if (code is 0)
        {
            await Cache.StoreGreenAsync(root, treeHash).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("ok").ConfigureAwait(false);
            return 0;
        }

        foreach (var line in output.Split('\n').Where(l => l.Contains(": error ", StringComparison.Ordinal)))
        {
            await Console.Error.WriteLineAsync(line.Trim()).ConfigureAwait(false);
        }

        return 1;
    }

    /// <summary>Applies safe then build-verified fixers, reverting any that break the build.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="props">Path to the injected props.</param>
    /// <returns>A task.</returns>
    private static async Task AutofixAsync(string root, string props)
    {
        var dbp = Path.Combine(root, "Directory.Build.props");
        var had = File.Exists(dbp);
        var bak = dbp + ".lintmaxbak";
        if (had)
        {
            File.Move(dbp, bak, overwrite: true);
        }

        await File.WriteAllTextAsync(dbp, $"<Project><Import Project=\"{props}\" /></Project>").ConfigureAwait(false);
        try
        {
            _ = await ShAsync(Dotnet, "restore").ConfigureAwait(false);
            _ = await ShAsync("csharpier", "format .").ConfigureAwait(false);
            _ = await ShAsync("dprint", "fmt").ConfigureAwait(false);
            _ = await ShAsync("typos", "--write-changes").ConfigureAwait(false);
            _ = await ShAsync("git", "add -A").ConfigureAwait(false);
            foreach (var fx in new[] { "format style --severity info", "format analyzers --severity info" })
            {
                _ = await ShAsync(Dotnet, fx).ConfigureAwait(false);
                var (vcode, _) = await ShAsync(Dotnet, "build -c Release").ConfigureAwait(false);
                if (vcode is 0)
                {
                    _ = await ShAsync("git", "add -A").ConfigureAwait(false);
                }
                else
                {
                    _ = await ShAsync("git", "checkout -- .").ConfigureAwait(false);
                    await Console.Error.WriteLineAsync($"lintmax-cs: '{fx}' broke the build; skipped.").ConfigureAwait(false);
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

    /// <summary>Runs a child process and captures its combined output.</summary>
    /// <param name="exe">Executable name.</param>
    /// <param name="args">Argument string.</param>
    /// <returns>The exit code and combined stdout+stderr.</returns>
    private static async Task<(int Code, string Output)> ShAsync(string exe, string args)
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
            var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await p.WaitForExitAsync().ConfigureAwait(false);
            return (p.ExitCode, stdout + stderr);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, e.ToString());
        }
    }
}
