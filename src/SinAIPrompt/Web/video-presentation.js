import {escapeHtml} from './bridge.js';
import {youtubeId,youtubeCard} from './youtube.js';

export const videoMode=node=>node?.dataset.sinVideoMode||(node?.tagName==='A'?'url':'embed');
export function videoData(node){
  const id=node.dataset.sinYoutube||youtubeId(node.href);
  return {id,title:node.querySelector('[data-video-title]')?.textContent||node.dataset.videoTitle||'YouTube video',
    author:node.querySelector('[data-video-author]')?.textContent||node.dataset.videoAuthor||'',
    image:node.querySelector('img')?.getAttribute('src')||node.dataset.videoImage||`https://i.ytimg.com/vi/${id}/hqdefault.jpg`};
}
const badge='<span style="display:inline-block;background:#f03;border-radius:4px;color:white;font:10px/12px Arial;width:17px;text-align:center;vertical-align:middle">▶</span>';
export function videoMarkup(video,mode){
  const url=`https://www.youtube.com/watch?v=${video.id}`,title=escapeHtml(video.title||'YouTube video'),author=escapeHtml(video.author||'YouTube');
  const data=`data-sin-youtube="${video.id}" data-sin-video-mode="${mode}" data-video-title="${title}" data-video-author="${author}" data-video-image="${escapeHtml(video.image)}"`;
  if(mode==='url')return `<a ${data} href="${url}">${url}</a>`;
  if(mode==='inline')return `<a ${data} href="${url}" contenteditable="false" style="color:#065fd4;text-decoration:none;white-space:normal">${badge} <span data-video-title>${title}</span></a>`;
  if(mode==='card')return `<figure ${data} contenteditable="false" style="display:flex;box-sizing:border-box;width:760px;max-width:100%;margin:1em auto;border:1px solid #d1d9e0;border-radius:8px;overflow:hidden;background:white;color:#172b4d;font:14px/1.45 'Segoe UI',sans-serif">
    <figcaption style="flex:1;min-width:0;padding:16px;display:flex;flex-direction:column;gap:12px"><strong style="color:#065fd4">${badge} <span data-video-title>${title}</span></strong><span>Created by <span data-video-author>${author}</span></span><span style="margin-top:auto;display:flex;align-items:center;gap:12px;font-size:12px">${badge} YouTube <a href="${url}" data-video-action="fullscreen" style="margin-left:auto;border:1px solid #d1d9e0;border-radius:4px;padding:2px 10px;color:#42526e;text-decoration:none">Open preview modal</a></span></figcaption>
    <div data-video-surface style="position:relative;width:30%;min-height:200px;background:#eee"><img src="${escapeHtml(video.image)}" alt="${title}" style="width:100%;height:100%;object-fit:cover;display:block"></div></figure>`;
  return youtubeCard(video).replace('<figure ','<figure data-sin-video-mode="embed" ').replace(/<p><br><\/p>$/,'');
}
const drawings={
  mode:'<rect x="3" y="4" width="18" height="16" rx="1"/><path d="M3 9h18M7 6h1M11 6h1"/>',
  align:'<path d="M3 5h18M3 10h12M3 15h18M3 20h12"/>',
  edit:'<path d="M10 5H4v15h15v-6M11 14l1-5 8-8 3 3-8 8zM18 3l3 3"/>',
  unlink:'<path d="m9 15 6-6M8 18l-1 1a4 4 0 0 1-5-5l2-2M16 6l1-1a4 4 0 0 1 5 5l-2 2" stroke-dasharray="2 3"/><path d="M5 3v4H1M19 21v-4h4"/>',
  external:'<path d="M10 4H3v17h17v-8M14 2h8v8M22 2 11 13"/>',
  copy:'<rect x="3" y="2" width="14" height="15" rx="1"/><path d="M8 21h13V7"/>',
  more:'<circle cx="4" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="20" cy="12" r="1"/>'
};
export const videoIcon=name=>`<svg viewBox="0 0 24 24" aria-hidden="true">${drawings[name]}</svg>`;
