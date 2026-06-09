# lintmax-cs

Maximum-strictness C#/.NET + XAML quality gate in one command. Zero per-project config. OSS-only. Member of the lintmax collection (parity with lintmax / lintmax-go / lintmax-rs).

## Why

Roslyn ships hundreds of analyzers off or advisory by default, spread across the SDK plus a dozen community packs. `lintmax-cs` turns every rule from every pack on at error, fills the off-by-default gap, formats with CSharpier, and gates every other file type — with nothing for the consumer to configure.

## Install

```sh
dotnet tool install -g lintmax-cs
# or
sh install.sh
```

## Use

```sh
lintmax-cs fix       # format + autofix + full gate (default agent action)
lintmax-cs check     # read-only verify (CI), green-tree-hash cached
lintmax-cs version   # print version
lintmax-cs rules     # list every active rule
```

`ok` on a single line + exit 0 on success; verbose only on failure.

## Self-evolving (automatic, never a command)

- Analyzer packs float at latest (`*`), refreshed on restore.
- Self-update under CI (`dotnet tool update -g lintmax-cs`).
- Green-tree-hash cache: unchanged tree + version → `ok (cached)`.
- Staleness advisory: outdated analyzer packages printed to stderr (never fails the gate).
- Bypass with `LINTMAX_NO_CACHE=1`; skip advisory with `LINTMAX_SKIP_STALENESS=1`.

## What runs

| Layer         | Tooling                                                                                                                                                                                                      |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| C# analyzers  | NetAnalyzers, Roslynator(.Analyzers/.Formatting), Meziantou, SonarAnalyzer.CSharp, StyleCop, VS.Threading, BannedApi, ErrorProne.NET, IDisposableAnalyzers — every rule + every off-by-default rule at error |
| C# format     | CSharpier (layout) + `dotnet format style`/`analyzers` (code fixes)                                                                                                                                          |
| Comments      | strip non-survivor comments (keep `///` doc + `#pragma` reasons + license headers)                                                                                                                           |
| Config/markup | dprint (json/jsonc/toml/markdown)                                                                                                                                                                            |
| Shell         | shfmt + shellcheck                                                                                                                                                                                           |
| Workflows     | actionlint                                                                                                                                                                                                   |
| PowerShell    | PSScriptAnalyzer                                                                                                                                                                                             |
| XAML          | XamlStyler                                                                                                                                                                                                   |
| Cross-file    | editorconfig-checker                                                                                                                                                                                         |
| Spell         | typos                                                                                                                                                                                                        |
| Supply chain  | NuGetAudit (vuln, all severities)                                                                                                                                                                            |

## Configless by default

Analyzers + config are injected into the target build via `-p:CustomBeforeMicrosoftCommonProps`; the consumer's csproj and editorconfig stay untouched. No file is written outside `fix`.

## Earned disables

The disable list starts empty. Every entry is earned by a concrete conflict found on real code (CSharpier-owned layout, contradicting analyzer pairs, project-policy header text) and documented in `assets/lintmax.globalconfig`.

## Strictness policy

Strictness is monotonic-up: adding a rule needs no evidence; removing one needs a documented conflict. Latest toolchain always; nothing pinned.

## License

MIT — see `LICENSE`.
