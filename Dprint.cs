// <copyright file="Dprint.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LintmaxCs;

/// <summary>
/// Never-stale dprint plugin resolution: rewrites the bundled config's pinned plugin URLs to each
/// plugin's current latest, so the shipped versions are only a bootstrap seed. Parity with
/// lintmax-rs. A floating URL is deliberately not used: dprint caches a plugin by URL, so a
/// constant URL would resolve once and freeze forever.
/// </summary>
internal static class Dprint
{
    private const string Host = "https://plugins.dprint.dev/";
    private const int BumpTtlHours = 6;

    private static readonly TimeSpan BumpTtl = TimeSpan.FromHours(BumpTtlHours);

    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>Rewrites every pinned plugin URL in the bundled config to its latest release.</summary>
    /// <param name="configPath">Path to the bundled dprint config.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    internal static async Task BumpAsync(string configPath, CancellationToken token)
    {
        if (!File.Exists(configPath) || await IsFreshAsync(configPath, token).ConfigureAwait(false))
        {
            return;
        }

        var original = await File.ReadAllTextAsync(configPath, token).ConfigureAwait(false);
        var lines = original.ReplaceLineEndings("\n").Split('\n');
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var rewritten = await RewriteAsync(lines[i], token).ConfigureAwait(false);
            if (!string.Equals(rewritten, lines[i], StringComparison.Ordinal))
            {
                lines[i] = rewritten;
                changed = true;
            }
        }

        if (changed)
        {
            await File.WriteAllTextAsync(configPath, string.Join('\n', lines), token)
                .ConfigureAwait(false);
        }

        await StampAsync(configPath, token).ConfigureAwait(false);
    }

    private static async Task<string> RewriteAsync(string line, CancellationToken token)
    {
        var inner = line.Trim().Trim('"', ',');
        if (!inner.StartsWith(Host, StringComparison.Ordinal))
        {
            return line;
        }

        var path = PluginPath(inner);
        if (path is null)
        {
            return line;
        }

        var latest = await LatestUrlAsync(path, token).ConfigureAwait(false);
        return latest is null ? line : line.Replace(inner, latest, StringComparison.Ordinal);
    }

    private static string? PluginPath(string pinned)
    {
        var tail = pinned[Host.Length..];
        var dot = tail.LastIndexOf('.');
        if (dot < 0)
        {
            return null;
        }

        var file = tail[..dot];
        var slash = file.IndexOf('/', StringComparison.Ordinal);
        return slash < 0
            ? "dprint/" + PluginName(file)
            : string.Concat(file.AsSpan(0, slash + 1), PluginName(file[(slash + 1)..]));
    }

    private static string PluginName(string file)
    {
        var cut = file.LastIndexOfAny(['-', '@']);
        if (cut < 0)
        {
            return file;
        }
        var tail = file[(cut + 1)..].TrimStart('v');
        var parts = tail.Split('.');
        var versioned = parts.Length is 3 && Array.TrueForAll(parts, IsNumber);
        return versioned ? file[..cut] : file;
    }

    private static bool IsNumber(string part) => part.Length > 0 && !part.AsSpan().ContainsAnyExceptInRange('0', '9');

    private static async Task<string?> LatestUrlAsync(string path, CancellationToken token)
    {
        try
        {
            var body = await Http.GetStringAsync(new Uri(Host + path + "/latest.json"), token)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var url = doc.RootElement.TryGetProperty("url", out var value)
                ? value.GetString()
                : null;
            return url?.StartsWith(Host, StringComparison.Ordinal) is true ? url : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StampPath(string configPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configPath)))[..16];
        return Path.Combine(home, ".cache", "lintmax-cs", key + ".dprint");
    }

    private static async Task<bool> IsFreshAsync(string configPath, CancellationToken token)
    {
        if (Evolve.IsCi)
        {
            return false;
        }

        var path = StampPath(configPath);
        if (!File.Exists(path))
        {
            return false;
        }

        var text = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        return long.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var ticks)
            && TimeProvider.System.GetUtcNow() - new DateTimeOffset(ticks, TimeSpan.Zero) <= BumpTtl;
    }

    private static Task StampAsync(string configPath, CancellationToken token)
    {
        var path = StampPath(configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(
            path,
            TimeProvider.System.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture),
            token
        );
    }
}
