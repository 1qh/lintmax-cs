// <copyright file="PathUtil.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Cross-platform path predicates (separator-agnostic).</summary>
internal static class PathUtil
{
    /// <summary>Gets a value indicating whether the path lies under obj, bin, or .git.</summary>
    /// <param name="path">A file path using either separator.</param>
    /// <returns>True when the path is a generated/VCS location to skip.</returns>
    internal static bool IsExcluded(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var n = path.Replace('\\', '/');
        return n.Contains("/obj/", StringComparison.Ordinal)
            || n.Contains("/bin/", StringComparison.Ordinal)
            || n.Contains("/.git/", StringComparison.Ordinal)
            || n.Contains("/node_modules/", StringComparison.Ordinal);
    }
}
