// Renders one variant of a design prototype to a PNG, to set beside the launcher.
//
//   node tools/testbed/reference.mjs <folder under docs> <prototype> <block id> <out.png>
//        [--props '{"transparencia":"Media"}'] [--frame N] [--click 'text'] [--rects 'css selector']
//
// The block id is what the prototype's own heading links to (68a, 97b...). --frame picks which
// child of that block is the design, default the last: the first is its caption. --rects prints
// each match's box and text colour, relative to that design, to check a measure without guessing.
//
// Edge, because it is on every Windows 11 and playwright-core needs no browser download. The
// prototypes load React and Babel from unpkg and a font from Google, so this needs the network.
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const docs = path.resolve(here, '..', '..', 'docs');
const types = { '.html': 'text/html', '.js': 'text/javascript', '.png': 'image/png', '.jpg': 'image/jpeg' };

const args = process.argv.slice(2);
const option = name => {
  const at = args.indexOf(name);
  return at < 0 ? null : args.splice(at, 2)[1];
};
const props = JSON.parse(option('--props') ?? '{}');
const frame = option('--frame');
const click = option('--click');
const rects = option('--rects');
const [folder, prototype, block, out] = args;
if (!out) {
  console.error('usage: reference.mjs <folder> <prototype> <block id> <out.png> [--props JSON] [--frame N]');
  process.exit(2);
}

const root = path.resolve(docs, folder);
const server = createServer(async (request, response) => {
  const file = path.resolve(root, '.' + decodeURIComponent(new URL(request.url, 'http://x').pathname));
  if (!file.startsWith(root)) { response.writeHead(403).end(); return; }
  try {
    const body = await readFile(file);
    response.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' }).end(body);
  } catch {
    response.writeHead(404).end();
  }
});
await new Promise(ready => server.listen(0, '127.0.0.1', ready));

const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 2700, height: 1600 }, deviceScaleFactor: 1 });
  page.on('pageerror', error => console.error('prototype error:', error.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/${encodeURIComponent(prototype)}`, { waitUntil: 'networkidle' });
  await page.waitForFunction(id => document.getElementById(id), block, { timeout: 30000 });
  if (Object.keys(props).length) {
    await page.evaluate(values => window.__dcSetProps(window.__dcRootName(), values), props);
  }
  await page.waitForTimeout(800);
  if (click) {
    await page.locator(`[id="${block}"]`).getByText(click, { exact: true }).first().click();
    await page.waitForTimeout(400);
  }
  const children = page.locator(`[id="${block}"] > *`);
  const count = await children.count();
  const target = children.nth(frame === null ? count - 1 : Number(frame));
  await mkdir(path.dirname(path.resolve(out)), { recursive: true });
  await target.screenshot({ path: out });
  const box = await target.boundingBox();
  console.log(`${out}: ${prototype} #${block}, ${Math.round(box.width)}x${Math.round(box.height)}`);
  if (rects) {
    const found = await target.locator(rects).evaluateAll((all, origin) => all.map(element => {
      const r = element.getBoundingClientRect(), s = getComputedStyle(element);
      return { x: r.x - origin.x, y: r.y - origin.y, w: r.width, h: r.height, font: s.fontSize, weight: s.fontWeight,
               color: s.color, background: s.backgroundColor, radius: s.borderRadius,
               text: element.textContent.slice(0, 40) };
    }), box);
    for (const item of found) console.log(JSON.stringify(item));
  }
} finally {
  await browser.close();
  server.close();
}
