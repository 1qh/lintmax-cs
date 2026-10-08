// <copyright file="VersionControlTests.cs" company="lintmax-cs contributors">
// Copyright (c) lintmax-cs contributors. Licensed under the MIT License.
// </copyright>

namespace LintmaxCs.Tests;

/// <summary>Dependency-free regression tests for version-control candidate selection.</summary>
internal static class VersionControlTests
{
    private const string IgnoredFile = "ignored.cs";
    private const string GitIgnoreFile = ".gitignore";
    private const string TrackedFile = "tracked.cs";

    private static async Task Main()
    {
        await InsideGitSkipsIgnoredFilesAsync().ConfigureAwait(false);
        await OutsideGitIncludesEveryFileAsync().ConfigureAwait(false);
        await InsideGitIncludesUntrackedFilesAsync().ConfigureAwait(false);
        await InvalidCheckoutFailsLoudlyAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            "ok (version-control tests)".AsMemory(),
            CancellationToken.None
        ).ConfigureAwait(false);
    }

    private static Task InsideGitSkipsIgnoredFilesAsync()
    {
        return WithFixtureAsync(
            static async (root, token) =>
            {
                await GitAsync(root, ["init"], token).ConfigureAwait(false);
                await GitAsync(root, ["add", TrackedFile], token).ConfigureAwait(false);
                await AssertFilesAsync(root, [TrackedFile], token).ConfigureAwait(false);
                var mirror = await VersionControl.MirrorAsync(root, files: null, token).ConfigureAwait(false);
                try
                {
                    if (File.Exists(Path.Combine(mirror, IgnoredFile)))
                    {
                        throw new InvalidOperationException("Ignored file reached the tool mirror.");
                    }
                }
                finally
                {
                    Directory.Delete(mirror, recursive: true);
                }
            },
            CancellationToken.None
        );
    }

    private static Task OutsideGitIncludesEveryFileAsync()
    {
        return WithFixtureAsync(
            static (root, token) => AssertFilesAsync(root, [GitIgnoreFile, IgnoredFile, TrackedFile], token),
            CancellationToken.None
        );
    }

    private static Task InsideGitIncludesUntrackedFilesAsync()
    {
        return WithFixtureAsync(
            static async (root, token) =>
            {
                await GitAsync(root, ["init"], token).ConfigureAwait(false);
                await GitAsync(root, ["add", TrackedFile], token).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(root, "untracked.cs"), string.Empty, token)
                    .ConfigureAwait(false);
                await AssertFilesAsync(root, [TrackedFile, "untracked.cs"], token).ConfigureAwait(false);
            },
            CancellationToken.None
        );
    }

    private static Task InvalidCheckoutFailsLoudlyAsync()
    {
        return WithFixtureAsync(
            static async (root, token) =>
            {
                await File.WriteAllTextAsync(Path.Combine(root, ".git"), "gitdir: missing\n", token)
                    .ConfigureAwait(false);
                try
                {
                    _ = (await VersionControl.FilesAsync(root, "*", token).ConfigureAwait(false)).ToArray();
                }
                catch (IOException)
                {
                    return;
                }

                throw new InvalidOperationException("Git failure silently included ignored files.");
            },
            CancellationToken.None
        );
    }

    private static async Task WithFixtureAsync(
        Func<string, CancellationToken, Task> test,
        CancellationToken token
    )
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lintmax-cs-test-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)
        );
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, TrackedFile), string.Empty, token)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(root, IgnoredFile), string.Empty, token)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(root, GitIgnoreFile), ".gitignore\nignored.cs\n", token)
                .ConfigureAwait(false);
            await test(root, token).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertFilesAsync(
        string root,
        string[] expected,
        CancellationToken token
    )
    {
        var actual = (await VersionControl.FilesAsync(root, "*", token).ConfigureAwait(false))
            .Select(f => Path.GetRelativePath(root, f))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Unexpected candidates: {string.Join(", ", actual)}");
    }

    private static async Task GitAsync(string root, string[] args, CancellationToken token)
    {
        var (code, _, error) = await Proc.RunSeparatedAsync(
            "git",
            ["-C", root, .. args],
            root,
            token
        ).ConfigureAwait(false);
        if (code is 0)
        {
            return;
        }

        throw new InvalidOperationException(error);
    }
}
