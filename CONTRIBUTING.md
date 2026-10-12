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
| `tour/` | The lessons shown while a drive is created, per distribution, and their pictures. See [tour/README.md](tour/README.md). |
| `tools/tour-images` | Draws the tour pictures from HTML scenes (Node.js, Playwright). See its [README](tools/tour-images/README.md). |

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

Developer options: `LinuxInstallHelper.exe --page Settings --theme dark --lang fr-FR --guidance alone --advanced`. None
of them is saved. `--guidance helper|alone` forces the "someone helps me" or "I am on my own" mode for the session; with
`--page`, the welcome screen that asks for it at the first start is never shown (`--page Welcome` opens it).
`--advanced` turns the advanced mode on without its warning (orange frame, technical details shown).

The "on my own" mode only adds explanations: its blocks are hidden otherwise (`helpers:Ui.Visible(...IsAlone)`), use
their own `Alone_*` strings, and never remove a confirmation or touch a safety check. Without a choice or with someone
helping, every page stays exactly as before.

The advanced mode (`AdvancedModeState`, the switch of the title bar) only shows or hides technical details: the pages bind
them to `Advanced.IsOn`. Never hide a confirmation, a warning or an error message behind it, and never make a check
depend on it.

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat(core): ...`, `fix(catalog): ...`,
  `ci: ...`, `docs: ...`.
- Open the pull request against `main`. The `Build` workflow must be green (build, tests, publish, the
  smoke test that starts the application and the layout test that uses its pages and checks that they never move
  sideways). Catalog changes also run `Check catalog links`.

## Adding or updating a distribution

See [catalog/README.md](catalog/README.md#adding-a-distribution). In short: edit `catalog/distros.json`,
bump `updated`, add the signing key to `catalog/keys/` if needed, write its tour in `tour/tours.json` (see
[tour/README.md](tour/README.md)), run the tests and the link checker.

## Translations

Strings live in `src/LinuxInstallHelper.App/Strings/<language>/Resources.resw` (English and French today).
To add a language, copy `en-US/Resources.resw` to a new folder named after the language tag, translate the
values, and add the tag to `UserSettings.SupportedLanguages` and to the language list of the settings page. The
distribution descriptions (`catalog/distros.json`) and the tour lessons (`tour/tours.json`) carry their English and
French texts themselves.

## Reporting a security issue

Please do not open a public issue for a vulnerability (for instance a way to make the application write to
a non-USB disk or to accept a forged image). Use GitHub's private vulnerability reporting instead.
