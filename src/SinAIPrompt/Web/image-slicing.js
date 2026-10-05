import {blobData,request,native} from './bridge.js';
import {loadImage} from './document.js';
import {clamp,imageClip,sceneSvg} from './annotation-model.js';

// Divider fractions are image-layer metadata; the dialog owns their history.
// All previews are SVG chrome. Only explicit copy/save/create operations render pixels.
export const slicingControls=`<style>
.annotation [data-slice-cell]{cursor:pointer}
.annotation [data-slice-divider]{cursor:ew-resize}
.annotation [data-slice-divider][data-slice-axis=y]{cursor:ns-resize}
.annotation [data-slice-overlay][data-slice-locked=true] [data-slice-divider]{cursor:default}
.annotation #imageSlices .field:has(#sliceCell){display:block}.annotation #sliceCell{width:100%;margin-top:4px}.annotation #sliceProgress{width:100%}
</style><div id="imageSlices" hidden><hr><h3>Image Cells</h3><button data-action="slice" class="wide">Divide Into Cells</button><div data-slice-options hidden><label class="field">Vertical dividers <input id="sliceDividerCount" type="number" min="0" max="32" value="3" step="1"></label><label class="field">Horizontal dividers <input id="sliceRowDividerCount" type="number" min="0" max="32" value="1" step="1"></label><label class="field">Selected Cell <select id="sliceCell"></select></label><button data-action="createSliceImages" class="wide" title="Create movable copies of every cell and keep the original image">Create Separate Images</button><button data-action="copySlice" class="wide" title="Copy Selected Cell As PNG (Ctrl+C)">Copy Selected Cell (PNG)</button><button data-action="saveAllSlices" class="wide">Save All Slices…</button><p class="hint">Drag the red handles to set unequal widths and heights. Create movable cell images while keeping the original, copy one cell, or save all cells as numbered PNGs, left to right then top to bottom. Guides never appear in the cell images.</p></div><progress id="sliceProgress" aria-label="Processing image cells" hidden></progress><button data-action="cancelSliceExport" class="wide" hidden>Stop Saving</button><p id="sliceStatus" class="hint" role="status"></p></div>`;

const dividerKey=axis=>axis==='y'?'sliceRowDividers':'sliceDividers';
const pixelSize=(clip,axis)=>Math.max(1,Math.round(clip[axis==='y'?'height':'width']));
const maximum=size=>Math.min(32,size-1);
function axisDividers(object,axis,size) {
  const saved=object[dividerKey(axis)];
  if(Array.isArray(saved)){
    const values=saved.filter(value=>Number.isFinite(value)&&value>0&&value<1).sort((a,b)=>a-b).slice(0,maximum(size)),result=[],gap=1/size;
    // A later crop/resize can leave too many cells or subpixel gaps. Project a
    // valid current layout without changing stored metadata merely by viewing it.
    values.forEach((value,index)=>result.push(clamp(value,(result.at(-1)??0)+gap,1-gap*(values.length-index))));
    return result;
  }
  const count=Math.min(axis==='y'?1:3,maximum(size));return Array.from({length:count},(_,index)=>(index+1)/(count+1));
}
export function sliceDividers(object,axis='x') {return axisDividers(object,axis,pixelSize(imageClip(object),axis));}
export function setSliceCount(object,value,axis='x') {
  if(!object||object.type!=='embedded-image'||object.locked)return;
  const count=clamp(Math.round(Number(value)||0),0,maximum(pixelSize(imageClip(object),axis)));
  object[dividerKey(axis)]=Array.from({length:count},(_,index)=>(index+1)/(count+1));
}
export function moveSliceDivider(object,index,position,axis='x') {
  if(!object||object.locked)return;
  const clip=imageClip(object),size=pixelSize(clip,axis),dividers=axisDividers(object,axis,size),gap=1/size;
  if(index<0||index>=dividers.length)return;
  dividers[index]=clamp((position-clip[axis])/clip[axis==='y'?'height':'width'],(dividers[index-1]??0)+gap,(dividers[index+1]??1)-gap);
  object[dividerKey(axis)]=dividers;
}
function sliceGrid(object) {
  const clip=imageClip(object),width=pixelSize(clip,'x'),height=pixelSize(clip,'y');
  const x=axisDividers(object,'x',width),y=axisDividers(object,'y',height);
  return {clip,x,y,columns:x.length+1,rows:y.length+1,
    xEdges:[0,...x,1].map(fraction=>Math.round(fraction*width)),yEdges:[0,...y,1].map(fraction=>Math.round(fraction*height))};
}
const gridCellCount=grid=>grid.columns*grid.rows;
function cellBounds(grid,index) {
  index=clamp(Math.trunc(Number(index)||0),0,gridCellCount(grid)-1);
  const column=index%grid.columns,row=Math.floor(index/grid.columns),left=grid.xEdges[column],top=grid.yEdges[row];
  return {x:grid.clip.x+left,y:grid.clip.y+top,width:Math.max(1,grid.xEdges[column+1]-left),height:Math.max(1,grid.yEdges[row+1]-top)};
}
export function sliceCellCount(object) {return gridCellCount(sliceGrid(object));}
export function sliceBounds(object,index) {return cellBounds(sliceGrid(object),index);}
export function sliceOverlay(object,cell,zoom) {
  const grid=sliceGrid(object),clip=grid.clip,size=12/zoom,count=gridCellCount(grid);
  let content='';
  for(let index=0;index<count;index++){
    const b=cellBounds(grid,index),selected=index===clamp(cell,0,count-1);
    content+=`<rect data-slice-cell="${index}" x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}" fill="${selected?'#0874c91c':'transparent'}" stroke="${selected?'#0874c9':'none'}" stroke-width="${1/zoom}"/><text x="${b.x+8/zoom}" y="${b.y+20/zoom}" fill="#ffffff" stroke="#273746" stroke-width="${3/zoom}" paint-order="stroke" font-size="${14/zoom}" font-family="Segoe UI" pointer-events="none">${index+1}</text>`;
  }
  for(const axis of ['x','y'])grid[axis].forEach((fraction,index)=>{
    const vertical=axis==='x',position=clip[axis]+fraction*clip[vertical?'width':'height'];
    const x1=vertical?position:clip.x,x2=vertical?position:clip.x+clip.width,y1=vertical?clip.y:position,y2=vertical?clip.y+clip.height:position;
    const handles=object.locked?'':[[x1,y1],[x2,y2]].map(([x,y])=>`<rect x="${x-size/2}" y="${y-size/2}" width="${size}" height="${size}" rx="${2/zoom}" fill="#ffffff" stroke="#d52f32" stroke-width="${2/zoom}"/>`).join('');
    content+=`<g data-slice-divider="${index}" data-slice-axis="${axis}"><line x1="${x1}" x2="${x2}" y1="${y1}" y2="${y2}" stroke="transparent" stroke-width="${14/zoom}"/><line x1="${x1}" x2="${x2}" y1="${y1}" y2="${y2}" stroke="#d52f32" stroke-width="${2/zoom}"/>${handles}</g>`;
  });
  return `<g data-slice-overlay data-slice-locked="${!!object.locked}">${content}</g>`;
}
export function updateSliceOverlay(svg,object,cell,zoom) {
  const overlay=svg.querySelector('[data-slice-overlay]');
  if(overlay)overlay.outerHTML=sliceOverlay(object,cell,zoom);
}
export function syncSliceControls(dialog,object,enabled,cell,busy=false) {
  const panel=dialog.querySelector('#imageSlices'),image=object?.type==='embedded-image';panel.hidden=!image&&!busy;
  if(!image)return;
  panel.querySelector('[data-action=slice]').classList.toggle('active',enabled);
  panel.querySelector('[data-action=slice]').textContent=enabled?'Hide Cell Dividers':'Divide Into Cells';
  panel.querySelector('[data-slice-options]').hidden=!enabled;
  const grid=sliceGrid(object),count=gridCellCount(grid);
  for(const axis of ['x','y']){
    const input=panel.querySelector(axis==='x'?'#sliceDividerCount':'#sliceRowDividerCount');input.value=grid[axis].length;input.max=maximum(pixelSize(grid.clip,axis));input.disabled=!!object.locked;
  }
  const select=panel.querySelector('#sliceCell');select.innerHTML=Array.from({length:count},(_,index)=>{
    const bounds=cellBounds(grid,index);return `<option value="${index}">Cell ${index+1}: Row ${Math.floor(index/grid.columns)+1}, Col ${index%grid.columns+1} (${bounds.width} × ${bounds.height} px)</option>`;
  }).join('');select.value=clamp(cell,0,count-1);
  for(const action of ['copySlice','saveAllSlices','createSliceImages'])panel.querySelector(`[data-action=${action}]`).disabled=busy||object.visible===false;
}
// Callers provide one detached snapshot for the operation, including all cells.
async function renderCell(object,index) {
  const bounds=sliceBounds(object,index);
  if(bounds.width*bounds.height>100000000)throw Error('This cell exceeds 100 megapixels. Reduce its dimensions before exporting.');
  const url=URL.createObjectURL(new Blob([sceneSvg({objects:[object],background:'none'},bounds)],{type:'image/svg+xml'}));
  try {
    const image=await loadImage(url),canvas=document.createElement('canvas');canvas.width=bounds.width;canvas.height=bounds.height;
    canvas.getContext('2d').drawImage(image,0,0);
    const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));
    if(!blob)throw Error('The selected cell could not be rendered.');
    return await blobData(blob);
  } finally {URL.revokeObjectURL(url);}
}
export async function copySlice(object,index) {await request('copy-image',{data:await renderCell(object,index)});}
export async function createSliceImages(object,onProgress,signal) {
  const grid=sliceGrid(object),count=gridCellCount(grid),images=[];
  for(let index=0;index<count;index++){
    if(signal?.aborted)return null;
    onProgress(index,count);
    await new Promise(resolve=>setTimeout(resolve,0));
    if(signal?.aborted)return null;
    const source=await renderCell(object,index);
    if(signal?.aborted)return null;
    images.push({source,...cellBounds(grid,index),row:Math.floor(index/grid.columns),column:index%grid.columns});
    onProgress(index+1,count);
  }
  return images;
}
export async function saveAllSlices(object,onProgress,signal) {
  if(!native)throw Error('Saving image cells is available in the desktop application.');
  const count=sliceCellCount(object),token=await request('slice-export-begin',{name:object.name||'Image',count});
  if(!token)return null;
  let failure,result;
  try {
    for(let index=0;index<count&&!signal.aborted;index++){
      onProgress(index,count);
      await new Promise(resolve=>setTimeout(resolve,0));
      if(signal.aborted)break;
      const data=await renderCell(object,index);
      if(signal.aborted)break;
      await request('slice-export-write',{token,index,data});
      onProgress(index+1,count);
    }
  } catch(error) {failure=error;}
  finally {result=await request('slice-export-end',{token});}
  if(failure)throw Error(`${failure.message} ${result.saved} of ${result.total} cells saved in ${result.folder}.`);
  return result;
}
