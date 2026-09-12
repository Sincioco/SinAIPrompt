import {send,request,native,blobData,escapeHtml,ask,report} from './bridge.js';
import {parseHtml,ensureStyle,editingStyles,serialize,toPng,portableHtml,pasteSafeHtml,renameImageFolder} from './document.js';
import {renameImageFile} from './asset-references.js';
import {createDocumentSearch} from './document-search.js';
import {attachListNumbering,toggleNumbering} from './list-numbering.js';
import {createImageSelection} from './image-selection.js';
import {createImageInsertion} from './image-insertion.js';
import {createImageActions} from './image-actions.js';
import {pasteMarkdown,looksLikeMarkdown,htmlToMarkdown} from './markdown.js';
import {editCodeBlock} from './code-blocks.js';
import {insertLink} from './link-insertion.js';
import {createYouTubePlayer} from './youtube.js';
import {annotate} from './annotation-ui.js';
import {id,outputBounds} from './annotation-model.js';
import {createRibbon} from './ribbon.js';
import {clipboardCommand} from './editor-clipboard.js';

const fontCss=native?await request('editor-fonts'):'';
if(fontCss){const style=document.createElement('style');style.textContent=fontCss;document.head.append(style);}
const frame=document.querySelector('#document'),$=s=>document.querySelector(s);
let doc=null,selection=null,currentRaw='',base='https://sin-document.local/',loading=Promise.resolve(),loadingNow=false;
let changeTimer,lastImageStatus="";
function imageStatus(source=""){if(source!==lastImageStatus){lastImageStatus=source;send('image-status',{source});}}
const exports=new Map();
const search=createDocumentSearch(()=>doc,changed);
const images=createImageSelection(frame,changed,image=>imageStatus(image?.src||''));
const imageActions=createImageActions(frame,{edit:openAnnotation,resize:()=>images.resize()});
const videos=createYouTubePlayer(frame);
const ribbon=createRibbon($('#toolbar'),{getDocument:()=>doc,saveSelection,restoreSelection,command,changed});
const imageInsertion=createImageInsertion({saveSelection,command,ready:()=>loading});
const {insertImage,storageChoice,storeImage}=imageInsertion;
function saveSelection(){if(!doc)return;const s=doc.getSelection();if(s.rangeCount && doc.body.contains(s.anchorNode))selection=s.getRangeAt(0).cloneRange();}
function restoreSelection(){if(!doc)return;doc.body.focus();const s=doc.getSelection();if(selection&&doc.body.contains(selection.startContainer)){s.removeAllRanges();s.addRange(selection);}else {const range=doc.createRange();range.selectNodeContents(doc.body);range.collapse(false);s.removeAllRanges();s.addRange(range);}}
async function focus(){
  await loading;
  if(!doc||!document.hasFocus()||document.fullscreenElement||document.activeElement?.matches('input,select,button')||document.querySelector('dialog[open]'))return;
  if(!selection&&!currentRaw.trim()){
    selection=doc.createRange();selection.selectNodeContents(doc.body.querySelector('p,h1,h2,h3,h4,h5,h6')||doc.body);selection.collapse(true);
  }
  restoreSelection();saveSelection();syncFormatting();
}
function changed(){
  if(!doc||loadingNow)return;search.invalidate();saveSelection();clearTimeout(changeTimer);
  // Keep full-document serialization and native synchronization out of keystrokes.
  changeTimer=setTimeout(()=>{currentRaw=serialize(doc);send('change',{html:currentRaw});},150);
}
function html(flush=false){if(flush)clearTimeout(changeTimer);return loadingNow?currentRaw:doc?serialize(doc):currentRaw;}
async function load(raw,newBase=base){
  search.invalidate();
  clearTimeout(changeTimer);
  videos.close();imageActions.close();images.select(null);imageStatus();currentRaw=raw||'';base=newBase;loadingNow=true;selection=null;
  const input=parseHtml(raw);ensureStyle(input);
  const baseTag=input.createElement('base');baseTag.href=base;baseTag.dataset.sinRuntime='1';
  // An explicit document base wins. Otherwise resolve its relative assets against the HTML folder.
  if(!input.querySelector('base[href]'))input.head.prepend(baseTag);
  const style=input.createElement('style');style.dataset.sinRuntime='1';style.textContent=fontCss+editingStyles;input.head.append(style);
  loading=new Promise(resolve=>{frame.onload=()=>{
    doc=frame.contentDocument;doc.body.contentEditable='true';doc.body.spellcheck=true;loadingNow=false;
    ribbon.attach(doc);
    attachListNumbering(doc);
    images.attach(doc);
    videos.attach(doc);
    doc.addEventListener('selectionchange',()=>{saveSelection();syncFormatting();});doc.addEventListener('input',changed);
    doc.addEventListener('click',event=>{if(event.target.closest('a'))event.preventDefault();const image=event.target.closest('img');selectImage(image);imageActions.open(image);});
    doc.addEventListener('dblclick',event=>{const image=event.target.closest('img'),code=event.target.closest('pre[data-sin-code]')||event.target.closest('details[data-sin-code-display]')?.querySelector('pre[data-sin-code]');if(image){selectImage(image);openAnnotation(image).catch(report);}else if(code)pasteCode(code).catch(report);});
    doc.addEventListener('pointerover',event=>imageStatus(event.target.closest('img')?.src||images.selected?.src||''));
    doc.addEventListener('pointerout',event=>{if(event.target.closest('img'))imageStatus(images.selected?.src||'');});
    doc.addEventListener('keydown',shortcuts);doc.addEventListener('paste',paste);
    doc.addEventListener('dragover',event=>{if(event.dataTransfer.types.includes('Files'))event.preventDefault();});
    doc.addEventListener('drop',event=>{const files=[...event.dataTransfer.files].filter(f=>f.type.startsWith('image/'));if(files.length){event.preventDefault();(async()=>{for(const f of files)await insertImage(await blobData(f));})().catch(report);}});
    resolve();
  };frame.srcdoc='<!DOCTYPE html>'+input.documentElement.outerHTML;});
  return loading;
}
async function setBase(newBase){
  saveSelection();
  const path=node=>{const indexes=[];while(node&&node!==doc.body){indexes.unshift([...node.parentNode.childNodes].indexOf(node));node=node.parentNode;}return indexes;};
  const saved=selection?{start:path(selection.startContainer),end:path(selection.endContainer),startOffset:selection.startOffset,endOffset:selection.endOffset}:null;
  const scroll={x:doc.defaultView.scrollX,y:doc.defaultView.scrollY};
  // WebView2 applies changed folder mappings to newly loaded documents.
  // Reload only the sandboxed document, keeping the pending paste and its caret.
  await load(html(),newBase);
  if(saved){const node=indexes=>indexes.reduce((parent,index)=>parent.childNodes[index],doc.body);selection=doc.createRange();selection.setStart(node(saved.start),saved.startOffset);selection.setEnd(node(saved.end),saved.endOffset);}
  restoreSelection();doc.defaultView.scrollTo(scroll.x,scroll.y);
}
function command(name,value=null){
  if(!doc)return;restoreSelection();
  if(['cut','copy','paste'].includes(name))return clipboardCommand(doc,name,{changed,insertImage}).catch(report);
  doc.execCommand('styleWithCSS',false,true);
  if(name==='insertOrderedList')toggleNumbering(doc);else doc.execCommand(name,false,value);changed();syncFormatting();
}
function syncFormatting(){ribbon.sync();}
function selectImage(image){images.select(image);}
async function paste(event){
  if(native&&event.isTrusted){event.preventDefault();await clipboardCommand(doc,'paste',{changed,insertImage}).catch(report);return;}
  const files=[...event.clipboardData.files],text=event.clipboardData.getData('text/plain');
  if(files.some(file=>/\.(md|markdown)$/i.test(file.name))){event.preventDefault();await clipboardCommand(doc,'paste',{changed,insertImage}).catch(report);return;}
  if(!doc.body.textContent.trim()&&!doc.body.querySelector('img,pre,table')&&looksLikeMarkdown(text)){
    event.preventDefault();try{await pasteMarkdown(doc,text,changed);ribbon.attach(doc);}catch(error){report(error);}return;
  }
  const images=[...event.clipboardData.items].filter(i=>i.type.startsWith('image/')).map(i=>i.getAsFile());
  if(images.length){event.preventDefault();saveSelection();try{for(const image of images)await insertImage(await blobData(image));}catch(e){report(e);}return;}
  const markup=event.clipboardData.getData('text/html');
  if(markup){event.preventDefault();command('insertHTML',pasteSafeHtml(markup));}
}
function pasteCode(existing=null){return editCodeBlock(existing,{getDocument:()=>doc,saveSelection,restoreSelection,command});}
async function openAnnotation(image=null,capturedSource=null){
  imageActions.close();
  saveSelection();let mode=image?.dataset.sinStorage||(image?(/^data:/.test(image.getAttribute('src'))?'inline':'separate'):null);
  let state;
  const displayWidth=image?.getBoundingClientRect().width||800;
  if(image?.dataset.sinAnnotation){state=JSON.parse(image.dataset.sinAnnotation);}
  else if(image||capturedSource){const png=await toPng(capturedSource||image.src);state={version:1,width:png.width,height:png.height,background:'none',objects:[{id:id(),type:'embedded-image',name:capturedSource?'Screen Capture':'Original Image',source:png.data,x:0,y:0,width:png.width,height:png.height,isOriginalImage:true,visible:true}]};}
  else {state={version:1,width:800,height:500,blankCanvas:true,background:'none',objects:[]};}
  const result=await annotate(state,{crop:!!capturedSource});if(!result)return;
  if(!mode)mode=await storageChoice();if(!mode)return;
  const imageIndex=image?[...doc.images].indexOf(image):-1;
  const source=await storeImage(result.data,mode);
  if(imageIndex>=0)image=doc.images[imageIndex];
  // PMT retains the document width and fits the expanded annotation bounds into it.
  const width=Math.max(1,Math.round(displayWidth));
  const markup=`<img src="${escapeHtml(source)}" data-sin-storage="${mode}" data-sin-annotation="${escapeHtml(JSON.stringify(result.state))}" style="width:${width}px;max-width:100%;height:auto" alt="${escapeHtml(image?.alt||'Annotated image')}">`;
  if(image){restoreSelection();const r=doc.createRange();r.selectNode(image);selection=r;}
  command('insertHTML',markup+(image?'':'<p><br></p>'));selectImage(null);
}
function shortcuts(event){
  const ctrl=event.ctrlKey||event.metaKey,key=event.key.toLowerCase();let action=null;
  if(native&&ctrl&&!event.shiftKey&&['c','x'].includes(key)){event.preventDefault();saveSelection();command(key==='c'?'copy':'cut');return;}
  if(ctrl){const map={s:event.shiftKey?'saveAs':'save',n:'new',t:'new',o:'open',w:'close',f:'find',h:'replace',d:'longDate',l:'separator',tab:event.shiftKey?'previous':'next','+':'zoomIn','=':'zoomIn','-':'zoomOut','0':'zoomReset'};action=map[key];if(key==='l'&&event.shiftKey)action='documentList';if(key==='u'&&event.shiftKey){event.preventDefault();send('source');return;}}
  if(event.key==='F5')action='date';
  if(event.key==='F3')action=event.shiftKey?'findPrevious':'findNext';
  if(action){event.preventDefault();changed();send('command',{command:action,html:html()});}
}
$('#pasteCode').onclick=()=>pasteCode().catch(report);
$('#insertImage').onclick=()=>openAnnotation(images.selected?.isConnected?images.selected:null).catch(report);
$('#screenCapture').onclick=async()=>{
  saveSelection();$('#screenCapture').disabled=true;
  try{const source=await request('screen-capture');if(source)await openAnnotation(null,source);}
  catch(error){report(error);}
  finally{$('#screenCapture').disabled=false;}
};
$('#toolbar').addEventListener('click',event=>{
  const action=event.target.closest('[data-native-command]')?.dataset.nativeCommand;
  if(action)send('command',{command:action,html:html(true)});
});
$('#regionCapture').onclick=async()=>{
  saveSelection();$('#regionCapture').disabled=true;
  try{const source=await request('region-capture');if(source)await insertImage(source,'separate');}
  catch(error){report(error);}
  finally{$('#regionCapture').disabled=false;}
};
$('#source').onclick=async()=>{if(native){send('source');return;}const answer=await ask('HTML Source',`<textarea name="source" aria-label="HTML Source" spellcheck="false">${escapeHtml(html())}</textarea>`);if(answer.choice==='ok'){await load(answer.values.source);changed();}};
$('#link').onclick=()=>insertLink({getDocument:()=>doc,saveSelection,command}).catch(report);
$('#notice').onclick=()=>$('#notice').hidden=true;
window.editor={load,html,setBase,focus,command,insertImage,openAnnotation,pasteCode,renameImageFolder,setImageStorage:imageInsertion.setStorage,ready:()=>loading,
  stopMedia:videos.close,
  renameImageFile,
  async renameOpenImageFile(documentUrl,oldUrl,newUrl){await loading;const updated=renameImageFile(html(true),documentUrl,oldUrl,newUrl);await load(updated);return updated;},
  search:options=>search.run(options),
  beginMarkdown(folder=null){const key=id();(async()=>{await loading;return await htmlToMarkdown(doc,async png=>{const path=await request('save-image-as',{data:png});return folder?new URL(path,folder).href:path;});})().then(html=>exports.set(key,{html})).catch(error=>exports.set(key,{error:error.message}));return key;},
  renameOpenImageFolder(oldName,newName){const updated=renameImageFolder(html(true),oldName,newName);load(updated);return updated;},
  beginPortable(duplicate=false){const key=id();(async()=>{await loading;return await portableHtml(html(),base,duplicate);})().then(html=>exports.set(key,{html})).catch(error=>exports.set(key,{error:error.message}));return key;},
  beginRelocate(){
    const key=id();
    (async()=>{
      await loading;const original=parseHtml(html()),output=parseHtml(await portableHtml(html(),base));
      const images=[...output.querySelectorAll('img')];
      for(const [index,img] of [...original.querySelectorAll('img')].entries())
        if(img.dataset.sinStorage==='separate'||!/^data:/i.test(img.getAttribute('src')||''))
          images[index].setAttribute('src',await request('save-image-as',{data:images[index].getAttribute('src')}));
      return serialize(output);
    })().then(html=>exports.set(key,{html})).catch(error=>exports.set(key,{error:error.message}));return key;
  },
  exportResult(key){const value=exports.get(key);if(value)exports.delete(key);return value||null;}
};
send('ready');if(!native)await load('');
