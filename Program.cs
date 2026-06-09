// <copyright file="Program.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Entry point: dispatches the four lintmax-cs commands.</summary>
internal static class Program
{
    private const string Usage = """
        lintmax-cs — maximum-strictness C#/.NET + XAML quality gate (OSS, always-latest, all-rules-on)
        usage:
          lintmax-cs fix       format + autofix + full gate (default)
          lintmax-cs check     verify only, read-only (CI)
          lintmax-cs version   print version
          lintmax-cs rules     list every active rule under the maxed config
        ok on success (exit 0); verbose only on failure.
        """;

    /// <summary>Dispatches on the first argument.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code.</returns>
    private static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0] : string.Empty;
        return cmd switch
        {
            "fix" => Gate.Run(fix: true),
            "check" => Gate.Run(fix: false),
            "version" => Emit(ThisAssembly.Version),
            "rules" => Rules.List(),
            _ => Emit(Usage, code: 2),
        };
    }

    /// <summary>Writes <paramref name="text"/> to stdout and returns <paramref name="code"/>.</summary>
    /// <param name="text">Text to print.</param>
    /// <param name="code">Exit code to return.</param>
    /// <returns>The supplied exit code.</returns>
    private static int Emit(string text, int code = 0)
    {
        Console.Out.WriteLine(text);
        return code;
    }
}
