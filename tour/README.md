# Linux tour

While a drive is being created, the application shows short lessons about the chosen distribution: how to start
from the drive, its boot menu, how to try it, its desktop, its software center, its updates, the terminal and where
to find help. The Linux guide page lists them too, with a list to pick another distribution.

- [`tours.json`](tours.json): the lessons, in English and French, and the tour of each distribution. It is embedded
  in `LinuxInstallHelper.Core` (`TourBook`).
- [`images/`](images): their pictures, copied to `Assets/Tour` next to the application. They are drawn by
  [`tools/tour-images`](../tools/tour-images), never edited by hand.

A local image, or a distribution added to the online catalog after a release, gets the `generic` tour.

## `tours.json`

```json
{
  "lessons": {
    "ubuntu-boot": {
      "glyph": "",
      "image": "ubuntu-boot",
      "title": { "en": "Ubuntu's boot menu", "fr": "Le menu de démarrage d'Ubuntu" },
      "body": { "en": "…", "fr": "…" },
      "steps": [{ "en": "…", "fr": "…" }],
      "windows": { "en": "…", "fr": "…" },
      "linux": { "en": "…", "fr": "…" },
      "command": { "en": "sudo apt upgrade   # comment", "fr": "sudo apt upgrade   # commentaire" }
    }
  },
  "tours": {
    "generic": ["usb-boot", "generic-try"],
    "ubuntu-desktop": ["usb-boot", "ubuntu-boot"]
  }
}
```

| Field | Required | Description |
|---|---|---|
| `glyph` | yes | Segoe Fluent Icons glyph, shown when the lesson has no picture. |
| `image` | no | Picture name in `images/`, without extension. |
| `localizedImage` | no | `true` when the picture contains text: `name.en.png` and `name.fr.png`. Otherwise `name.png`. |
| `title`, `body` | yes | Short texts, in English and French. |
| `steps` | no | Numbered steps, at most 5. Step *n* explains the mark *n* drawn on the picture. |
| `windows`, `linux` | no | How the same thing is done on Windows and on Linux. Both or none. |
| `command` | no | Commands to type, with comments in each language. |

A lesson can be shared by several tours (`usb-boot`, `terminal-apt`…). A tour has 5 to 12 lessons, and every lesson
is used by at least one tour.

## Adding the tour of a distribution

1. Write its lessons and add a tour named after its catalog `id`. Every distribution of the catalog needs one.
2. Draw the pictures it needs: add scenes to `tools/tour-images/scenes` and run `npm run render` there.
3. Check the texts against the distribution itself: the exact entries of its boot menu, the names of its installer,
   software center and update tool in both languages.
4. Run the tests: they check that each tour exists, that every text is translated and that every picture exists, is
   used and stays small.

   ```sh
   dotnet test tests/LinuxInstallHelper.Core.Tests --filter "FullyQualifiedName~TourBookTests"
   ```
