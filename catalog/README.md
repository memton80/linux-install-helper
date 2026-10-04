# Distribution catalog

`distros.json` lists the Linux distributions offered by Linux Install Helper. It is validated by
[`distros.schema.json`](distros.schema.json) (JSON Schema draft-07) and by extra rules in
`CatalogValidator` (HTTPS only, unique ids, valid regular expressions, coherent signatures).

At startup the application downloads the latest version of this file from the `main` branch
(`https://raw.githubusercontent.com/memton80/linux-install-helper/main/catalog/distros.json`). If that
fails or the file is invalid, it uses the last downloaded copy or the copy embedded in the build.

The [`check-links`](../.github/workflows/check-links.yml) workflow checks every download each Monday
(and on every change of this folder) and opens an issue when something is broken.

## Root object

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | integer | Always `1`. The application ignores catalogs with an unknown version. |
| `updated` | `YYYY-MM-DD` | Date of the last change. Bump it with every change. |
| `distros` | array | The distributions, in display order. |

## Distribution

| Field | Required | Description |
|---|---|---|
| `id` | yes | Stable identifier: lowercase letters, digits and dashes. Never reuse an id. |
| `name` | yes | Display name (`Linux Mint`). |
| `edition` | no | Edition shown after the name (`Cinnamon`, `Server`...). |
| `version` | yes | Version shown in the list (`22.3 (Zena)`). |
| `family` | yes | `ubuntu`, `debian`, `fedora`, `arch`, `opensuse` or `other`. |
| `categories` | yes | One or more of `beginner`, `desktop`, `lightweight`, `server`, `security`, `rolling`, `developer`, `gaming` (good choice for games: recent drivers, Steam ready). |
| `desktop` | no | Desktop environment, `None` for server images. |
| `description` | yes | `{ "en": "...", "fr": "..." }`, one sentence, 300 characters max. |
| `homepage` | yes | Official website (HTTPS). |
| `color` | yes | Brand color `#RRGGBB`, used for the badge. |
| `architecture` | yes | `x86_64` or `aarch64`. |
| `secureBoot` | no | `false` when the image does not boot with Secure Boot (a warning is shown). Default `true`. |
| `requirements` | no | `{ "ramMb": 4096, "diskGb": 25 }`. |
| `image` | yes | Where to download the ISO and how to verify it (below). |

## Image

| Field | Required | Description |
|---|---|---|
| `fileName` | yes | ISO file name when the catalog was updated. |
| `size` | yes | Size in bytes (the link checker prints the real size). |
| `urls` | yes | Official HTTPS URLs (official site first, then official mirrors). The app tries them in order. |
| `sha256` | * | SHA-256 of the ISO, when the distribution only publishes it on a web page. |
| `checksum.url` | * | Official checksum file (`hash  file`, `hash *file` or `SHA256 (file) = hash`, clearsigned or not). |
| `signature` | no | OpenPGP signature, see below. Strongly recommended when the distribution publishes one. |
| `resolve` | no | How to follow new point releases automatically, see below. |
| `hybrid` | no | `true` (default) for isohybrid images that can be written as-is. Only hybrid images are supported. |

\* At least one of `sha256`, `checksum` or a `json` resolver is required.

### Signature

```json
"signature": {
  "kind": "detached",
  "target": "checksum",
  "url": "https://releases.ubuntu.com/26.04/SHA256SUMS.gpg",
  "fingerprints": ["843938DF228D22F7B3742BC0D94AA3F0EFE21092"]
}
```

- `kind`: `detached` (separate `.gpg`, `.sig`, `.asc` file) or `clearsigned` (the checksum file itself is signed, like Fedora's `CHECKSUM`).
- `target`: `checksum` (the signature covers the checksum file) or `image` (it covers the ISO, like Arch Linux).
- `url`: required for `detached`. `{fileName}` is replaced by the resolved ISO name.
- `fingerprints`: full 40-character fingerprints of the keys allowed to sign, **taken from the official website of the distribution**.
  The public key must be added to [`keys/`](keys) as `<FINGERPRINT>.asc` (minimal export:
  `gpg --armor --export-options export-minimal --export <FINGERPRINT>`).

A signature that does not verify stops the process. When the signature cannot be downloaded the
application only checks the SHA-256 and shows a warning.

### Resolve

Distributions publish point releases (Debian 13.7 → 13.8) and remove the previous ISO. A resolver
lets the application find the new file without waiting for a catalog update:

- `{ "type": "checksum-pattern", "pattern": "^debian-live-(?<version>13\\.[0-9]+\\.[0-9]+)-amd64-gnome\\.iso$" }`:
  when `fileName` is no longer listed in the checksum file, the file matching `pattern` with the highest
  `version` group is used, and the last segment of each URL is replaced by its name.
- `{ "type": "json", "url": "...", "urlField": "url", "sha256Field": "sha_sum", "sizeField": "size", "versionField": "build" }`:
  an official JSON API returns the URL and SHA-256 of the current build (Pop!_OS).

## Adding a distribution

1. Use only official sources: the distribution's website, its download server or the official mirrors it lists.
2. Add an entry to `distros.json` (copy a similar one), bump `updated`.
3. If the distribution signs its checksums or ISO, add the fingerprint(s) and the public key in `keys/`.
4. Write its tour, the lessons shown while the drive is created: see [`tour/README.md`](../tour/README.md).
5. Run the tests and the link checker locally:
   ```sh
   dotnet test tests/LinuxInstallHelper.Core.Tests
   dotnet run --project tools/LinuxInstallHelper.LinkChecker -- --only my-distro-id
   ```
6. Open a pull request: the `check-links` workflow verifies every URL, the size, the checksum file and the signature.
