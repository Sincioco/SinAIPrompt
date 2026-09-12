import {ask,request} from './bridge.js';
import {sceneSvg,renderPng} from './annotation-model.js';
import {captureTemplate} from './templates.js';

export async function copyObjects(objects) {
  if(!objects.length)throw Error('Select objects to copy first.');
  const template=captureTemplate(objects,'Copied Objects');
  const answer=await ask('Copy Selected Objects','<p>Choose a clipboard format. Pasting into this annotation editor keeps the objects editable.</p>',[{value:'svg',label:'SVG'},{value:'png',label:'PNG'}]);
  if(!['svg','png'].includes(answer.choice))return;
  const state={objects:template.objects,background:'none'};
  const content=answer.choice==='svg'?sceneSvg(state):(await renderPng(state)).data;
  await request('annotation-copy',{format:answer.choice,content,objects:JSON.stringify(template)});
}
