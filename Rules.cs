// <copyright file="Rules.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

/*
 * <Your-Product-Name>
 * Copyright (c) <Year-From>-<Year-To> <Your-Company-Name>
 *
 * Please configure this header in your SonarCloud/SonarQube quality profile.
 * You can also set it in SonarLint.xml additional file for SonarLint or standalone NuGet analyzer.
 */

using System.Globalization;

namespace LintmaxCs
{
    internal static class Rules
    {
        /// <summary>
        /// enumerate active rules from the injected globalconfig (the SSOT of what we enforce)
        /// </summary>
        /// <returns></returns>
        public static int List()
        {
            string gc = Path.Combine(AppContext.BaseDirectory, "assets", "lintmax.globalconfig");
            if (!File.Exists(gc))
            {
                Console.Error.WriteLine("lintmax-cs: globalconfig missing");
                return 1;
            }

            int enabled = 0;
            foreach (string l in File.ReadLines(gc))
            {
                string t = l.Trim();
                if (
                    t.StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)
                    && t.Contains("severity = error", StringComparison.Ordinal)
                )
                {
                    Console.Out.WriteLine(t.Split('.')[1]);
                    enabled++;
                }
            }

            Console.Error.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{enabled} explicitly-forced rules + every enabled-by-default rule across all packs (-> error)"
                )
            );
            return 0;
        }
    }
}
