// Read from a document saved by the running Word instance on 2026-09-12.
// Word stores paragraph spacing in twips and automatic line spacing in 240ths.
const bodyFont='Aptos, "Segoe UI", Arial, sans-serif';
const displayFont='"Aptos Display", Aptos, "Segoe UI", Arial, sans-serif';
const common={fontWeight:'400',fontStyle:'normal',color:'#000000',textAlign:'left',textIndent:'0',letterSpacing:'normal',marginLeft:'0',marginRight:'0',orphans:'2',widows:'2'};
export const wordStyles = [
  {id:'normal',name:'Normal',tag:'p',next:'normal',css:{...common,fontFamily:bodyFont,fontSize:'12pt',lineHeight:String(278/240),marginTop:'0pt',marginBottom:'8pt'}},
  {id:'no-spacing',name:'No Spacing',tag:'p',next:'no-spacing',css:{...common,fontFamily:bodyFont,fontSize:'12pt',lineHeight:'1',marginTop:'0pt',marginBottom:'0pt'}},
  {id:'heading',name:'Heading',tag:'h1',next:'normal',css:{...common,fontFamily:displayFont,fontSize:'20pt',color:'#0f4761',lineHeight:String(278/240),marginTop:'18pt',marginBottom:'4pt',breakAfter:'avoid',breakInside:'avoid'}},
  {id:'heading2',name:'Heading2',tag:'h2',next:'normal',css:{...common,fontFamily:displayFont,fontSize:'16pt',color:'#0f4761',lineHeight:String(278/240),marginTop:'8pt',marginBottom:'4pt',breakAfter:'avoid',breakInside:'avoid'}},
  {id:'title',name:'Title',tag:'p',next:'normal',css:{...common,fontFamily:displayFont,fontSize:'28pt',lineHeight:'1',marginTop:'0pt',marginBottom:'4pt',letterSpacing:'-.5pt'}}
];
export const styleById=id=>wordStyles.find(style=>style.id===id);
export const cssText=properties=>Object.entries(properties).map(([key,value])=>`${key.replace(/[A-Z]/g,ch=>'-'+ch.toLowerCase())}:${value}`).join(';');
export const wordDocumentStyles=`body{font-family:${bodyFont};font-size:12pt;color:#000;line-height:${278/240};font-kerning:normal;font-variant-ligatures:common-ligatures contextual}p{margin:0 0 8pt}h1{${cssText(styleById('heading').css)}}h2{${cssText(styleById('heading2').css)}}[data-sin-style=title]+[data-sin-style=title]{margin-top:-4pt}`;

export function currentBlock(doc) {
  const selection=doc.getSelection();let node=selection?.anchorNode;
  if(node===doc.body)node=node.childNodes[selection.anchorOffset]||node.lastChild;
  return (node?.nodeType===1?node:node?.parentElement)?.closest('p,h1,h2,h3,h4,h5,h6,li,blockquote,div,td,th')||null;
}
export function selectedBlocks(doc) {
  const selection=doc.getSelection();if(!selection?.rangeCount)return [];
  const range=selection.getRangeAt(0);
  if(range.collapsed){const block=currentBlock(doc);return block?[block]:[];}
  return [...doc.body.querySelectorAll('p,h1,h2,h3,h4,h5,h6,li,blockquote,div,td,th')].filter(block=>{
    if(block.closest('[contenteditable=false]')||block.querySelector('p,h1,h2,h3,h4,h5,h6,li,blockquote,div'))return false;
    const edge=doc.createRange();edge.selectNodeContents(block);edge.collapse(false);
    if(range.compareBoundaryPoints(Range.START_TO_START,edge)>=0)return false;
    edge.collapse(true);edge.setStart(block,0);edge.collapse(true);
    return range.compareBoundaryPoints(Range.END_TO_END,edge)>0;
  });
}
function textBookmark(doc) {
  const selection=doc.getSelection(),range=selection.getRangeAt(0),prefix=doc.createRange();
  prefix.selectNodeContents(doc.body);prefix.setEnd(range.startContainer,range.startOffset);
  const start=prefix.toString().length;return {start,end:start+range.toString().length,empty:!currentBlock(doc)?.textContent};
}
function restoreBookmark(doc,{start,end,empty}) {
  if(empty){const block=currentBlock(doc);if(block){const range=doc.createRange();range.selectNodeContents(block);range.collapse(true);const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);return;}}
  const nodes=doc.createTreeWalker(doc.body,NodeFilter.SHOW_TEXT),range=doc.createRange();
  let node,offset=0,found=false;
  while((node=nodes.nextNode())){
    if(!found&&start<=offset+node.length){range.setStart(node,Math.max(0,start-offset));found=true;}
    if(found&&end<=offset+node.length){range.setEnd(node,Math.max(0,end-offset));break;}
    offset+=node.length;
  }
  if(!found){range.selectNodeContents(doc.body);range.collapse(false);}
  const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);
}
export function applyParagraphStyle(doc,id) {
  const style=styleById(id);if(!style||!doc.getSelection()?.rangeCount)return;
  const bookmark=textBookmark(doc);
  let blocks=selectedBlocks(doc);
  if(!blocks.length){doc.execCommand('formatBlock',false,'p');blocks=selectedBlocks(doc);}
  for(const block of blocks){
    const cell=block.matches('td,th');
    // A Word style formats a paragraph; it does not need to replace the HTML
    // block type. Keeping that type avoids nested paragraphs in native editing.
    const replacement=doc.createElement(cell?'p':block.localName);
    if(!cell)for(const attribute of block.attributes)replacement.setAttribute(attribute.name,attribute.value);
    replacement.innerHTML=block.innerHTML;replacement.dataset.sinStyle=style.id;
    replacement.setAttribute('role',['heading','heading2'].includes(id)?'heading':'paragraph');
    if(id==='heading'||id==='heading2')replacement.setAttribute('aria-level',id==='heading'?'1':'2');
    else replacement.removeAttribute('aria-level');
    // Paragraph presets replace old font overrides while retaining emphasis,
    // links, images and other paragraph contents.
    for(const child of replacement.querySelectorAll('span,font')){
      for(const property of ['font-family','font-size','color','letter-spacing'])child.style.removeProperty(property);
      for(const attribute of ['face','size','color','data-sin-character-style'])child.removeAttribute(attribute);
    }
    for(const property of ['break-after','break-inside'])replacement.style.removeProperty(property);
    Object.assign(replacement.style,style.css);
    const range=doc.createRange();range.selectNodeContents(block);
    const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);
    doc.execCommand('insertHTML',false,replacement.outerHTML);
  }
  restoreBookmark(doc,bookmark);
}

// Preview uses a runtime stylesheet, so hovering never changes saved HTML or undo.
export function previewParagraphStyle(doc,id) {
  if(!doc)return;
  doc.querySelector('style[data-sin-style-preview]')?.remove();
  if(!id)return;
  const style=styleById(id),blocks=selectedBlocks(doc);if(!style||!blocks.length)return;
  const selectors=blocks.map(block=>{
    const parts=[];
    while(block!==doc.body){parts.unshift(`${block.localName}:nth-child(${[...block.parentElement.children].indexOf(block)+1})`);block=block.parentElement;}
    return 'body > '+parts.join(' > ');
  });
  const sheet=doc.createElement('style');sheet.dataset.sinRuntime='1';sheet.dataset.sinStylePreview='1';
  sheet.textContent=selectors.join(',')+'{'+cssText(style.css).replaceAll(';','!important;')+'!important}';doc.head.append(sheet);
}
