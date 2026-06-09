# Benchmark

Measured on Apple Silicon, .NET SDK 10.0.300, lintmax-cs gating its own repo.

| Scenario                                           | Wall time |
| -------------------------------------------------- | --------- |
| `check` warm, cached (`ok (cached)`)               | ~1.0s     |
| `check` warm, full (analyzers + file-type linters) | ~3-5s     |
| `check` cold (restore + first analyzer load)       | ~30-60s   |

The cache hashes every tracked source + the globalconfig + the tool version; an unchanged tree short-circuits to `ok (cached)` without spawning a build.
