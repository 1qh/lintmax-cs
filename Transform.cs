// <copyright file="Transform.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace LintmaxCs;

/// <summary>Strips non-survivor comments from C# source (survivor set kept verbatim).</summary>
internal static class Transform
{
    /// <summary>Strips every C# file under <paramref name="root"/>; returns count changed.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Number of files rewritten.</returns>
    internal static async Task<int> StripAsync(string root, CancellationToken token)
    {
        var changed = 0;
        foreach (var file in CsFiles(root))
        {
            var original = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
            var stripped = await StripTextAsync(original, token).ConfigureAwait(false);
            if (!string.Equals(original, stripped, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(file, stripped, token).ConfigureAwait(false);
                changed++;
            }
        }

        return changed;
    }

    /// <summary>Lists files that still hold strippable comments.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The offending file paths.</returns>
    internal static async Task<IReadOnlyList<string>> OffendersAsync(
        string root,
        CancellationToken token
    )
    {
        var offenders = new List<string>();
        foreach (var file in CsFiles(root))
        {
            var original = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
            if (
                !string.Equals(
                    original,
                    await StripTextAsync(original, token).ConfigureAwait(false),
                    StringComparison.Ordinal
                )
            )
            {
                offenders.Add(file);
            }
        }

        return offenders;
    }

    private static IEnumerable<string> CsFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static f =>
                !f.Contains("/obj/", StringComparison.Ordinal)
                && !f.Contains("/bin/", StringComparison.Ordinal)
                && !f.EndsWith(".g.cs", StringComparison.Ordinal)
                && !f.EndsWith(".g.i.cs", StringComparison.Ordinal)
            );
    }

    private static Task<string> StripTextAsync(string text, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CoreAsync();

        async Task<string> CoreAsync()
        {
            var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: token);
            var root = await tree.GetRootAsync(token).ConfigureAwait(false);
            var kill = new List<TextSpan>();
            foreach (var trivia in root.DescendantTrivia())
            {
                var kind = trivia.Kind();
                var strippable =
                    kind is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia;
                if (strippable && !IsSurvivor(in trivia))
                {
                    kill.Add(trivia.FullSpan);
                }
            }

            if (kill.Count is 0)
            {
                return text;
            }

            var sb = new System.Text.StringBuilder(text.Length);
            var pos = 0;
            foreach (var span in kill.OrderBy(static s => s.Start))
            {
                sb.Append(text, pos, span.Start - pos);
                pos = span.End;
            }

            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }
    }

    private static bool IsSurvivor(in SyntaxTrivia trivia)
    {
        var t = trivia.ToString();
        string[] markers = ["reason:", "<copyright", "Licensed under", "</copyright>", "SPDX"];
        return t.StartsWith("///", StringComparison.Ordinal)
            || t.StartsWith("/**", StringComparison.Ordinal)
            || Array.Exists(markers, m => t.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}
