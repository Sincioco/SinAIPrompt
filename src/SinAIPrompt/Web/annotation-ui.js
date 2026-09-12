import {request,blobData,escapeHtml,ask} from './bridge.js';
import {toPng} from './document.js';
import {clone,id,isLine,clamp,bounds,union,outputBounds,imageClip,move,resize,objectSvg,renderPng} from './annotation-model.js';
import {captureTemplate,parseTemplate,templateJson,instantiate} from './templates.js';
import {copyObjects} from './annotation-clipboard.js';

export async function annotate(initial) {
  const dialog=document.createElement('dialog'); dialog.className='annotation'; dialog.setAttribute('aria-label','Image annotation');
  dialog.innerHTML=`<div class="annotation-layout"><header><div><h2>Image annotation <small>Shapes, images &amp; reusable objects</small></h2></div><div><button data-action="undo" title="Undo (Ctrl+Z)">↶ Undo</button> <button data-action="redo" title="Redo (Ctrl+Y)">↷ Redo</button> <select id="canvasZoom" aria-label="Canvas zoom"><option value="fit">Fit</option>${[25,50,75,100,150,200].map(n=>`<option value="${n}">${n}%</option>`).join('')}</select></div></header>
  <div class="annotation-body"><aside class="left"><h3>Draw</h3><div class="tools">${[['select','↖ Select'],['arrow','↗ Arrow'],['line','╱ Line'],['rectangle','▭ Rectangle'],['circle','◯ Circle'],['text','T Text']].map(([tool,label])=>`<button data-tool="${tool}">${label}</button>`).join('')}</div><button data-action="addImage" class="wide">＋ Add image…</button><p class="hint">Paste images with Ctrl+V.<br>Drag empty canvas to select touching objects.<br>Shift adds to the selection.</p><hr><h3>Layers</h3><div id="layers"></div><div class="small-actions"><button data-action="front">To front</button><button data-action="back">To back</button><button data-action="copy" title="Copy selected objects (Ctrl+C)">Copy…</button><button data-action="paste" title="Paste objects or an image (Ctrl+V)">Paste</button><button data-action="duplicate">Duplicate</button><button data-action="delete">Delete</button></div><hr><h3>Object templates</h3><button data-action="saveTemplate" class="wide">Save selected as template</button><button data-action="importTemplate" class="wide">Import PMT template…</button><div id="templates"></div></aside>
  <main class="canvas-viewport"><div class="canvas-stage"><svg id="canvas" tabindex="0" xmlns="http://www.w3.org/2000/svg" aria-label="Annotation canvas"></svg></div></main>
  <aside class="right"><h3>Appearance</h3><label class="field">Outline <input id="stroke" type="color" value="#dc3939"></label><label class="field"><span>Transparent outline</span><input id="noStroke" type="checkbox"></label><label class="field">Fill <input id="fill" type="color" value="#fff2a6"></label><label class="field"><span>Transparent fill</span><input id="noFill" type="checkbox" checked></label><label class="field">Thickness <input id="strokeWidth" type="number" min="1" max="80" value="4"></label><label class="field">Arrow head <input id="arrowSize" type="number" min="4" max="160" value="20"></label><label class="field">Opacity % <input id="opacity" type="number" min="0" max="100" value="100"></label>
  <div id="geometry"><hr><h3>Position &amp; size (px)</h3><div class="pair">${['x','y','width','height'].map(k=>`<label>${k[0].toUpperCase()+k.slice(1)}<input data-geometry="${k}" type="number" step="1"></label>`).join('')}</div></div>
  <div id="endpoints" hidden><hr><h3>Endpoints (px)</h3><div class="pair">${['x1','y1','x2','y2'].map(k=>`<label>${k}<input data-endpoint="${k}" type="number"></label>`).join('')}</div></div>
  <div id="imageCrop" hidden><hr><h3>Reversible crop (px)</h3><button data-action="crop" class="wide">Drag crop handles</button><div class="pair">${['top','right','bottom','left'].map(k=>`<label>${k[0].toUpperCase()+k.slice(1)}<input data-crop-value="${k}" type="number" min="0" value="0"></label>`).join('')}</div><h3 style="margin-top:15px">Corner radius (px)</h3><div class="pair">${[['topLeft','Top left'],['topRight','Top right'],['bottomLeft','Bottom left'],['bottomRight','Bottom right']].map(([k,label])=>`<label>${label}<input data-radius="${k}" type="number" min="0" value="0"></label>`).join('')}</div><button data-action="resetCrop" class="wide">Reset crop &amp; corners</button><p class="hint">Crop values use the image’s canvas pixels. The original image stays available.</p></div>
  <div id="textFields" hidden><hr><h3>Text</h3><textarea id="shapeText" rows="4" style="width:100%"></textarea><label class="field">Font size <input id="shapeFontSize" type="number" min="6" max="200" value="20"></label><label class="field">Text color <input id="textColor" type="color" value="#20252c"></label></div>
  <hr><h3>Canvas</h3><label class="field">Background <input id="canvasColor" type="color" value="#ffffff"></label><label class="field"><span>Transparent</span><input id="canvasTransparent" type="checkbox" checked></label><p id="dimensions" class="hint"></p></aside></div>
  <footer><span class="annotation-error" role="alert"></span><span class="hint">Corners keep proportions · Sides stretch · Wheel zooms · Del removes · Esc cancels</span><div class="actions"><button data-action="cancel">Cancel</button><button data-action="apply" class="primary">Apply to document</button></div></footer></div>`;
  await request('annotation-mode',{open:true});
  try {
  document.body.append(dialog);dialog.showModal();
  const $=s=>dialog.querySelector(s),$$=s=>[...dialog.querySelectorAll(s)];
  const svg=$('#canvas'),viewport=$('.canvas-viewport');
  let state=clone(initial),selected=[],tool='select',cropMode=false,gesture=null,zoom=1,viewBox,templates=[];
  let history=[clone(state)],historyIndex=0,finished=false;
  const selectedObjects=()=>state.objects.filter(o=>selected.includes(o.id));
  const one=()=>selectedObjects().length===1?selectedObjects()[0]:null;
  const error=e=>{$('.annotation-error').textContent=e?.message||String(e||'');};
  const historySave=()=>{if(JSON.stringify(history[historyIndex])===JSON.stringify(state))return;history=history.slice(0,historyIndex+1);history.push(clone(state));if(history.length>100)history.shift();historyIndex=history.length-1;};
  const change=fn=>{fn();historySave();render();};
  const historyMove=delta=>{historyIndex=clamp(historyIndex+delta,0,history.length-1);state=clone(history[historyIndex]);selected=selected.filter(key=>state.objects.some(o=>o.id===key));render();};
  const style=()=>({stroke:$('#noStroke').checked?'none':$('#stroke').value,fill:$('#noFill').checked?'none':$('#fill').value,strokeWidth:clamp($('#strokeWidth').value,1,80),arrowSize:clamp($('#arrowSize').value,4,160),opacity:clamp($('#opacity').value,0,100)/100,outlineVisible:!$('#noStroke').checked});
  function workspace() {
    const b=union([{x:0,y:0,width:state.width||800,height:state.height||500},...state.objects.map(o=>bounds(o))]);
    return {x:b.x-90,y:b.y-90,width:b.width+180,height:b.height+180};
  }
  function render(keepInspector=false,fit=false) {
    const previous=viewBox;
    if(!gesture){
      viewBox=workspace();
      // Fit on opening, a window resize, or an explicit zoom choice. Moving artwork
      // expands the scrollable workspace without repeatedly shrinking everything.
      if(fit||!previous){if($('#canvasZoom').value==='fit')zoom=Math.min(1,Math.max(.05,(viewport.clientWidth-100)/viewBox.width),Math.max(.05,(viewport.clientHeight-100)/viewBox.height));else zoom=Number($('#canvasZoom').value)/100;}
    }
    svg.setAttribute('viewBox',`${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}`);svg.setAttribute('width',viewBox.width*zoom);svg.setAttribute('height',viewBox.height*zoom);
    let content=state.background && state.background!=='none'?`<rect x="${viewBox.x}" y="${viewBox.y}" width="${viewBox.width}" height="${viewBox.height}" fill="${escapeHtml(state.background)}"/>`:'';
    content+=state.objects.filter(o=>o.visible!==false).map(o=>objectSvg(o,true)).join('');
    const size=8/zoom;
    for(const o of selectedObjects().filter(o=>o.visible!==false)) {
      const b=bounds(o,false);content+=`<rect x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}" fill="none" stroke="#0874c9" stroke-width="${1/zoom}" stroke-dasharray="${4/zoom}" pointer-events="none"/>`;
      const handle=(x,y,attrs)=>`<rect x="${x-size/2}" y="${y-size/2}" width="${size}" height="${size}" ${attrs} data-id="${o.id}"/>`;
      if(isLine(o)) content+=handle(o.x1,o.y1,'data-handle="end" data-end="1"')+handle(o.x2,o.y2,'data-handle="end" data-end="2"');
      else if(selected.length===1){const c=cropMode&&o.type==='embedded-image'?imageClip(o):b;for(const [key,x,y] of [['nw',c.x,c.y],['ne',c.x+c.width,c.y],['se',c.x+c.width,c.y+c.height],['sw',c.x,c.y+c.height],['n',c.x+c.width/2,c.y],['e',c.x+c.width,c.y+c.height/2],['s',c.x+c.width/2,c.y+c.height],['w',c.x,c.y+c.height/2]])content+=handle(x,y,`${cropMode&&o.type==='embedded-image'?'data-crop':'data-handle'}="${key}"`);}
    }
    if(gesture?.kind==='marquee')content+='<rect data-marquee fill="#0874c922" stroke="#0874c9" stroke-width="'+1/zoom+'" pointer-events="none"/>';
    svg.innerHTML=content;svg.style.cursor=tool==='select'?'default':'crosshair';
    if(previous&&!gesture&&!fit){viewport.scrollLeft+=(previous.x-viewBox.x)*zoom;viewport.scrollTop+=(previous.y-viewBox.y)*zoom;}
    $$('#layers .layer').length;$('#layers').innerHTML=[...state.objects].reverse().map(o=>`<button class="layer ${selected.includes(o.id)?'selected':''}" data-layer="${o.id}" title="${escapeHtml(o.name||o.type)}"><input type="checkbox" data-visible="${o.id}" aria-label="Show ${escapeHtml(o.name||o.type)}" ${o.visible!==false?'checked':''}><span>${escapeHtml(o.name||o.type)}</span></button>`).join('');
    $$('[data-tool]').forEach(b=>b.classList.toggle('active',b.dataset.tool===tool));
    $('[data-action=undo]').disabled=historyIndex===0;$('[data-action=redo]').disabled=historyIndex===history.length-1;
    const b=outputBounds(state);$('#dimensions').textContent=`Output: ${Math.ceil(b.width)} × ${Math.ceil(b.height)} px. The canvas expands as you add or move objects.`;
    if(!keepInspector) syncInspector();
  }
  function syncInspector() {
    const o=one();$('#geometry').hidden=!o||isLine(o);$('#endpoints').hidden=!o||!isLine(o);$('#imageCrop').hidden=o?.type!=='embedded-image';$('#textFields').hidden=o?.type!=='textbox';
    if(!o)return;
    if(o.stroke && o.stroke!=='none')$('#stroke').value=o.stroke;if(o.fill && o.fill!=='none')$('#fill').value=o.fill;
    $('#noStroke').checked=o.stroke==='none'||o.outlineVisible===false;$('#noFill').checked=!o.fill||o.fill==='none';$('#strokeWidth').value=o.strokeWidth||4;$('#arrowSize').value=o.arrowSize||20;$('#opacity').value=Math.round((o.opacity??1)*100);
    $$('[data-geometry]').forEach(el=>el.value=Math.round(o[el.dataset.geometry]||0));$$('[data-endpoint]').forEach(el=>el.value=Math.round(o[el.dataset.endpoint]||0));
    if(o.type==='embedded-image') {const c=imageClip(o),insets={top:c.y-o.y,left:c.x-o.x,right:o.x+o.width-c.x-c.width,bottom:o.y+o.height-c.y-c.height};$$('[data-crop-value]').forEach(el=>el.value=Math.round(insets[el.dataset.cropValue]));$$('[data-radius]').forEach(el=>el.value=o.cropCornerRadii?.[el.dataset.radius]??o.cropCornerRadius??0);}
    if(o.type==='textbox'){$('#shapeText').value=o.text||'';$('#shapeFontSize').value=o.fontSize||20;$('#textColor').value=o.textColor||'#20252c';}
    $('[data-action=crop]').classList.toggle('active',cropMode);
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
    const handle=target.dataset.handle,crop=target.dataset.crop,end=target.dataset.end;
    if(tool==='select' && (handle||crop)){gesture={kind:end?'endpoint':crop?'crop':'resize',point:p,objects:clone(selectedObjects()),handle:crop||handle,end};}
    else if(tool==='select' && objectId){if(event.shiftKey)selected=selected.includes(objectId)?selected.filter(x=>x!==objectId):[...selected,objectId];else if(!selected.includes(objectId))selected=[objectId];gesture={kind:'move',point:p,objects:clone(selectedObjects())};}
    else if(tool==='select'){gesture={kind:'marquee',point:p,end:p,previous:[...selected],add:event.shiftKey};if(!event.shiftKey)selected=[];cropMode=false;}
    else {
      const type=tool==='text'?'textbox':tool; const o={id:id(),type,name:type==='embedded-image'?'Image':type,visible:true,...style()};
      if(isLine(o))Object.assign(o,{x1:p.x,y1:p.y,x2:p.x+1,y2:p.y+1});else Object.assign(o,{x:p.x,y:p.y,width:1,height:1});
      if(type==='textbox')Object.assign(o,{text:'Text',fontSize:20,textColor:'#20252c',fontFamily:'Segoe UI'});
      state.objects.push(o);selected=[o.id];gesture={kind:'draw',point:p,objects:[clone(o)]};
    }
    viewport.setPointerCapture(event.pointerId);render();
  });
  viewport.addEventListener('pointermove',event=>{
    if(!gesture)return;const p=point(event),dx=p.x-gesture.point.x,dy=p.y-gesture.point.y;
    if(gesture.kind==='marquee'){
      gesture.end=p;
      const rect=$('[data-marquee]');
      for(const [key,value] of Object.entries({x:Math.min(p.x,gesture.point.x),y:Math.min(p.y,gesture.point.y),width:Math.abs(dx),height:Math.abs(dy)}))rect.setAttribute(key,value);
      return;
    }
    for(const original of gesture.objects){const o=state.objects.find(o=>o.id===original.id);if(!o)continue;Object.assign(o,clone(original));
      if(gesture.kind==='move')move(o,dx,dy);
      else if(gesture.kind==='endpoint'){o['x'+gesture.end]=p.x;o['y'+gesture.end]=p.y;}
      else if(gesture.kind==='draw'){if(isLine(o)){o.x2=p.x;o.y2=p.y;}else{const w=Math.abs(dx),h=event.shiftKey?w:Math.abs(dy);Object.assign(o,{x:Math.min(p.x,gesture.point.x),y:Math.min(p.y,gesture.point.y),width:Math.max(1,w),height:Math.max(1,h)});}}
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
      if(gesture.kind==='draw'){const o=one();if(o&&!isLine(o)&&o.width<5&&o.height<5){o.width=o.type==='textbox'?240:120;o.height=o.type==='textbox'?70:90;}tool='select';}
      historySave();
    }
    gesture=null;if(viewport.hasPointerCapture(event.pointerId))viewport.releasePointerCapture(event.pointerId);render();
  });
  viewport.addEventListener('pointercancel',()=>{if(gesture?.kind==='marquee')selected=gesture.previous;else state=clone(history[historyIndex]);gesture=null;render();});
  svg.addEventListener('dblclick',event=>{const o=state.objects.find(o=>o.id===event.target.closest('[data-object]')?.dataset.object);if(o?.type==='textbox'){$('#shapeText').focus();$('#shapeText').select();}});
  async function addImage(file){const png=await toPng(await blobData(file));const b=outputBounds(state);change(()=>{const o={id:id(),type:'embedded-image',name:file.name||'Pasted image',source:png.data,x:state.objects.length?b.x+b.width+24:0,y:0,width:png.width,height:png.height,visible:true};state.objects.push(o);selected=[o.id];});}
  async function pasteObjects(){
    const clipboard=await request('annotation-paste');
    if(clipboard?.objects){
      const template=await parseTemplate(clipboard.objects),selection=selectedObjects();
      const r=viewport.getBoundingClientRect(),b=selection.length?union(selection.map(o=>bounds(o))):null;
      const center=b?{x:b.x+b.width/2+24,y:b.y+b.height/2+24}:point({clientX:r.x+viewport.clientWidth/2,clientY:r.y+viewport.clientHeight/2});
      change(()=>{const objects=instantiate(template,center);state.objects.push(...objects);selected=objects.map(o=>o.id);tool='select';});
      return true;
    }
    if(clipboard?.image){await addImage(new File([await (await fetch(clipboard.image)).blob()],'Pasted image'));return true;}
    return false;
  }
  function chooseFile(accept,action){const input=document.createElement('input');input.type='file';input.accept=accept;input.onchange=()=>{if(input.files[0])action(input.files[0]).catch(error);};input.click();}
  async function persistTemplates(){await request('templates-save',{templates});renderTemplates();}
  function renderTemplates(){$('#templates').innerHTML=templates.map((t,i)=>`<div class="template-row"><button data-template="${i}">${escapeHtml(t.name)}</button><button data-export="${i}" title="Export PMT template">↓</button><button data-remove-template="${i}" title="Delete template">×</button></div>`).join('');}
  function remove(){change(()=>{state.objects=state.objects.filter(o=>!selected.includes(o.id));selected=[];});}
  function duplicate(){change(()=>{const copies=clone(selectedObjects());copies.forEach(o=>{o.id=id();move(o,24,24);});state.objects.push(...copies);selected=copies.map(o=>o.id);});}
  dialog.addEventListener('click',async event=>{
    const target=event.target.closest('button');if(!target)return;
    try{
      if(target.dataset.tool){tool=target.dataset.tool;cropMode=false;render();return;}
      if(target.dataset.layer){if(event.target.matches('input'))return;const key=target.dataset.layer;selected=event.shiftKey?(selected.includes(key)?selected.filter(x=>x!==key):[...selected,key]):[key];cropMode=false;tool='select';render();return;}
      if(target.dataset.template!=null){const t=templates[Number(target.dataset.template)];change(()=>{const objects=instantiate(t,{x:viewBox.x+viewBox.width/2,y:viewBox.y+viewBox.height/2});state.objects.push(...objects);selected=objects.map(o=>o.id);});return;}
      if(target.dataset.export!=null){await request('export-template',{contents:templateJson(templates[Number(target.dataset.export)])});return;}
      if(target.dataset.removeTemplate!=null){templates.splice(Number(target.dataset.removeTemplate),1);await persistTemplates();return;}
      switch(target.dataset.action){
        case 'undo':historyMove(-1);break;case 'redo':historyMove(1);break;
        case 'delete':remove();break;case 'duplicate':duplicate();break;
        case 'copy':await copyObjects(selectedObjects());break;
        case 'paste':if(!await pasteObjects())error('Copy objects or an image first.');break;
        case 'front':change(()=>{state.objects=[...state.objects.filter(o=>!selected.includes(o.id)),...selectedObjects()];});break;
        case 'back':change(()=>{state.objects=[...selectedObjects(),...state.objects.filter(o=>!selected.includes(o.id))];});break;
        case 'crop':cropMode=!cropMode;tool='select';render();break;
        case 'resetCrop':change(()=>{const o=one();if(o){delete o.imageClip;delete o.cropCornerRadii;o.cropCornerRadius=0;o.cropVisible=true;}});break;
        case 'addImage':chooseFile('image/*',addImage);break;
        case 'saveTemplate':if(!selected.length){error('Select one or more objects first.');break;}{const answer=await ask('Save object template','<label>Name <input name="name" value="My object" required maxlength="120"></label>');if(answer.choice==='ok'){templates.push(captureTemplate(selectedObjects(),answer.values.name));await persistTemplates();}}break;
        case 'importTemplate':chooseFile('.json',async file=>{templates.push(await parseTemplate(await file.text()));await persistTemplates();});break;
        case 'cancel':dialog.close('cancel');break;
        case 'apply':target.disabled=true;error('Rendering lossless PNG…');try{const rendered=await renderPng(state);dialog.result={state:clone(state),...rendered};dialog.close('apply');}finally{target.disabled=false;}break;
      }
    }catch(e){error(e);}
  });
  dialog.addEventListener('change',event=>{
    const el=event.target,o=one();
    if(el.dataset.visible){change(()=>{state.objects.find(o=>o.id===el.dataset.visible).visible=el.checked;});return;}
    if(el.id==='canvasZoom'){render(false,true);return;}
    if(['stroke','fill','noStroke','noFill','strokeWidth','arrowSize','opacity'].includes(el.id)){change(()=>selectedObjects().forEach(o=>Object.assign(o,style())));return;}
    if(['canvasColor','canvasTransparent'].includes(el.id)){change(()=>state.background=$('#canvasTransparent').checked?'none':$('#canvasColor').value);return;}
    if(!o)return;
    change(()=>{
      if(el.dataset.geometry){const b={x:o.x,y:o.y,width:o.width,height:o.height};b[el.dataset.geometry]=Number(el.value)||0;b.width=Math.max(1,b.width);b.height=Math.max(1,b.height);resize(o,b);}
      if(el.dataset.endpoint)o[el.dataset.endpoint]=Number(el.value)||0;
      if(el.dataset.cropValue){let values=Object.fromEntries($$('[data-crop-value]').map(el=>[el.dataset.cropValue,Math.max(0,Number(el.value)||0)]));values.left=Math.min(o.width-1,values.left);values.right=Math.min(o.width-values.left-1,values.right);values.top=Math.min(o.height-1,values.top);values.bottom=Math.min(o.height-values.top-1,values.bottom);o.imageClip={x:o.x+values.left,y:o.y+values.top,width:o.width-values.left-values.right,height:o.height-values.top-values.bottom};o.cropVisible=true;}
      if(el.dataset.radius){o.cropCornerRadii=Object.fromEntries($$('[data-radius]').map(el=>[el.dataset.radius,clamp(el.value,0,Math.min(imageClip(o).width,imageClip(o).height)/2)]));}
      if(el.id==='shapeText')o.text=el.value;if(el.id==='shapeFontSize')o.fontSize=clamp(el.value,6,200);if(el.id==='textColor')o.textColor=el.value;
    });
  });
  dialog.addEventListener('keydown',event=>{
    event.stopPropagation();if(event.target.matches('input,textarea,select'))return;
    if(event.key==='Delete'||event.key==='Backspace'){event.preventDefault();remove();}
    if(event.ctrlKey&&event.key.toLowerCase()==='c'){event.preventDefault();copyObjects(selectedObjects()).catch(error);}
    if(event.ctrlKey&&event.key.toLowerCase()==='v'){event.preventDefault();pasteObjects().catch(error);}
    if(event.ctrlKey&&event.key.toLowerCase()==='z'){event.preventDefault();historyMove(event.shiftKey?1:-1);}
    if(event.ctrlKey&&event.key.toLowerCase()==='y'){event.preventDefault();historyMove(1);}
    if(event.ctrlKey&&event.key.toLowerCase()==='d'){event.preventDefault();duplicate();}
    if(event.ctrlKey&&event.key.toLowerCase()==='a'){event.preventDefault();selected=state.objects.map(o=>o.id);render();}
    if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)){event.preventDefault();const step=event.shiftKey?10:1;change(()=>selectedObjects().forEach(o=>move(o,event.key==='ArrowLeft'?-step:event.key==='ArrowRight'?step:0,event.key==='ArrowUp'?-step:event.key==='ArrowDown'?step:0)));}
  });
  dialog.addEventListener('paste',event=>{if(event.target.matches('input,textarea'))return;const files=[...event.clipboardData.items].filter(i=>i.type.startsWith('image/')).map(i=>i.getAsFile());event.preventDefault();event.stopPropagation();(async()=>{if(event.isTrusted&&await pasteObjects())return;for(const file of files)await addImage(file);})().catch(error);});
  request('templates-load').then(values=>{templates=values||[];renderTemplates();}).catch(error);
  $('#canvasTransparent').checked=!state.background||state.background==='none';if(!$('#canvasTransparent').checked)$('#canvasColor').value=state.background;
  const observer=new ResizeObserver(()=>{if(!gesture)render(false,true);});observer.observe(viewport,{box:'border-box'});render();
  return await new Promise(resolve=>dialog.addEventListener('close',()=>{if(finished)return;finished=true;observer.disconnect();resolve(dialog.result||null);},{once:true}));
  } finally {dialog.remove();await request('annotation-mode',{open:false});}
}
