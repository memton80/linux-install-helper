// Installers, software centers and update tools. They contain text, so they exist in English and French.
import { icon, mark } from '../lib/page.mjs';

const STYLES = ['desktop', 'apps', 'scenes-apps'];

const lines = (...widths) => `<div class="lines">${widths.map((w) => `<span class="line w${w}"></span>`).join('')}</div>`;
const check = (on) => `<span class="check${on ? ' on' : ''}">${on ? icon('check') : ''}</span>`;
const radio = (on) => `<span class="radio${on ? ' on' : ''}"></span>`;
const dots = '<span class="end"><i></i><i></i><i></i></span>';

/** A window on a wallpaper. */
function onDesktop(theme, cls, inner) {
  return `<div class="wall"></div><div class="appwin ${theme} ${cls}">${inner}</div>`;
}

/** The page of GIMP in a software center, with marks on the search (1) and the Install button (2). */
function appPage(t, { search = true, button = t('Install', 'Installer'), extra = '' } = {}) {
  return `<div class="pane">
  ${search ? `<span class="entry focus search-field">${icon('search', 's')}gimp<span class="cursor"></span>${mark(1, 'r')}</span>` : ''}
  <div class="app-head">
    <span class="gimp">${icon('palette', 'l')}</span>
    <span class="name"><b>GIMP</b><span class="line"></span></span>
    <span class="btn primary big">${icon('download', 's')}${button}${mark(2, 'l')}</span>
  </div>
  ${lines(90, 80, 60)}
  <div class="shots"><span></span><span></span><span></span></div>
  ${extra}
</div>`;
}

export default [
  {
    name: 'ubuntu-start',
    localized: true,
    styles: STYLES,
    body: 'ubuntu',
    html: (t) => onDesktop('t-yaru', '', `
  <div class="hb"><span class="title">${t('Install Ubuntu 26.04 LTS', 'Installer Ubuntu 26.04 LTS')}</span>${dots}</div>
  <div class="pane">
    <h1 class="heading">${t('What do you want to do with Ubuntu?', 'Que voulez-vous faire avec Ubuntu ?')}</h1>
    <div class="option">${radio(false)}<span class="text"><b>${t('Install Ubuntu 26.04 LTS', 'Installer Ubuntu 26.04 LTS')}</b><span class="line w80"></span></span></div>
    <div class="option on">${radio(true)}<span class="text"><b>${t('Try Ubuntu 26.04 LTS', 'Essayer Ubuntu 26.04 LTS')}${mark(1, 'r')}</b><span class="line w70"></span></span></div>
    <div class="buttons"><span class="btn outline">${t('Back', 'Précédent')}</span><span class="btn primary push">${t('Close', 'Fermer')}${mark(2, 'l')}</span></div>
  </div>`),
  },
  {
    name: 'zorin-start',
    localized: true,
    styles: STYLES,
    body: 'zorin',
    html: (t) => {
      const languages = ['Deutsch', 'English', 'Español', 'Français', 'Italiano', 'Nederlands', 'Polski']
        .map((l) => {
          const on = l === t('English', 'Français');
          return `<div class="row${on ? ' selected' : ''}">${l}${on ? mark(1, 'r') : ''}</div>`;
        })
        .join('');
      return onDesktop('t-zorin', '', `
  <div class="hb"><span class="title">${t('Welcome', 'Bienvenue')}</span>${dots}</div>
  <div class="content">
    <div class="pane languages"><div class="list">${languages}</div></div>
    <div class="pane choices">
      <span class="choice">${icon('monitor-play', 'xl')}<span class="btn primary">${t('Try Zorin OS', 'Essayer Zorin OS')}${mark(2, 'r')}</span></span>
      <span class="choice">${icon('hard-drive-download', 'xl')}<span class="btn">${t('Install Zorin OS', 'Installer Zorin OS')}</span></span>
    </div>
  </div>`);
    },
  },
  {
    name: 'pop-start',
    localized: true,
    styles: STYLES,
    body: 'pop',
    html: (t) => onDesktop('t-pop', '', `
  <div class="pane centered">
    ${icon('monitor-play', 'xl')}
    <h1 class="heading">${t('Install or Try Demo Mode', 'Installer ou Tester')}</h1>
    <p class="muted">${t('You can install Pop!_OS on this device now, or try Demo Mode without installing.',
      'Vous pouvez installer Pop!_OS sur ce périphérique maintenant, ou essayer le mode de démonstration sans installer.')}</p>
    <div class="option on">${radio(true)}<span class="text"><b>Clean Install${mark(2, 'r')}</b><span class="line w80"></span></span></div>
    <div class="option">${radio(false)}<span class="text"><b>Custom (Advanced)</b><span class="line w70"></span></span></div>
    <div class="buttons"><span class="btn">${t('Try Demo Mode', 'Essayer le mode de démonstration')}${mark(1, 'r')}</span><span class="btn primary push">${t('Next', 'Suivant')}</span></div>
  </div>`),
  },
  {
    name: 'manjaro-hello',
    localized: true,
    styles: STYLES,
    body: 'manjaro',
    html: (t) => {
      const group = (title) => `<div class="group"><b>${title}</b><span class="btn"><span class="line w80"></span></span><span class="btn"><span class="line w60"></span></span><span class="btn"><span class="line w70"></span></span></div>`;
      return onDesktop('t-manjaro', '', `
  <div class="hb"><span class="title">Manjaro Hello</span>${dots}</div>
  <div class="pane centered">
    <span class="logo">${icon('layout-grid', 'l')}</span>
    <h1 class="heading">${t('Welcome to Manjaro!', 'Bienvenue sur Manjaro !')}</h1>
    ${lines(90, 70)}
    <div class="groups">${group('DOCUMENTATION')}${group('SUPPORT')}${group(t('PROJECT', 'PROJET'))}</div>
    <span class="btn primary big">${icon('hard-drive-download', 's')}${t('Launch installer', "Lancer l'installateur")}${mark(1, 'r')}</span>
  </div>`);
    },
  },
  {
    name: 'agama',
    localized: true,
    styles: STYLES,
    html: (t) => {
      const nav = [
        [t('Overview', 'Aperçu'), 'list-checks', 0],
        [t('Localization', 'Localisation'), 'languages', 0],
        [t('Network', 'Réseau'), 'network', 0],
        [t('Storage', 'Stockage'), 'hard-drive', 1],
        [t('Software', 'Logiciels'), 'package', 2],
        [t('Users', 'Utilisateurs'), 'users', 3],
      ].map(([label, glyph, n], i) => `<span class="nav${i === 0 ? ' on' : ''}">${icon(glyph, 's')}${label}${n ? mark(n, 'r') : ''}</span>`).join('');
      const card = (glyph, a, b) => `<div class="card">${icon(glyph, 'm')}<span class="text"><span class="line w${a}"></span><span class="line w${b}"></span></span></div>`;
      return `<div class="appwin t-agama full">
  <div class="hb"><span>openSUSE Leap 16.0</span><span class="btn primary install">${t('Install', 'Installer')}${mark(4, 'l')}</span></div>
  <div class="content">
    <div class="sidebar">${nav}</div>
    <div class="pane">
      <h1 class="heading">${t('Overview', 'Aperçu')}</h1>
      ${card('languages', 60, 40)}${card('hard-drive', 70, 50)}${card('package', 50, 60)}${card('users', 40, 30)}
    </div>
  </div>
</div>`;
    },
  },
  {
    name: 'yast-role',
    localized: true,
    styles: STYLES,
    html: (t) => {
      const steps = [
        t('Language, Keyboard', 'Langue, clavier'),
        t('Online Repositories', 'Dépôts en ligne'),
        t('System Role', 'Rôle système'),
        t('Suggested Partitioning', 'Partitionnement suggéré'),
        t('Clock and Time Zone', 'Horloge et fuseau horaire'),
        t('Local User', 'Utilisateur local'),
        t('Installation Settings', "Paramètres d'installation"),
      ].map((s, i) => `<span class="step${i === 2 ? ' on' : i < 2 ? ' done' : ''}">${s}</span>`).join('');
      const roles = [
        t('Desktop with KDE Plasma', 'Bureau avec KDE Plasma'),
        t('Desktop with GNOME', 'Bureau avec GNOME'),
        t('Desktop with Xfce', 'Bureau avec Xfce'),
        t('Generic Desktop', 'Bureau générique'),
        t('Server', 'Serveur'),
        t('Transactional Server', 'Serveur transactionnel'),
      ].map((r, i) => `<span class="role">${radio(i === 0)}${r}${i === 0 ? mark(1, 'r') : ''}</span>`).join('');
      return `<div class="appwin t-yast full">
  <div class="content">
    <div class="sidebar steps">${steps}</div>
    <div class="pane">
      <h1 class="heading">${t('System Role', 'Rôle système')}</h1>
      <div class="roles">${roles}</div>
      <div class="buttons"><span class="btn">${t('Help', 'Aide')}</span>
        <span class="btn push">${t('Abort', 'Interrompre')}</span><span class="btn">${t('Back', 'Retour')}</span><span class="btn primary">${t('Next', 'Suivant')}${mark(2, 't')}</span></div>
    </div>
  </div>
</div>`;
    },
  },
  {
    name: 'debian-tasksel',
    localized: true,
    styles: STYLES,
    html: (t) => {
      const tasks = [
        [t('Debian desktop environment', 'environnement de bureau Debian'), true, 0],
        [t('... GNOME', '... GNOME'), true, 1],
        ['... Xfce', false, 0],
        ['... KDE Plasma', false, 0],
        ['... Cinnamon', false, 0],
        [t('web server', 'serveur web'), false, 0],
        [t('SSH server', 'serveur SSH'), false, 2],
        [t('standard system utilities', 'utilitaires usuels du système'), true, 0],
      ];
      return installerTasks(t, 't-di', tasks);
    },
  },
  {
    name: 'kali-tasksel',
    localized: true,
    styles: STYLES,
    html: (t) => installerTasks(t, 't-kali', [
      ['Desktop environment [selecting this item has no effect]', true, 0],
      ["... Xfce (Kali's default desktop environment)", true, 1],
      ['... GNOME', false, 0],
      ['... KDE Plasma', false, 0],
      ['Collection of tools [selecting this item has no effect]', true, 0],
      ['... top10 -- the 10 most popular tools', true, 0],
      ['... default -- recommended tools (available in the live system)', true, 2],
      ['... large -- default selection plus additional tools', false, 0],
    ]),
  },
  {
    name: 'ubuntu-software',
    localized: true,
    styles: STYLES,
    body: 'ubuntu',
    html: (t) => onDesktop('t-yaru', '', `
  <div class="hb"><span class="title">${t('App Center', "Centre d'applications")}</span>${dots}</div>
  <div class="content">
    <div class="rail">${['compass', 'grid-2x2', 'gamepad-2', 'download'].map((g, i) => `<span class="${i === 0 ? 'on' : ''}">${icon(g)}</span>`).join('')}</div>
    ${appPage(t)}
  </div>`),
  },
  {
    name: 'gnome-software',
    localized: true,
    styles: STYLES,
    body: 'fedora',
    html: (t) => onDesktop('t-adw', '', `
  <div class="hb"><span class="switcher"><span class="on">${icon('compass', 's')}${t('Explore', 'Explorer')}</span><span>${icon('hard-drive', 's')}${t('Installed', 'Installés')}</span><span>${icon('refresh-cw', 's')}${t('Updates', 'Mises à jour')}</span></span>${dots}</div>
  ${appPage(t)}`),
  },
  {
    name: 'gnome-software-updates',
    localized: true,
    styles: STYLES,
    body: 'fedora',
    html: (t) => {
      const rows = [70, 50, 60, 40].map((w) => `<div class="row"><span class="app small a-settings">${icon('package')}</span><span class="line w${w}"></span></div>`).join('');
      return onDesktop('t-adw', '', `
  <div class="hb"><span class="switcher"><span>${icon('compass', 's')}${t('Explore', 'Explorer')}</span><span>${icon('hard-drive', 's')}${t('Installed', 'Installés')}</span><span class="on">${icon('refresh-cw', 's')}${t('Updates', 'Mises à jour')}${mark(1, 'b')}</span></span>${dots}</div>
  <div class="pane">
    <div class="buttons top"><h2 class="heading">${t('Updates', 'Mises à jour')}</h2><span class="btn primary push">${t('Restart &amp; Update', 'Redémarrer et mettre à jour')}${mark(2, 'b')}</span></div>
    <div class="list">${rows}</div>
  </div>`);
    },
  },
  {
    name: 'discover',
    localized: true,
    styles: STYLES,
    body: 'lubuntu',
    html: (t) => onDesktop('t-breeze', '', `
  <div class="hb"><span class="title">Discover</span>${dots}</div>
  <div class="content">
    <div class="sidebar">
      <span class="entry focus">${icon('search', 's')}gimp<span class="cursor"></span>${mark(1, 'r')}</span>
      ${['house', 'shapes', 'gamepad-2', 'pen-tool', 'globe'].map((g, i) => `<span class="nav${i === 0 ? '' : ''}">${icon(g, 's')}<span class="line w70"></span></span>`).join('')}
    </div>
    ${appPage(t, { search: false })}
  </div>`),
  },
  {
    name: 'mint-software',
    localized: true,
    styles: STYLES,
    body: 'mint',
    html: (t) => onDesktop('t-mint', '', `
  <div class="hb"><span class="title">${t('Software Manager', 'Logithèque')}</span>${dots}</div>
  ${appPage(t)}`),
  },
  {
    name: 'mint-updates',
    localized: true,
    styles: STYLES,
    body: 'mint',
    html: (t) => {
      const rows = [60, 45, 70, 50].map((w) => `<div class="row">${check(true)}${icon('package', 's')}<span class="line w${w}"></span><span class="line w20 end"></span></div>`).join('');
      return `<div class="wall"></div>
<div class="appwin t-mint mint-updates">
  <div class="hb"><span class="title">${t('Update Manager', 'Gestionnaire de mises à jour')}</span>${dots}</div>
  <div class="toolbar">
    <span class="tool">${icon('eraser', 'm')}${t('Clear', 'Effacer')}</span>
    <span class="tool">${icon('list-checks', 'm')}${t('Select All', 'Tout sélectionner')}</span>
    <span class="tool">${icon('refresh-cw', 'm')}${t('Refresh', 'Rafraîchir')}</span>
    <span class="tool">${icon('download', 'm')}${t('Install Updates', 'Installer les mises à jour')}${mark(2, 'b')}</span>
  </div>
  <div class="pane"><div class="list">${rows}</div></div>
</div>
<div class="panel bottom cinnamon-panel">
  <span class="menu-button">${icon('leaf')}</span>
  <span class="tray"><span class="shield ring">${icon('shield-alert')}${mark(1, 'l')}</span>${icon('wifi')}${icon('volume-2')}<span class="clock">10:42</span></span>
</div>`;
    },
  },
  {
    name: 'ubuntu-updates',
    localized: true,
    styles: STYLES,
    body: 'ubuntu',
    html: (t) => onDesktop('t-yaru', 'dialog', `
  <div class="hb"><span class="title">${t('Software Updater', 'Gestionnaire de mises à jour')}</span>${dots}</div>
  <div class="pane">
    <div class="app-head"><span class="updater">${icon('refresh-cw', 'l')}</span>
      <span class="name">${t('Updated software is available for this computer. Do you want to install it now?',
        'Des mises à jour de logiciels sont disponibles pour cet ordinateur. Voulez-vous les installer maintenant ?')}</span></div>
    <div class="buttons"><span class="btn">${t('Settings…', 'Paramètres…')}</span><span class="btn push">${t('Remind Me Later', 'Me le rappeler plus tard')}</span><span class="btn primary">${t('Install Now', 'Installer maintenant')}${mark(1, 't')}</span></div>
  </div>`),
  },
  {
    name: 'pamac',
    localized: true,
    styles: STYLES,
    body: 'manjaro',
    html: (t) => onDesktop('t-manjaro', '', `
  <div class="hb"><span class="switcher"><span class="on">${t('Browse', 'Parcourir')}</span><span>${t('Installed', 'Installés')}</span><span>${t('Updates', 'Mises à jour')} <span class="badge">3</span>${mark(3, 'r')}</span></span>${dots}</div>
  <div class="pane">
    <span class="entry focus search-field">${icon('search', 's')}gimp<span class="cursor"></span>${mark(1, 'r')}</span>
    <div class="list">
      <div class="row"><span class="gimp small">${icon('palette')}</span><span class="name"><b>GIMP</b><span class="line w80"></span></span><span class="btn primary end">${t('Install', 'Installer')}${mark(2, 'l')}</span></div>
      <div class="row"><span class="gimp small other">${icon('brush')}</span><span class="name"><b class="line w50"></b><span class="line w70"></span></span><span class="btn end">${t('Install', 'Installer')}</span></div>
    </div>
    <div class="buttons"><span class="btn primary push">${t('Apply', 'Appliquer')}</span></div>
  </div>`),
  },
  {
    name: 'cosmic-store',
    localized: true,
    styles: STYLES,
    body: 'pop',
    html: (t) => onDesktop('t-cosmic', '', `
  <div class="hb"><span class="title">COSMIC Store</span>${dots}</div>
  <div class="content">
    <div class="sidebar">
      <span class="nav on">${icon('compass', 's')}${t('Explore', 'Explorer')}</span>
      <span class="nav">${icon('hard-drive', 's')}${t('Installed', 'Installées')}</span>
      <span class="nav">${icon('refresh-cw', 's')}${t('Updates', 'Mises à jour')}<span class="badge">3</span>${mark(3, 'r')}</span>
      <span class="nav"><span class="line w70"></span></span><span class="nav"><span class="line w60"></span></span><span class="nav"><span class="line w80"></span></span>
    </div>
    ${appPage(t)}
  </div>`),
  },
];

/** The software selection of the Debian installer (also used by Kali): tasks are [label, ticked, mark]. */
function installerTasks(t, theme, tasks) {
  const rows = tasks.map(([label, on, n]) => `<div class="row">${check(on)}<span>${label}</span>${n ? mark(n, 'ir') : ''}</div>`).join('');
  return `<div class="appwin ${theme} full">
  <div class="banner"></div>
  <div class="pane">
    <h2 class="heading">${t('Software selection', 'Sélection des logiciels')}</h2>
    ${lines(90, 60)}
    <b>${t('Choose software to install:', 'Logiciels à installer :')}</b>
    <div class="list tasks">${rows}</div>
    <div class="buttons"><span class="btn">${t('Screenshot', "Capture d'écran")}</span><span class="btn push">${t('Go Back', 'Revenir en arrière')}</span><span class="btn primary">${t('Continue', 'Continuer')}${mark(3, 't')}</span></div>
  </div>
</div>`;
}
