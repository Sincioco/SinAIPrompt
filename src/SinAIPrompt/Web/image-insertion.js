import {request,ask,escapeHtml} from './bridge.js';
import {toPng,loadImage} from './document.js';

// One editor owns its storage preference and image-insertion workflow.
export function createImageInsertion({saveSelection,command,ready,getDocument}){
  let storage='';
  async function storageChoice(reference=false){
    if(storage&&(storage!=='reference'||reference))return storage;
    const choices=[{value:'inline',label:'Inline (Base64)'},{value:'separate',label:'Separate PNG File'}];
    if(reference)choices.push({value:'reference',label:'Reference Original Image'});
    const answer=await ask('Store Image','<p>Choose how this image is stored with your HTML Document.</p><p><b>Inline:</b> embed the lossless PNG in the HTML file.<br><b>Separate file:</b> store a PNG in a folder named after the HTML file.'+(reference?'<br><b>Reference original:</b> link to the existing image without copying it. If the clipboard has no original path, choose its file.':'')+'</p>',choices);
    return choices.some(choice=>choice.value===answer.choice)?answer.choice:null;
  }
  async function storeImage(png,mode){
    const source=mode==='separate'?await request('save-image',{data:png}):png;
    await ready();return source;
  }
  async function insertImage(source,mode=null){
    saveSelection();mode=mode||storage;
    const png=mode==='reference'?null:await toPng(source);
    const existing=!['inline','reference'].includes(mode)?await request('reuse-image',{data:png.data}):null;
    mode=mode||(existing?'separate':await storageChoice(true));if(!mode)return;
    if(mode==='reference'){
      const choice=await ask('Image Reference','<p>Use a relative path from the HTML document, or the full absolute image path?</p>',[{value:'relative',label:'Relative Reference'},{value:'absolute',label:'Absolute Reference'}]);
      if(!['relative','absolute'].includes(choice.choice))return;
      const result=await request('reference-image',{source,absolute:choice.choice==='absolute',base:getDocument().querySelector('base[href]')?.getAttribute('href')});
      if(!result)return;
      if(choice.choice==='relative'&&result.reference.startsWith('file:')){
        const fallback=await ask('Absolute Reference Required','<p>The image and document are on different drives, or the document uses a non-file base address. Use the absolute image path?</p>',[{value:'absolute',label:'Use Absolute Reference'}]);
        if(fallback.choice!=='absolute')return;
      }
      await ready();const image=await loadImage(result.display);
      command('insertHTML',`<img src="${escapeHtml(result.reference)}" srcset="${escapeHtml(result.display)}" data-sin-storage="reference" style="width:${image.naturalWidth}px;max-width:100%;height:auto" alt=""><p><br></p>`);return;
    }
    const src=existing||await storeImage(png.data,mode);
    command('insertHTML',`<img src="${escapeHtml(src)}" data-sin-storage="${mode}" style="width:${png.width}px;max-width:100%;height:auto" alt=""><p><br></p>`);
  }
  return {insertImage,storageChoice,storeImage,setStorage:value=>storage=['inline','separate','reference'].includes(value)?value:''};
}
