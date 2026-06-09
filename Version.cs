// <copyright file="Version.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Exposes the running tool version.</summary>
internal static class ThisAssembly
{
    /// <summary>Gets the assembly version as a three-part string.</summary>
    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
