// Desktops, with marks on the menu, the taskbar or dock and the system tray.
import { icon, mark } from '../lib/page.mjs';

const APPS = {
  web: ['a-web', 'globe'],
  files: ['a-files', 'folder'],
  terminal: ['a-terminal', 'square-terminal'],
  office: ['a-office', 'file-text'],
  mail: ['a-mail', 'mail'],
  music: ['a-music', 'music'],
  photos: ['a-photos', 'image'],
  settings: ['a-settings', 'settings'],
  help: ['a-help', 'life-buoy'],
  storeUbuntu: ['a-store-ubuntu', 'shopping-bag'],
  store: ['a-store', 'shopping-bag'],
  text: ['a-text', 'notebook-pen'],
};

/** An application icon; extra: 'running', 'small', 'round'… */
function app(name, extra = '', content = '') {
  const [cls, glyph] = APPS[name];
  return `<span class="app ${cls} ${extra}">${icon(glyph)}${content}</span>`;
}

function tray(...names) {
  return names.map((n) => icon(n)).join('');
}

/** A file manager window seen from far away. */
function filesWindow(cls) {
  const tiles = Array.from({ length: 8 }, () => `<span class="tile">${icon('folder', 'l')}</span>`).join('');
  return `<div class="win ${cls}">
  <div class="bar"><span class="line w30"></span><span class="dots"><i></i><i></i><i></i></span></div>
  <div class="body">
    <div class="side"><span class="line w80"></span><span class="line w60"></span><span class="line w70"></span><span class="line w50"></span><span class="line w60"></span></div>
    <div class="main">${tiles}</div>
  </div>
</div>`;
}

const TRAY = tray('wifi', 'volume-2', 'battery-full');

function ubuntuDesktop() {
  return `<div class="desk">
  <div class="wall"></div>
  ${filesWindow('files-ubuntu')}
  <div class="gnome-top">
    <span class="workspaces"><i class="on"></i><i></i></span>
    <span class="clock">10:42</span>
    <span class="tray">${TRAY}${icon('power')}${mark(3, 'b')}</span>
  </div>
  <div class="ubuntu-dock">
    ${mark(1, 'r')}
    ${app('web', 'running')}${app('mail')}${app('files', 'running')}${app('music')}${app('office')}${app('storeUbuntu')}${app('help')}
    <span class="apps-button">${icon('grip', 'm')}${mark(2, 'r')}</span>
  </div>
</div>`;
}

/** GNOME's overview, as Debian and Fedora show it. installer: the class of the installer icon, shown with mark 1. */
function gnomeOverview(installer) {
  const dash = installer
    ? `${app('web')}${app('files')}${app('store')}<span class="app ${installer}">${icon('hard-drive-download')}${mark(1, 't')}</span>`
    : `<span class="dash-apps">${app('web')}${app('mail')}${app('files', 'running')}${app('office')}${app('store')}${mark(2, 't')}</span>`;
  const gridMark = installer ? '' : mark(3, 'r');
  return `<div class="desk">
  <div class="wall"></div>
  <div class="overview">
    <div class="search">${icon('search', 's')}<span class="line w60"></span></div>
    <div class="workspace"><div class="wall"></div>${filesWindow('files-overview')}</div>
    <div class="dash">${dash}<span class="sep"></span><span class="apps-button">${icon('grip', 'm')}${gridMark}</span></div>
  </div>
  <div class="gnome-top adwaita">
    <span class="workspaces ${installer ? '' : 'ring'}"><i class="on"></i><i></i>${installer ? '' : mark(1, 'r')}</span>
    <span class="clock">10:42</span>
    <span class="tray">${TRAY}${icon('power')}${installer ? '' : mark(4, 'b')}</span>
  </div>
</div>`;
}

/** A bottom panel: menu (1), taskbar (2), tray (3). */
function bottomPanel({ panel, menuIcon, pinned, trayIcons, startIcons = '' }) {
  return `<div class="desk">
  <div class="wall"></div>
  ${startIcons}
  ${filesWindow('files-panel')}
  <div class="panel bottom ${panel}">
    <span class="menu-button">${icon(menuIcon)}${mark(1, 't')}</span>
    <span class="tasks">${pinned}<span class="task">${app('files', 'small')}<span class="line"></span></span>${mark(2, 't')}</span>
    <span class="tray">${trayIcons}<span class="clock">10:42</span>${mark(3, 't')}</span>
  </div>
</div>`;
}

/** A desktop icon with its label; name is an application of APPS or ready-made HTML. */
function desktopIcon(name, label, n = 0) {
  return `<span class="desk-icon">${name.startsWith('<') ? name : app(name)}<span>${label}</span>${n ? mark(n, 'r') : ''}</span>`;
}

export default [
  {
    name: 'ubuntu-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'ubuntu',
    html: ubuntuDesktop,
  },
  {
    name: 'debian-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'debian',
    html: () => gnomeOverview(null),
  },
  {
    name: 'fedora-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'fedora',
    html: () => gnomeOverview(null),
  },
  {
    name: 'debian-live-start',
    styles: ['desktop', 'scenes-desktop'],
    body: 'debian',
    html: () => gnomeOverview('a-install-red'),
  },
  {
    name: 'fedora-start',
    styles: ['desktop', 'scenes-desktop'],
    body: 'fedora',
    html: () => gnomeOverview('a-install-blue'),
  },
  {
    name: 'lubuntu-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'lubuntu',
    html: () => bottomPanel({
      panel: 'lxqt-panel',
      menuIcon: 'bird',
      pinned: `${app('web', 'small')}${app('files', 'small')}`,
      trayIcons: tray('wifi', 'volume-2'),
    }),
  },
  {
    name: 'mint-desktop-cinnamon',
    styles: ['desktop', 'scenes-desktop'],
    body: 'mint',
    html: () => bottomPanel({
      panel: 'cinnamon-panel',
      menuIcon: 'leaf',
      pinned: `${app('web', 'small')}${app('terminal', 'small')}${app('files', 'small')}`,
      trayIcons: `<span class="shield">${icon('shield-check')}</span>${tray('wifi', 'volume-2')}`,
    }),
  },
  {
    name: 'mint-desktop-xfce',
    styles: ['desktop', 'scenes-desktop'],
    body: 'mint',
    html: () => bottomPanel({
      panel: 'xfce-panel',
      menuIcon: 'leaf',
      pinned: `${app('web', 'small')}${app('terminal', 'small')}${app('files', 'small')}`,
      trayIcons: `<span class="shield">${icon('shield-check')}</span>${tray('wifi', 'volume-2')}`,
    }),
  },
  {
    name: 'zorin-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'zorin',
    html: () => bottomPanel({
      panel: 'zorin-panel',
      menuIcon: 'layout-grid',
      pinned: `${app('web', 'small')}${app('files', 'small')}${app('store', 'small')}`,
      trayIcons: tray('wifi', 'volume-2', 'battery-full'),
    }),
  },
  {
    name: 'lubuntu-start',
    styles: ['desktop', 'scenes-desktop'],
    body: 'lubuntu',
    html: () => `<div class="desk">
  <div class="wall"></div>
  <div class="icons">
    ${desktopIcon('files', 'Home')}
    ${desktopIcon(`<span class="app a-install-blue">${icon('hard-drive-download')}</span>`, 'Install Lubuntu 26.04 LTS', 1)}
  </div>
  <div class="panel bottom lxqt-panel">
    <span class="menu-button">${icon('bird')}</span>
    <span class="tasks">${app('web', 'small')}${app('files', 'small')}</span>
    <span class="tray">${tray('wifi', 'volume-2')}<span class="clock">10:42</span></span>
  </div>
</div>`,
  },
  {
    name: 'mint-start',
    styles: ['desktop', 'scenes-desktop'],
    body: 'mint',
    html: () => `<div class="desk">
  <div class="wall"></div>
  <div class="icons">
    ${desktopIcon('settings', 'Computer')}
    ${desktopIcon('files', 'Home')}
    ${desktopIcon(`<span class="app a-install">${icon('disc-3')}</span>`, 'Install Linux Mint', 1)}
  </div>
  <div class="panel bottom cinnamon-panel">
    <span class="menu-button">${icon('leaf')}</span>
    <span class="tasks">${app('web', 'small')}${app('terminal', 'small')}${app('files', 'small')}</span>
    <span class="tray">${tray('wifi', 'volume-2')}<span class="clock">10:42</span></span>
  </div>
</div>`,
  },
  {
    name: 'manjaro-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'manjaro',
    html: () => `<div class="desk">
  <div class="wall"></div>
  ${filesWindow('files-panel')}
  <div class="plasma-panel">
    <span class="menu-button">${icon('layout-grid')}${mark(1, 't')}</span>
    <span class="tasks">${app('web', 'small')}${app('files', 'small running')}${app('terminal', 'small')}${app('store', 'small')}${mark(2, 't')}</span>
    <span class="tray">${tray('bell', 'package', 'wifi', 'volume-2')}<span class="clock">10:42<br>04/10/2026</span>${mark(3, 't')}</span>
  </div>
</div>`,
  },
  {
    name: 'pop-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'pop',
    html: () => `<div class="desk">
  <div class="wall"></div>
  ${filesWindow('files-pop')}
  <div class="cosmic-top">
    <span class="pill">${icon('layout-panel-left', 's')}</span>
    <span class="pill">${icon('layout-grid', 's')}Applications${mark(1, 'b')}</span>
    <span class="center clock">10:42</span>
    <span class="tray"><span class="pill">${icon('columns-2', 's')}${mark(3, 'b')}</span>${TRAY}${icon('power')}${mark(4, 'b')}</span>
  </div>
  <div class="cosmic-dock">${app('web')}${app('files', 'running')}${app('terminal')}${app('store')}${app('settings')}${mark(2, 'r')}</div>
</div>`,
  },
  {
    name: 'kali-desktop',
    styles: ['desktop', 'scenes-desktop'],
    body: 'kali',
    html: () => `<div class="desk">
  <div class="wall"></div>
  ${filesWindow('files-kali')}
  <div class="panel top kali-panel">
    <span class="menu-button">${icon('shield-half')}${mark(1, 'b')}</span>
    <span class="tasks">${app('files', 'small')}${app('terminal', 'small')}${app('web', 'small')}${app('text', 'small')}${mark(2, 'b')}</span>
    <span class="ws"><i class="on"></i><i></i><i></i><i></i></span>
    <span class="tray">${tray('wifi', 'volume-2', 'battery-full')}<span class="clock">10:42</span>${mark(3, 'b')}</span>
  </div>
</div>`,
  },
];
