// Draws the pictures of the Linux tour: every scene of scenes/*.mjs becomes tour/images/<name>.png, or
// <name>.en.png and <name>.fr.png when it contains text. Usage: npm run render [-- part-of-a-name ...]
import { mkdtemp, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium } from 'playwright';
import sharp from 'sharp';
import { page } from './lib/page.mjs';

const root = fileURLToPath(new URL('.', import.meta.url));
const output = join(root, '..', '..', 'tour', 'images');
const WIDTH = 480;
const HEIGHT = 300;
const SCALE = 2;

async function loadScenes() {
  const folder = join(root, 'scenes');
  const scenes = [];
  for (const file of (await readdir(folder)).filter((f) => f.endsWith('.mjs')).sort()) {
    const module = await import(pathToFileURL(join(folder, file)).href);
    scenes.push(...module.default);
  }
  const names = new Set();
  for (const scene of scenes) {
    if (names.has(scene.name)) throw new Error(`Two scenes are named ${scene.name}.`);
    names.add(scene.name);
  }
  return scenes;
}

const filters = process.argv.slice(2);
const scenes = (await loadScenes()).filter((s) => filters.length === 0 || filters.some((f) => s.name.includes(f)));
const work = await mkdtemp(join(tmpdir(), 'tour-images-'));
const browser = await chromium.launch();
const tab = await browser.newPage({ viewport: { width: WIDTH, height: HEIGHT }, deviceScaleFactor: SCALE });

try {
  for (const scene of scenes) {
    for (const lang of scene.localized ? ['en', 'fr'] : ['en']) {
      const file = join(work, `${scene.name}.${lang}.html`);
      await writeFile(file, page(root, scene, lang));
      await tab.goto(pathToFileURL(file).href);
      await tab.evaluate(() => document.fonts.ready);
      const shot = await tab.screenshot({ clip: { x: 0, y: 0, width: WIDTH, height: HEIGHT } });
      const name = scene.localized ? `${scene.name}.${lang}.png` : `${scene.name}.png`;
      // A palette keeps these flat drawings small without visible loss.
      await sharp(shot).png({ palette: true, quality: 92, effort: 10, compressionLevel: 9 }).toFile(join(output, name));
      console.log(name);
    }
  }
} finally {
  await browser.close();
  await rm(work, { recursive: true, force: true });
}
