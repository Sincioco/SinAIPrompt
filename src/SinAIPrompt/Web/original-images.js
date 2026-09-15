import {request} from './bridge.js';

// src stays portable HTML. A transient srcset displays the original through the
// native editor's file mapping, including files outside the document folder.
export async function prepareOriginalImages(root){
  const base=root.querySelector('base[href]')?.getAttribute('href');
  await Promise.all([...root.querySelectorAll('img[data-sin-storage=reference]')].map(async image=>{
    try{const mapped=await request('map-original-image',{source:image.getAttribute('src'),base});image.srcset=mapped.display;}
    catch{image.removeAttribute('srcset');} // An unavailable image must not hide the rest of the document.
  }));
}
export function cleanOriginalImages(root){
  root.querySelectorAll('img[data-sin-storage=reference]').forEach(image=>image.removeAttribute('srcset'));
}
export function observeOriginalImages(doc){
  new MutationObserver(records=>{
    for(const record of records)for(const node of record.addedNodes){
      if(node.nodeType!==1)continue;
      const images=node.matches('img[data-sin-storage=reference]')?[node]:[...node.querySelectorAll('img[data-sin-storage=reference]')];
      for(const image of images)if(!image.srcset)request('map-original-image',{source:image.getAttribute('src'),base:doc.querySelector('base[href]')?.getAttribute('href')}).then(mapped=>{if(image.isConnected)image.srcset=mapped.display;}).catch(()=>{});
    }
  }).observe(doc.body,{childList:true,subtree:true});
}
