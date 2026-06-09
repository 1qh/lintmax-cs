using System.Diagnostics;

namespace LintmaxCs;

/// <summary>Runs the configless max-strict gate against the current directory.</summary>
internal static class Gate
{
    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>Runs the gate; when <paramref name="fix"/> is true, autofixes first.</summary>
    public static int Run(bool fix)
    {
        var root = Directory.GetCurrentDirectory();
        var props = Path.Combine(AssetsDir, "inject.props");
        if (!File.Exists(props))
        {
            return Fail($"assets missing: {props}");
        }

        if (fix)
        {
            Autofix(root, props);
        }

        var (code, output) = Sh(
            "dotnet",
            $"build -c Release -p:CustomBeforeMicrosoftCommonProps=\"{props}\" -warnaserror"
        );
        if (code == 0)
        {
            Console.Out.WriteLine("ok");
            return 0;
        }

        foreach (var line in output.Split('\n'))
        {
            if (line.Contains(": error ", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(line.Trim());
            }
        }

        return 1;
    }

    private static void Autofix(string root, string props)
    {
        var dbp = Path.Combine(root, "Directory.Build.props");
        var had = File.Exists(dbp);
        var bak = dbp + ".lintmaxbak";
        if (had)
        {
            File.Move(dbp, bak, overwrite: true);
        }

        File.WriteAllText(dbp, $"<Project><Import Project=\"{props}\" /></Project>");
        try
        {
            _ = Sh("dotnet", "restore");
            // safe text/layout fixers, then stage so later reverts preserve them
            _ = Sh("csharpier", "format .");
            _ = Sh("dprint", "fmt");
            _ = Sh("typos", "--write-changes");
            _ = Sh("git", "add -A");
            // analyzer code-fixes: keep if build stays green, else revert just this fixer to last-good
            foreach (
                var fx in new[]
                {
                    "format style --severity info",
                    "format analyzers --severity info",
                }
            )
            {
                _ = Sh("dotnet", fx);
                if (Sh("dotnet", "build -c Release").Code == 0)
                {
                    _ = Sh("git", "add -A");
                }
                else
                {
                    _ = Sh("git", "checkout -- .");
                    Console.Error.WriteLine(
                        $"lintmax-cs: '{fx}' broke the build; skipped (hand-fix)."
                    );
                }
            }
        }
        finally
        {
            File.Delete(dbp);
            if (had)
            {
                File.Move(bak, dbp, overwrite: true);
            }
        }
    }

    private static (int Code, string Output) Sh(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
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
