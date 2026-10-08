// <copyright file="VersionControl.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Restricts filesystem candidates to tracked and untracked-but-not-ignored files.</summary>
internal static class VersionControl
{
    /// <summary>Walks a directory, respecting the enclosing checkout's standard Git exclusions.</summary>
    /// <param name="root">Target directory, which may be below the checkout root.</param>
    /// <param name="pattern">Filesystem search pattern.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Existing candidate paths; outside a checkout every file is eligible.</returns>
    internal static async Task<IEnumerable<string>> FilesAsync(
        string root,
        string pattern,
        CancellationToken token
    )
    {
        root = Path.GetFullPath(root);
        var candidates = await CandidatesAsync(root, token).ConfigureAwait(false);
        return Directory
            .EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => candidates?.Contains(Path.GetFullPath(f)) is not false);
    }

    /// <summary>Reads the version-control set, or returns null outside a checkout.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Absolute candidate paths, or null when no checkout encloses the directory.</returns>
    internal static async Task<HashSet<string>?> CandidatesAsync(string root, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        var directory = new DirectoryInfo(root);
        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return await ReadCandidatesAsync(root, token).ConfigureAwait(false);
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Copies eligible files for tools whose directory or project traversal cannot be restricted.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="files">Optional prefiltered candidate paths.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A temporary directory that the caller must delete.</returns>
    internal static async Task<string> MirrorAsync(
        string root,
        IEnumerable<string>? files,
        CancellationToken token
    )
    {
        var mirror = Path.Combine(
            Path.GetTempPath(),
            "lintmax-cs-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)
        );
        Directory.CreateDirectory(mirror);
        try
        {
            var candidates = files ?? await FilesAsync(root, "*", token).ConfigureAwait(false);
            foreach (var file in candidates.Where(static f => !PathUtil.IsExcluded(f)))
            {
                var target = Path.Combine(mirror, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            return mirror;
        }
        catch
        {
            Directory.Delete(mirror, recursive: true);
            throw;
        }
    }

    /// <summary>Reads eligible paths from Git without mixing diagnostics into filenames.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Absolute eligible paths.</returns>
    /// <exception cref="IOException">Git cannot enumerate the checkout.</exception>
    private static async Task<HashSet<string>> ReadCandidatesAsync(string root, CancellationToken token)
    {
        var (code, output, error) = await Proc.RunSeparatedAsync(
            "git",
            ["-C", root, "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
            root,
            token
        ).ConfigureAwait(false);
        if (code is not 0)
        {
            throw new IOException($"lintmax-cs: Git file enumeration failed in '{root}': {error.Trim()}");
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return new HashSet<string>(
            output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => Path.GetFullPath(Path.Combine(root, f))),
            comparer
        );
    }
}
