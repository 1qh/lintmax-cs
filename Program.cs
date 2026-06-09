// <copyright file="Program.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Entry point: dispatches the four lintmax-cs commands.</summary>
internal static class Program
{
    private const string Usage =
        "lintmax-cs — max-strictness C#/.NET + XAML gate (OSS, all-rules-on)\n"
        + "usage: lintmax-cs fix|check|version|rules\n"
        + "ok on success (exit 0); verbose only on failure.";

    /// <summary>Dispatches on the first argument.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var cmd = (args.Length > 0) ? args[0] : string.Empty;
        return cmd switch
        {
            "fix" => await Gate.RunAsync(fix: true).ConfigureAwait(false),
            "check" => await Gate.RunAsync(fix: false).ConfigureAwait(false),
            "version" => await EmitAsync(ThisAssembly.Version).ConfigureAwait(false),
            "rules" => await Rules.ListAsync().ConfigureAwait(false),
            _ => await EmitAsync(Usage, code: 2).ConfigureAwait(false),
        };
    }

    /// <summary>Writes text to stdout and returns the given code.</summary>
    /// <param name="text">Text to print.</param>
    /// <param name="code">Exit code to return.</param>
    /// <returns>The supplied exit code.</returns>
    private static async Task<int> EmitAsync(string text, int code = 0)
    {
        await Console.Out.WriteLineAsync(text).ConfigureAwait(false);
        return code;
    }
}
