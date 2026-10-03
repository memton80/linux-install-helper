# Contributing to Linux Install Helper

Thanks for helping! Bug reports, catalog updates, translations and code are all welcome.

## Ground rules

- **Safety first.** Anything that can erase a disk must keep the existing guarantees: only removable USB
  drives are offered (`DiskFilter`), the target is checked again right before writing (`TargetGuard`), and
  nothing is written before the image is verified. Pull requests that weaken these checks will not be merged.
- **Official sources only** in the catalog: the distribution's website, its download server or the
  mirrors it officially lists.
- Keep pull requests small and focused, with tests for the logic in `LinuxInstallHelper.Core`.

## Project layout

| Path | Content |
|---|---|
| `src/LinuxInstallHelper.App` | WinUI 3 application (views, view models, Windows services). |
| `src/LinuxInstallHelper.Core` | Platform-independent logic: catalog, download, verification, disk filtering, write engine, pipeline. Windows-specific code lives in `Disks/Windows`. |
| `tests/LinuxInstallHelper.Core.Tests` | xUnit tests (run on Linux and Windows). |
| `tools/LinuxInstallHelper.LinkChecker` | Console tool used by the `check-links` workflow. |
| `catalog/` | `distros.json`, its JSON schema, the pinned OpenPGP keys. See [catalog/README.md](catalog/README.md). |

## Building

The application targets Windows (WinUI 3, .NET 8). Everything is built by GitHub Actions on
`windows-latest`, so you do not need a local Windows machine to contribute to the core.

```sh
# Core library and tests (Windows, Linux or macOS)
dotnet test tests/LinuxInstallHelper.Core.Tests

# Link checker
dotnet run --project tools/LinuxInstallHelper.LinkChecker -- --only ubuntu-desktop

# Application (Windows only, Visual Studio 2022 17.8+ with the "Windows application development" workload, or the .NET 8 SDK)
dotnet publish src/LinuxInstallHelper.App -c Release -r win-x64 -p:Platform=x64 -o publish
```

The application asks for administrator rights (writing to a raw disk requires them).

Developer options: `LinuxInstallHelper.exe --page Settings --theme dark --lang fr-FR`.

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat(core): ...`, `fix(catalog): ...`,
  `ci: ...`, `docs: ...`.
- Open the pull request against `main`. The `Build` workflow must be green (build, tests, publish and the
  smoke test that starts the application). Catalog changes also run `Check catalog links`.

## Adding or updating a distribution

See [catalog/README.md](catalog/README.md#adding-a-distribution). In short: edit `catalog/distros.json`,
bump `updated`, add the signing key to `catalog/keys/` if needed, run the tests and the link checker.

## Translations

Strings live in `src/LinuxInstallHelper.App/Strings/<language>/Resources.resw` (English and French today).
To add a language, copy `en-US/Resources.resw` to a new folder named after the language tag, translate the
values, and add the tag to `UserSettings.SupportedLanguages` and to the language list of the settings page.

## Reporting a security issue

Please do not open a public issue for a vulnerability (for instance a way to make the application write to
a non-USB disk or to accept a forged image). Use GitHub's private vulnerability reporting instead.
