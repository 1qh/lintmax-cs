// <copyright file="Cache.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Security.Cryptography;
using System.Text;

namespace LintmaxCs;

/// <summary>Green-tree-hash cache: skips the gate when nothing relevant changed.</summary>
internal static class Cache
{
    private static readonly HashSet<string> TrackedExtensions = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        ".cs",
        ".csproj",
        ".xaml",
        ".json",
        ".md",
        ".toml",
        ".yml",
        ".yaml",
        ".sh",
        ".props",
        ".targets",
    };

    /// <summary>Computes a hash over hand-written source + the gate config.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="configHash">Hash of the injected globalconfig.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A hex digest of the tree state.</returns>
    internal static async Task<string> TreeHashAsync(
        string root,
        string configHash,
        CancellationToken token
    )
    {
        var files = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsTracked)
            .Order(StringComparer.Ordinal);
        var sb = new StringBuilder(configHash);
        foreach (var f in files)
        {
            var bytes = await File.ReadAllBytesAsync(f, token).ConfigureAwait(false);
            sb.Append(f).Append(Convert.ToHexString(SHA256.HashData(bytes)));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Returns the cached green hash for <paramref name="root"/>, or null.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The stored hash, or null when absent.</returns>
    internal static async Task<string?> LastGreenAsync(string root, CancellationToken token)
    {
        var path = CachePath(root);
        return File.Exists(path)
            ? (await File.ReadAllTextAsync(path, token).ConfigureAwait(false)).Trim()
            : null;
    }

    /// <summary>Records <paramref name="hash"/> as the green state for <paramref name="root"/>.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="hash">The green tree hash.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    internal static async Task StoreGreenAsync(string root, string hash, CancellationToken token)
    {
        var path = CachePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, hash, token).ConfigureAwait(false);
    }

    private static bool IsTracked(string f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var excluded =
            f.Contains("/obj/", StringComparison.Ordinal)
            || f.Contains("/bin/", StringComparison.Ordinal)
            || f.Contains("/.git/", StringComparison.Ordinal);
        return !excluded && TrackedExtensions.Contains(Path.GetExtension(f));
    }

    private static string CachePath(string root)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        return Path.Combine(home, ".cache", "lintmax-cs", key + ".green");
    }
}
