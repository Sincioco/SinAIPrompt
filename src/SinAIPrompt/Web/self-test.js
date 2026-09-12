import {normalizeIndent,parseHtml,portableHtml,renameImageFolder} from './document.js';
import {prepareRichSourceHighlight} from './source-highlighting.js';
import {captureTemplate,parseTemplate,templateJson} from './templates.js';
import {renderPng,outputBounds,move,id} from './annotation-model.js';
import {request} from './bridge.js';
import {runRibbonTests} from './ribbon-self-test.js';

export async function run(){
  const results=[];const check=(value,name)=>{if(!value)throw Error(name);results.push(name);};
  const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  const doc=()=>document.querySelector('#document').contentDocument;
  const selectText=(selector)=>{const range=doc().createRange();range.selectNodeContents(doc().querySelector(selector));const s=doc().getSelection();s.removeAllRanges();s.addRange(range);doc().dispatchEvent(new Event('selectionchange'));};
  const click=selector=>{const element=document.querySelector(selector);if(!element)throw Error('Missing control: '+selector);element.click();};
  const waitFor=async selector=>{for(let i=0;i<100;i++){const el=document.querySelector(selector);if(el)return el;await delay(50);}throw Error('Timed out: '+selector);};
  await window.editor.ready();
  check(doc().body.isContentEditable,'Visual HTML is editable');
  const renamed=parseHtml(renameImageFolder('<p>Old folder/photo.png</p><img src="./Old%20folder/photo.png?size=1#preview"><img src="https://example.invalid/Old%20folder/photo.png"><img src="Other/photo.png">','Old folder','New # folder'));
  check(renamed.images[0].getAttribute('src')==='New%20%23%20folder/photo.png?size=1#preview'&&renamed.images[1].getAttribute('src').startsWith('https://example.invalid/')&&renamed.images[2].getAttribute('src')==='Other/photo.png'&&renamed.querySelector('p').textContent==='Old folder/photo.png','Folder rename updates encoded local image references without replacing other text or URLs');
  const savedCaret=doc().createRange();savedCaret.setStart(doc().querySelector('p').firstChild,7);savedCaret.collapse(true);
  doc().getSelection().removeAllRanges();doc().getSelection().addRange(savedCaret);doc().dispatchEvent(new Event('selectionchange'));
  await window.editor.setBase(doc().baseURI);
  check(doc().getSelection().anchorNode===doc().querySelector('p').firstChild&&doc().getSelection().anchorOffset===7,'Refreshing the saved asset folder preserves the text insertion point');
  selectText('p');document.querySelector('#fontSize').value='28';document.querySelector('#fontSize').dispatchEvent(new Event('change'));
  check(doc().querySelector('p').innerHTML.includes('28pt'),'Font size control applies the requested point size');
  selectText('p');window.editor.command('bold');check(/font-weight: bold|<b>|<strong>/.test(window.editor.html()),'Bold applies to selected text');
  window.editor.command('italic');window.editor.command('underline');window.editor.command('strikeThrough');
  window.editor.command('foreColor','#c22c44');window.editor.command('hiliteColor','#fff2a6');
  check(window.editor.html().includes('text-decoration')&&window.editor.html().includes('rgb(194, 44, 68)'),'Text decoration and selection colors persist');
  selectText('p');document.querySelector('#fontSize').value='22';document.querySelector('#fontSize').dispatchEvent(new Event('change'));
  check(doc().querySelector('p').innerHTML.includes('22pt'),'Font size can change after other rich-text formatting');
  check(normalizeIndent('    if (ready) {\n        run();\n    }')==='if (ready) {\n    run();\n}','Code indentation preserves internal alignment');
  for(const language of ['csharp','html','javascript','css','tsql'])check(prepareRichSourceHighlight(language==='html'?'<div>Hello</div>':'SELECT const public color 123 "hello"',language).highlighted,'PMT highlighting supports '+language);
  const range=doc().createRange();range.selectNodeContents(doc().body);range.collapse(false);doc().getSelection().removeAllRanges();doc().getSelection().addRange(range);doc().dispatchEvent(new Event('selectionchange'));
  const insert=window.editor.pasteCode();await waitFor('dialog textarea[name=code]');document.querySelector('[name=code]').value='    const greeting = "Hello";\n    console.log(greeting);';document.querySelector('[name=language]').value='javascript';click('dialog button[value=ok]');await insert;
  check(doc().querySelector('pre[data-sin-code=javascript] .rich-source-token-keyword'),'Paste Code produces highlighted editable blocks');
  const edit=window.editor.pasteCode(doc().querySelector('pre[data-sin-code]'));await waitFor('dialog textarea[name=code]');document.querySelector('[name=code]').value='    SELECT Id\n    FROM Prompts;';document.querySelector('[name=language]').value='tsql';click('dialog button[value=ok]');await edit;
  check(doc().querySelector('pre[data-sin-code=tsql]')?.textContent==='SELECT Id\nFROM Prompts;','Code language and text can be edited again');
  const canvas=document.createElement('canvas');canvas.width=360;canvas.height=200;const ctx=canvas.getContext('2d');ctx.fillStyle='#dce9f8';ctx.fillRect(0,0,360,200);ctx.fillStyle='#145c96';ctx.font='bold 30px Segoe UI';ctx.fillText('Prompt canvas',25,70);ctx.font='18px Segoe UI';ctx.fillText('Images + editable annotations',25,110);const png=canvas.toDataURL('image/png');
  const insertion=window.editor.insertImage(png);await waitFor('dialog button[value=separate]');click('dialog button[value=separate]');await insertion;
  const image=doc().querySelector('img');check(image&&image.getAttribute('src').includes('/image-'),'Image storage dialog creates separate PNG reference');
  await image.decode();check(image.naturalWidth===360,'Separate PNG is visible immediately after saving the prompt');
  image.click();document.querySelector('#imageWidth').value='220';document.querySelector('#imageWidth').dispatchEvent(new Event('change'));
  check(doc().querySelector('img').style.width==='220px','Image width control resizes selected image');
  window.editor.command('undo');check(doc().querySelector('img').style.width!=='220px','Image resize is undoable');
  doc().querySelector('img').click();
  const dialogRun=document.querySelector('#insertImage').onclick();await waitFor('dialog.annotation');
  check(document.querySelectorAll('[data-layer]').length===1,'Editor button opens the selected image and its existing layer');
  const fullLayout=await request('test-annotation-layout'),dialogBounds=document.querySelector('dialog.annotation').getBoundingClientRect();
  check(fullLayout.expanded&&!fullLayout.backgroundEnabled&&Math.abs(fullLayout.x)<1&&Math.abs(fullLayout.y)<1&&Math.abs(fullLayout.width-fullLayout.clientWidth)<1&&Math.abs(fullLayout.height-fullLayout.clientHeight)<1,'Annotation covers the native menu, document list, and status bar');
  check(dialogBounds.x===0&&dialogBounds.y===0&&dialogBounds.width===innerWidth&&dialogBounds.height===innerHeight,'Annotation fills its entire browser viewport');
  check(document.querySelectorAll('dialog.annotation .color-picker').length===4&&!document.querySelector('input[type=color]'),'Annotation outline, fill, text and canvas use the Office palette');
  click('#canvasColor');click('.color-palette [data-color="#156082"]');
  check(document.querySelector('#canvas > rect').getAttribute('fill')==='#156082'&&!document.querySelector('#canvasTransparent').checked,'Canvas palette updates the background and clears transparency');
  click('#canvasColor');click('.color-palette [data-color=none]');
  check(document.querySelector('#canvasTransparent').checked&&document.querySelector('dialog.annotation').open,'No Color restores transparency without closing annotation');
  const pointer=async(type,x,y,modifiers=0)=>request('test-mouse',{parameters:{type,x,y,modifiers,button:type==='mouseMoved'?'none':'left',buttons:type==='mouseReleased'?0:1,clickCount:1}});
  const canvasPoint=(x,y)=>{const matrix=document.querySelector('#canvas').getScreenCTM();return new DOMPoint(x,y).matrixTransform(matrix);};
  const drag=async(from,to,modifiers=0)=>{await pointer('mousePressed',from.x,from.y,modifiers);await pointer('mouseMoved',to.x,to.y,modifiers);await pointer('mouseReleased',to.x,to.y,modifiers);};
  const imageWidth=()=>document.querySelector('#canvas image').getBoundingClientRect().width;
  const initialImageWidth=imageWidth();
  for(const [x,y] of [[-180,-100],[180,-100],[180,100],[-180,100]]){
    const imageRect=document.querySelector('#canvas image').getBoundingClientRect();
    const center={x:imageRect.x+imageRect.width/2,y:imageRect.y+imageRect.height/2};
    await drag(center,{x:center.x+x,y:center.y+y});await delay(50);
    check(Math.abs(imageWidth()-initialImageWidth)<1,'Moving image toward corner '+x+','+y+' preserves its displayed size');
    check(Number(document.querySelector('[data-geometry=width]').value)===360&&Number(document.querySelector('[data-geometry=height]').value)===200,'Moving image preserves its pixel dimensions');
    click('[data-action=undo]');
  }
  const geometry=()=>{const image=document.querySelector('#canvas image');return Object.fromEntries(['x','y','width','height'].map(key=>[key,Number(image.getAttribute(key))]));};
  async function dragHandle(key,dx,dy){const r=document.querySelector('[data-handle='+key+']').getBoundingClientRect();const start={x:r.x+r.width/2,y:r.y+r.height/2};await drag(start,{x:start.x+dx,y:start.y+dy});}
  for(const key of ['nw','ne','se','sw']){
    const before=geometry();await dragHandle(key,key.includes('w')?-40:40,key.includes('n')?-10:10);const after=geometry();
    const oppositeX=key.includes('w')?'right':'left',oppositeY=key.includes('n')?'bottom':'top';
    check(Math.abs(after.width/after.height-before.width/before.height)<.001&&Math.abs((after.x+(oppositeX==='right'?after.width:0))-(before.x+(oppositeX==='right'?before.width:0)))<.01&&Math.abs((after.y+(oppositeY==='bottom'?after.height:0))-(before.y+(oppositeY==='bottom'?before.height:0)))<.01,'Corner '+key+' keeps proportions and anchors the opposite corner without Shift');
    click('[data-action=undo]');
  }
  for(const key of ['n','e','s','w']){
    const before=geometry();await dragHandle(key,key==='w'?-40:key==='e'?40:0,key==='n'?-20:key==='s'?20:0);const after=geometry();
    check(key==='n'||key==='s'?after.width===before.width&&after.height>before.height:after.height===before.height&&after.width>before.width,'Side '+key+' resizes freely without changing the other dimension');
    click('[data-action=undo]');
  }
  const wheelPoint=canvasPoint(180,100),zoomBefore=document.querySelector('#canvas').getScreenCTM().a;
  await request('test-mouse',{parameters:{type:'mouseWheel',x:wheelPoint.x,y:wheelPoint.y,deltaX:0,deltaY:-120}});await delay(50);
  const zoomIn=document.querySelector('#canvas').getScreenCTM().a;
  check(zoomIn>zoomBefore&&geometry().width===360,'Mouse wheel zooms in without resizing objects: '+zoomBefore+' to '+zoomIn);
  await request('test-mouse',{parameters:{type:'mouseWheel',x:wheelPoint.x,y:wheelPoint.y,deltaX:0,deltaY:120}});await delay(50);
  check(document.querySelector('#canvas').getScreenCTM().a<zoomIn,'Mouse wheel zooms out');
  document.querySelector('#canvasZoom').value='fit';document.querySelector('#canvasZoom').dispatchEvent(new Event('change',{bubbles:true}));
  click('[data-tool=arrow]');await drag(canvasPoint(50,150),canvasPoint(430,75));
  check(document.querySelectorAll('[data-layer]').length===2,'Dragging draws an arrow on a separate layer');
  const endpoint=document.querySelector('[data-end="2"]');const endRect=endpoint.getBoundingClientRect();
  await drag({x:endRect.x+endRect.width/2,y:endRect.y+endRect.height/2},canvasPoint(470,90));
  check(Math.abs(Number(document.querySelector('[data-endpoint=x2]').value)-470)<2,'Dragging arrow endpoint moves its target independently');
  click('[data-tool=rectangle]');await drag(canvasPoint(30,125),canvasPoint(200,180));
  check(document.querySelectorAll('[data-layer]').length===3,'Dragging draws a resizable rectangle');
  const handle=document.querySelector('[data-handle=se]');const handleRect=handle.getBoundingClientRect();
  await drag({x:handleRect.x+handleRect.width/2,y:handleRect.y+handleRect.height/2},canvasPoint(240,195));
  check(Number(document.querySelector('[data-geometry=width]').value)>200,'Dragging rectangle handle resizes the object');
  click('[data-tool=circle]');await drag(canvasPoint(255,20),canvasPoint(320,80));
  const stroke=document.querySelector('#noStroke');stroke.checked=true;stroke.dispatchEvent(new Event('change',{bubbles:true}));
  const fill=document.querySelector('#noFill');fill.checked=false;fill.dispatchEvent(new Event('change',{bubbles:true}));
  check(document.querySelectorAll('[data-layer]').length===4,'Circle drawing and transparent outline controls work');
  click('[data-tool=line]');await drag(canvasPoint(10,190),canvasPoint(200,210));
  check(document.querySelector('[data-end="1"]'),'Line exposes independently draggable endpoints');
  click('[data-action=undo]');check(document.querySelectorAll('[data-layer]').length===4,'Annotation undo removes the last drawing');
  click('[data-action=redo]');check(document.querySelectorAll('[data-layer]').length===5,'Annotation redo restores the drawing');
  click('[data-tool=select]');
  await drag(canvasPoint(-40,-40),canvasPoint(280,145));
  check(document.querySelectorAll('.layer.selected').length===4,'Marquee selects partially touched images, shapes, and arrows, excluding objects outside it');
  await drag(canvasPoint(500,250),canvasPoint(400,80));
  check(document.querySelectorAll('.layer.selected').length===1,'Reverse-direction marquee replaces the selection');
  await drag(canvasPoint(-40,-40),canvasPoint(10,10),8);
  check(document.querySelectorAll('.layer.selected').length===2,'Shift-marquee adds touched objects to the selection');
  await drag(canvasPoint(-40,-40),canvasPoint(500,250));
  const selectedIds=[...document.querySelectorAll('.layer.selected')].map(el=>el.dataset.layer);
  check(selectedIds.length===5,'Marquee selects all five objects without changing their layers');
  async function shortcut(key){await request('test-key',{parameters:{type:'keyDown',key,code:'Key'+key.toUpperCase(),windowsVirtualKeyCode:key.toUpperCase().charCodeAt(0),modifiers:2}});await request('test-key',{parameters:{type:'keyUp',key,code:'Key'+key.toUpperCase(),windowsVirtualKeyCode:key.toUpperCase().charCodeAt(0)}});}
  for(const format of ['svg','png']){
    if(format==='svg')await shortcut('c');else click('[data-action=copy]');
    await waitFor('dialog.form-dialog button[value='+format+']');click('dialog.form-dialog button[value='+format+']');
    const expected=format==='svg'?'image/svg+xml':'PNG';let formats=[];
    for(let i=0;i<100;i++){formats=await request('test-clipboard-formats')||[];if(formats.includes(expected))break;await delay(20);}
    check(formats.includes(expected)&&formats.includes('SinAIPrompt.AnnotationObjects'),'Copy '+format.toUpperCase()+' provides the selected format and editable object metadata');
    if(format==='svg')await shortcut('v');else click('[data-action=paste]');
    for(let i=0;i<100&&document.querySelectorAll('[data-layer]').length!==10;i++)await delay(20);
    const pasted=[...document.querySelectorAll('.layer.selected')].map(el=>el.dataset.layer);
    check(document.querySelectorAll('[data-layer]').length===10&&pasted.length===5&&pasted.every(key=>!selectedIds.includes(key)),'Paste after '+format.toUpperCase()+' copy inserts five independently editable objects with new IDs');
    click('[data-action=undo]');check(document.querySelectorAll('[data-layer]').length===5,'Pasting '+format.toUpperCase()+' objects is one undo step');
    await drag(canvasPoint(-40,-40),canvasPoint(500,250));
  }
  const copiedBeforeCancel=await request('annotation-paste');
  click('[data-action=copy]');await waitFor('dialog.form-dialog button[value=cancel]');click('dialog.form-dialog button[value=cancel]');
  check(JSON.stringify(await request('annotation-paste'))===JSON.stringify(copiedBeforeCancel),'Canceling the format choice preserves the clipboard');
  const original=[...document.querySelectorAll('[data-layer]')].find(el=>el.textContent.includes('Original Image'));original.click();
  const crop=document.querySelector('[data-crop-value=left]');crop.value='20';crop.dispatchEvent(new Event('change',{bubbles:true}));
  const radius=document.querySelector('[data-radius=topLeft]');radius.value='16';radius.dispatchEvent(new Event('change',{bubbles:true}));
  click('[data-action=crop]');const cropHandle=document.querySelector('[data-crop=e]'),cropRect=cropHandle.getBoundingClientRect();
  await drag({x:cropRect.x+cropRect.width/2,y:cropRect.y+cropRect.height/2},canvasPoint(340,100));
  check(Number(document.querySelector('[data-crop-value=right]').value)>=19,'Dragging crop handle changes the precise crop value');
  await request('test-capture');
  click('[data-action=saveTemplate]');await waitFor('dialog.form-dialog [name=name]');document.querySelector('dialog.form-dialog [name=name]').value='Reusable cropped image';click('dialog.form-dialog button[value=ok]');await delay(100);
  check(document.querySelector('[data-template]'),'Object template appears in library');
  const templateData=(await request('templates-load'))[0];const imported=await parseTemplate(templateJson(templateData));check(imported.objects[0].type==='embedded-image','PMT image template JSON round-trips');
  click('[data-action=apply]');await dialogRun;
  const restoredLayout=await request('test-annotation-layout');
  check(!restoredLayout.expanded&&restoredLayout.backgroundEnabled&&restoredLayout.width<fullLayout.width&&restoredLayout.height<fullLayout.height,'Apply restores the editor beside the document list and below the menu');
  const annotated=doc().querySelector('img[data-sin-annotation]');check(annotated,'Annotation apply retains layer metadata');
  const state=JSON.parse(annotated.dataset.sinAnnotation);check(state.objects[0].imageClip.x===20&&state.objects[0].cropCornerRadii.topLeft===16,'Numeric crop and per-corner radius retained non-destructively');
  state.objects.push({id:id(),type:'arrow',x1:60,y1:160,x2:410,y2:80,stroke:'#d53139',strokeWidth:5,arrowSize:22,opacity:1,visible:true});
  const before=outputBounds(state);move(state.objects[1],100,0);check(outputBounds(state).width>before.width,'Annotations outside image expand output width');
  const template=captureTemplate(state.objects,'Image and arrow');const roundtrip=await parseTemplate(templateJson(template));check(roundtrip.objects.length===state.objects.length&&roundtrip.objects.some(o=>o.type==='arrow'),'PMT image and shape template exchange retains all objects');
  state.objects.push({id:id(),type:'rectangle',x:30,y:130,width:190,height:45,stroke:'#246bac',strokeWidth:3,fill:'none',opacity:1,visible:true});
  const rendered=await renderPng(state);annotated.src=rendered.data;annotated.dataset.sinAnnotation=JSON.stringify(state);annotated.dataset.sinStorage='inline';doc().body.dispatchEvent(new Event('input',{bubbles:true}));
  const exported=await portableHtml(window.editor.html(),'https://sin-document.local/');const parsed=parseHtml(exported);check([...parsed.images].every(i=>i.src.startsWith('data:image/png;base64,')),'Standalone HTML embeds every image as PNG');
  check(!exported.includes('data-sin-runtime')&&!exported.includes('data-sin-selected'),'Runtime selection and editor styles are excluded from saved HTML');
  const stored=window.editor.html();await window.editor.load('<html><head><title>Keep title</title></head><body><script>parent.document.body.dataset.unsafe="yes"</script><p onclick="parent.document.body.dataset.unsafe=\'yes\'">Scripts stay inert</p></body></html>');await delay(50);doc().querySelector('p').click();check(!document.body.dataset.unsafe,'Document scripts and event handlers cannot run in editor');check(window.editor.html().includes('Keep title')&&window.editor.html().includes('<script>'),'Source round-trip preserves document head and script text');
  await window.editor.load(stored);await window.editor.ready();
  doc().body.click();
  const blank=document.querySelector('#insertImage').onclick();await waitFor('dialog.annotation');
  check(document.querySelectorAll('[data-layer]').length===0,'Insert an Image opens a blank drawing canvas');
  const imageBlob=await (await fetch(png)).blob();
  async function pasteCanvasImage(name){const transfer=new DataTransfer();transfer.items.add(new File([imageBlob],name,{type:'image/png'}));document.querySelector('dialog.annotation').dispatchEvent(new ClipboardEvent('paste',{clipboardData:transfer,bubbles:true,cancelable:true}));}
  await pasteCanvasImage('First.png');for(let i=0;i<100&&document.querySelectorAll('[data-layer]').length<1;i++)await delay(20);
  await pasteCanvasImage('Second.png');for(let i=0;i<100&&document.querySelectorAll('[data-layer]').length<2;i++)await delay(20);
  check(document.querySelectorAll('[data-layer]').length===2,'Pasting multiple images creates independent canvas layers');
  check(Number(document.querySelector('[data-geometry=x]').value)>360,'Second pasted image expands the virtual canvas');
  click('[data-action=apply]');await waitFor('dialog button[value=inline]');click('dialog button[value=inline]');await blank;
  check(doc().querySelectorAll('img[data-sin-annotation]').length===2,'Blank canvas with multiple images inserts into the HTML document');
  const latest=doc().querySelectorAll('img[data-sin-annotation]')[1];const originalWidth=latest.style.width;
  const reopen=window.editor.openAnnotation(latest);await waitFor('dialog.annotation');
  check(document.querySelectorAll('[data-layer]').length===2,'Reopening annotation restores every image layer');
  click('[data-action=apply]');await reopen;
  check(doc().querySelectorAll('img[data-sin-annotation]')[1].style.width===originalWidth,'Annotation apply preserves document display width like PMT');
  const beforeCancel=window.editor.html();
  for(const cancel of ['button','escape']){
    const canceled=window.editor.openAnnotation(doc().querySelector('img'));await waitFor('dialog.annotation');
    if(cancel==='button')click('[data-action=cancel]');else await request('test-key',{parameters:{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27}});
    await canceled;
    const layout=await request('test-annotation-layout');
    check(!layout.expanded&&layout.backgroundEnabled&&window.editor.html()===beforeCancel,'Annotation '+cancel+' restores the shell without changing the document');
  }
  const beforeCapture=window.editor.html();
  const canceledCapture=window.editor.openAnnotation(null,png);await waitFor('dialog.annotation');
  check(document.querySelectorAll('[data-layer]').length===1&&document.querySelector('#canvas image').getAttribute('width')==='360','Screen capture opens as one image layer at its original resolution');
  click('[data-action=cancel]');await canceledCapture;
  check(window.editor.html()===beforeCapture,'Canceling a captured image leaves document content unchanged');
  selectText('p');doc().getSelection().collapseToStart();doc().dispatchEvent(new Event('selectionchange'));
  const preceding=doc().createRange();preceding.selectNodeContents(doc().body);preceding.setEnd(doc().getSelection().anchorNode,doc().getSelection().anchorOffset);
  const textBeforeCapture=preceding.toString();
  const captured=window.editor.openAnnotation(null,png);await waitFor('dialog.annotation');click('[data-action=apply]');
  await waitFor('dialog button[value=inline]');click('dialog button[value=inline]');await captured;
  const capturedImage=[...doc().images].find(image=>image.dataset.sinAnnotation&&JSON.parse(image.dataset.sinAnnotation).objects[0].name==='Screen Capture');
  if(capturedImage)preceding.setEndBefore(capturedImage);
  check(capturedImage?.dataset.sinStorage==='inline'&&preceding.toString()===textBeforeCapture,'Applying a screen capture inserts at the saved caret with the chosen storage and editable layer');
  await window.editor.load(beforeCapture);
  const savedHtml=window.editor.html();
  await runRibbonTests(check);
  await window.editor.load('<p id="typing">Typing:</p>');
  const largeImage=doc().createElement('img');largeImage.src=png;
  largeImage.dataset.sinAnnotation=JSON.stringify({version:1,width:360,height:200,objects:Array.from({length:200},()=>({id:id(),type:'embedded-image',x:0,y:0,width:360,height:200,source:png}))});
  doc().body.append(largeImage);selectText('#typing');doc().getSelection().collapseToEnd();doc().body.focus();
  const root=doc().documentElement,originalClone=root.cloneNode;let snapshots=0;
  root.cloneNode=function(...args){snapshots++;return originalClone.apply(this,args);};
  const timings=[];
  try{
    for(const letter of 'responsive typing'){
      const start=performance.now();doc().execCommand('insertText',false,letter);timings.push(performance.now()-start);await delay(15);
    }
    check(snapshots===0,'Typing in a document with 2 MB of image metadata avoids full HTML snapshots on each key');
    const typed=parseHtml(window.editor.html()).querySelector('#typing').textContent.replace(/\u00a0/g,' ');
    check(typed==='Typing:responsive typing','Immediate save reads the latest text before the deferred update');
    const beforeIdle=snapshots;await delay(250);
    check(snapshots===beforeIdle+1,'A typing burst synchronizes one HTML snapshot after the user pauses');
    results.push('Typing input handling: maximum '+Math.max(...timings).toFixed(1)+' ms per character with '+(largeImage.dataset.sinAnnotation.length/1048576).toFixed(1)+' MB of image metadata');
  }finally{delete root.cloneNode;await window.editor.load(savedHtml);doc().body.dispatchEvent(new Event('input',{bubbles:true}));}
  return results;
}
