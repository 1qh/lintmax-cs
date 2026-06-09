// <copyright file="Rules.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs;

/// <summary>Lists the active rule set from the injected globalconfig.</summary>
internal static class Rules
{
    /// <summary>Prints each explicitly-forced rule id.</summary>
    /// <returns>Process exit code.</returns>
    internal static async Task<int> ListAsync()
    {
        var gc = Path.Combine(AppContext.BaseDirectory, "assets", "lintmax.globalconfig");
        if (!File.Exists(gc))
        {
            await Console
                .Error.WriteLineAsync("lintmax-cs: globalconfig missing")
                .ConfigureAwait(false);
            return 1;
        }

        var enabled = 0;
        foreach (var line in await File.ReadAllLinesAsync(gc).ConfigureAwait(false))
        {
            var t = line.Trim();
            if (
                t.StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)
                && t.EndsWith("severity = error", StringComparison.Ordinal)
            )
            {
                await Console.Out.WriteLineAsync(t.Split('.')[1]).ConfigureAwait(false);
                enabled++;
            }
        }

        await Console
            .Error.WriteLineAsync(
                $"{enabled.ToString(System.Globalization.CultureInfo.InvariantCulture)} forced rules + every enabled-by-default rule (-> error)"
            )
            .ConfigureAwait(false);
        return 0;
    }
}
