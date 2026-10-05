import {request,blobData,escapeHtml,ask} from './bridge.js';
import {toPng} from './document.js';
import {clone,id,isLine,clamp,bounds,union,outputBounds,imageClip,move,resize,objectSvg,renderPng} from './annotation-model.js';
import {captureTemplate,parseTemplate,templateJson,instantiate} from './templates.js';
import {copyObjects} from './annotation-clipboard.js';
import {connectAnnotationColors} from './annotation-colors.js';
import {connectCanvasPan} from './annotation-pan.js';
import {cropControls,resetCrop,applyCrop} from './annotation-crop.js';
import {createAnnotationInspector} from './annotation-inspector.js';
import {connectAnnotationMenu} from './annotation-menu.js';
import {slicingControls,setSliceCount,moveSliceDivider,sliceOverlay,updateSliceOverlay,syncSliceControls,copySlice,saveAllSlices} from './image-slicing.js';

export async function annotate(initial,{crop=false}={}) {
  const dialog=document.createElement('dialog'); dialog.className='annotation'; dialog.setAttribute('aria-label','Image Annotation');
  dialog.innerHTML=`<div class="annotation-layout"><header><div><h2>Image Annotation <small>Shapes, images &amp; reusable objects</small></h2></div><div><button data-action="undo" title="Undo (Ctrl+Z)">↶ Undo</button> <button data-action="redo" title="Redo (Ctrl+Y)">↷ Redo</button> <button data-action="resetAllCrops">Reset All Crops</button> <select id="canvasZoom" aria-label="Canvas Zoom"><option value="fit">Fit</option>${[25,50,75,100,150,200].map(n=>`<option value="${n}">${n}%</option>`).join('')}</select></div></header>
  <div class="annotation-body"><aside class="left"><h3>Draw</h3><div class="tools">${[['select','↖ Select'],['pan','✋ Pan'],['arrow','↗ Arrow'],['line','╱ Line'],['rectangle','▭ Rectangle'],['circle','◯ Circle'],['text','T Text']].map(([tool,label])=>`<button data-tool="${tool}">${label}</button>`).join('')}</div><button data-action="addImage" class="wide">＋ Add Image…</button><button data-action="screenCapture" class="wide">Screen Capture…</button><button data-action="regionCapture" class="wide">Region Capture…</button><p class="hint">Paste images with Ctrl+V.<br>Drag empty canvas to select touching objects.<br>Shift adds to the selection.</p><hr><h3>Layers</h3><div id="layers"></div><div class="small-actions"><button data-action="front">To Front</button><button data-action="back">To Back</button><button data-action="copy" title="Copy Selected Objects (Ctrl+C)">Copy…</button><button data-action="paste" title="Paste Objects Or An Image (Ctrl+V)">Paste</button><button data-action="duplicate">Duplicate</button><button data-action="delete">Delete</button><button data-action="lock">Lock</button><button data-action="unlock">Unlock</button></div><hr><h3>Object Templates</h3><button data-action="saveTemplate" class="wide">Save Selected As Template</button><button data-action="importTemplate" class="wide">Import PMT Template…</button><div id="templates"></div></aside>
  <main class="canvas-viewport"><div class="canvas-stage"><svg id="canvas" tabindex="0" xmlns="http://www.w3.org/2000/svg" aria-label="Annotation Canvas"></svg></div></main>
  <aside class="right"><h3>Appearance</h3><label class="field">Outline <button id="stroke" type="button" aria-label="Outline Color" value="#dc3939"></button></label><label class="field"><span>Transparent Outline</span><input id="noStroke" type="checkbox"></label><label class="field">Fill <button id="fill" type="button" aria-label="Fill Color" value="#fff2a6"></button></label><label class="field"><span>Transparent Fill</span><input id="noFill" type="checkbox" checked></label><label class="field">Thickness <input id="strokeWidth" type="number" min="1" max="80" value="4"></label><label class="field">Arrow Head <input id="arrowSize" type="number" min="4" max="160" value="20"></label><label class="field">Opacity % <input id="opacity" type="number" min="0" max="100" value="100"></label>
  <div id="geometry"><hr><h3>Position &amp; Size (px)</h3><div class="pair">${['x','y','width','height'].map(k=>`<label>${k[0].toUpperCase()+k.slice(1)}<input data-geometry="${k}" type="number" step="1"></label>`).join('')}</div></div>
  <div id="endpoints" hidden><hr><h3>Endpoints (px)</h3><div class="pair">${['x1','y1','x2','y2'].map(k=>`<label>${k}<input data-endpoint="${k}" type="number"></label>`).join('')}</div></div>
  ${slicingControls}${cropControls}
  <div id="textFields" hidden><hr><h3>Text</h3><textarea id="shapeText" rows="4" style="width:100%"></textarea><label class="field">Font Size <input id="shapeFontSize" type="number" min="6" max="200" value="20"></label><label class="field">Text Color <button id="textColor" type="button" aria-label="Text Color" value="#20252c"></button></label></div>
  <hr><h3>Canvas</h3><label class="field">Background <button id="canvasColor" type="button" aria-label="Canvas Background" value="#ffffff"></button></label><label class="field"><span>Transparent</span><input id="canvasTransparent" type="checkbox" checked></label><p id="dimensions" class="hint"></p></aside></div>
  <footer><span class="annotation-error" role="alert"></span><span class="hint">Corners keep proportions · Sides stretch · Wheel zooms · Del removes · Esc cancels</span><div class="actions"><button data-action="cancel">Cancel</button><button data-action="apply" class="primary">Apply To Document</button></div></footer></div>`;
  await request('annotation-mode',{open:true});
  try {
  document.body.append(dialog);dialog.showModal();
  const $=s=>dialog.querySelector(s),$$=s=>[...dialog.querySelectorAll(s)];
  const colors=connectAnnotationColors(dialog);
  const inspector=createAnnotationInspector(dialog);
  const svg=$('#canvas'),viewport=$('.canvas-viewport');
  let state=clone(initial),selected=[],tool='select',cropMode=crop,sliceMode=false,sliceCell=0,sliceCopying=false,sliceExport=null,gesture=null,zoom=1,viewBox,templates=[];
  connectCanvasPan(viewport,svg,()=>tool==='pan');
  const initialImage=state.objects.find(o=>o.type==='embedded-image'&&o.visible!==false);
  if(initialImage)selected=[initialImage.id];
  let history=[clone(state)],historyIndex=0,finished=false;
  const selectedObjects=()=>state.objects.filter(o=>selected.includes(o.id));
  const editableObjects=()=>selectedObjects().filter(o=>!o.locked);
  const one=()=>selectedObjects().length===1?selectedObjects()[0]:null;
  const error=e=>{$('.annotation-error').textContent=e?.message||String(e||'');};
  const historySave=()=>{if(JSON.stringify(history[historyIndex])===JSON.stringify(state))return;history=history.slice(0,historyIndex+1);history.push(clone(state));if(history.length>100)history.shift();historyIndex=history.length-1;};
  const change=fn=>{fn();historySave();render();};
  const historyMove=delta=>{historyIndex=clamp(historyIndex+delta,0,history.length-1);state=clone(history[historyIndex]);selected=selected.filter(key=>state.objects.some(o=>o.id===key));render();};
  const setLocked=locked=>change(()=>selectedObjects().forEach(o=>o.locked=locked));
  connectAnnotationMenu(dialog,{getSelection:selectedObjects,selectObject(key){if(!selected.includes(key)){selected=[key];render();}},copy:format=>copyObjects(selectedObjects(),format),setLocked,onError:error});
  function workspace() {
    const b=outputBounds(state),padding=24/zoom;
    return {x:b.x-padding,y:b.y-padding,width:b.width+padding*2,height:b.height+padding*2};
  }
  function render(keepInspector=false,fit=false) {
    viewport.dataset.tool=tool;
    const previous=viewBox;
    if(!gesture){
      // Fit on opening, a window resize, or an explicit zoom choice. Moving artwork
      // expands the scrollable workspace without repeatedly shrinking everything.
      if(fit||!previous){const b=outputBounds(state);if($('#canvasZoom').value==='fit')zoom=clamp(Math.min((viewport.clientWidth-148)/b.width,(viewport.clientHeight-148)/b.height),.05,4);else zoom=Number($('#canvasZoom').value)/100;}
      viewBox=workspace();
    }
    svg.setAttribute('viewBox',`${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}`);svg.setAttribute('width',viewBox.width*zoom);svg.setAttribute('height',viewBox.height*zoom);
    let content=state.background && state.background!=='none'?`<rect x="${viewBox.x}" y="${viewBox.y}" width="${viewBox.width}" height="${viewBox.height}" fill="${escapeHtml(state.background)}"/>`:'';
    content+=state.objects.filter(o=>o.visible!==false).map(o=>objectSvg(o,true)).join('');
    const size=8/zoom;
    for(const o of selectedObjects().filter(o=>o.visible!==false)) {
      if(['line','arrow','circle'].includes(tool)&&(isLine(o)||o.type==='circle'))continue;
      const b=bounds(o,false);content+=`<rect data-selection-frame x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}" fill="none" stroke="#0874c9" stroke-width="${1/zoom}" stroke-dasharray="${4/zoom}" pointer-events="none"/>`;
      if(sliceMode&&one()?.type==='embedded-image')continue;
      if(o.locked||tool!=='select')continue;
      const handle=(x,y,attrs)=>`<rect x="${x-size/2}" y="${y-size/2}" width="${size}" height="${size}" ${attrs} data-id="${o.id}"/>`;
      if(isLine(o)) content+=handle(o.x1,o.y1,'data-handle="end" data-end="1"')+handle(o.x2,o.y2,'data-handle="end" data-end="2"');
      else if(selected.length===1){const c=cropMode&&o.type==='embedded-image'?imageClip(o):b;for(const [key,x,y] of [['nw',c.x,c.y],['ne',c.x+c.width,c.y],['se',c.x+c.width,c.y+c.height],['sw',c.x,c.y+c.height],['n',c.x+c.width/2,c.y],['e',c.x+c.width,c.y+c.height/2],['s',c.x+c.width/2,c.y+c.height],['w',c.x,c.y+c.height/2]])content+=handle(x,y,`${cropMode&&o.type==='embedded-image'?'data-crop':'data-handle'}="${key}"`);}
    }
    if(sliceMode&&one()?.type==='embedded-image'&&one().visible!==false)content+=sliceOverlay(one(),sliceCell,zoom);
    if(gesture?.kind==='marquee')content+='<rect data-marquee fill="#0874c922" stroke="#0874c9" stroke-width="'+1/zoom+'" pointer-events="none"/>';
    svg.innerHTML=content;svg.style.cursor=tool==='select'?'default':'crosshair';
    if(previous&&!gesture&&!fit){viewport.scrollLeft+=(previous.x-viewBox.x)*zoom;viewport.scrollTop+=(previous.y-viewBox.y)*zoom;}
    $('#layers').innerHTML=[...state.objects].reverse().map(o=>`<button class="layer ${selected.includes(o.id)?'selected':''}" data-layer="${o.id}" title="${escapeHtml(o.name||o.type)}${o.locked?' (Locked)':''}"><input type="checkbox" data-visible="${o.id}" aria-label="Show ${escapeHtml(o.name||o.type)}" ${o.visible!==false?'checked':''} ${o.locked?'disabled':''}><span>${o.locked?'🔒 ':''}${escapeHtml(o.name||o.type)}</span></button>`).join('');
    $$('[data-tool]').forEach(b=>b.classList.toggle('active',b.dataset.tool===tool));
    $('[data-action=undo]').disabled=historyIndex===0;$('[data-action=redo]').disabled=historyIndex===history.length-1;
    $('[data-action=lock]').disabled=!editableObjects().length;$('[data-action=unlock]').disabled=!selectedObjects().some(o=>o.locked);
    for(const action of ['delete','front','back'])$(`[data-action=${action}]`).disabled=!editableObjects().length;
    const b=outputBounds(state);$('#dimensions').textContent=`Output: ${Math.ceil(b.width)} × ${Math.ceil(b.height)} px. The canvas expands as you add or move objects.`;
    if(!keepInspector) inspector.sync(selectedObjects(),cropMode);
    syncSliceControls(dialog,one(),sliceMode,sliceCell,sliceCopying);
    colors.sync();
  }
  function point(event){const p=new DOMPoint(event.clientX,event.clientY).matrixTransform(svg.getScreenCTM().inverse());return {x:p.x,y:p.y};}
  viewport.addEventListener('wheel',event=>{
    event.preventDefault();if(gesture||!event.deltaY)return;
    const anchor=point(event),percent=clamp(Math.round(zoom*100*(event.deltaY<0?1.1:1/1.1)),5,400),select=$('#canvasZoom');
    let option=$('#wheelZoom');if(!option){option=new Option();option.id='wheelZoom';select.append(option);}
    option.value=String(percent);option.textContent=percent+'%';select.value=option.value;
    render(true,true);
    const position=new DOMPoint(anchor.x,anchor.y).matrixTransform(svg.getScreenCTM());
    viewport.scrollLeft+=position.x-event.clientX;viewport.scrollTop+=position.y-event.clientY;
  },{passive:false});
  viewport.addEventListener('pointerdown',event=>{
    if(event.button!==0)return;event.preventDefault();svg.focus({preventScroll:true});error('');const p=point(event),target=event.target,objectId=target.closest('[data-object]')?.dataset.object;
    if(sliceMode&&one()?.type==='embedded-image'){
      const divider=target.closest('[data-slice-divider]'),cell=target.closest('[data-slice-cell]');
      if(divider){if(!one().locked){gesture={kind:'slice',point:p,objectId:one().id,index:Number(divider.dataset.sliceDivider)};viewport.setPointerCapture(event.pointerId);}return;}
      if(cell){sliceCell=Number(cell.dataset.sliceCell);updateSliceOverlay(svg,one(),sliceCell,zoom);syncSliceControls(dialog,one(),true,sliceCell,sliceCopying);return;}
      sliceMode=false;
    }
    const handle=target.dataset.handle,crop=target.dataset.crop,end=target.dataset.end;
    if(tool==='select' && (handle||crop)){gesture={kind:end?'endpoint':crop?'crop':'resize',point:p,objects:clone(editableObjects()),handle:crop||handle,end};}
    else if(tool==='select' && objectId){if(event.shiftKey)selected=selected.includes(objectId)?selected.filter(x=>x!==objectId):[...selected,objectId];else if(!selected.includes(objectId))selected=[objectId];gesture={kind:'move',point:p,objects:clone(editableObjects())};}
    else if(tool==='select'){gesture={kind:'marquee',point:p,end:p,previous:[...selected],add:event.shiftKey};if(!event.shiftKey)selected=[];cropMode=false;}
    else {
      const type=tool==='text'?'textbox':tool; const o={id:id(),type,name:type==='embedded-image'?'Image':type[0].toUpperCase()+type.slice(1),visible:true,...inspector.style()};
      if(isLine(o))Object.assign(o,{x1:p.x,y1:p.y,x2:p.x+1,y2:p.y+1});else Object.assign(o,{x:p.x,y:p.y,width:1,height:1});
      if(type==='textbox')Object.assign(o,{text:'Text',fontSize:20,textColor:'#20252c',fontFamily:'Segoe UI'});
      state.objects.push(o);selected=[o.id];gesture={kind:'draw',point:p,objects:[clone(o)]};
    }
    viewport.setPointerCapture(event.pointerId);render();
  });
  viewport.addEventListener('pointermove',event=>{
    if(!gesture)return;const p=point(event),dx=p.x-gesture.point.x,dy=p.y-gesture.point.y;
    if(gesture.kind==='slice'){
      const image=state.objects.find(o=>o.id===gesture.objectId);if(image&&!image.locked){moveSliceDivider(image,gesture.index,p.x);updateSliceOverlay(svg,image,sliceCell,zoom);syncSliceControls(dialog,image,true,sliceCell,sliceCopying);}return;
    }
    if(gesture.kind==='marquee'){
      gesture.end=p;
      const rect=$('[data-marquee]');
      for(const [key,value] of Object.entries({x:Math.min(p.x,gesture.point.x),y:Math.min(p.y,gesture.point.y),width:Math.abs(dx),height:Math.abs(dy)}))rect.setAttribute(key,value);
      return;
    }
    for(const original of gesture.objects){const o=state.objects.find(o=>o.id===original.id);if(!o||o.locked)continue;Object.assign(o,clone(original));
      if(gesture.kind==='move')move(o,dx,dy);
      else if(gesture.kind==='endpoint'){o['x'+gesture.end]=p.x;o['y'+gesture.end]=p.y;}
      else if(gesture.kind==='draw'){if(isLine(o)){o.x2=p.x;o.y2=p.y;if(o.type==='line'&&event.shiftKey){if(Math.abs(dx)>=Math.abs(dy))o.y2=o.y1;else o.x2=o.x1;}}else{const w=Math.abs(dx),h=event.shiftKey?w:Math.abs(dy);Object.assign(o,{x:Math.min(p.x,gesture.point.x),y:Math.min(p.y,gesture.point.y),width:Math.max(1,w),height:Math.max(1,h)});}}
      else {const b=gesture.kind==='crop'?imageClip(original):bounds(original,false);let left=b.x,top=b.y,right=b.x+b.width,bottom=b.y+b.height;const key=gesture.handle;
        if(key.includes('w'))left=Math.min(right-1,b.x+dx);if(key.includes('e'))right=Math.max(left+1,b.x+b.width+dx);if(key.includes('n'))top=Math.min(bottom-1,b.y+dy);if(key.includes('s'))bottom=Math.max(top+1,b.y+b.height+dy);
        if(gesture.kind==='crop'){left=clamp(left,o.x,o.x+o.width-1);top=clamp(top,o.y,o.y+o.height-1);right=clamp(right,left+1,o.x+o.width);bottom=clamp(bottom,top+1,o.y+o.height);o.imageClip={x:left,y:top,width:right-left,height:bottom-top};o.cropVisible=true;}
        else {
          if(key.length===2){
            const sx=(right-left)/b.width,sy=(bottom-top)/b.height;
            const scale=Math.max(1/b.width,1/b.height,Math.abs(sx-1)>=Math.abs(sy-1)?sx:sy);
            if(key.includes('w'))left=right-b.width*scale;else right=left+b.width*scale;
            if(key.includes('n'))top=bottom-b.height*scale;else bottom=top+b.height*scale;
          }
          resize(o,{x:left,y:top,width:right-left,height:bottom-top});
        }
      }
    }render(true);
  });
  viewport.addEventListener('pointerup',event=>{
    if(!gesture)return;
    if(gesture.kind==='marquee'){
      const a=gesture.point,b=gesture.end,left=Math.min(a.x,b.x),top=Math.min(a.y,b.y),right=Math.max(a.x,b.x),bottom=Math.max(a.y,b.y);
      const hits=right-left<2/zoom&&bottom-top<2/zoom?[]:state.objects.filter(o=>{const r=bounds(o);return o.visible!==false&&r.x<=right&&r.x+r.width>=left&&r.y<=bottom&&r.y+r.height>=top;}).map(o=>o.id);
      selected=[...new Set([...(gesture.add?gesture.previous:[]),...hits])];
    }else{
      if(gesture.kind==='draw'){const o=one();if(o&&!o.locked&&!isLine(o)&&o.width<5&&o.height<5){o.width=o.type==='textbox'?240:120;o.height=o.type==='textbox'?70:90;}}
      historySave();
    }
    gesture=null;if(viewport.hasPointerCapture(event.pointerId))viewport.releasePointerCapture(event.pointerId);render();
  });
  viewport.addEventListener('pointercancel',()=>{if(gesture?.kind==='marquee')selected=gesture.previous;else state=clone(history[historyIndex]);gesture=null;render();});
  svg.addEventListener('dblclick',event=>{const o=state.objects.find(o=>o.id===event.target.closest('[data-object]')?.dataset.object);if(o?.type==='textbox'&&!o.locked){$('#shapeText').focus();$('#shapeText').select();}});
  async function addImage(file){const png=await toPng(await blobData(file));const b=outputBounds(state);change(()=>{const o={id:id(),type:'embedded-image',name:file.name||'Pasted Image',source:png.data,x:state.objects.length?b.x+b.width+24:0,y:0,width:png.width,height:png.height,visible:true};state.objects.push(o);selected=[o.id];});}
  async function captureImage(region=false){
    const result=await request(region?'region-capture':'screen-capture'),source=region?result?.source:result;if(!source)return;
    await addImage(new File([await (await fetch(source)).blob()],'Screen Capture'));
    cropMode=true;tool='select';
    const b=bounds(one()),percent=clamp(Math.round(Math.min((viewport.clientWidth-148)/b.width,(viewport.clientHeight-148)/b.height)*100),5,400);
    let option=$('#wheelZoom');if(!option){option=new Option();option.id='wheelZoom';$('#canvasZoom').append(option);}
    option.value=String(percent);option.textContent=percent+'%';$('#canvasZoom').value=option.value;render(false,true);
    const center=new DOMPoint(b.x+b.width/2,b.y+b.height/2).matrixTransform(svg.getScreenCTM()),r=viewport.getBoundingClientRect();
    viewport.scrollLeft+=center.x-r.x-viewport.clientWidth/2;viewport.scrollTop+=center.y-r.y-viewport.clientHeight/2;
  }
  async function pasteObjects(){
    const clipboard=await request('annotation-paste');
    if(clipboard?.objects){
      const template=await parseTemplate(clipboard.objects),selection=selectedObjects();
      const r=viewport.getBoundingClientRect(),b=selection.length?union(selection.map(o=>bounds(o))):null;
      const center=b?{x:b.x+b.width/2+24,y:b.y+b.height/2+24}:point({clientX:r.x+viewport.clientWidth/2,clientY:r.y+viewport.clientHeight/2});
      change(()=>{const objects=instantiate(template,center);state.objects.push(...objects);selected=objects.map(o=>o.id);tool='select';});
      return true;
    }
    if(clipboard?.image){await addImage(new File([await (await fetch(clipboard.image)).blob()],'Pasted Image'));return true;}
    return false;
  }
  function chooseFile(accept,action){const input=document.createElement('input');input.type='file';input.accept=accept;input.onchange=()=>{if(input.files[0])action(input.files[0]).catch(error);};input.click();}
  async function persistTemplates(){await request('templates-save',{templates});renderTemplates();}
  function renderTemplates(){$('#templates').innerHTML=templates.map((t,i)=>`<div class="template-row"><button data-template="${i}">${escapeHtml(t.name)}</button><button data-export="${i}" title="Export PMT Template">↓</button><button data-remove-template="${i}" title="Delete Template">×</button></div>`).join('');}
  function remove(){change(()=>{state.objects=state.objects.filter(o=>o.locked||!selected.includes(o.id));selected=selected.filter(key=>state.objects.some(o=>o.id===key));});}
  function duplicate(){change(()=>{const copies=clone(selectedObjects());copies.forEach(o=>{o.id=id();o.locked=false;move(o,24,24);});state.objects.push(...copies);selected=copies.map(o=>o.id);});}
  function reorder(front){change(()=>{
    const remaining=state.objects.filter(o=>!o.locked&&!selected.includes(o.id)),chosen=editableObjects();
    const ordered=front?[...remaining,...chosen]:[...chosen,...remaining];let index=0;
    state.objects=state.objects.map(o=>o.locked?o:ordered[index++]);
  });}
  async function copySelectedCell(saveAll=false){
    const image=one();if(!sliceMode||image?.type!=='embedded-image'||image.visible===false||sliceCopying)return;
    const snapshot=clone(image),cell=sliceCell;
    sliceCopying=true;syncSliceControls(dialog,image,true,sliceCell,true);error('');
    const progress=$('#sliceProgress'),status=$('#sliceStatus'),stop=$('[data-action=cancelSliceExport]');
    progress.hidden=false;progress.removeAttribute('value');status.textContent=saveAll?'Choose a folder for all image cells…':'Copying selected cell…';
    sliceExport=saveAll?new AbortController():null;stop.hidden=!saveAll;
    try{
      await new Promise(resolve=>setTimeout(resolve,0));
      if(saveAll){
        const result=await saveAllSlices(snapshot,(saved,total)=>{progress.max=total;progress.value=saved;status.textContent=`Saving cells: ${saved} of ${total}…`;},sliceExport.signal);
        status.textContent=result?`${result.saved===result.total?'Saved':'Stopped: saved'} ${result.saved} of ${result.total} cells in ${result.folder}`:'Save canceled.';
      }else{await copySlice(snapshot,cell);status.textContent='Selected cell copied.';}
    } catch(e){status.textContent=e.message;throw e;}
    finally{sliceCopying=false;sliceExport=null;stop.hidden=true;progress.hidden=true;syncSliceControls(dialog,one(),sliceMode,sliceCell);}
  }
  dialog.addEventListener('click',async event=>{
    const target=event.target.closest('button');if(!target)return;
    try{
      if(target.dataset.tool){tool=target.dataset.tool;cropMode=false;sliceMode=false;render();return;}
      if(target.dataset.layer){if(event.target.matches('input'))return;const key=target.dataset.layer;selected=event.shiftKey?(selected.includes(key)?selected.filter(x=>x!==key):[...selected,key]):[key];cropMode=false;sliceMode=false;tool='select';render();return;}
      if(target.dataset.template!=null){const t=templates[Number(target.dataset.template)];change(()=>{const objects=instantiate(t,{x:viewBox.x+viewBox.width/2,y:viewBox.y+viewBox.height/2});state.objects.push(...objects);selected=objects.map(o=>o.id);});return;}
      if(target.dataset.export!=null){await request('export-template',{contents:templateJson(templates[Number(target.dataset.export)])});return;}
      if(target.dataset.removeTemplate!=null){templates.splice(Number(target.dataset.removeTemplate),1);await persistTemplates();return;}
      switch(target.dataset.action){
        case 'undo':historyMove(-1);break;case 'redo':historyMove(1);break;
        case 'delete':remove();break;case 'duplicate':duplicate();break;
        case 'lock':setLocked(true);break;case 'unlock':setLocked(false);break;
        case 'copy':await copyObjects(selectedObjects());break;
        case 'paste':if(!await pasteObjects())error('Copy objects or an image first.');break;
        case 'front':reorder(true);break;case 'back':reorder(false);break;
        case 'slice':if(one()?.type!=='embedded-image')break;sliceMode=!sliceMode;cropMode=false;tool='select';sliceCell=0;change(()=>{if(sliceMode&&!one().locked&&!Array.isArray(one().sliceDividers))setSliceCount(one(),3);});break;
        case 'copySlice':await copySelectedCell();break;
        case 'saveAllSlices':await copySelectedCell(true);break;
        case 'cancelSliceExport':sliceExport?.abort();break;
        case 'crop':if(one()?.locked)break;cropMode=!cropMode;sliceMode=false;tool='select';render();break;
        case 'resetCrop':change(()=>{if(one()&&!one().locked)resetCrop(one());});break;
        case 'resetAllCrops':change(()=>state.objects.filter(o=>o.type==='embedded-image'&&!o.locked).forEach(resetCrop));break;
        case 'applyCrop':{
          const object=one();if(!object||object.locked)break;
          target.disabled=true;error('Applying image crop…');
          const snapshot=JSON.stringify(object);
          try{const cropped=await applyCrop(clone(object));if(state.objects.includes(object)&&JSON.stringify(object)===snapshot&&!object.locked){change(()=>Object.assign(object,cropped));cropMode=false;render();}error('');}
          finally{inspector.sync(selectedObjects(),cropMode);}
          break;
        }
        case 'addImage':chooseFile('image/*',addImage);break;
        case 'screenCapture':target.disabled=true;try{await captureImage();}finally{target.disabled=false;}break;
        case 'regionCapture':target.disabled=true;try{await captureImage(true);}finally{target.disabled=false;}break;
        case 'saveTemplate':if(!selected.length){error('Select one or more objects first.');break;}{const answer=await ask('Save Object Template','<label>Name <input name="name" value="My Object" required maxlength="120"></label>');if(answer.choice==='ok'){templates.push(captureTemplate(selectedObjects(),answer.values.name));await persistTemplates();}}break;
        case 'importTemplate':chooseFile('.json',async file=>{templates.push(await parseTemplate(await file.text()));await persistTemplates();});break;
        case 'cancel':dialog.close('cancel');break;
        case 'apply':target.disabled=true;error('Rendering lossless PNG…');try{const rendered=await renderPng(state);dialog.result={state:clone(state),...rendered};dialog.close('apply');}finally{target.disabled=false;}break;
      }
    }catch(e){error(e);}
  });
  dialog.addEventListener('change',event=>{
    const el=event.target;
    if(el.dataset.visible){change(()=>{const object=state.objects.find(o=>o.id===el.dataset.visible);if(object&&!object.locked)object.visible=el.checked;});return;}
    if(el.id==='canvasZoom'){render(false,true);return;}
    if(el.id==='sliceDividerCount'){change(()=>{setSliceCount(one(),el.value);sliceCell=0;});return;}
    if(el.id==='sliceCell'){sliceCell=Number(el.value);if(sliceMode&&one()?.type==='embedded-image')updateSliceOverlay(svg,one(),sliceCell,zoom);return;}
    if(['canvasColor','canvasTransparent'].includes(el.id)){change(()=>state.background=$('#canvasTransparent').checked?'none':$('#canvasColor').value);return;}
    change(()=>inspector.edit(selectedObjects(),el));
  });
  dialog.addEventListener('keydown',event=>{
    event.stopPropagation();if(event.target.matches('input,textarea,select'))return;
    if(event.key==='Delete'||event.key==='Backspace'){event.preventDefault();remove();}
    if(event.ctrlKey&&event.key.toLowerCase()==='c'){event.preventDefault();(sliceMode&&one()?.type==='embedded-image'?copySelectedCell():copyObjects(selectedObjects())).catch(error);}
    if(event.ctrlKey&&event.key.toLowerCase()==='v'){event.preventDefault();pasteObjects().catch(error);}
    if(event.ctrlKey&&event.key.toLowerCase()==='z'){event.preventDefault();historyMove(event.shiftKey?1:-1);}
    if(event.ctrlKey&&event.key.toLowerCase()==='y'){event.preventDefault();historyMove(1);}
    if(event.ctrlKey&&event.key.toLowerCase()==='d'){event.preventDefault();duplicate();}
    if(event.ctrlKey&&event.key.toLowerCase()==='a'){event.preventDefault();selected=state.objects.map(o=>o.id);render();}
    if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)){event.preventDefault();const step=event.shiftKey?10:1;change(()=>editableObjects().forEach(o=>move(o,event.key==='ArrowLeft'?-step:event.key==='ArrowRight'?step:0,event.key==='ArrowUp'?-step:event.key==='ArrowDown'?step:0)));}
  });
  dialog.addEventListener('paste',event=>{if(event.target.matches('input,textarea'))return;const files=[...event.clipboardData.items].filter(i=>i.type.startsWith('image/')).map(i=>i.getAsFile());event.preventDefault();event.stopPropagation();(async()=>{if(event.isTrusted&&await pasteObjects())return;for(const file of files)await addImage(file);})().catch(error);});
  request('templates-load').then(values=>{templates=values||[];renderTemplates();}).catch(error);
  $('#canvasTransparent').checked=!state.background||state.background==='none';if(!$('#canvasTransparent').checked)$('#canvasColor').value=state.background;
  const observer=new ResizeObserver(()=>{if(!gesture)render(false,true);});observer.observe(viewport,{box:'border-box'});render();
  return await new Promise(resolve=>dialog.addEventListener('close',()=>{if(finished)return;finished=true;sliceExport?.abort();observer.disconnect();resolve(dialog.result||null);},{once:true}));
  } finally {dialog.remove();await request('annotation-mode',{open:false});}
}
