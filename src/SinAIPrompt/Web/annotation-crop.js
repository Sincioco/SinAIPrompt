import {clamp,imageClip,renderPng} from './annotation-model.js';

export const cropControls=`<div id="imageCrop" hidden><hr><h3>Reversible Crop (px)</h3><button data-action="crop" class="wide">Drag Crop Handles</button><div class="pair">${['top','right','bottom','left'].map(k=>`<label>${k[0].toUpperCase()+k.slice(1)}<input data-crop-value="${k}" type="number" min="0" value="0"></label>`).join('')}</div><h3 style="margin-top:15px">Corner Radius (px)</h3><label class="field"><input id="syncCorners" type="checkbox" checked>Sync All Four Corners</label><div class="pair">${[['topLeft','Top Left'],['topRight','Top Right'],['bottomLeft','Bottom Left'],['bottomRight','Bottom Right']].map(([k,label])=>`<label>${label}<input data-radius="${k}" type="number" min="0" value="0"></label>`).join('')}</div><button data-action="resetCrop" class="wide">Reset Crop &amp; Corners</button><button data-action="applyCrop" class="wide">Permanently Apply Crop</button><p class="hint">Apply Crop discards trimmed pixels from this layer. Undo remains available until you apply to the document.</p></div>`;

export function resetCrop(object){
  delete object.imageClip;delete object.cropCornerRadii;
  object.cropCornerRadius=0;object.cropVisible=true;
}
export function setCropInsets(object,values){
  values.left=clamp(values.left,0,object.width-1);
  values.right=clamp(values.right,0,object.width-values.left-1);
  values.top=clamp(values.top,0,object.height-1);
  values.bottom=clamp(values.bottom,0,object.height-values.top-1);
  object.imageClip={x:object.x+values.left,y:object.y+values.top,width:object.width-values.left-values.right,height:object.height-values.top-values.bottom};
  object.cropVisible=true;
}
export function setCornerRadii(object,key,value,sync){
  const clip=imageClip(object),radius=clamp(value,0,Math.min(clip.width,clip.height)/2);
  const previous=object.cropCornerRadii||{};
  object.cropCornerRadii=Object.fromEntries(['topLeft','topRight','bottomLeft','bottomRight'].map(k=>[k,sync||k===key?radius:previous[k]??object.cropCornerRadius??0]));
}
export async function applyCrop(object){
  const clip=imageClip(object);
  const rendered=await renderPng({objects:[{...object,opacity:1,visible:true}],background:'none'});
  return {...object,...clip,source:rendered.data,imageClip:undefined,cropCornerRadii:undefined,cropCornerRadius:0,cropVisible:true};
}
