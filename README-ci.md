# Continuous integration and packaging

One workflow, `release.yml`, deliberately not on every push.

| Trigger | What it does |
| --- | --- |
| a `v*` tag | Builds, tests, packs `HKX2`, publishes to GitHub Packages, creates a GitHub release |
| `workflow_dispatch` | The same up to and including packing, then stops. Nothing is published |

Building and releasing share one workflow because they share a trigger. Split
across two, a tag would build the same assembly twice and could publish one that
was never the one tested.

A manual run still packs, so packaging breakage — a missing readme, a malformed
nuspec, a project that stopped being packable — shows up there rather than on
the tag. It uses a `0.0.0-ci.<run>` version that goes nowhere.

## Releasing

Tag a commit and push the tag:

    git tag v1.2.3
    git push origin v1.2.3

The version comes from the tag with the `v` stripped, and has to be a semantic
version: `vMAJOR.MINOR.PATCH`, optionally with a prerelease suffix such as
`v1.2.3-rc.1`. Anything else fails in the first step rather than after a build.
A version containing `-` is marked as a prerelease on the GitHub release.

The release carries `HKX2.dll`, `HKX2.pdb` and the `.nupkg`. It is created after
the package push, so a failed push never leaves a release advertising a version
that is not on the feed.

## Consuming the package

The package goes to GitHub Packages, not nuget.org. Consumers need a
`nuget.config` pointing at the feed and a personal access token with
`read:packages`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/aerisarn-two/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_PAT_WITH_read:packages" />
    </github>
  </packageSourceCredentials>
</configuration>
```

Publishing needs no secret: `GITHUB_TOKEN` is enough for GitHub Packages, which
is why the workflow grants the job `packages: write`.

GitHub Packages matches a package to its repository through the `repository`
element in the nuspec. That comes from `RepositoryUrl` in `HKX2.csproj`, which
the workflow overrides with the repository it is running in, so a fork publishes
to its own feed without editing anything.

## What the tests cover in CI

Most of the suite runs. The corpus tests do not, because they need a directory
of real `.hkx` files — extracted game data, which cannot be committed here or
shipped to a runner. Those five report Inconclusive and the run still passes:

    Passed!  -  Failed: 0, Passed: 24, Skipped: 5, Total: 29

So CI checks that the library builds, packs, and that everything not needing the
corpus still holds: float and string round-trip fidelity, the null-versus-empty
string pointer distinction, and the flag helpers.

The corpus tests are the ones that prove byte-for-byte fidelity over all 7699
vanilla files, and they have to be run locally:

    HKX2_CORPUS=/path/to/extracted/meshes dotnet test

Run that before tagging. CI cannot do it for you.

## Framework versions

The library targets `net7.0`, which no current SDK ships a runtime for. That is
fine — reference assemblies still come down as a package, and a library needs no
runtime to build. Only the tests need one, and they target `net9.0`, so the
workflow installs the .NET 9 SDK and nothing else.

Keeping the library on `net7.0` is deliberate: it is consumed as a submodule by
projects that have not moved, and nothing in it needs anything newer.
