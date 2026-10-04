// Terminals and text consoles. Marks: the command (1), the password (2), the confirmation (3).
import { mark } from '../lib/page.mjs';

const STYLES = ['desktop', 'console'];
const CURSOR = '<span class="cursor-block"></span>';
const dots = '<span class="end"><i></i><i></i><i></i></span>';

/** A terminal window; lines are HTML, already escaped. */
function terminal(cls, title, lines) {
  return `<div class="wall"></div>
<div class="term ${cls}">
  <div class="hb"><span class="title">${title}</span>${dots}</div>
  <pre>${lines.join('\n')}</pre>
</div>`;
}

const marked = (text, n) => `<span>${text}${mark(n)}</span>`;

const sudo = (t) => marked(t('[sudo] password for user: ', '[sudo] Mot de passe de user : '), 2);

/** apt's answer to an install, then its question. */
function aptOutput(t, packages) {
  return [
    sudo(t),
    t('Reading package lists... Done', 'Lecture des listes de paquets... Fait'),
    t('Building dependency tree... Done', "Construction de l'arbre des dépendances... Fait"),
    t('Reading state information... Done', "Lecture des informations d'état... Fait"),
    t('The following NEW packages will be installed:', 'Les NOUVEAUX paquets suivants seront installés :'),
    `  ${packages}`,
    t('0 upgraded, 12 newly installed, 0 to remove and 0 not upgraded.',
      '0 mis à jour, 12 nouvellement installés, 0 à enlever et 0 non mis à jour.'),
    t('Need to get 21.4 MB of archives.', 'Il est nécessaire de prendre 21,4 Mo dans les archives.'),
    t('After this operation, 98.2 MB of additional disk space will be used.',
      "Après cette opération, 98,2 Mo d'espace disque supplémentaires seront utilisés."),
    marked(`${t('Do you want to continue? [Y/n] ', 'Souhaitez-vous continuer ? [O/n] ')}${CURSOR}`, 3),
  ];
}

export default [
  {
    name: 'terminal-apt',
    localized: true,
    styles: STYLES,
    body: 'ubuntu',
    html: (t) => terminal('', 'user@pc: ~', [
      marked('<span class="c-user">user@pc</span>:<span class="c-path">~</span>$ sudo apt install gimp', 1),
      ...aptOutput(t, 'gimp gimp-data libbabl-0.1-0 libgegl-0.4-0 libgimp-3.0-0 …'),
    ]),
  },
  {
    name: 'terminal-kali',
    localized: true,
    styles: STYLES,
    body: 'kali',
    html: (t) => terminal('kali-term', 'user@kali: ~', [
      '<span class="c-kali">┌──(</span><span class="c-user">user㉿kali</span><span class="c-kali">)-[</span>~<span class="c-kali">]</span>',
      marked('<span class="c-kali">└─$</span> sudo apt install kali-tools-wireless', 1),
      ...aptOutput(t, 'aircrack-ng bully kismet reaver wifite kali-tools-wireless …'),
    ]),
  },
  {
    name: 'terminal-dnf',
    localized: true,
    styles: STYLES,
    body: 'fedora',
    html: (t) => terminal('', 'user@fedora: ~', [
      marked('[user@fedora ~]$ sudo dnf install gimp', 1),
      sudo(t),
      t('Updating and loading repositories:', 'Mise à jour et chargement des dépôts :'),
      t('Repositories loaded.', 'Dépôts chargés.'),
      t('Package          Arch    Version            Repository      Size', 'Paquet           Arch    Version            Dépôt         Taille'),
      t('Installing:', 'Installation :'),
      ' gimp            x86_64  3:3.0.6-1.fc44     fedora      87.0 MiB',
      t('Installing dependencies:', 'Installation des dépendances :'),
      ' babl            x86_64  0.1.114-1.fc44     fedora       6.1 MiB',
      ' gegl04          x86_64  0.4.62-1.fc44      fedora      11.8 MiB',
      '',
      t('Transaction Summary:', 'Résumé de la transaction :'),
      t(' Installing:        12 packages', ' Installation :     12 paquets'),
      '',
      marked(`${t('Is this ok [y/N]: ', 'Est-ce correct [o/N] : ')}${CURSOR}`, 3),
    ]),
  },
  {
    name: 'terminal-pacman',
    localized: true,
    styles: STYLES,
    body: 'manjaro',
    html: (t) => terminal('konsole', 'user@pc: ~ — Konsole', [
      marked('[user@pc ~]$ sudo pacman -S gimp', 1),
      sudo(t),
      t('resolving dependencies...', 'résolution des dépendances...'),
      t('looking for conflicting packages...', 'recherche des conflits entre paquets...'),
      '',
      t('Packages (12) babl-0.1.114-1  gegl-0.4.62-1  gimp-3.0.6-1  …', 'Paquets (12) babl-0.1.114-1  gegl-0.4.62-1  gimp-3.0.6-1  …'),
      '',
      t('Total Download Size:    25.10 MiB', 'Taille totale du téléchargement :  25,10 MiB'),
      t('Total Installed Size:  110.42 MiB', 'Taille totale installée :         110,42 MiB'),
      '',
      marked(`${t(':: Proceed with installation? [Y/n] ', ":: Procéder à l'installation ? [O/n] ")}${CURSOR}`, 3),
    ]),
  },
  {
    name: 'terminal-zypper',
    localized: true,
    styles: STYLES,
    body: 'opensuse',
    html: (t) => terminal('suse', 'user@localhost:~', [
      marked('user@localhost:~&gt; sudo zypper install gimp', 1),
      sudo(t),
      t('Loading repository data...', 'Chargement des données du dépôt...'),
      t('Reading installed packages...', 'Lecture des paquets installés...'),
      t('Resolving package dependencies...', 'Résolution des dépendances de paquets...'),
      '',
      t('The following 12 NEW packages are going to be installed:', 'Les 12 NOUVEAUX paquets suivants vont être installés :'),
      '  babl gegl gimp gimp-lang libbabl-0_1-0 libgegl-0_4-0 …',
      '',
      t('12 new packages to install.', '12 nouveaux paquets à installer.'),
      t('Overall download size: 25.1 MiB. Already cached: 0 B.', 'Taille totale du téléchargement : 25,1 MiB. Déjà en cache : 0 B.'),
      marked(`${t('Continue? [y/n/v/...? shows all options] (y): ', 'Continuer ? [o/n/v/...? affiche toutes les options] (o) : ')}${CURSOR}`, 3),
    ]),
  },
  {
    name: 'ssh-powershell',
    styles: STYLES,
    body: 'zorin',
    html: () => `<div class="wall"></div>
<div class="term wt">
  <div class="hb"><span class="tab">Windows PowerShell</span><span class="end"><i></i><i></i><i></i></span></div>
  <pre>${[
    marked('PS C:\\Users\\user&gt; ssh user@192.168.1.50', 1),
    "The authenticity of host '192.168.1.50 (192.168.1.50)' can't be established.",
    'ED25519 key fingerprint is SHA256:q3Hk1tV8rX0c9W2yLmN5pZbF7aJ4dE6sU1oT2gR8vYc.',
    'This key is not known by any other names.',
    marked('Are you sure you want to continue connecting (yes/no/[fingerprint])? yes', 2),
    "Warning: Permanently added '192.168.1.50' (ED25519) to the list of known hosts.",
    marked("user@192.168.1.50's password:", 3),
    '',
    'Last login: Sat Oct  3 18:12:40 2026',
    `<span class="c-user">user@server</span>:<span class="c-path">~</span>$ ${CURSOR}`,
  ].join('\n')}</pre>
</div>`,
  },
  {
    name: 'arch-console',
    styles: STYLES,
    html: () => {
      const prompt = '<span class="c-root">root</span>@archiso <span class="c-path">~</span> #';
      return `<div class="console"><pre>${[
        'To install <span class="c-path">Arch Linux</span> follow the installation guide:',
        'https://wiki.archlinux.org/title/Installation_guide',
        '',
        'For Wi-Fi, authenticate to the wireless network using the iwctl utility.',
        'Ethernet, WLAN and WWAN interfaces using DHCP should work automatically.',
        '',
        marked(`${prompt} loadkeys fr`, 1),
        marked(`${prompt} iwctl --passphrase "********" station wlan0 connect "Wifi-5G"`, 2),
        marked(`${prompt} ping -c 3 archlinux.org`, 3),
        'PING archlinux.org (95.217.163.246) 56(84) bytes of data.',
        '64 bytes from archlinux.org (95.217.163.246): icmp_seq=1 ttl=52 time=31.2 ms',
        '64 bytes from archlinux.org (95.217.163.246): icmp_seq=2 ttl=52 time=30.8 ms',
        '64 bytes from archlinux.org (95.217.163.246): icmp_seq=3 ttl=52 time=31.0 ms',
        '',
        `${prompt} archinstall${CURSOR}`,
      ].join('\n')}</pre></div>`;
    },
  },
  {
    name: 'arch-archinstall',
    styles: STYLES,
    html: () => {
      const items = [
        ['Locales', 'Keyboard layout: us'],
        ['Mirrors and repositories', ''],
        ['Disk configuration', '', 1],
        ['Swap', 'Enabled'],
        ['Bootloader', 'Systemd-boot'],
        ['Hostname', 'archlinux'],
        ['Authentication', '', 2],
        ['Profile', '', 3],
        ['Applications', ''],
        ['Kernels', 'linux'],
        ['Network configuration', 'Not configured', 4],
        ['Timezone', 'UTC'],
        ['', ''],
        ['Install', '', 5],
        ['Abort', ''],
      ];
      const rows = items.map(([name, value, n], i) => {
        const text = `${i === 2 ? '&gt; ' : '  '}${value ? name.padEnd(26) + value : name}`;
        return n ? `<span class="${i === 2 ? 'ai-sel' : ''}">${text}${mark(n)}</span>` : text;
      });
      return `<div class="console"><pre>${['Press ? for help', '', ...rows].join('\n')}</pre></div>`;
    },
  },
  {
    name: 'ubuntu-server-installer',
    styles: STYLES,
    html: () => `<div class="subiquity">
  <div class="header"><span>SSH configuration</span><span>[ Help ]</span></div>
  <div class="body">
    <p>You can choose to install the OpenSSH server package to enable secure remote access to your server.</p>
    <span class="field focus">[X]  Install OpenSSH server${mark(1)}</span>
    <span class="field">[ ]  Allow password authentication over SSH</span>
    <span class="field">Import SSH key  [ No                ▾ ]</span>
  </div>
  <div class="buttons"><span class="focus">[ Done ]${mark(2)}</span><span>[ Back ]</span></div>
</div>`,
  },
];
