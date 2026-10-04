// Boot menus. Their entries are copied from each distribution's ISO (see README.md).
import { mark } from '../lib/page.mjs';

const GRUB_HELP = `Use the ↑ and ↓ keys to select which entry is highlighted.
Press enter to boot the selected OS, \`e' to edit the commands
before booting or \`c' for a command-line.`;

/** entries: [text, mark number or 0], the first one is selected. */
function grub(entries, { version = '2.12', seconds = 30 } = {}) {
  const items = entries
    .map(([text, n], i) => `<li${i === 0 ? ' class="sel"' : ''}><span>${i === 0 ? '*' : ' '}${text}${n ? mark(n) : ''}</span></li>`)
    .join('');
  return `<div class="grub">
  <div class="grub-title">GNU GRUB  version ${version}</div>
  <div class="grub-box"><ul>${items}</ul></div>
  <p class="grub-help">${GRUB_HELP}
   The highlighted entry will be executed automatically in ${seconds}s.</p>
</div>`;
}

/** A themed menu: entries are [text, mark number or 0, extra class]; the first entry without class is selected. */
function themed(theme, banner, entries, hint = '') {
  let selected = false;
  const items = entries
    .map(([text, n, cls = '']) => {
      const sel = !selected && cls === '';
      selected ||= sel;
      return `<li class="${cls}${sel ? ' sel' : ''}"><span>${text}${n ? mark(n) : ''}</span></li>`;
    })
    .join('');
  return `<div class="themed ${theme}">
  ${banner ? `<div class="banner">${banner}</div>` : ''}
  <ul>${items}</ul>
  ${hint ? `<p class="hint">${hint}</p>` : ''}
</div>`;
}

const THEMED_HINT = 'Use ↑ and ↓ to choose, Enter to start.';

export default [
  {
    name: 'firmware-boot-menu',
    styles: ['boot'],
    html: () => `<div class="firmware">
  <div class="box">
    <h1>Please select boot device:</h1>
    <ul>
      <li><span>Windows Boot Manager (NVMe SSD 1TB)${mark(2)}</span></li>
      <li class="sel"><span>UEFI: USB DISK 3.0, Partition 2${mark(1)}</span></li>
      <li><span>USB DISK 3.0</span></li>
      <li><span>Enter Setup</span></li>
    </ul>
  </div>
  <p class="keys">↑ and ↓ to move selection<br>ENTER to select boot device<br>ESC to boot using defaults</p>
</div>`,
  },
  {
    name: 'uefi-secureboot',
    styles: ['boot'],
    html: () => `<div class="setup">
  <div class="tabs"><span>Main</span><span>Advanced</span><span>Boot</span><span class="on">Security${mark(1, 'tr')}</span><span>Exit</span></div>
  <div class="panel">
    <div class="options">
      <div class="row"><span>Administrator Password</span><span class="value">Not Installed</span></div>
      <div class="row"><span>User Password</span><span class="value">Not Installed</span></div>
      <div class="row"><span>TPM Device</span><span class="value">[Enabled]</span></div>
      <div class="row sel"><span>Secure Boot</span><span class="value">[Disabled]${mark(2, 'l')}</span></div>
      <div class="row"><span>Secure Boot Mode</span><span class="value">[Standard]</span></div>
    </div>
    <div class="help">Secure Boot only starts operating systems signed with the keys of the computer.</div>
  </div>
  <div class="footer">↑↓: Select  Enter: Change  F10: Save and Exit  Esc: Exit</div>
</div>`,
  },
  {
    name: 'ubuntu-boot',
    styles: ['boot'],
    html: () => grub([['Try or Install Ubuntu', 1], ['Ubuntu (safe graphics)', 2], ['Boot from next volume'], ['UEFI Firmware Settings']]),
  },
  {
    name: 'ubuntu-server-boot',
    styles: ['boot'],
    html: () => grub([['Try or Install Ubuntu Server', 1], ['Boot from next volume'], ['UEFI Firmware Settings']]),
  },
  {
    name: 'lubuntu-boot',
    styles: ['boot'],
    html: () => grub([['Try or Install Lubuntu', 1], ['Lubuntu (safe graphics)', 2], ['Boot from next volume'], ['UEFI Firmware Settings']]),
  },
  {
    name: 'zorin-boot',
    styles: ['boot'],
    html: () => grub([
      ['Try or Install Zorin OS', 1],
      ['Try or Install Zorin OS (safe graphics)'],
      ['Try or Install Zorin OS (modern NVIDIA drivers)', 2],
      ['Boot from next volume'],
      ['UEFI Firmware Settings'],
    ]),
  },
  {
    name: 'mint-boot-cinnamon',
    styles: ['boot'],
    html: () => grub([
      ['Start Linux Mint 22.3 Cinnamon 64-bit', 1],
      ['Start Linux Mint 22.3 Cinnamon 64-bit (compatibility mode)', 2],
      ['OEM install (for manufacturers)'],
      ['UEFI Firmware Settings'],
    ], { seconds: 10 }),
  },
  {
    name: 'mint-boot-xfce',
    styles: ['boot'],
    html: () => grub([
      ['Start Linux Mint 22.3 Xfce 64-bit', 1],
      ['Start Linux Mint 22.3 Xfce 64-bit (compatibility mode)', 2],
      ['OEM install (for manufacturers)'],
      ['UEFI Firmware Settings'],
    ], { seconds: 10 }),
  },
  {
    name: 'fedora-boot',
    styles: ['boot'],
    html: () => grub([
      ['Start Fedora-Workstation-Live 44', 1],
      ['Test this media & start Fedora-Workstation-Live 44'],
      ['Troubleshooting -->', 2],
    ], { seconds: 60 }),
  },
  {
    name: 'pop-boot',
    styles: ['boot'],
    html: () => grub([['Try or Install Pop!_OS', 1]], { seconds: 10 }),
  },
  {
    name: 'arch-boot',
    styles: ['boot'],
    html: () => `<div class="sdboot">
  <ul>
    <li class="sel"><span>Arch Linux install medium (x86_64, UEFI)${mark(1)}</span></li>
    <li>Arch Linux install medium (x86_64, UEFI) with speech</li>
    <li>Memtest86+</li>
    <li>Reboot Into Firmware Interface</li>
  </ul>
  <p class="timer">Boot in 15 s.</p>
</div>`,
  },
  {
    name: 'debian-live-boot',
    styles: ['boot'],
    html: () => themed('debian-live', 'Debian GNU/Linux Live', [
      ['Live system (amd64)', 1],
      ['Live system (amd64 fail-safe mode)'],
      ['Start installer', 2],
      ['Start installer with speech synthesis'],
      ['Advanced install options ...'],
      ['Utilities...'],
    ], THEMED_HINT),
  },
  {
    name: 'debian-netinst-boot',
    styles: ['boot'],
    html: () => themed('d-i', 'Debian GNU/Linux UEFI Installer menu', [
      ['Graphical install', 1],
      ['Install'],
      ['Advanced options ...'],
      ['Accessible dark contrast installer menu ...'],
      ['Help'],
      ['Install with speech synthesis'],
    ], THEMED_HINT),
  },
  {
    name: 'kali-boot',
    styles: ['boot'],
    html: () => themed('d-i kali-installer', 'Kali Linux installer menu (UEFI mode)', [
      ['Graphical install', 1],
      ['Install'],
      ['Advanced options ...'],
      ['Install with speech synthesis'],
    ], THEMED_HINT),
  },
  {
    name: 'manjaro-boot',
    styles: ['boot'],
    html: () => themed('manjaro', '', [
      ['tz=UTC', 0, 'option'],
      ['keytable=us', 1, 'option'],
      ['lang=en_US', 0, 'option'],
      ['Boot with open source drivers', 2],
      ['Boot with proprietary drivers'],
      ['Detect EFI bootloaders'],
      ['Reboot'],
      ['Power Off'],
    ], THEMED_HINT),
  },
  {
    name: 'leap-boot',
    styles: ['boot'],
    html: () => themed('opensuse', 'openSUSE Leap 16.0', [
      ['Install Leap 16.0 (x86_64)', 1],
      ['Failsafe -- Install Leap 16.0 (x86_64)', 2],
      ['Check Installation Medium'],
      ['Rescue System'],
      ['UEFI Firmware Settings'],
    ], THEMED_HINT),
  },
  {
    name: 'tw-boot',
    styles: ['boot'],
    // The installer selects its second entry by default.
    html: () => themed('opensuse', 'openSUSE Tumbleweed', [
      ['Boot from Hard Disk', 2, 'first'],
      ['Installation', 1],
      ['Upgrade'],
      ['More ...'],
    ], THEMED_HINT),
  },
  {
    name: 'opensuse-snapshots',
    styles: ['boot'],
    html: () => themed('opensuse', 'openSUSE', [
      ['openSUSE Tumbleweed', 0, 'first'],
      ['Advanced options for openSUSE Tumbleweed', 0, 'first'],
      ['Start bootloader from a read-only snapshot', 1],
      ['openSUSE Tumbleweed (2026-10-02 09:14, pre, zypp(zypper))', 2, 'sub sel'],
      ['openSUSE Tumbleweed (2026-09-28 18:40, post, zypp(zypper))', 0, 'sub'],
      ['UEFI Firmware Settings', 0, 'first'],
    ], THEMED_HINT),
  },
];
