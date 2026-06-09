# lintmax-cs

Maximum-strictness C#/.NET + XAML quality gate in one command. Zero per-project config.
Member of the lintmax collection (parity with lintmax / lintmax-go / lintmax-rs).

## Commands (exactly four)

- `fix` format + autofix + full gate (default agent action)
- `check` verify only, read-only (CI), green-tree-hash cached
- `version` print version
- `rules` list every active rule under the maxed config

`ok` on success (exit 0); verbose only on failure. Self-evolving (automatic, never commands):
analyzer @latest refresh, self-update, green-tree-hash cache, staleness scan.

## Adopted (all rules -> error; configless, injected, consumer untouched)

analyzers: NetAnalyzers (SDK, AnalysisMode=All, api_surface=all) + Roslynator(.Analyzers/.Formatting) +
Meziantou (all-errors) + SonarAnalyzer.CSharp + StyleCop + VS.Threading + BannedApi(engine) + ErrorProne + IDisposable.
format/fix: dotnet format style+analyzers -> CSharpier (layout; IDE0055 off) -> XamlStyler (xaml).
other file types: dprint (json/yaml/toml/md/docker) + actionlint + shfmt/shellcheck + PSScriptAnalyzer +
editorconfig-checker + typos (spell).
inject: -p:CustomBeforeMicrosoftCommonProps=<tool>/inject.props (PackageReferences + GlobalAnalyzerConfigFiles,
tool-owned NUGET_PACKAGES, floating * = rolling-latest); gate = child-process `dotnet build` + per-project SARIF ErrorLog.

## Proven (native-spike/lintmax-cs-spike, SDK 10.0.300)

96 distinct rules fire on dirty C#; configless injection leaves consumer untouched; fix pipeline idempotent;
runs on real WinUI 3 app; generated-code excluded. earned-disables (evidenced): MA0038, IDE0055, IDE0130.
