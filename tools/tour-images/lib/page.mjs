// Builds the HTML page of a scene. Pages hold markup only: every style lives in styles/*.css.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = new URL('..', import.meta.url);

/** Returns a function picking the English or French text: t('Install', 'Installer'). */
export function texts(lang) {
  return (en, fr) => (lang === 'fr' ? fr : en);
}

/** A numbered mark, matching a numbered step of the lesson. side: r, l, t, b, tr, tl, br, bl, c. */
export function mark(n, side = 'r') {
  return `<span class="mark ${side}">${n}</span>`;
}

const iconCache = new Map();

/** A Lucide icon (ISC license), drawn with the current text color. */
export function icon(name, cls = '') {
  if (!iconCache.has(name)) {
    const svg = readFileSync(new URL(`node_modules/lucide-static/icons/${name}.svg`, root), 'utf8');
    const inner = svg.replace(/<!--.*?-->/gs, '').replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '').trim();
    iconCache.set(name, inner);
  }
  return `<svg class="ic ${cls}" viewBox="0 0 24 24" aria-hidden="true">${iconCache.get(name)}</svg>`;
}

export function page(folder, scene, lang) {
  const sheets = ['fonts', 'base', ...(scene.styles ?? [])]
    .map((name) => `<link rel="stylesheet" href="${pathToFileURL(join(folder, 'styles', `${name}.css`)).href}">`)
    .join('\n');
  return `<!doctype html>
<html lang="${lang}">
<head>
<meta charset="utf-8">
<title>${scene.name}</title>
${sheets}
</head>
<body class="${scene.body ?? ''}">
${scene.html(texts(lang), lang)}
</body>
</html>`;
}
