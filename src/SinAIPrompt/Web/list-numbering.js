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
const depth=el=>{let n=0;for(let p=el.parentElement;p;p=p.parentElement)if(p.matches('ol,ul'))n++;return n;};
function previousItem(doc,item){
  return [...doc.querySelectorAll('ol > li')].filter(el=>el!==item&&(el.compareDocumentPosition(item)&4)&&depth(el)===depth(item)).at(-1);
}
function prepareParagraph(doc){
  const node=doc.getSelection()?.anchorNode,element=node?.nodeType===1?node:node?.parentElement;
  // Chromium can nest the new list inside a retained paragraph element. A div
  // can contain a list in both the live DOM and serialized/reopened HTML.
  if(element?.closest('p,h1,h2,h3,h4,h5,h6'))doc.execCommand('formatBlock',false,'div');
}
function refreshContinuations(doc){
  const lists=new Map(),previous=new Map();
  for(const item of doc.querySelectorAll('ol > li')){
    const list=item.parentElement,level=depth(item),step=list.reversed?-1:1;
    if(!lists.has(list))lists.set(list,{next:list.hasAttribute('start')?list.start:list.reversed?list.children.length:1,ids:new Set()});
    const state=lists.get(list),id=item.dataset.sinListId;
    if(id&&state.ids.has(id)){item.removeAttribute('value');item.removeAttribute('data-sin-list-mode');item.removeAttribute('data-sin-list-id');}
    if(id)state.ids.add(id);
    if(item.dataset.sinListMode==='continue')item.value=previous.get(level)??1;
    const number=item.hasAttribute('value')?item.value:state.next;
    state.next=number+step;previous.set(level,state.next);
  }
}
export function attachListNumbering(doc){
  let beforeEnter=null;
  doc.addEventListener('beforeinput',event=>{
    beforeEnter=null;if(event.inputType!=='insertParagraph')return;
    const anchor=currentItem(doc);
    if(anchor?.hasAttribute('value'))anchor.dataset.sinListId||=crypto.randomUUID();
    beforeEnter={items:new Set(doc.querySelectorAll('li')),anchor};
  });
  doc.addEventListener('input',event=>{
    // Chromium copies li[value] and custom attributes when Enter splits an item.
    // Only the first resulting paragraph owns a restart/continuation instruction.
    if(beforeEnter){
      const anchor=beforeEnter.anchor,previous=anchor?.previousElementSibling;
      const first=previous&&!beforeEnter.items.has(previous)?previous:anchor;
      if(first&&first!==anchor)for(const name of ['value','data-sin-list-mode','data-sin-list-id']){
        if(anchor.hasAttribute(name))first.setAttribute(name,anchor.getAttribute(name));anchor.removeAttribute(name);
      }
      for(const item of doc.querySelectorAll('li'))if(item!==first&&!beforeEnter.items.has(item)){
        item.removeAttribute('value');item.removeAttribute('data-sin-list-mode');item.removeAttribute('data-sin-list-id');
      }
      beforeEnter=null;
    }
    if(event.inputType!=='insertText'&&event.inputType!=='insertCompositionText')refreshContinuations(doc);
  });
  refreshContinuations(doc);
}
export function toggleNumbering(doc){
  const wasNumbered=currentItem(doc)?.parentElement.tagName==='OL';
  const existing=new Set(doc.querySelectorAll('ol > li'));
  if(!currentItem(doc))prepareParagraph(doc);
  doc.execCommand('insertOrderedList');
  const item=[...doc.querySelectorAll('ol > li')].find(el=>!existing.has(el))||currentItem(doc);
  if(!wasNumbered&&item?.parentElement.tagName==='OL'){
    const previous=previousItem(doc,item);
    if(previous)setListNumber(doc,'continue',1,item);
  }
  refreshContinuations(doc);
}
export function setListNumber(doc,mode,value=1,target=currentItem(doc)){
  const saved=doc.getSelection()?.rangeCount?doc.getSelection().getRangeAt(0).cloneRange():null;
  doc.body.focus();
  if(saved){doc.getSelection().removeAllRanges();doc.getSelection().addRange(saved);}
  let item=target?.isConnected?target:currentItem(doc);
  if(!item||item.parentElement.tagName!=='OL'){
    const unordered=item?.parentElement.tagName==='UL'?item.parentElement:null;
    const attributes=unordered?[...unordered.attributes].map(a=>[a.name,a.value]):[];
    const marker=crypto.randomUUID();
    if(unordered){
      item.dataset.sinNumbering=marker;
      const range=doc.createRange();range.setStart(unordered.firstElementChild,0);range.setEnd(unordered.lastElementChild,unordered.lastElementChild.childNodes.length);
      doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);
    }else prepareParagraph(doc);
    doc.execCommand('insertOrderedList');item=currentItem(doc);
    if(unordered){item=doc.querySelector(`[data-sin-numbering="${marker}"]`)||item;item?.removeAttribute('data-sin-numbering');}
    if(item?.parentElement.tagName==='OL')for(const [name,value] of attributes)item.parentElement.setAttribute(name,value);
  }
  if(!item||item.parentElement.tagName!=='OL')throw Error('Place the cursor in a numbered paragraph.');
  item.parentElement.style.listStyleType='decimal';
  const previous=previousItem(doc,item);
  const number=mode==='continue'?(previous?numberOf(previous)+(previous.parentElement.reversed?-1:1):1):mode==='restart'?1:Number(value);
  if(!Number.isInteger(number)||number<1||number>2147483647)throw Error('Enter a whole number from 1 to 2147483647.');
  const list=item.parentElement,copy=list.cloneNode(true),index=[...list.querySelectorAll('li')].indexOf(item);
  const updated=copy.querySelectorAll('li')[index];
  // Repair the rest of this section together, including old duplicated values.
  for(let sibling=updated;sibling;sibling=sibling.nextElementSibling){sibling.removeAttribute('value');sibling.removeAttribute('data-sin-list-mode');sibling.removeAttribute('data-sin-list-id');sibling.style.removeProperty('list-style-type');}
  copy.style.listStyleType='decimal';
  updated.value=number;updated.dataset.sinListMode=mode;updated.dataset.sinListId=crypto.randomUUID();
  const marker=crypto.randomUUID();updated.dataset.sinNumbering=marker;
  // Chromium can merge the outer list on insertion. An explicit item value
  // survives that merge and continues numbering for its following siblings.
  // End inside the final item so the following heading cannot join the list.
  const range=doc.createRange();range.setStart(list.firstElementChild,0);range.setEnd(list.lastElementChild,list.lastElementChild.childNodes.length);const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);
  doc.execCommand('insertHTML',false,copy.outerHTML);
  const inserted=doc.querySelector(`[data-sin-numbering="${marker}"]`);inserted?.removeAttribute('data-sin-numbering');
  if(inserted){range.selectNodeContents(inserted);range.collapse(true);selection.removeAllRanges();selection.addRange(range);}
  refreshContinuations(doc);
}
export async function numberingOptions(doc,changed){
  const item=currentItem(doc);
  const range=doc.getSelection()?.rangeCount?doc.getSelection().getRangeAt(0).cloneRange():null;
  const answer=await ask('List Numbering','<p>Apply to the current numbered paragraph and the items that follow it.</p><label>Start at <input name="number" type="number" value="1" min="1" max="2147483647" required></label>',[
    {value:'restart',label:'Restart At 1'},{value:'continue',label:'Continue Previous List'},{value:'set',label:'Set Number'}]);
  if(answer.choice==='cancel'||!range?.startContainer.isConnected)return;
  try{doc.body.focus();const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);setListNumber(doc,answer.choice,answer.values.number,item);changed();}
  catch(error){report(error);}
}
