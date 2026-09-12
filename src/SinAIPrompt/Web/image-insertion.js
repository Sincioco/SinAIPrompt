import {request,ask,escapeHtml} from './bridge.js';
import {toPng} from './document.js';

// One editor owns its storage preference and image-insertion workflow.
export function createImageInsertion({saveSelection,command,ready}){
  let storage='';
  async function storageChoice(){
    if(storage)return storage;
    const answer=await ask('Store Image','<p>Choose how this image is stored with your HTML Document.</p><p><b>Inline:</b> embed the lossless PNG in the HTML file.<br><b>Separate file:</b> store a PNG in a folder named after the HTML file.</p>',[{value:'inline',label:'Inline (Base64)'},{value:'separate',label:'Separate PNG File'}]);
    return ['inline','separate'].includes(answer.choice)?answer.choice:null;
  }
  async function storeImage(png,mode){
    const source=mode==='separate'?await request('save-image',{data:png}):png;
    await ready();return source;
  }
  async function insertImage(source,mode=null){
    saveSelection();const png=await toPng(source);
    mode=mode||storage;
    const existing=mode!=='inline'?await request('reuse-image',{data:png.data}):null;
    mode=mode||(existing?'separate':await storageChoice());if(!mode)return;
    const src=existing||await storeImage(png.data,mode);
    command('insertHTML',`<img src="${escapeHtml(src)}" data-sin-storage="${mode}" style="width:${png.width}px;max-width:100%;height:auto" alt=""><p><br></p>`);
  }
  return {insertImage,storageChoice,storeImage,setStorage:value=>storage=['inline','separate'].includes(value)?value:''};
}
