import {annotate} from './annotation-ui.js';
import {request} from './bridge.js';
import {sliceBounds,sliceDividers,sliceCellCount,moveSliceDivider} from './image-slicing.js';

export async function runImageSlicingTests(check) {
  for(const cropped of [false,true])for(const width of [1,2,100]){
    const object={type:'embedded-image',x:10,y:20,width:cropped?400:width,height:8,sliceDividers:[.1,.1025,.75],sliceRowDividers:[]};
    if(cropped)object.imageClip={x:13,y:20,width,height:8};
    const valid=()=>{
      const fractions=sliceDividers(object),cells=Array.from({length:fractions.length+1},(_,index)=>sliceBounds(object,index));
      return fractions.every((fraction,index)=>Number.isFinite(fraction)&&fraction>0&&fraction<1&&(!index||fraction>fractions[index-1]))&&
        cells[0].x===(cropped?13:10)&&cells.every((cell,index)=>cell.width>0&&(!index||cell.x===cells[index-1].x+cells[index-1].width))&&cells.reduce((sum,cell)=>sum+cell.width,0)===width;
    };
    check(valid(),`Saved dividers remain contiguous without empty or overlapping cells after ${cropped?'cropping':'resizing'} to ${width} px`);
    moveSliceDivider(object,0,-1000);const leftValid=valid();moveSliceDivider(object,0,1000);
    check(leftValid&&valid(),`Divider dragging stays inside resized or cropped ${width} px image bounds`);
  }
  for(const cropped of [false,true])for(const height of [1,2,5]){
    const object={type:'embedded-image',x:10,y:20,width:7,height:cropped?400:height,sliceDividers:[],sliceRowDividers:[.1,.1025,.75]};
    if(cropped)object.imageClip={x:10,y:23,width:7,height};
    const valid=()=>{
      const cells=Array.from({length:sliceCellCount(object)},(_,index)=>sliceBounds(object,index));
      return cells[0].y===(cropped?23:20)&&cells.every((cell,index)=>cell.height>0&&cell.width===7&&(!index||cell.y===cells[index-1].y+cells[index-1].height))&&cells.reduce((sum,cell)=>sum+cell.height,0)===height;
    };
    check(valid(),`Horizontal dividers preserve contiguous rows after ${cropped?'cropping':'resizing'} to ${height} px tall`);
    moveSliceDivider(object,0,-1000,'y');const topValid=valid();moveSliceDivider(object,0,1000,'y');
    check(topValid&&valid(),`Horizontal divider dragging stays inside resized or cropped ${height} px image bounds`);
  }
  const oddHeight={type:'embedded-image',x:0,y:0,width:7,height:5,sliceDividers:[]};
  check(sliceDividers(oddHeight,'y')[0]===.5&&sliceBounds(oddHeight,0).height===3&&sliceBounds(oddHeight,1).height===2,
    'The default centered horizontal divider allocates every pixel of an odd-height image exactly once');
  const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  const $=selector=>document.querySelector('dialog.annotation '+selector);
  const $$=selector=>[...document.querySelectorAll('dialog.annotation '+selector)];
  const click=selector=>$(selector).click();
  async function until(predicate,name,timeout=3000){const deadline=Date.now()+timeout;while(Date.now()<deadline){if(await predicate())return;await delay(30);}throw Error('Timed out: '+name+'; '+($('#sliceStatus')?.textContent||''));}
  async function open(state){
    const result=annotate(state);await until(()=>$('[data-tool=select]'),'image slicing dialog');
    $('#canvasZoom').value='100';$('#canvasZoom').dispatchEvent(new Event('change',{bubbles:true}));await delay(30);
    return {result};
  }
  const point=(x,y)=>new DOMPoint(x,y).matrixTransform($('#canvas').getScreenCTM());
  const mouse=(type,p,modifiers=0)=>request('test-mouse',{parameters:{type,x:p.x,y:p.y,modifiers,button:type==='mouseMoved'?'none':'left',buttons:type==='mouseReleased'?0:1,clickCount:1}});
  async function press(node){const b=node.getBoundingClientRect(),p={x:b.x+b.width/2,y:b.y+b.height/2};await mouse('mousePressed',p);await mouse('mouseReleased',p);}
  async function drag(from,to,modifiers=0){await mouse('mousePressed',from,modifiers);await mouse('mouseMoved',to,modifiers);await mouse('mouseReleased',to,modifiers);}
  async function copyShortcut(){
    await request('test-key',{parameters:{type:'keyDown',key:'c',code:'KeyC',windowsVirtualKeyCode:67,modifiers:2}});
    await request('test-key',{parameters:{type:'keyUp',key:'c',code:'KeyC',windowsVirtualKeyCode:67}});
  }
  const change=(selector,value)=>{const input=$(selector);input.value=String(value);input.dispatchEvent(new Event('change',{bubbles:true}));};
  const dividerX=index=>Number($(`[data-slice-axis="x"][data-slice-divider="${index}"] line`).getAttribute('x1'));
  const dividerY=index=>Number($(`[data-slice-axis="y"][data-slice-divider="${index}"] line`).getAttribute('y1'));
  async function pixels(source){
    const image=new Image();image.src=source;await image.decode();
    const canvas=document.createElement('canvas');canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;
    const context=canvas.getContext('2d');context.drawImage(image,0,0);
    return {width:canvas.width,height:canvas.height,data:context.getImageData(0,0,canvas.width,canvas.height).data};
  }
  const colorMatches=(image,expected)=>image.data.every((value,index)=>value===expected(Math.floor(index/4)%image.width,Math.floor(index/4/image.width))[index%4]);
  async function copyCell(index,shortcut=false){
    await press($(`[data-slice-cell="${index}"]`));
    await request('editor-copy',{html:'<p>Clipboard fixture</p>',text:'Clipboard fixture'});
    if(shortcut)await copyShortcut();else click('[data-action=copySlice]');
    await until(async()=>(await request('editor-paste'))?.image?.startsWith('data:image/png;base64,'),'copied slice PNG');
    return pixels((await request('editor-paste')).image);
  }

  const palette=[[255,0,0,255],[0,255,0,255],[0,0,255,255],[255,255,0,255]];
  const canvas=document.createElement('canvas');canvas.width=320;canvas.height=80;
  const context=canvas.getContext('2d');
  palette.forEach((color,index)=>{context.fillStyle=`rgb(${color.slice(0,3).join(',')})`;context.fillRect(index*80,0,80,80);});
  const png=canvas.toDataURL('image/png');
  const initial={version:1,width:320,height:80,background:'none',objects:[{id:'slice-image',type:'embedded-image',name:'Four colors',source:png,x:0,y:0,width:320,height:80,visible:true,sliceRowDividers:[]}]};
  const original=JSON.stringify(initial),session=await open(initial);
  try {
    click('[data-action=slice]');
    check($$('[data-slice-divider]').length===3&&$$('[data-slice-cell]').length===4&&$('#sliceDividerCount').value==='3',
      'Image slicing starts with three dividers and four selectable cells');
    check([0,1,2].every(index=>Math.abs(dividerX(index)-(index+1)*80)<.01)&&$$('[data-layer]').length===1,
      'Default slice guides evenly divide the selected image without creating image layers');
    const blue=await copyCell(2);
    check(blue.width===80&&blue.height===80&&colorMatches(blue,()=>palette[2])&&document.querySelector('dialog.annotation').open,
      'Copying a slice produces its exact original PNG pixels and dimensions without guides or closing the editor');

    // Divider movement must update cheap guides, not repeatedly encode pixels.
    const dataUrl=HTMLCanvasElement.prototype.toDataURL,toBlob=HTMLCanvasElement.prototype.toBlob;let encodes=0;
    HTMLCanvasElement.prototype.toDataURL=function(...args){encodes++;return dataUrl.apply(this,args);};
    HTMLCanvasElement.prototype.toBlob=function(...args){encodes++;return toBlob.apply(this,args);};
    try {
      await mouse('mousePressed',point(80,40));
      for(let x=75;x>=40;x-=5)await mouse('mouseMoved',point(x,40));
      await mouse('mouseReleased',point(40,40));
    } finally {HTMLCanvasElement.prototype.toDataURL=dataUrl;HTMLCanvasElement.prototype.toBlob=toBlob;}
    check(Math.abs(dividerX(0)-40)<1&&encodes===0&&$('#canvas image').getAttribute('href')===png,
      'Dragging a divider creates unequal cell widths without encoding or replacing the source image');
    const unequal=await copyCell(1);
    check(unequal.width===120&&unequal.height===80&&colorMatches(unequal,x=>palette[x<40?0:1]),
      'Copying an unequal cell crops the correct source columns and excludes divider and selection graphics');
    await request('test-capture',{name:'image-slicing'});
    const shortcutCopy=await copyCell(3,true);
    check(shortcutCopy.width===80&&shortcutCopy.height===80&&colorMatches(shortcutCopy,()=>palette[3])&&!document.querySelector('dialog.form-dialog'),
      'Ctrl+C copies only the selected image cell as PNG without opening the object-format dialog');

    const beforeExport=await request('test-slice-export-files'),progressValues=new Set();let responsiveTicks=0;
    const progressObserver=new MutationObserver(()=>progressValues.add($('#sliceProgress').value));
    progressObserver.observe($('#sliceProgress'),{attributes:true,attributeFilter:['value']});
    const tick=setInterval(()=>responsiveTicks++,1);
    try {
      click('[data-action=saveAllSlices]');
      check(!$('#sliceProgress').hidden&&$('[data-action=saveAllSlices]').disabled&&$('[data-action=copySlice]').disabled,
        'Save All Slices shows progress and prevents overlapping image exports');
      await until(()=>$('#sliceStatus').textContent.startsWith('Saved 4 of 4 cells in ')&&$('#sliceProgress').hidden,'saving all image cells',15000);
    } finally {clearInterval(tick);progressObserver.disconnect();}
    let saved=await request('test-slice-export-files');
    const newCells=saved.filter(file=>!beforeExport.some(old=>old.folder===file.folder));
    const renderedCells=await Promise.all(newCells.map(file=>pixels(file.data)));
    check(newCells.length===4&&newCells.every((file,index)=>file.name===`Four colors - Cell 0${index+1}.png`)&&
      renderedCells.every((image,index)=>image.width===[40,120,80,80][index]&&image.height===80&&colorMatches(image,x=>palette[index===1?(x<40?0:1):index])),
      'Save All Slices creates correctly numbered PNGs with the exact unequal-width pixels and no guides');
    check(progressValues.has(0)&&progressValues.has(4)&&responsiveTicks>0&&document.querySelector('dialog.annotation').open,
      'Saving every slice advances visible progress while browser callbacks continue and keeps the image editor open');
    const previousFiles=JSON.stringify(saved);
    click('[data-action=saveAllSlices]');
    await until(()=>$('#sliceStatus').textContent.startsWith('Saved 4 of 4 cells in ')&&$('#sliceProgress').hidden,'saving all cells again',15000);
    saved=await request('test-slice-export-files');
    check(saved.length===beforeExport.length+8&&new Set(saved.map(file=>file.folder)).size===new Set(beforeExport.map(file=>file.folder)).size+2&&
      JSON.stringify(saved.filter(file=>newCells.some(old=>old.folder===file.folder)||beforeExport.some(old=>old.folder===file.folder)))===previousFiles,
      'Saving all slices again uses a new folder and leaves all previous PNG files unchanged');
    const cancelObserver=new MutationObserver(()=>{if($('#sliceProgress').value>=1)click('[data-action=cancelSliceExport]');});
    cancelObserver.observe($('#sliceProgress'),{attributes:true,attributeFilter:['value']});
    try {
      click('[data-action=saveAllSlices]');
      await until(()=>$('#sliceStatus').textContent.startsWith('Stopped: saved 1 of 4 cells in ')&&$('#sliceProgress').hidden,'stopping a slice export',15000);
    } finally {cancelObserver.disconnect();}
    check((await request('test-slice-export-files')).length===saved.length+1&&Math.abs(dividerX(0)-40)<1&&$('#canvas image').getAttribute('href')===png,
      'Stopping Save All Slices retains completed PNGs and preserves the source image and divider layout');
    change('#sliceDividerCount',2);
    check($$('[data-slice-divider]').length===2&&$$('[data-slice-cell]').length===3,
      'Changing the divider count rebuilds the requested number of image cells');
    click('[data-action=undo]');
    check($$('[data-slice-divider]').length===3&&Math.abs(dividerX(0)-40)<1,'Undo restores the previous count and unequal divider positions');
    click('[data-action=redo]');check($$('[data-slice-divider]').length===2,'Changing the divider count supports redo');
    click('[data-action=undo]');
    click('[data-action=lock]');const lockedX=dividerX(0);
    change('#sliceDividerCount',5);await drag(point(lockedX,40),point(65,40));
    check($('#sliceDividerCount').disabled&&$$('[data-slice-divider]').length===3&&dividerX(0)===lockedX,
      'Locked image layers reject divider-count changes and divider movement');
    const lockedCopy=await copyCell(2);
    check(lockedCopy.width===80&&colorMatches(lockedCopy,()=>palette[2]),'Locked image layers still allow selecting and copying cells');
    click('[data-action=slice]');$('#canvas').focus();await copyShortcut();
    await until(()=>document.querySelector('dialog.form-dialog'),'normal object copy dialog');
    check(document.querySelector('dialog.form-dialog h2').textContent==='Copy Selected Objects','Leaving slicing restores Ctrl+C object copying');
    document.querySelector('dialog.form-dialog button[value=cancel]').click();
    await until(()=>!document.querySelector('dialog.form-dialog'),'closing object copy dialog');
    click('[data-action=cancel]');
    check(await session.result===null&&JSON.stringify(initial)===original,'Canceling slicing preserves the original image and annotation state');
  } finally {if(document.querySelector('dialog.annotation')){click('[data-action=cancel]');await session.result;}}

  const applied=await open(initial);
  try {
    click('[data-action=slice]');change('#sliceDividerCount',2);click('[data-action=apply]');
    const result=await applied.result,rendered=await pixels(result.data);
    check(result.state.objects[0].source===png&&result.state.objects[0].sliceDividers.length===2&&rendered.width===320&&rendered.height===80&&colorMatches(rendered,x=>palette[Math.floor(x/80)]),
      'Applying slicing preserves the full image and editable divider metadata while excluding all guides from the rendered PNG');
    const reopened=await open(JSON.parse(JSON.stringify(result.state)));
    try {click('[data-action=slice]');check($$('[data-slice-divider]').length===2,'Reopening the image restores its saved divider layout');}
    finally {click('[data-action=cancel]');await reopened.result;}
  } finally {if(document.querySelector('dialog.annotation')){click('[data-action=cancel]');await applied.result;}}

  const contactSheet=document.createElement('canvas');contactSheet.width=160;contactSheet.height=120;
  const contactContext=contactSheet.getContext('2d');
  palette.forEach((color,index)=>{contactContext.fillStyle=`rgb(${color.slice(0,3).join(',')})`;contactContext.fillRect(index%2*80,Math.floor(index/2)*60,80,60);});
  const contactPng=contactSheet.toDataURL('image/png');
  const contactInitial={version:1,width:160,height:120,background:'none',objects:[{id:'contact-sheet',type:'embedded-image',name:'Four quadrants',source:contactPng,x:0,y:0,width:160,height:120,visible:true}]};
  const contactOriginal=JSON.stringify(contactInitial),contact=await open(contactInitial);
  try {
    click('[data-action=slice]');
    check($('#sliceRowDividerCount').value==='1'&&$$('[data-slice-axis=y]').length===1&&dividerY(0)===60&&$$('[data-slice-cell]').length===8,
      'A fresh image starts with one vertically centered horizontal divider and retains the three default vertical dividers');
    change('#sliceDividerCount',1);
    check($$('[data-slice-cell]').length===4&&dividerX(0)===80&&dividerY(0)===60,
      'One vertical and one horizontal divider create four selectable contact-sheet cells');
    for(const index of [0,3]){
      const cell=await copyCell(index,index===3);
      check(cell.width===80&&cell.height===60&&colorMatches(cell,()=>palette[index]),
        `Contact-sheet cell ${index+1} copies the correct row-major quadrant as exact PNG pixels`);
    }
    const beforeContactExport=await request('test-slice-export-files');
    click('[data-action=saveAllSlices]');
    await until(()=>$('#sliceStatus').textContent.startsWith('Saved 4 of 4 cells in ')&&$('#sliceProgress').hidden,'saving the 2 by 2 contact sheet',15000);
    const contactFiles=(await request('test-slice-export-files')).filter(file=>!beforeContactExport.some(old=>old.folder===file.folder));
    const contactPixels=await Promise.all(contactFiles.map(file=>pixels(file.data)));
    check(contactFiles.length===4&&contactFiles.every((file,index)=>file.name===`Four quadrants - Cell 0${index+1}.png`)&&
      contactPixels.every((image,index)=>image.width===80&&image.height===60&&colorMatches(image,()=>palette[index])),
      'Save All Slices exports all four contact-sheet quadrants in left-to-right, top-to-bottom order with exact pixels');
    $('#imageSlices').scrollIntoView({block:'start'});await delay(30);
    await request('test-capture',{name:'image-slicing-rows'});

    const dataUrl=HTMLCanvasElement.prototype.toDataURL,toBlob=HTMLCanvasElement.prototype.toBlob;let encodes=0;
    HTMLCanvasElement.prototype.toDataURL=function(...args){encodes++;return dataUrl.apply(this,args);};
    HTMLCanvasElement.prototype.toBlob=function(...args){encodes++;return toBlob.apply(this,args);};
    try {
      await mouse('mousePressed',point(40,60));
      for(let y=55;y>=30;y-=5)await mouse('mouseMoved',point(40,y));
      await mouse('mouseReleased',point(40,30));
    } finally {HTMLCanvasElement.prototype.toDataURL=dataUrl;HTMLCanvasElement.prototype.toBlob=toBlob;}
    check(Math.abs(dividerY(0)-30)<1&&encodes===0&&$('#canvas image').getAttribute('href')===contactPng,
      'Dragging a horizontal divider creates unequal row heights without encoding or replacing the source image');
    const lowerLeft=await copyCell(2);
    check(lowerLeft.width===80&&lowerLeft.height===90&&colorMatches(lowerLeft,(x,y)=>palette[y<30?0:2]),
      'An unequal-height lower cell crops the correct source rows without guide graphics');
    click('[data-action=undo]');check(dividerY(0)===60,'Undo restores the centered horizontal divider after a drag');
    click('[data-action=redo]');check(dividerY(0)===30,'Redo restores the unequal horizontal divider position');
    change('#sliceRowDividerCount',2);
    check($$('[data-slice-axis=y]').length===2&&$$('[data-slice-cell]').length===6,
      'Changing the horizontal divider count creates the requested number of rows');
    click('[data-action=undo]');check($$('[data-slice-axis=y]').length===1&&dividerY(0)===30,'Undo restores the previous horizontal count and unequal row heights');
    click('[data-action=redo]');check($$('[data-slice-axis=y]').length===2,'The horizontal divider count supports redo');
    click('[data-action=undo]');click('[data-action=lock]');
    change('#sliceRowDividerCount',3);await drag(point(40,30),point(40,45));
    check($('#sliceRowDividerCount').disabled&&$$('[data-slice-axis=y]').length===1&&dividerY(0)===30,
      'Locked images reject horizontal divider-count changes and horizontal guide movement');
    const lockedLowerRight=await copyCell(3);
    check(lockedLowerRight.height===90&&colorMatches(lockedLowerRight,(x,y)=>palette[y<30?1:3]),
      'A locked contact sheet still permits copying its selected lower-row cell');
    click('[data-action=unlock]');change('#sliceRowDividerCount',0);
    const fullColumn=await copyCell(0);
    check($$('[data-slice-axis=y]').length===0&&$$('[data-slice-cell]').length===2&&fullColumn.height===120&&colorMatches(fullColumn,(x,y)=>palette[y<60?0:2]),
      'Zero horizontal dividers restores one full-height row while preserving vertical slicing');
    click('[data-action=undo]');click('[data-action=apply]');
    const result=await contact.result,rendered=await pixels(result.data),image=result.state.objects[0];
    check(image.source===contactPng&&image.sliceDividers[0]===.5&&image.sliceRowDividers[0]===.25&&rendered.width===160&&rendered.height===120&&
      colorMatches(rendered,(x,y)=>palette[(y<60?0:2)+(x<80?0:1)])&&JSON.stringify(contactInitial)===contactOriginal,
      'Applying row slicing preserves original full-image pixels and both divider axes as editable metadata');
    const reopened=await open(JSON.parse(JSON.stringify(result.state)));
    try {
      click('[data-action=slice]');
      check($('#sliceRowDividerCount').value==='1'&&dividerX(0)===80&&dividerY(0)===30&&$$('[data-slice-cell]').length===4,
        'Reopening the contact sheet restores its unequal saved horizontal and vertical divider layout');
    } finally {click('[data-action=cancel]');await reopened.result;}
  } finally {if(document.querySelector('dialog.annotation')){click('[data-action=cancel]');await contact.result;}}

  const separateInitial=structuredClone(contactInitial);
  Object.assign(separateInitial.objects[0],{sliceDividers:[.5],sliceRowDividers:[.5]});
  const separateOriginal=JSON.stringify(separateInitial.objects[0]),separate=await open(separateInitial);
  const canvasImages=()=>$$('#canvas [data-object]').map(node=>{
    const image=node.querySelector('image');
    return {id:node.dataset.object,source:image.getAttribute('href'),x:Number(image.getAttribute('x')),y:Number(image.getAttribute('y')),width:Number(image.getAttribute('width')),height:Number(image.getAttribute('height'))};
  });
  try {
    click('[data-action=slice]');let partialLayers=false;
    const cancelCreation=new MutationObserver(()=>{
      if($('#sliceProgress').value>=1){partialLayers=$$('[data-layer]').length!==1;click('[data-action=cancelSliceExport]');}
    });
    cancelCreation.observe($('#sliceProgress'),{attributes:true,attributeFilter:['value']});
    try {
      click('[data-action=createSliceImages]');
      check(!$('#sliceProgress').hidden&&$('[data-action=createSliceImages]').disabled&&$('[data-action=apply]').disabled&&!$('[data-action=cancelSliceExport]').hidden,
        'Creating separate cell images shows progress, permits stopping, and prevents overlapping creation or premature Apply');
      await until(()=>$('#sliceProgress').hidden&&$('#sliceStatus').textContent.startsWith('Stopped'),'canceling separate image creation',15000);
    } finally {cancelCreation.disconnect();}
    check(!partialLayers&&$$('[data-layer]').length===1&&canvasImages()[0].source===contactPng&&$$('[data-slice-cell]').length===4,
      'Stopping separate-image creation adds no partial layers and preserves the original contact sheet and divider layout');

    const createProgress=new Set();let creationTicks=0;
    const progressObserver=new MutationObserver(()=>createProgress.add($('#sliceProgress').value));
    progressObserver.observe($('#sliceProgress'),{attributes:true,attributeFilter:['value']});
    const tick=setInterval(()=>creationTicks++,1);
    try {
      click('[data-action=createSliceImages]');
      await until(()=>$('#sliceProgress').hidden&&$('#sliceStatus').textContent.startsWith('Created 4 separate images'),'creating four separate images',15000);
    } finally {clearInterval(tick);progressObserver.disconnect();}
    const originalImage=canvasImages().find(image=>image.id==='contact-sheet'),created=canvasImages().filter(image=>image.id!=='contact-sheet');
    const createdPixels=await Promise.all(created.map(image=>pixels(image.source)));
    check(created.length===4&&$$('[data-layer]').length===5&&new Set(canvasImages().map(image=>image.id)).size===5&&created.every(image=>image.x>=160&&image.width===80&&image.height===60)&&
      createdPixels.every((image,index)=>image.width===80&&image.height===60&&colorMatches(image,()=>palette[index])),
      'Create Separate Images makes four independent, uniquely identified PNG layers with exact row-major quadrant pixels beside the original');
    check(originalImage.source===contactPng&&originalImage.x===0&&originalImage.y===0&&originalImage.width===160&&originalImage.height===120&&$$('[data-slice-cell]').length===0,
      'Creating movable images keeps the complete original contact sheet in place and leaves slicing mode');
    check(createProgress.has(0)&&createProgress.has(4)&&creationTicks>0&&$$('[data-layer].selected').length===1,
      'Separate-image creation advances progress while browser callbacks continue and selects one new image for independent movement');
    click('[data-action=undo]');
    check($$('[data-layer]').length===1&&canvasImages()[0].source===contactPng,'One Undo removes the entire created-image batch while retaining the original');
    click('[data-action=redo]');
    check(JSON.stringify(canvasImages().filter(image=>image.id!=='contact-sheet'))===JSON.stringify(created),
      'One Redo restores every separate image with its original identity, location, and PNG source');
    click(`[data-layer="${created[0].id}"]`);
    const first=created[0],from=point(first.x+first.width/2,first.y+first.height/2),to=point(first.x+first.width/2+11,first.y+first.height/2+13);
    await drag(from,to);
    const moved=canvasImages(),movedFirst=moved.find(image=>image.id===first.id);
    check(Math.abs(movedFirst.x-first.x-11)<.01&&Math.abs(movedFirst.y-first.y-13)<.01&&moved.filter(image=>image.id!==first.id).every(image=>{
      const before=[originalImage,...created].find(old=>old.id===image.id);return JSON.stringify(image)===JSON.stringify(before);
    }),'Dragging one created image moves that image alone without changing the source contact sheet or the other cells');
    await request('test-capture',{name:'image-slicing-separate-images'});
    click('[data-action=apply]');
    const result=await separate.result;
    check(result.state.objects.length===5&&JSON.stringify(result.state.objects.find(image=>image.id==='contact-sheet'))===separateOriginal&&
      result.state.objects.filter(image=>image.id!=='contact-sheet').every(image=>image.source===created.find(cell=>cell.id===image.id)?.source),
      'Applying separate images preserves the original image object exactly and stores each independent PNG layer in editable annotation state');
    const reopened=await open(JSON.parse(JSON.stringify(result.state)));
    try {
      check($$('[data-layer]').length===5&&JSON.stringify(canvasImages())===JSON.stringify(moved),
        'Reopening the annotation restores all independent image layers, their positions, and the untouched original');
    } finally {click('[data-action=cancel]');await reopened.result;}
  } finally {if(document.querySelector('dialog.annotation')){click('[data-action=cancel]');await separate.result;}}

  const drawing=await open({version:1,width:320,height:240,blankCanvas:true,background:'none',objects:[]});
  try {
    for(const tool of ['line','arrow','circle']){
      click(`[data-tool=${tool}]`);await mouse('mousePressed',point(50,50));await mouse('mouseMoved',point(170,95));
      check(!$('#canvas > rect[stroke-dasharray]'),'Drawing '+tool+' shows the shape without a rectangular selection outline');
      await mouse('mouseReleased',point(170,95));
      check(!$('#canvas > rect[stroke-dasharray]'),'The active '+tool+' drawing tool keeps the finished shape free of a rectangular selection outline');
      click('[data-tool=select]');
      check(!!$('#canvas > rect[stroke-dasharray]'),'Selecting a finished '+tool+' still shows its selection outline');
      click('[data-action=undo]');
    }
    for(const [from,to,axis] of [[{x:50,y:70},{x:210,y:105},'y'],[{x:240,y:210},{x:210,y:70},'x']]){
      click('[data-tool=line]');await drag(point(from.x,from.y),point(to.x,to.y),8);
      check($(`[data-endpoint=${axis}1]`).value===$(`[data-endpoint=${axis}2]`).value,
        'Shift constrains a drawn line to the nearest '+(axis==='y'?'horizontal':'vertical')+' axis');
      check($('[data-tool=line]').classList.contains('active'),'Shift-constrained line drawing keeps the Line tool active');
      click('[data-action=undo]');
    }
    click('[data-tool=line]');await drag(point(50,70),point(210,105));
    check($('[data-endpoint=x1]').value!==$('[data-endpoint=x2]').value&&$('[data-endpoint=y1]').value!==$('[data-endpoint=y2]').value,
      'Drawing a line without Shift retains its diagonal direction');
  } finally {click('[data-action=cancel]');await drawing.result;}
}
