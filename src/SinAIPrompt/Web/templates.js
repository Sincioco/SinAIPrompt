import {clone,id,bounds,union,move} from './annotation-model.js';
import {toPng} from './document.js';
export const templateFormat='pmt-image-annotation-template';
const supported=['arrow','line','rectangle','circle','embedded-image','textbox'];
export function captureTemplate(objects,name) {
  const copies=clone(objects), b=union(copies.map(o=>bounds(o)));
  copies.forEach(o=>{move(o,-b.x,-b.y);o.groupId='';o.locked=false;o.isOriginalImage=false;});
  return {id:id(),name,width:b.width,height:b.height,createdAt:new Date().toISOString(),updatedAt:new Date().toISOString(),grouped:copies.length>1,groupName:name,groupVisible:true,objects:copies};
}
export async function parseTemplate(text) {
  let input;try{input=JSON.parse(text);}catch{throw Error('Choose a valid PMT template JSON file.');}
  if(input.format===templateFormat && input.version!==1) throw Error('Unsupported PMT template version.');
  const template=input.format===templateFormat?input.template:input;
  if(!template?.objects?.length) throw Error('This file does not contain object templates.');
  const invalid=template.objects.filter(o=>!supported.includes(o.type));
  if(invalid.length) throw Error('This template contains unsupported objects: '+[...new Set(invalid.map(o=>o.type))].join(', ')+'. Use a template containing images, shapes, lines, arrows, or text.');
  for(const o of template.objects) {
    for(const key of (['arrow','line'].includes(o.type)?['x1','y1','x2','y2']:['x','y','width','height'])) if(!Number.isFinite(o[key])) throw Error('Invalid object geometry in template.');
    if(o.type==='embedded-image') o.source=(await toPng(o.source)).data;
  }
  return {...template,id:id(),name:String(template.name||'Imported template').slice(0,120)};
}
export function templateJson(template){return JSON.stringify({format:templateFormat,version:1,template},null,2);}
export function instantiate(template,center) {
  const objects=clone(template.objects),groupId=id();
  objects.forEach(o=>{o.id=id();o.groupId=groupId;move(o,center.x-template.width/2,center.y-template.height/2);});return objects;
}
