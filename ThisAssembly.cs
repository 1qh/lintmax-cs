// <copyright file="ThisAssembly.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Exposes the running tool version.</summary>
internal static class ThisAssembly
{
    private const int VersionParts = 3;

    /// <summary>Gets the assembly version as a three-part string.</summary>
    internal static string Version =>
        typeof(ThisAssembly).Assembly.GetName().Version?.ToString(VersionParts) ?? "0.0.0";
}
