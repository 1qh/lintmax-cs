// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.

// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

/*
 * <Your-Product-Name>
 * Copyright (c) <Year-From>-<Year-To> <Your-Company-Name>
 *
 * Please configure this header in your SonarCloud/SonarQube quality profile.
 * You can also set it in SonarLint.xml additional file for SonarLint or standalone NuGet analyzer.
 */

namespace LintmaxCs
{
    internal static class Program
    {
        private const string Usage = """
            lintmax-cs — maximum-strictness C#/.NET + XAML quality gate (OSS, always-latest, all-rules-on)
            usage:
              lintmax-cs fix       format + autofix + full gate (default)
              lintmax-cs check     verify only, read-only (CI), green-tree-hash cached
              lintmax-cs version   print version
              lintmax-cs rules     list every active rule under the maxed config
            ok on success (exit 0); verbose only on failure. Self-evolving: analyzer @latest, green-cache, staleness.
            """;

        private static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : string.Empty;
            return cmd switch
            {
                "fix" => Gate.Run(fix: true),
                "check" => Gate.Run(fix: false),
                "version" => Emit(ThisAssembly.Version),
                "rules" => Rules.List(),
                _ => Emit(Usage, code: 2),
            };
        }

        private static int Emit(string text, int code = 0)
        {
            Console.Out.WriteLine(text);
            return code;
        }
    }
}
