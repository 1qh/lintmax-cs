// <copyright file="Evolve.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LintmaxCs;

/// <summary>Self-evolving behaviors: CI detection, self-update, gating dependency-staleness scan.</summary>
internal static class Evolve
{
    private const string SkipStaleness = "LINTMAX_SKIP_STALENESS";
    private const string StalenessAllow = "LINTMAX_STALENESS_ALLOW";
    private const int StalenessTtlHours = 6;
    private const int StaleCacheHeaderLines = 2;

    private static readonly TimeSpan StalenessTtl = TimeSpan.FromHours(StalenessTtlHours);

    private static readonly HashSet<string> ManifestExtensions = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        ".csproj",
        ".props",
    };

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

    /// <summary>
    /// Returns every DIRECT dependency that lags its latest stable release, as "id current -&gt; latest".
    /// Empty when all deps are current. Gates the build (Latest-only; parity with lintmax-go's staleness scan).
    /// Honors the LINTMAX_SKIP_STALENESS variable. Result is TTL-cached locally so upstream is re-queried at
    /// most every few hours; CI always re-scans.
    /// </summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Stale direct-dependency descriptions; empty when all current.</returns>
    internal static async Task<IReadOnlyList<string>> StalenessAsync(
        string root,
        CancellationToken token
    )
    {
        if (Flag(SkipStaleness))
        {
            return [];
        }

        var manifestKey = ManifestKey(root);
        if (!IsCi)
        {
            var cached = await ReadStaleCacheAsync(root, manifestKey, token).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        // reason: scan PER-PROJECT not `dotnet list <dir>` (dir form self-restores + reads-stale, giving 0 projects/false-stale); prerelease excluded so only latest STABLE gates.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stale = new List<string>();
        foreach (var proj in ProjectFiles(root))
        {
            var (_, json) = await ShAsync(
                    "dotnet",
                    $"list \"{proj}\" package --outdated --format json",
                    token
                )
                .ConfigureAwait(false);
            stale.AddRange(ParseStale(json).Where(seen.Add));
        }

        var cacheBody = StaleCacheBody(root, manifestKey, stale);
        await File.WriteAllTextAsync(StaleCachePath(root), cacheBody, token).ConfigureAwait(false);
        return stale;
    }

    private static IEnumerable<string> ProjectFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(static f => !PathUtil.IsExcluded(f))
            .Order(StringComparer.Ordinal);

    // reason: CA1859 wants a concrete List for perf but CA1002 forbids returning List<T>; keep the IReadOnlyList abstraction.
#pragma warning disable CA1859
    private static IReadOnlyList<string> ParseStale(string json)
#pragma warning restore CA1859
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{'))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("projects", out var projects))
        {
            return [];
        }

        var allowed = new HashSet<string>(
            (Environment.GetEnvironmentVariable(StalenessAllow) ?? string.Empty).Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            ),
            StringComparer.OrdinalIgnoreCase
        );
        var stale = new List<string>();
        foreach (var pkg in TopLevelPackages(projects))
        {
            if (allowed.Contains(Str(pkg, "id")))
            {
                continue;
            }

            var entry = StaleEntry(pkg);
            if (entry is not null && !stale.Contains(entry, StringComparer.Ordinal))
            {
                stale.Add(entry);
            }
        }

        return stale;
    }

    // reason: IDISP004 false-positives on JsonElement.EnumerateArray() in foreach — the struct ArrayEnumerator is disposed by the foreach.
#pragma warning disable IDISP004
    private static IEnumerable<JsonElement> TopLevelPackages(JsonElement projects)
    {
        foreach (var project in projects.EnumerateArray())
        {
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                continue;
            }

            foreach (var pkg in FrameworkPackages(frameworks))
            {
                yield return pkg;
            }
        }
    }

    private static IEnumerable<JsonElement> FrameworkPackages(JsonElement frameworks)
    {
        foreach (var fw in frameworks.EnumerateArray())
        {
            if (!fw.TryGetProperty("topLevelPackages", out var pkgs))
            {
                continue;
            }

            foreach (var pkg in pkgs.EnumerateArray())
            {
                yield return pkg;
            }
        }
    }
#pragma warning restore IDISP004

    private static string? StaleEntry(JsonElement pkg)
    {
        var id = Str(pkg, "id");
        var resolved = Str(pkg, "resolvedVersion");
        var latest = Str(pkg, "latestVersion");
        return latest.Length > 0
            && resolved.Length > 0
            && !string.Equals(latest, resolved, StringComparison.Ordinal)
            ? $"{id} {resolved} -> {latest}"
            : null;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static string ManifestKey(string root)
    {
        var sb = new StringBuilder();
        foreach (
            var f in Directory
                .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(static f =>
                    (
                        ManifestExtensions.Contains(Path.GetExtension(f))
                        || string.Equals(
                            Path.GetFileName(f),
                            "global.json",
                            StringComparison.Ordinal
                        )
                    ) && !PathUtil.IsExcluded(f)
                )
                .Order(StringComparer.Ordinal)
        )
        {
            sb.Append(f).Append(File.GetLastWriteTimeUtc(f).Ticks.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static string StaleCachePath(string root)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        return Path.Combine(home, ".cache", "lintmax-cs", key + ".staleness");
    }

    private static async Task<IReadOnlyList<string>?> ReadStaleCacheAsync(
        string root,
        string manifestKey,
        CancellationToken token
    )
    {
        var path = StaleCachePath(root);
        if (!File.Exists(path))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(path, token).ConfigureAwait(false);
        return IsCacheFresh(lines, manifestKey)
            ? [.. lines.Skip(StaleCacheHeaderLines).Where(static l => l.Length > 0)]
            : null;
    }

    private static bool IsCacheFresh(string[] lines, string manifestKey) =>
        lines.Length >= StaleCacheHeaderLines
        && string.Equals(lines[0], manifestKey, StringComparison.Ordinal)
        && long.TryParse(lines[1], CultureInfo.InvariantCulture, out var stampTicks)
        && TimeProvider.System.GetUtcNow() - new DateTimeOffset(stampTicks, TimeSpan.Zero)
            <= StalenessTtl;

    private static string StaleCacheBody(string root, string manifestKey, IReadOnlyList<string> stale)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StaleCachePath(root))!);
        var body = new StringBuilder()
            .AppendLine(manifestKey)
            .AppendLine(
                TimeProvider.System.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture)
            );
        foreach (var s in stale)
        {
            body.AppendLine(s);
        }

        return body.ToString();
    }

    private static bool Flag(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrEmpty(value) && !string.Equals(value, "0", StringComparison.Ordinal);
    }

    private static async Task<(int Code, string Output)> ShAsync(
        string exe,
        string args,
        CancellationToken token
    )
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

            // reason: drain stderr concurrently even though only stdout is used — an undrained redirected stderr deadlocks the child once its pipe buffer fills.
            var outTask = p.StandardOutput.ReadToEndAsync(token);
            var errTask = p.StandardError.ReadToEndAsync(token);
            var stdout = await outTask.ConfigureAwait(false);
            _ = await errTask.ConfigureAwait(false);
            await p.WaitForExitAsync(token).ConfigureAwait(false);
            return (p.ExitCode, stdout);
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
