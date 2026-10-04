# Tour pictures

Draws the pictures of the [Linux tour](../../tour/README.md): boot menus, desktops, installers, software centers and
terminals of each distribution, with numbered marks that match the numbered steps of the lessons.

Each scene of [`scenes/`](scenes) is an HTML page (markup only, every style lives in [`styles/`](styles)) rendered by
Chromium at twice its size (960×600), then saved as a palette PNG in `tour/images`. A scene that contains text is
rendered once in English and once in French.

```sh
cd tools/tour-images
npm ci
npx playwright install chromium   # unless a Chromium for Playwright is already installed
npm run render                    # every picture
npm run render -- ubuntu kali     # only the scenes whose name contains one of these words
```

Then run the tour tests (see [`tour/README.md`](../../tour/README.md)).

## Writing a scene

```js
{
  name: 'ubuntu-boot',          // tour/images/ubuntu-boot.png
  localized: false,             // true: ubuntu-boot.en.png and ubuntu-boot.fr.png
  styles: ['boot'],             // style sheets of styles/, after fonts.css and base.css
  body: 'ubuntu',               // classes of <body>, for instance the wallpaper
  html: (t, lang) => `…`,       // t('Install', 'Installer') picks the text of the language
}
```

- `mark(n, side)` draws the mark *n* next to the element that contains it (`r`, `l`, `t`, `b`, corners `tr`…,
  inside `ir`, centered `c`). The `ring` class circles an element.
- `icon(name)` inserts a [Lucide](https://lucide.dev) icon.
- Keep text that matters (menu entries, buttons) real and exact; other text is drawn as grey lines.

## Sources

The boot menu entries of Arch Linux (archiso), Pop!_OS (pop-os/iso), openSUSE Leap (agama), openSUSE Tumbleweed
(installation-images) and Fedora (lorax) are copied from their build files, and the labels of the Ubuntu installer,
the App Center and the Pop!_OS installer from their translations (ubuntu-desktop-provision, ubuntu/app-center,
pop-os/installer). The other screens follow the published ISOs: check them when a new version comes out.

## Credits

- Icons: [Lucide](https://lucide.dev), ISC license.
- Fonts, from [Fontsource](https://fontsource.org): Ubuntu and Ubuntu Mono (Ubuntu Font Licence); Inter, Noto Sans,
  Noto Sans Mono, Open Sans, Red Hat Display, Source Code Pro, Cascadia Mono and GNU Unifont (SIL Open Font License).

The pictures are drawings: they show the layout and the exact labels of each screen, not screenshots.
