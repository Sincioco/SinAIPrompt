import {request} from './bridge.js';

// src stays portable HTML. A transient srcset displays the original through the
// native editor's file mapping, including files outside the document folder.
const selector='img[data-sin-storage=reference],img[src^="file:" i]';
const savedSrcset='data-sin-original-srcset';
async function prepareImage(image,base,requireConnected=false){
  const source=image.getAttribute('src'),reference=image.dataset.sinStorage==='reference';
  try{
    const mapped=await request('map-original-image',{source,base});
    if(image.getAttribute('src')!==source||(requireConnected&&!image.isConnected))return;
    // Metadata survives document cloning so serialize/clipboard cleanup can
    // restore an ordinary image's authored srcset exactly, even when empty.
    if(!reference&&!image.hasAttribute(savedSrcset))image.setAttribute(savedSrcset,JSON.stringify(image.getAttribute('srcset')));
    image.srcset=mapped.display;
  }catch{if(reference&&image.getAttribute('src')===source)image.removeAttribute('srcset');}
}
export async function prepareOriginalImages(root){
  const base=root.querySelector('base[href]')?.getAttribute('href');
  await Promise.all([...root.querySelectorAll(selector)].map(image=>prepareImage(image,base)));
}
export function cleanOriginalImages(root){
  root.querySelectorAll('img['+savedSrcset+']').forEach(image=>{
    try{const value=JSON.parse(image.getAttribute(savedSrcset));if(value===null)image.removeAttribute('srcset');else image.setAttribute('srcset',value);}
    catch{image.removeAttribute('srcset');}
    image.removeAttribute(savedSrcset);
  });
  root.querySelectorAll('img[data-sin-storage=reference]').forEach(image=>image.removeAttribute('srcset'));
}
export function observeOriginalImages(doc){
  new MutationObserver(records=>{
    for(const record of records)for(const node of record.addedNodes){
      if(node.nodeType!==1)continue;
      const images=node.matches(selector)?[node]:[...node.querySelectorAll(selector)];
      for(const image of images)prepareImage(image,doc.querySelector('base[href]')?.getAttribute('href'),true);
    }
  }).observe(doc.body,{childList:true,subtree:true});
}
