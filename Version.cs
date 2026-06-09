// <copyright file="Version.cs" company="PlaceholderCompany">
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
    internal static class ThisAssembly
    {
        public static string Version =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.0.0";
    }
}
