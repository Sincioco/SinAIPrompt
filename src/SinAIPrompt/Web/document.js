import { request, escapeHtml } from './bridge.js';

export const documentStyles = `body{font-family:Segoe UI,Arial,sans-serif;font-size:16px;line-height:1.55;margin:32px;color:#20252c;background:#fff;overflow-wrap:break-word}img{max-width:100%;height:auto}pre[data-sin-code]{white-space:pre;overflow:auto;padding:18px;border:1px solid #d8dfe6;border-radius:6px;background:#f6f8fa;color:#24292f;font:14px/1.6 Consolas,monospace;tab-size:4}pre[data-sin-code] code{font:inherit}.rich-source-token-keyword,.rich-source-token-property{color:#0954b5}.rich-source-token-comment{color:#50784a;font-style:italic}.rich-source-token-string{color:#a12623}.rich-source-token-number{color:#8250a3}table{border-collapse:collapse}td,th{border:1px solid #aaa;padding:6px 10px}`;
export const editingStyles = `body{min-height:calc(100vh - 80px);outline:none}img[data-sin-selected]{outline:3px solid #156bc1;outline-offset:3px}pre[data-sin-code]{cursor:pointer}a{cursor:text}`;

export function normalizeIndent(code) {
  const lines = String(code).replace(/\r\n?/g, '\n').replace(/\t/g, '    ').split('\n');
  while (lines.length && !lines[0].trim()) lines.shift();
  while (lines.length && !lines.at(-1).trim()) lines.pop();
  const indents = lines.filter(l => l.trim()).map(l => /^ */.exec(l)[0].length);
  const minimum = indents.length ? Math.min(...indents) : 0;
  return lines.map(l => l.slice(Math.min(minimum, /^ */.exec(l)[0].length))).join('\n');
}
export function parseHtml(html) { return new DOMParser().parseFromString(html || '<!doctype html><html><head><meta charset="utf-8"><title>Prompt</title></head><body><p><br></p></body></html>', 'text/html'); }
export function ensureStyle(doc) {
  if (!doc.querySelector('style[data-sin-document]')) { const style = doc.createElement('style'); style.dataset.sinDocument = '1'; style.textContent = documentStyles; doc.head.append(style); }
}
export function serialize(doc) {
  const clone = doc.documentElement.cloneNode(true);
  clone.querySelectorAll('[data-sin-runtime]').forEach(el => el.remove());
  clone.querySelectorAll('[data-sin-selected]').forEach(el => el.removeAttribute('data-sin-selected'));
  clone.querySelector('body').removeAttribute('contenteditable');
  clone.querySelector('body').removeAttribute('spellcheck');
  return '<!DOCTYPE html>\n' + clone.outerHTML;
}
export async function loadImage(source) {
  const image = new Image();
  image.src = source;
  await image.decode(); return image;
}
export async function toPng(source) {
  if (!source.startsWith('data:') && !source.startsWith('blob:')) source = await request('read-image', {source});
  const image = await loadImage(source);
  const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
  if (!canvas.width || !canvas.height) throw Error('The image has no usable dimensions.');
  canvas.getContext('2d').drawImage(image, 0, 0);
  const data = canvas.toDataURL('image/png');
  if (data === 'data:,') throw Error('The image is too large to render.');
  return {data, width:canvas.width, height:canvas.height};
}
export async function portableHtml(html, base) {
  const doc = parseHtml(html); ensureStyle(doc);
  const actualBase = doc.querySelector('base[href]') ? new URL(doc.querySelector('base').getAttribute('href'), base).href : base;
  const resolve = source => new URL(source, actualBase).href;
  for (const img of doc.querySelectorAll('img')) {
    const source = img.getAttribute('src');
    if (source) img.setAttribute('src', (await toPng(resolve(source))).data);
    img.removeAttribute('srcset');
  }
  // Prefer the now-embedded fallback image in picture elements.
  doc.querySelectorAll('picture source').forEach(source => source.remove());
  for (const link of doc.querySelectorAll('link[rel="stylesheet"]')) {
    const url = resolve(link.getAttribute('href'));
    const response = await fetch(url); if (!response.ok) throw Error('Could not embed stylesheet: ' + url);
    const style = doc.createElement('style'); style.textContent = await embedCss(await response.text(), url); link.replaceWith(style);
  }
  for (const style of doc.querySelectorAll('style')) style.textContent = await embedCss(style.textContent, actualBase);
  for (const el of doc.querySelectorAll('[style]')) el.setAttribute('style', await embedCss(el.getAttribute('style'), actualBase));
  doc.querySelectorAll('base').forEach(el => el.remove());
  return serialize(doc);
}
async function embedCss(css, base) {
  const matches = [...css.matchAll(/url\(\s*(['"]?)(.*?)\1\s*\)/gi)];
  for (const match of matches) {
    if (!match[2] || /^(data:|#)/i.test(match[2])) continue;
    const png = await toPng(new URL(match[2], base).href);
    css = css.replace(match[0], `url("${png.data}")`);
  }
  return css;
}
export function pasteSafeHtml(html) {
  const doc = parseHtml(html);
  doc.querySelectorAll('script,iframe,object,embed,link,meta,base').forEach(el => el.remove());
  doc.querySelectorAll('*').forEach(el => [...el.attributes].forEach(a => { if (/^on/i.test(a.name) || /javascript:/i.test(a.value)) el.removeAttribute(a.name); }));
  return doc.body.innerHTML;
}
export function codeHtml(text, language, highlighted) { return `<pre data-sin-code="${escapeHtml(language)}" contenteditable="false"><code>${highlighted || escapeHtml(text)}</code></pre>`; }
