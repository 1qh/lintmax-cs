// <copyright file="Gate.cs" company="PlaceholderCompany">
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
    using System.Diagnostics;

    internal static class Gate
    {
        private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "assets");

        public static int Run(bool fix)
        {
            string root = Directory.GetCurrentDirectory();
            string props = Path.Combine(AssetsDir, "inject.props");
            if (!File.Exists(props))
            {
                return Fail($"assets missing: {props}");
            }

            if (fix)
            {
                // inject analyzers+config so `dotnet format` applies analyzer code-fixes (temp Directory.Build.props)
                string tempDbp = Path.Combine(root, "Directory.Build.props");
                bool hadDbp = File.Exists(tempDbp);
                string backup = tempDbp + ".lintmaxbak";
                if (hadDbp)
                {
                    File.Move(tempDbp, backup, overwrite: true);
                }

                await File.WriteAllTextAsync(
                    tempDbp,
                    $"<Project><Import Project=\"{props}\" /></Project>"
                );
                try
                {
                    _ = Sh("dotnet", "restore");
                    _ = Sh("dotnet", "format style --severity info");
                    _ = Sh("dotnet", "format analyzers --severity info");
                    _ = Sh("csharpier", "format .");
                    _ = Sh("dprint", "fmt");
                    _ = Sh("typos", "--write-changes");
                }
                finally
                {
                    File.Delete(tempDbp);
                    if (hadDbp)
                    {
                        File.Move(backup, tempDbp, overwrite: true);
                    }
                }
            }

            // gate = child-process build with configless injection (consumer untouched)
            (int code, string? output) = Sh(
                "dotnet",
                $"build -c Release -p:CustomBeforeMicrosoftCommonProps=\"{props}\" -p:TreatWarningsAsErrors=true -warnaserror"
            );
            if (code is 0)
            {
                Console.Out.WriteLine("ok");
                return 0;
            }

            foreach (string line in output.Split('\n'))
            {
                if (line.Contains(": error ", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(line.Trim());
                }
            }

            return 1;
        }

        private static (int, string) Sh(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            try
            {
                using Process p = Process.Start(psi)!;
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, o);
            }
            catch (Exception e)
            {
                return (-1, e.Message);
            }
        }

        private static int Fail(string msg)
        {
            Console.Error.WriteLine($"lintmax-cs: {msg}");
            return 1;
        }
    }
}
