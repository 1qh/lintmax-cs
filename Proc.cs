// <copyright file="Proc.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Resolves bare tool names to a launchable path across platforms.</summary>
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
}
