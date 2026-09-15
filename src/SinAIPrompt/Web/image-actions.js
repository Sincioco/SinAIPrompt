import {request,report} from './bridge.js';
import {toPng} from './document.js';

// Image actions are temporary editor chrome, separate from document/resize state.
export function createImageActions(frame,{edit,resize,remove}){
  const menu=document.createElement('div');menu.className='image-actions';menu.popover='auto';menu.setAttribute('role','menu');
  menu.innerHTML=[['edit','Edit Image'],['resize','Resize Image'],['rename','Rename Image'],['delete','Delete Image'],['fullscreen','View Full Screen'],['external','View Externally']]
    .map(([action,label])=>`<button role="menuitem" data-image-action="${action}">${label}</button>`).join('');
  document.body.append(menu);let image=null;
  function close(){menu.hidePopover();image=null;}
  function open(target,event){
    close();if(!target)return;image=target;
    const rect=target.getBoundingClientRect(),host=frame.getBoundingClientRect();
    menu.showPopover();
    for(const action of ['edit','resize','rename','delete'])menu.querySelector(`[data-image-action=${action}]`).disabled=!target.ownerDocument.body.isContentEditable||action==='rename'&&!/^https:\/\/sin-document\.local\/|^file:/i.test(target.src);
    menu.style.left=Math.max(6,Math.min(innerWidth-menu.offsetWidth-6,host.left+(event?.clientX??rect.left)))+'px';
    menu.style.top=Math.max(host.top+6,Math.min(innerHeight-menu.offsetHeight-6,host.top+(event?.clientY??rect.top+12)))+'px';
  }
  menu.addEventListener('click',event=>{
    const action=event.target.closest('[data-image-action]')?.dataset.imageAction,target=image;
    if(!action||!target?.isConnected)return;close();
    if(action==='resize')resize();
    if(action==='edit')edit(target).catch(report);
    if(action==='delete')remove(target);
    if(action==='rename')request('rename-image',{source:target.getAttribute('src')}).catch(report);
    if(action==='fullscreen')showFullScreenImage(target.currentSrc||target.src).catch(report);
    if(action==='external')(async()=>request('open-image',{data:(await toPng(target.currentSrc||target.src)).data}))().catch(report);
  });
  return {open,close};
}

export async function showFullScreenImage(source){
  const view=document.createElement('div');view.className='media-fullscreen';
  const image=document.createElement('img');image.src=source;image.alt='Full screen image';
  const close=document.createElement('button');close.textContent='Close Full Screen (Esc)';close.onclick=()=>document.exitFullscreen();
  view.append(image,close);document.body.append(view);
  const changed=()=>{if(!document.fullscreenElement){view.remove();document.removeEventListener('fullscreenchange',changed);}};
  document.addEventListener('fullscreenchange',changed);
  try{await view.requestFullscreen();}catch(error){view.remove();document.removeEventListener('fullscreenchange',changed);throw error;}
}
