import {currentBlock,applyParagraphStyle} from './word-styles.js';
import {styleById} from './document-styles.js';

export const inlineProperties=['fontFamily','fontSize','fontWeight','fontStyle','textDecoration','color','backgroundColor','letterSpacing'];
export function selectionElement(doc) {
  const selection=doc?.getSelection();let node=selection?.anchorNode;
  if(node?.nodeType===1){
    node=node.childNodes[selection.anchorOffset]||node.lastChild||node;
    while(node.firstChild)node=node.firstChild;
  }
  return node?.nodeType===1?node:node?.parentElement;
}
export function captureFormat(doc) {
  const element=selectionElement(doc);if(!element)return null;
  const computed=doc.defaultView.getComputedStyle(element);
  const format=Object.fromEntries(inlineProperties.map(key=>[key,computed[key]]));
  format.textDecoration=[doc.queryCommandState('underline')?'underline':'',doc.queryCommandState('strikeThrough')?'line-through':''].filter(Boolean).join(' ')||'none';
  return format;
}
function clearInlineFormat(root) {
  for(const element of root.querySelectorAll('*')){
    if(element.closest('[contenteditable=false]'))continue;
    for(const property of inlineProperties)element.style[property]='';
    for(const attribute of ['face','size','color','data-sin-character-style'])element.removeAttribute(attribute);
    if(element.matches('b,strong,i,em,u,s,strike,font')){
      const span=root.ownerDocument.createElement('span');span.append(...element.childNodes);element.replaceWith(span);
    }
  }
}
export function applyInlineFormat(doc,format) {
  const selection=doc.getSelection();if(!selection?.rangeCount)return false;
  const range=selection.getRangeAt(0);if(range.collapsed)return false;
  const wrapper=doc.createElement('span');wrapper.append(range.cloneContents());clearInlineFormat(wrapper);
  Object.assign(wrapper.style,format);
  doc.execCommand('insertHTML',false,wrapper.outerHTML);return true;
}
export function selectWordAtCaret(doc) {
  const selection=doc.getSelection();if(!selection?.rangeCount)return false;
  const range=selection.getRangeAt(0);if(!range.collapsed)return true;
  if(range.startContainer.nodeType!==Node.TEXT_NODE)return false;
  const node=range.startContainer,offset=range.startOffset;
  for(const part of new Intl.Segmenter(undefined,{granularity:'word'}).segment(node.data)){
    if(part.isWordLike&&offset>=part.index&&offset<=part.index+part.segment.length){
      range.setStart(node,part.index);range.setEnd(node,part.index+part.segment.length);return true;
    }
  }
  return false;
}

// Each document owns its pending insertion font. Changing font at a caret must
// normalize the first native font marker after typing, including IME input.
export function createTextFormatting(doc) {
  doc.execCommand('defaultParagraphSeparator',false,'p');
  let pendingSize=null,existingFonts=new Set();
  function normalizeFonts(range=null) {
    if(pendingSize==null)return;
    for(const font of doc.querySelectorAll('font[size="7"]')){
      if(existingFonts.has(font)&&(!range||!range.intersectsNode(font)))continue;
      font.removeAttribute('size');font.style.fontSize=pendingSize+'pt';
    }
    // With CSS editing enabled Chromium expresses the pending size as a span.
    // Inspect only the insertion ancestors, not every styled node on each key.
    if(!range){
      let element=selectionElement(doc);
      while(element&&element!==doc.body){
        if(element.style.fontSize==='xxx-large'){element.style.fontSize=pendingSize+'pt';break;}
        element=element.parentElement;
      }
      pendingSize=null;existingFonts.clear();
    }
  }
  function setFontSize(points) {
    points=Math.max(1,Math.min(400,Number(points)||12));
    const range=doc.getSelection()?.rangeCount?doc.getSelection().getRangeAt(0).cloneRange():null;
    existingFonts=new Set(doc.querySelectorAll('font[size="7"]'));pendingSize=points;
    doc.execCommand('styleWithCSS',false,false);doc.execCommand('fontSize',false,'7');
    normalizeFonts(range);doc.execCommand('styleWithCSS',false,true);
  }
  doc.addEventListener('input',()=>normalizeFonts());
  doc.addEventListener('pointerdown',()=>pendingSize=null);
  doc.addEventListener('paste',()=>pendingSize=null);
  doc.addEventListener('keydown',event=>{if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key))pendingSize=null;});
  function followingParagraph(event) {
    if(event.inputType!=='insertParagraph')return;
    const previous=currentBlock(doc),style=styleById(previous?.dataset.sinStyle,doc);
    if(!style||style.next===style.id)return;
    // The browser performs the split (and owns undo); style only its new paragraph.
    doc.addEventListener('input',()=>{
      const next=currentBlock(doc);
      if(next&&next!==previous&&next.textContent.length===0){
        applyParagraphStyle(doc,style.next);
        const selection=doc.getSelection();selection?.collapseToEnd();
      }
    },{once:true});
  }
  doc.addEventListener('beforeinput',followingParagraph);
  return {setFontSize};
}
