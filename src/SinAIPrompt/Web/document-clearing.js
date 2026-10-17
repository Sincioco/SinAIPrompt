import {styleById} from './document-styles.js';
import {editVideoAtoms} from './youtube.js';

export function selectAllContent(doc){
  const range=doc.createRange();range.selectNodeContents(doc.body);
  const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);
}

function coversVisibleContent(doc,range){
  // Chromium's own Select All can start/end inside text nodes and omit hidden
  // siblings. Check the visible edges too, without joining the entire document.
  const walker=doc.createTreeWalker(doc.body,NodeFilter.SHOW_TEXT,{acceptNode(node){
    if(!/\S/.test(node.data)||doc.defaultView.getComputedStyle(node.parentElement).visibility!=='visible')return NodeFilter.FILTER_REJECT;
    const edge=doc.createRange();edge.selectNodeContents(node);
    return [...edge.getClientRects()].some(rect=>rect.width&&rect.height)?NodeFilter.FILTER_ACCEPT:NodeFilter.FILTER_REJECT;
  }});
  const first=walker.nextNode();walker.currentNode=doc.body;const last=walker.lastChild();
  if(first){
    let end=last.length;while(end&&/\s/.test(last.data[end-1]))end--;
    if(range.comparePoint(first,first.data.search(/\S/))!==0||range.comparePoint(last,end)!==0)return false;
  }
  const atoms=[...doc.body.querySelectorAll('img,hr,video,audio,iframe,object,embed,svg,canvas,input,textarea,select')]
    .filter(node=>node.getClientRects().length&&doc.defaultView.getComputedStyle(node).visibility==='visible');
  return !!(first||atoms.length)&&atoms.every(node=>{
    const edge=doc.createRange();edge.selectNode(node);
    return range.compareBoundaryPoints(Range.START_TO_START,edge)<=0&&range.compareBoundaryPoints(Range.END_TO_END,edge)>=0;
  });
}

// Delete complete root nodes instead of letting Chromium keep the first styled
// container and skip hidden descendants. Native editing still owns Undo/Redo.
export function clearAllSelected(doc){
  const selection=doc.getSelection();
  if(!doc.body.isContentEditable||!selection?.rangeCount||selection.isCollapsed)return false;
  const range=selection.getRangeAt(0),body=doc.createRange();body.selectNodeContents(doc.body);
  if((range.compareBoundaryPoints(Range.START_TO_START,body)>0||range.compareBoundaryPoints(Range.END_TO_END,body)<0)&&!coversVisibleContent(doc,range))return false;
  const paragraph=doc.createElement('p');paragraph.dataset.sinStyle='normal';paragraph.setAttribute('role','paragraph');
  Object.assign(paragraph.style,styleById('normal',doc).css);paragraph.append(doc.createElement('br'));
  const originals=[...doc.body.children].map(node=>({node,attributes:['contenteditable','style','hidden',...(node.matches('input[type=hidden]')?['type']:[])].map(name=>[name,node.getAttribute(name)])}));
  return editVideoAtoms(doc,()=>{
    try{
      // These temporary atoms are removed in this same synchronous operation,
      // before painting. Restore attributes on detached nodes retained by Undo.
      for(const {node} of originals){
        node.setAttribute('contenteditable','false');node.removeAttribute('hidden');
        if(node.matches('input[type=hidden]'))node.setAttribute('type','text');
        for(const [name,value] of [['display','inline-block'],['visibility','visible'],['min-width','1px'],['min-height','1px']])node.style.setProperty(name,value,'important');
      }
      selectAllContent(doc);
      return doc.execCommand('insertHTML',false,paragraph.outerHTML);
    }finally{
      for(const {node,attributes} of originals)for(const [name,value] of attributes){
        if(value==null)node.removeAttribute(name);else node.setAttribute(name,value);
      }
    }
  });
}
