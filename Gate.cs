// <copyright file="Gate.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

using System.Diagnostics;

namespace LintmaxCs;

/// <summary>Runs the configless max-strict gate against the current directory.</summary>
internal static class Gate
{
    private const string Dotnet = "dotnet";

    private static readonly string[] CSharpierExtensions =
    [
        ".cs",
        ".csx",
        ".csproj",
        ".props",
        ".targets",
        ".xml",
        ".config",
        ".xaml",
        ".resx",
    ];

    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>Runs the gate, optionally autofixing first.</summary>
    /// <param name="fix">When true, formats and autofixes before gating.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Zero when clean, one otherwise.</returns>
    internal static async Task<int> RunAsync(bool fix, CancellationToken token)
    {
        await Evolve.SelfUpdateAsync(token).ConfigureAwait(false);
        var root = Directory.GetCurrentDirectory();
        var props = Path.Combine(AssetsDir, "inject.props");
        if (!File.Exists(props))
        {
            await Console
                .Error.WriteLineAsync($"lintmax-cs: assets missing: {props}".AsMemory(), token)
                .ConfigureAwait(false);
            return 1;
        }

        if (fix)
        {
            await AutofixAsync(root, props, token).ConfigureAwait(false);
        }

        var (treeHash, hit) = await CacheStateAsync(root, fix, token).ConfigureAwait(false);
        if (hit)
        {
            if (!await StalenessGateAsync(root, token).ConfigureAwait(false))
            {
                return 1;
            }

            await Console.Out.WriteLineAsync("ok (cached)".AsMemory(), token).ConfigureAwait(false);
            return 0;
        }

        var watch = Evolve.Timing ? Stopwatch.StartNew() : null;
        var passed = await RunLintersAsync(root, props, token).ConfigureAwait(false);
        if (watch is not null)
        {
            await Console
                .Error.WriteLineAsync(
                    $"timing: {watch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}ms".AsMemory(),
                    token
                )
                .ConfigureAwait(false);
        }

        if (passed)
        {
            await File.WriteAllTextAsync(Cache.GreenPath(root), treeHash, token).ConfigureAwait(false);
            if (!await StalenessGateAsync(root, token).ConfigureAwait(false))
            {
                return 1;
            }

            await Console.Out.WriteLineAsync("ok".AsMemory(), token).ConfigureAwait(false);
            return 0;
        }

        return 1;
    }

    /// <summary>Fails the gate when any direct dependency lags latest (Latest-only enforcement).</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when every dependency is current; false (with stderr detail) when any is stale.</returns>
    private static async Task<bool> StalenessGateAsync(string root, CancellationToken token)
    {
        var stale = await Evolve.StalenessAsync(root, token).ConfigureAwait(false);
        if (stale.Count is 0)
        {
            return true;
        }

        foreach (var s in stale)
        {
            await Console.Error.WriteLineAsync($"stale: {s}".AsMemory(), token).ConfigureAwait(false);
        }

        var count = stale.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Console
            .Error.WriteLineAsync(
                $"{count} direct dep(s) behind latest; bump to latest or set LINTMAX_SKIP_STALENESS=1".AsMemory(),
                token
            )
            .ConfigureAwait(false);
        return false;
    }

    private static async Task<(string TreeHash, bool Hit)> CacheStateAsync(
        string root,
        bool fix,
        CancellationToken token
    )
    {
        var cfgBytes = await File.ReadAllBytesAsync(
                Path.Combine(AssetsDir, "lintmax.globalconfig"),
                token
            )
            .ConfigureAwait(false);
        var cfgHash =
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cfgBytes))
            + ThisAssembly.Version;
        var treeHash = await Cache.TreeHashAsync(root, cfgHash, token).ConfigureAwait(false);
        var hit =
            !fix
            && !Evolve.NoCache
            && string.Equals(
                await Cache.LastGreenAsync(root, token).ConfigureAwait(false),
                treeHash,
                StringComparison.Ordinal
            );
        return (treeHash, hit);
    }

    /// <summary>Runs the C# gate plus the file-type linters, printing any findings.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="props">Path to the injected props.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when every linter passes.</returns>
    private static async Task<(int Code, bool NoDeprecated)> CSharpGateAsync(
        string root,
        string props,
        CancellationToken token
    )
    {
        var projects = await ProjectFilesAsync(root, token).ConfigureAwait(false);
        if (projects.Length is 0)
        {
            return (0, true);
        }

        // reason: even an explicit SDK project expands Compile globs; a filtered mirror keeps ignored source out of the build.
        var mirror = await VersionControl.MirrorAsync(root, files: null, token).ConfigureAwait(false);
        try
        {
            var code = 0;
            var noDeprecated = true;
            foreach (var project in projects)
            {
                var target = Path.Combine(mirror, Path.GetRelativePath(root, project));
                var (buildCode, buildOutput) = await Proc.RunAsync(
                    Dotnet,
                    ["build", target, "-c", "Release", $"-p:CustomBeforeMicrosoftCommonProps={props}", "-warnaserror"],
                    mirror,
                    token
                ).ConfigureAwait(false);
                if (buildCode is not 0)
                {
                    code = buildCode;
                    await Console.Error.WriteLineAsync(
                        buildOutput.Replace(mirror, root, StringComparison.Ordinal).Trim().AsMemory(), token
                    ).ConfigureAwait(false);
                }

                var (deprecatedCode, deprecatedOut) = await Proc.RunAsync(
                    Dotnet,
                    ["list", target, "package", "--deprecated", "--format", "json"],
                    mirror,
                    token
                ).ConfigureAwait(false);
                if (deprecatedCode is not 0 || deprecatedOut.Contains("\"deprecationReasons\"", StringComparison.Ordinal))
                {
                    noDeprecated = false;
                    await Console.Error.WriteLineAsync(
                        deprecatedOut.Replace(mirror, root, StringComparison.Ordinal).Trim().AsMemory(), token
                    ).ConfigureAwait(false);
                }
            }

            return (code, noDeprecated);
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    private static async Task<string[]> ProjectFilesAsync(string root, CancellationToken token) =>
        [.. (await VersionControl.FilesAsync(root, "*.csproj", token).ConfigureAwait(false))
            .Where(static f => !PathUtil.IsExcluded(f))];

    private static async Task<bool> RunLintersAsync(
        string root,
        string props,
        CancellationToken token
    )
    {
        var cfg = Path.Combine(AssetsDir, "dprint.json");
        await Dprint.BumpAsync(cfg, token).ConfigureAwait(false);
        var (code, noDeprecated) = await CSharpGateAsync(root, props, token).ConfigureAwait(false);

        var fileTypesOk = await Linters.CheckAsync(root, cfg, token).ConfigureAwait(false);
        var offenders = await Transform.OffendersAsync(root, token).ConfigureAwait(false);
        var badXml = await Transform.MalformedXmlAsync(root, token).ConfigureAwait(false);
        foreach (var f in badXml)
        {
            await Console
                .Error.WriteLineAsync($"{f}: malformed XML".AsMemory(), token)
                .ConfigureAwait(false);
        }
        foreach (var f in offenders)
        {
            await Console
                .Error.WriteLineAsync($"{f}: strippable comment (run fix)".AsMemory(), token)
                .ConfigureAwait(false);
        }

        var buildClean = code is 0 && fileTypesOk && noDeprecated;
        return buildClean && offenders.Count is 0 && badXml.Count is 0;
    }

    /// <summary>Applies safe then build-verified fixers, reverting any that break the build.</summary>
    /// <param name="root">Target directory.</param>
    /// <param name="props">Path to the injected props.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task.</returns>
    private static async Task AutofixAsync(string root, string props, CancellationToken token)
    {
        _ = await Transform.StripAsync(root, token).ConfigureAwait(false);
        var dprintCfg = Path.Combine(AssetsDir, "dprint.json");
        await Dprint.BumpAsync(dprintCfg, token).ConfigureAwait(false);
        await Linters.FixAsync(root, dprintCfg, token).ConfigureAwait(false);

        var files = (await VersionControl.FilesAsync(root, "*", token).ConfigureAwait(false))
            .Where(static f => !PathUtil.IsExcluded(f)).ToArray();
        var projects = await ProjectFilesAsync(root, token).ConfigureAwait(false);
        var mirror = await VersionControl.MirrorAsync(root, files: null, token).ConfigureAwait(false);
        try
        {
            var sources = MirrorPaths(root, mirror, files.Where(IsSource));
            var formatted = MirrorPaths(root, mirror, files.Where(IsCSharpierFile));
            if (formatted.Length > 0)
            {
                _ = await Proc.RunAsync("csharpier", ["format", .. formatted], mirror, token).ConfigureAwait(false);
            }

            var dbp = AddPropsImport(mirror, props);
            foreach (var project in projects)
            {
                var target = Path.Combine(mirror, Path.GetRelativePath(root, project));
                await FixProjectAsync(target, mirror, sources, token).ConfigureAwait(false);
            }

            foreach (var source in formatted.Where(f => !string.Equals(f, dbp, StringComparison.Ordinal)))
            {
                var target = Path.Combine(root, Path.GetRelativePath(mirror, source));
                File.Copy(source, target, overwrite: true);
            }
        }
        finally
        {
            Directory.Delete(mirror, recursive: true);
        }
    }

    private static bool IsSource(string file) =>
        string.Equals(Path.GetExtension(file), ".cs", StringComparison.Ordinal)
        || string.Equals(Path.GetExtension(file), ".csx", StringComparison.Ordinal);

    private static bool IsCSharpierFile(string file) =>
        CSharpierExtensions.Contains(Path.GetExtension(file), StringComparer.Ordinal);

    private static string[] MirrorPaths(string root, string mirror, IEnumerable<string> files) =>
        [.. files.Select(f => Path.Combine(mirror, Path.GetRelativePath(root, f)))];

    private static string AddPropsImport(string mirror, string props)
    {
        var dbp = Path.Combine(mirror, "Directory.Build.props");
        var document = File.Exists(dbp)
            ? System.Xml.Linq.XDocument.Load(dbp)
            : new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project"));
        document.Root!.Add(new System.Xml.Linq.XElement(
            document.Root.Name.Namespace + "Import",
            new System.Xml.Linq.XAttribute("Project", props)
        ));
        document.Save(dbp);
        return dbp;
    }

    private static async Task FixProjectAsync(
        string target,
        string mirror,
        string[] sources,
        CancellationToken token
    )
    {
        _ = await Proc.RunAsync(Dotnet, ["restore", target], mirror, token).ConfigureAwait(false);
        foreach (var fixer in new[] { "style", "analyzers" })
        {
            await ApplyVerifiedFixAsync(target, mirror, sources, fixer, token).ConfigureAwait(false);
        }
    }

    private static async Task ApplyVerifiedFixAsync(
        string target,
        string mirror,
        string[] sources,
        string fixer,
        CancellationToken token
    )
    {
        var backup = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            backup.Add(source, await File.ReadAllBytesAsync(source, token).ConfigureAwait(false));
        }

        if (sources.Length is 0)
        {
            return;
        }

        _ = await Proc.RunAsync(
            Dotnet,
            ["format", fixer, target, "--severity", "info", "--include", .. sources],
            mirror,
            token
        ).ConfigureAwait(false);
        var (code, _) = await Proc.RunAsync(
            Dotnet,
            ["build", target, "-c", "Release"],
            mirror,
            token
        ).ConfigureAwait(false);
        if (code is 0)
        {
            return;
        }

        foreach (var (source, bytes) in backup)
        {
            await File.WriteAllBytesAsync(source, bytes, token).ConfigureAwait(false);
        }

        await Console.Error.WriteLineAsync(
            $"lintmax-cs: 'format {fixer}' broke the build; skipped.".AsMemory(),
            token
        ).ConfigureAwait(false);
    }
}
