import {ask,request} from './bridge.js';
import {sceneSvg,renderPng} from './annotation-model.js';
import {captureTemplate} from './templates.js';

export async function copyObjects(objects,format) {
  if(!objects.length)throw Error('Select objects to copy first.');
  const template=captureTemplate(objects,'Copied Objects');
  if(!format){const answer=await ask('Copy Selected Objects','<p>Choose a clipboard format. Pasting into this annotation editor keeps the objects editable.</p>',[{value:'svg',label:'SVG'},{value:'png',label:'PNG'}]);format=answer.choice;}
  if(!['svg','png'].includes(format))return;
  const state={objects:template.objects,background:'none'};
  const content=format==='svg'?sceneSvg(state):(await renderPng(state)).data;
  await request('annotation-copy',{format,content,objects:JSON.stringify(template)});
}
