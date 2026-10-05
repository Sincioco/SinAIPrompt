import {blobData,request} from './bridge.js';
import {loadImage} from './document.js';
import {clamp,clone,imageClip,sceneSvg} from './annotation-model.js';

// Divider fractions are image-layer metadata; the dialog owns their history.
// All previews are SVG chrome. Only an explicit Copy operation renders pixels.
export const slicingControls=`<style>
.annotation [data-slice-cell]{cursor:pointer}
.annotation [data-slice-divider]{cursor:ew-resize}
.annotation [data-slice-overlay][data-slice-locked=true] [data-slice-divider]{cursor:default}
.annotation #sliceCell{max-width:130px}.annotation #sliceProgress{width:100%}
</style><div id="imageSlices" hidden><hr><h3>Image Cells</h3><button data-action="slice" class="wide">Divide Into Cells</button><div data-slice-options hidden><label class="field">Dividers <input id="sliceDividerCount" type="number" min="0" max="32" value="3" step="1"></label><label class="field">Selected Cell <select id="sliceCell"></select></label><button data-action="copySlice" class="wide" title="Copy Selected Cell As PNG (Ctrl+C)">Copy Selected Cell (PNG)</button><progress id="sliceProgress" aria-label="Copying selected cell" hidden></progress><p class="hint">Drag the red handles to set unequal widths. Click a cell, then copy it. Dividers stay editable and never appear in copied images.</p></div></div>`;

const pixelWidth=object=>Math.max(1,Math.round(imageClip(object).width));
const maximum=object=>Math.min(32,pixelWidth(object)-1);
export function sliceDividers(object) {
  if(Array.isArray(object.sliceDividers)){
    const values=object.sliceDividers.filter(value=>Number.isFinite(value)&&value>0&&value<1).sort((a,b)=>a-b).slice(0,maximum(object)),result=[],gap=1/pixelWidth(object);
    // A later crop/resize can leave too many cells or subpixel gaps. Project a
    // valid current layout without changing stored metadata merely by viewing it.
    values.forEach((value,index)=>result.push(clamp(value,(result.at(-1)??0)+gap,1-gap*(values.length-index))));
    return result;
  }
  const count=Math.min(3,maximum(object));return Array.from({length:count},(_,index)=>(index+1)/(count+1));
}
export function setSliceCount(object,value) {
  if(!object||object.type!=='embedded-image'||object.locked)return;
  const count=clamp(Math.round(Number(value)||0),0,maximum(object));
  object.sliceDividers=Array.from({length:count},(_,index)=>(index+1)/(count+1));
}
export function moveSliceDivider(object,index,x) {
  if(!object||object.locked)return;
  const dividers=sliceDividers(object),clip=imageClip(object),gap=1/pixelWidth(object);
  if(index<0||index>=dividers.length)return;
  dividers[index]=clamp((x-clip.x)/clip.width,(dividers[index-1]??0)+gap,(dividers[index+1]??1)-gap);
  object.sliceDividers=dividers;
}
export function sliceBounds(object,index) {
  const clip=imageClip(object),edges=[0,...sliceDividers(object),1],width=pixelWidth(object);
  index=clamp(index,0,edges.length-2);
  const left=Math.round(edges[index]*width),right=Math.round(edges[index+1]*width);
  return {x:clip.x+left,y:clip.y,width:Math.max(1,right-left),height:Math.max(1,Math.round(clip.height))};
}
export function sliceOverlay(object,cell,zoom) {
  const clip=imageClip(object),dividers=sliceDividers(object),size=12/zoom;
  let content='';
  for(let index=0;index<=dividers.length;index++){
    const b=sliceBounds(object,index),selected=index===clamp(cell,0,dividers.length);
    content+=`<rect data-slice-cell="${index}" x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}" fill="${selected?'#0874c91c':'transparent'}" stroke="${selected?'#0874c9':'none'}" stroke-width="${1/zoom}"/><text x="${b.x+8/zoom}" y="${b.y+20/zoom}" fill="#ffffff" stroke="#273746" stroke-width="${3/zoom}" paint-order="stroke" font-size="${14/zoom}" font-family="Segoe UI" pointer-events="none">${index+1}</text>`;
  }
  dividers.forEach((fraction,index)=>{
    const x=clip.x+fraction*clip.width;
    content+=`<g data-slice-divider="${index}"><line x1="${x}" x2="${x}" y1="${clip.y}" y2="${clip.y+clip.height}" stroke="transparent" stroke-width="${14/zoom}"/><line x1="${x}" x2="${x}" y1="${clip.y}" y2="${clip.y+clip.height}" stroke="#d52f32" stroke-width="${2/zoom}"/>${object.locked?'':`<rect x="${x-size/2}" y="${clip.y-size/2}" width="${size}" height="${size}" rx="${2/zoom}" fill="#ffffff" stroke="#d52f32" stroke-width="${2/zoom}"/><rect x="${x-size/2}" y="${clip.y+clip.height-size/2}" width="${size}" height="${size}" rx="${2/zoom}" fill="#ffffff" stroke="#d52f32" stroke-width="${2/zoom}"/>`}</g>`;
  });
  return `<g data-slice-overlay data-slice-locked="${!!object.locked}">${content}</g>`;
}
export function updateSliceOverlay(svg,object,cell,zoom) {
  const overlay=svg.querySelector('[data-slice-overlay]');
  if(overlay)overlay.outerHTML=sliceOverlay(object,cell,zoom);
}
export function syncSliceControls(dialog,object,enabled,cell,busy=false) {
  const panel=dialog.querySelector('#imageSlices'),image=object?.type==='embedded-image';panel.hidden=!image;
  if(!image)return;
  panel.querySelector('[data-action=slice]').classList.toggle('active',enabled);
  panel.querySelector('[data-action=slice]').textContent=enabled?'Hide Cell Dividers':'Divide Into Cells';
  panel.querySelector('[data-slice-options]').hidden=!enabled;
  const dividers=sliceDividers(object),count=panel.querySelector('#sliceDividerCount');count.value=dividers.length;count.max=maximum(object);count.disabled=!!object.locked;
  const select=panel.querySelector('#sliceCell');select.innerHTML=Array.from({length:dividers.length+1},(_,index)=>`<option value="${index}">Cell ${index+1} (${sliceBounds(object,index).width} px)</option>`).join('');select.value=clamp(cell,0,dividers.length);
  panel.querySelector('[data-action=copySlice]').disabled=busy||object.visible===false;
  panel.querySelector('#sliceProgress').hidden=!busy;
}
export async function copySlice(object,index) {
  const snapshot=clone(object),bounds=sliceBounds(snapshot,index);
  if(bounds.width*bounds.height>100000000)throw Error('This cell exceeds 100 megapixels. Reduce its dimensions before copying.');
  const url=URL.createObjectURL(new Blob([sceneSvg({objects:[snapshot],background:'none'},bounds)],{type:'image/svg+xml'}));
  try {
    const image=await loadImage(url),canvas=document.createElement('canvas');canvas.width=bounds.width;canvas.height=bounds.height;
    canvas.getContext('2d').drawImage(image,0,0);
    const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));
    if(!blob)throw Error('The selected cell could not be rendered.');
    await request('copy-image',{data:await blobData(blob)});
  } finally {URL.revokeObjectURL(url);}
}
