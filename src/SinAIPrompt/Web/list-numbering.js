import {ask,report} from './bridge.js';

function currentItem(doc){
  const node=doc.getSelection()?.anchorNode;
  return (node?.nodeType===1?node:node?.parentElement)?.closest('li');
}
function numberOf(item){
  const list=item.parentElement,items=[...list.children].filter(el=>el.tagName==='LI');
  let number=list.hasAttribute('start')?Number(list.start):list.reversed?items.length:1;
  for(const sibling of items){
    if(sibling.hasAttribute('value'))number=sibling.value;
    if(sibling===item)return number;
    number+=list.reversed?-1:1;
  }
}
export function setListNumber(doc,mode,value=1){
  doc.body.focus();
  let item=currentItem(doc);
  if(!item||item.parentElement.tagName!=='OL'){doc.execCommand('insertOrderedList');item=currentItem(doc);}
  if(!item||item.parentElement.tagName!=='OL')throw Error('Place the cursor in a numbered paragraph.');
  const depth=el=>{let n=0;for(let p=el.parentElement;p;p=p.parentElement)if(p.matches('ol,ul'))n++;return n;};
  const previous=[...doc.querySelectorAll('ol > li')].filter(el=>el!==item&&(el.compareDocumentPosition(item)&4)&&depth(el)===depth(item)).at(-1);
  const number=mode==='continue'?(previous?numberOf(previous)+(previous.parentElement.reversed?-1:1):1):mode==='restart'?1:Number(value);
  if(!Number.isInteger(number)||number<1||number>2147483647)throw Error('Enter a whole number from 1 to 2147483647.');
  const list=item.parentElement,copy=list.cloneNode(true),index=[...list.querySelectorAll('li')].indexOf(item);
  const updated=copy.querySelectorAll('li')[index];
  updated.value=number;
  const marker=crypto.randomUUID();updated.dataset.sinNumbering=marker;
  // Chromium can merge the outer list on insertion. An explicit item value
  // survives that merge and continues numbering for its following siblings.
  const range=doc.createRange();range.selectNode(list);const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);
  doc.execCommand('insertHTML',false,copy.outerHTML);
  const inserted=doc.querySelector(`[data-sin-numbering="${marker}"]`);inserted?.removeAttribute('data-sin-numbering');
  if(inserted){range.selectNodeContents(inserted);range.collapse(true);selection.removeAllRanges();selection.addRange(range);}
}
export async function numberingOptions(doc,changed){
  const range=doc.getSelection()?.rangeCount?doc.getSelection().getRangeAt(0).cloneRange():null;
  const answer=await ask('List Numbering','<p>Apply to the current numbered paragraph and the items that follow it.</p><label>Start at <input name="number" type="number" value="1" min="1" max="2147483647" required></label>',[
    {value:'restart',label:'Restart At 1'},{value:'continue',label:'Continue Previous List'},{value:'set',label:'Set Number'}]);
  if(answer.choice==='cancel'||!range?.startContainer.isConnected)return;
  try{doc.body.focus();const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);setListNumber(doc,answer.choice,answer.values.number);changed();}
  catch(error){report(error);}
}
