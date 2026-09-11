import {send,request,native,blobData,escapeHtml,ask,report} from './bridge.js';
import {parseHtml,ensureStyle,editingStyles,serialize,normalizeIndent,codeHtml,toPng,portableHtml,pasteSafeHtml} from './document.js';
import {prepareRichSourceHighlight,RICH_SOURCE_TEXT_TYPES} from './source-highlighting.js';
import {annotate} from './annotation-ui.js';
import {id,outputBounds} from './annotation-model.js';

const frame=document.querySelector('#document'),$=s=>document.querySelector(s);
let doc=null,selection=null,selectedImage=null,currentRaw='',base='https://sin-document.local/',loading=Promise.resolve(),loadingNow=false;
const exports=new Map();
function saveSelection(){if(!doc)return;const s=doc.getSelection();if(s.rangeCount && doc.body.contains(s.anchorNode))selection=s.getRangeAt(0).cloneRange();}
function restoreSelection(){if(!doc)return;doc.body.focus();const s=doc.getSelection();if(selection&&doc.body.contains(selection.startContainer)){s.removeAllRanges();s.addRange(selection);}else {const range=doc.createRange();range.selectNodeContents(doc.body);range.collapse(false);s.removeAllRanges();s.addRange(range);}}
function changed(){if(!doc||loadingNow)return;saveSelection();currentRaw=serialize(doc);send('change',{html:currentRaw});}
function html(){return loadingNow?currentRaw:doc?serialize(doc):currentRaw;}
async function load(raw,newBase=base){
  currentRaw=raw||'';base=newBase;loadingNow=true;selection=null;selectedImage=null;$('#imagebar').hidden=true;
  const input=parseHtml(raw);ensureStyle(input);
  const baseTag=input.createElement('base');baseTag.href=base;baseTag.dataset.sinRuntime='1';
  // An explicit document base wins. Otherwise resolve its relative assets against the HTML folder.
  if(!input.querySelector('base[href]'))input.head.prepend(baseTag);
  const style=input.createElement('style');style.dataset.sinRuntime='1';style.textContent=editingStyles;input.head.append(style);
  loading=new Promise(resolve=>{frame.onload=()=>{
    doc=frame.contentDocument;doc.body.contentEditable='true';doc.body.spellcheck=true;loadingNow=false;
    doc.addEventListener('selectionchange',()=>{saveSelection();syncFormatting();});doc.addEventListener('input',changed);
    doc.addEventListener('click',event=>{if(event.target.closest('a'))event.preventDefault();selectImage(event.target.closest('img'));});
    doc.addEventListener('dblclick',event=>{const image=event.target.closest('img'),code=event.target.closest('pre[data-sin-code]');if(image){selectImage(image);openAnnotation(image).catch(report);}else if(code)pasteCode(code).catch(report);});
    doc.addEventListener('keydown',shortcuts);doc.addEventListener('paste',paste);
    doc.addEventListener('dragover',event=>{if(event.dataTransfer.types.includes('Files'))event.preventDefault();});
    doc.addEventListener('drop',event=>{const files=[...event.dataTransfer.files].filter(f=>f.type.startsWith('image/'));if(files.length){event.preventDefault();(async()=>{for(const f of files)await insertImage(await blobData(f));})().catch(report);}});
    resolve();
  };frame.srcdoc='<!DOCTYPE html>'+input.documentElement.outerHTML;});
  return loading;
}
function setBase(newBase){base=newBase;const el=doc?.querySelector('base[data-sin-runtime]');if(el)el.href=base;}
function command(name,value=null){
  if(!doc)return;restoreSelection();
  doc.execCommand('styleWithCSS',false,true);
  if(name==='paste'){doc.execCommand('paste');return;}
  doc.execCommand(name,false,value);changed();syncFormatting();
}
function syncFormatting(){for(const b of document.querySelectorAll('[data-cmd]'))b.setAttribute('aria-pressed',String(doc?.queryCommandState(b.dataset.cmd)||false));}
function selectImage(image){doc?.querySelectorAll('[data-sin-selected]').forEach(el=>el.removeAttribute('data-sin-selected'));selectedImage=image;$('#imagebar').hidden=!image;if(image){image.dataset.sinSelected='1';$('#imageWidth').value=Math.round(image.getBoundingClientRect().width);$('#imageHeight').value=Math.round(image.getBoundingClientRect().height);}}
async function storageChoice(){const answer=await ask('Store image','<p>Choose how this image is stored with your HTML document.</p><p><b>Inline:</b> embed the lossless PNG in the HTML file.<br><b>Separate file:</b> store a PNG in a folder named after the HTML file.</p>',[{value:'inline',label:'Inline (Base64)'},{value:'separate',label:'Separate PNG file'}]);return ['inline','separate'].includes(answer.choice)?answer.choice:null;}
async function storeImage(png,mode){return mode==='separate'?await request('save-image',{data:png}):png;}
async function insertImage(source){saveSelection();const mode=await storageChoice();if(!mode)return;const png=await toPng(source),src=await storeImage(png.data,mode);restoreSelection();command('insertHTML',`<img src="${escapeHtml(src)}" data-sin-storage="${mode}" style="width:${png.width}px;max-width:100%;height:auto" alt=""><p><br></p>`);}
async function paste(event){
  const images=[...event.clipboardData.items].filter(i=>i.type.startsWith('image/')).map(i=>i.getAsFile());
  if(images.length){event.preventDefault();saveSelection();try{for(const image of images)await insertImage(await blobData(image));}catch(e){report(e);}return;}
  const markup=event.clipboardData.getData('text/html');
  if(markup){event.preventDefault();command('insertHTML',pasteSafeHtml(markup));}
}
async function pasteCode(existing=null){
  saveSelection();const value=existing?.textContent||'',language=existing?.dataset.sinCode||'csharp';
  const options=RICH_SOURCE_TEXT_TYPES.filter(t=>t.value).map(t=>`<option value="${t.value}" ${t.value===language?'selected':''}>${escapeHtml(t.value==='tsql'?'SQL / T-SQL':t.label)}</option>`).join('');
  const answer=await ask(existing?'Edit code block':'Paste Code',`<label>Language <select name="language">${options}</select></label><textarea name="code" aria-label="Code" spellcheck="false" autofocus>${escapeHtml(value)}</textarea><p class="hint">Common indentation is trimmed; indentation inside the code is preserved. Double-click the rendered block to edit it again.</p>`,[{value:'ok',label:existing?'Update code':'Insert code'}]);
  if(answer.choice!=='ok')return;
  const text=normalizeIndent(answer.values.code),highlight=prepareRichSourceHighlight(text,answer.values.language);
  const previousBlocks=new Set(doc.querySelectorAll('pre[data-sin-code]'));
  const previousIndex=existing?[...previousBlocks].indexOf(existing):-1;
  if(existing){restoreSelection();const range=doc.createRange();range.selectNode(existing);const s=doc.getSelection();s.removeAllRanges();s.addRange(range);selection=range;}
  command('insertHTML',codeHtml(text,answer.values.language,highlight.html)+(existing?'':'<p><br></p>'));
  const afterBlocks=[...doc.querySelectorAll('pre[data-sin-code]')];
  const inserted=previousIndex>=0?afterBlocks[previousIndex]:afterBlocks.find(block=>!previousBlocks.has(block));
  if(inserted){
    let next=inserted.nextElementSibling;
    if(!next||next.tagName!=='P'){next=doc.createElement('p');next.innerHTML='<br>';inserted.after(next);}
    const range=doc.createRange();range.selectNodeContents(next);range.collapse(true);
    const current=doc.getSelection();current.removeAllRanges();current.addRange(range);saveSelection();
  }
  if(highlight.error)report(highlight.error);
}
async function openAnnotation(image=null){
  saveSelection();const mode=image?.dataset.sinStorage||(image?(/^data:/.test(image.getAttribute('src'))?'inline':'separate'):await storageChoice());if(!mode)return;
  let state;
  const displayWidth=image?.getBoundingClientRect().width||800;
  if(image?.dataset.sinAnnotation){state=JSON.parse(image.dataset.sinAnnotation);}
  else if(image){const png=await toPng(image.src);state={version:1,width:png.width,height:png.height,background:'none',objects:[{id:id(),type:'embedded-image',name:'Original image',source:png.data,x:0,y:0,width:png.width,height:png.height,isOriginalImage:true,visible:true}]};}
  else {state={version:1,width:800,height:500,blankCanvas:true,background:'none',objects:[]};}
  const result=await annotate(state);if(!result)return;
  const source=await storeImage(result.data,mode);
  // PMT retains the document width and fits the expanded annotation bounds into it.
  const width=Math.max(1,Math.round(displayWidth));
  const markup=`<img src="${escapeHtml(source)}" data-sin-storage="${mode}" data-sin-annotation="${escapeHtml(JSON.stringify(result.state))}" style="width:${width}px;max-width:100%;height:auto" alt="${escapeHtml(image?.alt||'Annotated image')}">`;
  if(image){restoreSelection();const r=doc.createRange();r.selectNode(image);selection=r;}
  command('insertHTML',markup+(image?'':'<p><br></p>'));selectImage(null);
}
function shortcuts(event){
  const ctrl=event.ctrlKey||event.metaKey,key=event.key.toLowerCase();let action=null;
  if(ctrl){const map={s:event.shiftKey?'saveAs':'save',n:'new',t:'new',o:'open',w:'close',f:'find',h:'replace',d:'longDate',l:'separator',tab:event.shiftKey?'previous':'next','+':'zoomIn','=':'zoomIn','-':'zoomOut','0':'zoomReset'};action=map[key];if(key==='l'&&event.shiftKey)action='documentList';if(key==='u'&&event.shiftKey){event.preventDefault();send('source');return;}}
  if(event.key==='F5')action='date';
  if(action){event.preventDefault();changed();send('command',{command:action,html:html()});}
}
$('#toolbar').addEventListener('mousedown',event=>{saveSelection();if(event.target.closest('button'))event.preventDefault();});
$('#toolbar').addEventListener('click',event=>{const cmd=event.target.closest('[data-cmd]')?.dataset.cmd;if(cmd)command(cmd);});
$('#font').onchange=()=>command('fontName',$('#font').value);
$('#block').onchange=()=>command('formatBlock',$('#block').value);
$('#fontColor').oninput=()=>command('foreColor',$('#fontColor').value);
$('#backColor').oninput=()=>command('hiliteColor',$('#backColor').value);
$('#fontSize').onchange=()=>{
  restoreSelection();
  // Generate a recognizable font marker even after another command enabled CSS formatting.
  doc.execCommand('styleWithCSS',false,false);
  doc.execCommand('fontSize',false,'7');
  doc.querySelectorAll('font[size="7"]').forEach(el=>{
    el.removeAttribute('size');
    el.style.fontSize=Math.max(6,Math.min(144,Number($('#fontSize').value)||16))+'px';
  });
  doc.execCommand('styleWithCSS',false,true);
  changed();
};
$('#pasteCode').onclick=()=>pasteCode().catch(report);
$('#insertImage').onclick=()=>openAnnotation().catch(report);
$('#annotate').onclick=()=>openAnnotation(selectedImage).catch(report);
$('#deleteImage').onclick=()=>{if(selectedImage){const r=doc.createRange();r.selectNode(selectedImage);selection=r;command('delete');selectImage(null);}};
for(const dimension of ['Width','Height'])$('#image'+dimension).onchange=()=>{
  if(!selectedImage)return;
  const value=Math.max(1,Number($('#image'+dimension).value)||1),rect=selectedImage.getBoundingClientRect();
  const index=[...doc.images].indexOf(selectedImage),replacement=selectedImage.cloneNode(true);
  if($('#lockRatio').checked){const width=dimension==='Width'?value:value*rect.width/rect.height;replacement.style.width=width+'px';replacement.style.height='auto';}
  else replacement.style[dimension.toLowerCase()]=value+'px';
  const range=doc.createRange();range.selectNode(selectedImage);selection=range;
  command('insertHTML',replacement.outerHTML);selectImage(doc.images[index]);
};
$('#source').onclick=async()=>{if(native){send('source');return;}const answer=await ask('HTML source',`<textarea name="source" aria-label="HTML source" spellcheck="false">${escapeHtml(html())}</textarea>`);if(answer.choice==='ok'){await load(answer.values.source);changed();}};
$('#link').onclick=async()=>{saveSelection();const answer=await ask('Insert link','<label>Address <input name="url" type="url" required placeholder="https://…"></label>');if(answer.choice==='ok')command('createLink',answer.values.url);};
$('#notice').onclick=()=>$('#notice').hidden=true;
window.editor={load,html,setBase,command,insertImage,openAnnotation,pasteCode,ready:()=>loading,
  beginPortable(){const key=id();(async()=>{await loading;return await portableHtml(html(),base);})().then(html=>exports.set(key,{html})).catch(error=>exports.set(key,{error:error.message}));return key;},
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
