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
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
#pragma warning disable MA0045 // reason: CancelKeyPress handler must be synchronous (event signature)
            cts.Cancel();
#pragma warning restore MA0045
        };
        var token = cts.Token;
        var cmd = args.Length > 0 ? args[0] : string.Empty;
        return cmd switch
        {
            "fix" => await Gate.RunAsync(fix: true, token).ConfigureAwait(false),
            "check" => await Gate.RunAsync(fix: false, token).ConfigureAwait(false),
            "version" => await EmitAsync(ThisAssembly.Version, token).ConfigureAwait(false),
            "rules" => await Rules.ListAsync(token).ConfigureAwait(false),
            _ => await EmitAsync(Usage, token, code: 2).ConfigureAwait(false),
        };
    }

    /// <summary>Writes text to stdout and returns the given code.</summary>
    /// <param name="text">Text to print.</param>
    /// <param name="token">Cancellation token.</param>
    /// <param name="code">Exit code to return.</param>
    /// <returns>The supplied exit code.</returns>
    private static async Task<int> EmitAsync(string text, CancellationToken token, int code = 0)
    {
        await Console.Out.WriteLineAsync(text.AsMemory(), token).ConfigureAwait(false);
        return code;
    }
}
