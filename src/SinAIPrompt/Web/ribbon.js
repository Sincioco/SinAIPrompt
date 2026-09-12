import {report} from './bridge.js';
import {createColorPicker} from './color-picker.js';
import {currentBlock,applyParagraphStyle,previewParagraphStyle} from './word-styles.js';
import {wordStyles,stylesFor,documentStyleMode,setDocumentStyle} from './document-styles.js';
import {createTextFormatting,selectionElement,captureFormat,applyInlineFormat,selectWordAtCaret} from './text-formatting.js';

const icons={
  editor:'<path d="m3 21 2-7L16 2l4 4L9 18zM5 14l4 4M3 21l6-3M13 9h9M12 14h7M11 19h5"/>',
  highlight:'<path d="m7 13 8-9a2 2 0 0 1 3 0l2 2a2 2 0 0 1 0 3l-9 8z" fill="#8c8c8c" stroke="#666"/><path d="m7 13 4 4-2 2-4-1z" fill="#fff" stroke="#666"/><path d="m5 18 4 1-4 2H2z" fill="#646464" stroke="none"/>',
  paste:'<path d="M8 5H4v17h15V5h-4M9 3h5v4H9z"/><path d="M8 11h7M8 15h7M8 19h5"/>',
  cut:'<circle cx="5" cy="18" r="3"/><circle cx="18" cy="18" r="3"/><path d="m7 16 12-13M16 16 3 3"/>',
  copy:'<path d="M8 6H3v15h12v-4M9 2h8l5 5v10H9zM17 2v5h5"/>',
  paint:'<path d="m3 3 10 2-2 8-9-2zM11 9l7 2-1 5-5-1-1 7h-3l2-10"/>',
  bullets:'<path d="M9 4h13M9 12h13M9 20h13"/><path d="M2 3h2v2H2zM2 11h2v2H2zM2 19h2v2H2z" fill="#1996c4" stroke="none"/>',
  numbering:'<path d="M10 4h12M10 12h12M10 20h12"/><g fill="currentColor" stroke="none" font-family="Segoe UI,Arial,sans-serif" font-size="8"><text x="1" y="7">1</text><text x="1" y="15">2</text><text x="1" y="23">3</text></g>',
  left:'<path d="M2 4h20M2 9h13M2 14h20M2 19h13"/>',
  center:'<path d="M2 4h20M6 9h12M2 14h20M6 19h12"/>',
  right:'<path d="M2 4h20M9 9h13M2 14h20M9 19h13"/>',
  justify:'<path d="M2 4h20M2 9h20M2 14h20M2 19h20"/>',
  outdent:'<path d="M2 4h20M12 9h10M12 15h10M2 20h20"/><path d="m6 9-3 3 3 3M3 12h6" stroke="#1996c4"/>',
  indent:'<path d="M2 4h20M12 9h10M12 15h10M2 20h20"/><path d="m5 9 3 3-3 3M2 12h6" stroke="#1996c4"/>'
};
const icon=name=>`<svg viewBox="0 0 24 24" aria-hidden="true">${icons[name]}</svg>`;
const button=(command,label,content)=>`<button type="button" data-cmd="${command}" aria-label="${label}" title="${label}">${content}</button>`;
const fontSizes=[8,9,10,11,12,14,16,18,20,22,24,26,28,36,48,72,96,144,200,300,400];

export function createRibbon(root,{getDocument,saveSelection,restoreSelection,command,changed}) {
  const styleTile=style=>`<button type="button" class="style-tile style-${style.id}" data-style="${style.id}" title="${style.name}: ${style.css.fontFamily.split(',')[0].replaceAll('"','')}, ${style.css.fontSize}" aria-label="${style.name}"><span>${style.name}</span></button>`;
  root.innerHTML=`<div class="ribbon-groups">
    <section class="ribbon-group clipboard-group" aria-label="Clipboard"><div class="clipboard-controls">
      <button class="paste-large" data-cmd="paste" title="Paste (Ctrl+V)">${icon('paste')}<span>Paste</span></button>
      <div class="clipboard-small">${button('cut','Cut (Ctrl+X)',icon('cut')+'Cut')}${button('copy','Copy (Ctrl+C)',icon('copy')+'Copy')}
      <button id="formatPainter" aria-pressed="false" title="Format Painter: click once for one selection; double-click to keep painting. Esc cancels.">${icon('paint')}<span>Format Painter</span></button></div></div><div class="group-caption">Clipboard</div></section>
    <section class="ribbon-group font-group" aria-label="Font"><div class="font-top">
      <select id="font" aria-label="Font">${['Aptos','Aptos Display','Segoe UI','Arial','Calibri','Cambria','Verdana','Georgia','Times New Roman','Consolas','Courier New'].map(name=>`<option>${name}</option>`).join('')}</select>
      <input id="fontSize" aria-label="Font Size In Points" title="Font Size In Points" type="number" min="1" max="400" step=".5" value="12">
      <button id="growFont" aria-label="Increase Font Size" title="Increase Font Size"><span class="grow-a">A</span><sup>⌃</sup></button><button id="shrinkFont" aria-label="Decrease Font Size" title="Decrease Font Size">A<sup>⌄</sup></button>
    </div><div class="font-bottom">
      ${button('bold','Bold (Ctrl+B)','<b>B</b>')}${button('italic','Italic (Ctrl+I)','<i>I</i>')}${button('underline','Underline (Ctrl+U)','<u>U</u>')}${button('strikeThrough','Strikethrough','<s>ab</s>')}
      ${button('subscript','Subscript','x<sub>2</sub>')}${button('superscript','Superscript','x<sup>2</sup>')}
      <button id="backColor" value="#ffff00" class="text-color highlight-color" aria-label="Text Highlight Color"><span class="color-glyph">${icon('highlight')}</span></button><button id="fontColor" value="#000000" class="text-color" aria-label="Font Color"><span class="color-glyph">A</span></button>
    </div><div class="group-caption">Font</div></section>
    <section class="ribbon-group paragraph-group" aria-label="Paragraph"><div class="paragraph-row">
      ${button('insertUnorderedList','Bullets',icon('bullets'))}${button('insertOrderedList','Numbering',icon('numbering'))}${button('outdent','Decrease Indent',icon('outdent'))}${button('indent','Increase Indent',icon('indent'))}
    </div><div class="paragraph-row">${['Left','Center','Right','Full'].map((alignment,i)=>button('justify'+alignment,['Align Left','Center','Align Right','Justify'][i],icon(['left','center','right','justify'][i]))).join('')}</div><div class="group-caption">Paragraph</div></section>
    <section class="ribbon-group styles-group" aria-label="Styles"><div class="style-gallery"><div class="style-strip">${wordStyles.map(styleTile).join('')}</div><button id="moreStyles" aria-label="More Styles" title="More Styles" aria-expanded="false">⌄</button></div><div class="group-caption">Styles</div></section>
  </div><div class="ribbon-actions"><button data-native-command="save" title="Save (Ctrl+S)">Save</button><button data-native-command="saveAll" title="Save All (Ctrl+Alt+S)">Save All</button><button data-native-command="rename">Rename</button><span class="action-divider"></span>${button('undo','Undo (Ctrl+Z)','↶')}${button('redo','Redo (Ctrl+Y)','↷')}<button id="link">Link</button><button id="pasteCode">&lt;/&gt; Paste Code</button><select id="documentStyle" aria-label="Document Style" title="Document Style"><option value="modern">Modern</option><option value="office">MS Office Style</option></select><span class="ribbon-hint" id="painterHint" hidden>Select text to paint its formatting · Esc cancels</span><button id="source">View Source</button><button id="screenCapture">Screen Capture</button><button id="insertImage" title="Edit the selected image, or create a new image">${icon('editor')}<span>Editor</span></button></div>`;
  const $=selector=>root.querySelector(selector);
  let formatting=null,painter=null,locked=false,painterSheet=null,stylePopup=null,syncFrame=0;
  const colorPickers=[
    createColorPicker($('#fontColor'),{emptyValue:'#000000',onChange:value=>{command('foreColor',value);$('#fontColor').value=value;}}),
    createColorPicker($('#backColor'),{emptyLabel:'No Color',emptyValue:'transparent',onChange:value=>{command('hiliteColor',value);$('#backColor').value=value;}})
  ];
  function cancelPainter(){painter=null;locked=false;painterSheet?.remove();painterSheet=null;$('#formatPainter').setAttribute('aria-pressed','false');$('#painterHint').hidden=true;}
  function finishChange(){saveSelection();changed();sync();}
  function refreshStyles(){
    const doc=getDocument();root.dataset.styleMode=documentStyleMode(doc);
    $('#documentStyle').value=documentStyleMode(doc);
    $('.style-strip').innerHTML=stylesFor(doc).map(styleTile).join('');
    stylePopup?.hidePopover();
  }
  $('#documentStyle').onchange=()=>{
    const doc=getDocument();previewParagraphStyle(doc,null);restoreSelection();
    setDocumentStyle(doc,$('#documentStyle').value);refreshStyles();finishChange();
  };
  function applyStyle(id){const doc=getDocument();previewParagraphStyle(doc,null);restoreSelection();applyParagraphStyle(doc,id);finishChange();stylePopup?.hidePopover();}
  function wireGallery(gallery){
    gallery.addEventListener('mouseover',event=>{const tile=event.target.closest('[data-style]');if(tile)previewParagraphStyle(getDocument(),tile.dataset.style);});
    gallery.addEventListener('mouseleave',()=>previewParagraphStyle(getDocument(),null));
    gallery.addEventListener('click',event=>{const tile=event.target.closest('[data-style]');if(tile)applyStyle(tile.dataset.style);});
  }
  wireGallery($('.style-strip'));
  $('#moreStyles').onclick=()=>{
    if(stylePopup){stylePopup.remove();stylePopup=null;$('#moreStyles').setAttribute('aria-expanded','false');return;}
    stylePopup=document.createElement('div');stylePopup.className='styles-popup';stylePopup.setAttribute('popover','auto');stylePopup.setAttribute('role','group');stylePopup.setAttribute('aria-label','Paragraph Styles');
    stylePopup.innerHTML=stylesFor(getDocument()).map(styleTile).join('');root.append(stylePopup);wireGallery(stylePopup);
    stylePopup.addEventListener('mousedown',event=>event.preventDefault());
    stylePopup.addEventListener('toggle',event=>{if(event.newState==='closed'){previewParagraphStyle(getDocument(),null);stylePopup?.remove();stylePopup=null;$('#moreStyles').setAttribute('aria-expanded','false');}});
    stylePopup.showPopover();const rect=$('#moreStyles').getBoundingClientRect();stylePopup.style.top=rect.bottom+4+'px';stylePopup.style.left=Math.max(8,Math.min(rect.right-stylePopup.offsetWidth,innerWidth-stylePopup.offsetWidth-8))+'px';$('#moreStyles').setAttribute('aria-expanded','true');
  };
  root.addEventListener('mousedown',event=>{saveSelection();if(event.target.closest('button'))event.preventDefault();});
  root.addEventListener('click',event=>{
    const target=event.target.closest('[data-cmd]');if(!target)return;
    previewParagraphStyle(getDocument(),null);
    Promise.resolve(command(target.dataset.cmd)).catch(report);
  });
  $('#font').onchange=()=>command('fontName',$('#font').value);
  function fontSize(size){restoreSelection();formatting?.setFontSize(size);finishChange();}
  $('#fontSize').onchange=()=>fontSize($('#fontSize').value);
  $('#growFont').onclick=()=>fontSize(fontSizes.find(size=>size>Number($('#fontSize').value))||400);
  $('#shrinkFont').onclick=()=>fontSize(fontSizes.findLast(size=>size<Number($('#fontSize').value))||1);
  $('#formatPainter').onclick=event=>{
    if(event.detail===2&&painter){locked=true;return;}
    if(painter){cancelPainter();return;}
    restoreSelection();painter=captureFormat(getDocument());if(!painter)return;
    painterSheet=getDocument().createElement('style');painterSheet.dataset.sinRuntime='1';painterSheet.textContent='body,body *{cursor:copy!important}';getDocument().head.append(painterSheet);
    $('#formatPainter').setAttribute('aria-pressed','true');$('#painterHint').hidden=false;
  };
  function attach(doc){
    cancelPainter();formatting=createTextFormatting(doc);refreshStyles();
    doc.addEventListener('pointerup',()=>{
      if(!painter)return;
      if(selectWordAtCaret(doc)&&applyInlineFormat(doc,painter)){if(!locked)cancelPainter();finishChange();}
    });
    doc.addEventListener('keydown',event=>{if(event.key==='Escape'&&painter){event.preventDefault();cancelPainter();}});
    doc.addEventListener('selectionchange',()=>previewParagraphStyle(doc,null));
    sync();
  }
  function sync(){
    if(syncFrame)return;
    syncFrame=requestAnimationFrame(()=>{
      syncFrame=0;const doc=getDocument(),element=selectionElement(doc);if(!doc||!element)return;
      const css=doc.defaultView.getComputedStyle(element),font=css.fontFamily.split(',')[0].replaceAll('"','').trim();
      for(const control of root.querySelectorAll('[data-cmd]')){
        if(['cut','copy'].includes(control.dataset.cmd))control.disabled=doc.getSelection().isCollapsed;
        else if(!['paste','undo','redo','indent','outdent'].includes(control.dataset.cmd))control.setAttribute('aria-pressed',String(doc.queryCommandState(control.dataset.cmd)));
      }
      if(document.activeElement!==$('#font')){if(![...$('#font').options].some(option=>option.value===font))$('#font').add(new Option(font,font));$('#font').value=font;}
      if(document.activeElement!==$('#fontSize'))$('#fontSize').value=String(Math.round(parseFloat(css.fontSize)*.75*100)/100);
      const block=currentBlock(doc);
      const style=block?.dataset.sinStyle||({H1:'heading',H2:'heading2'}[block?.tagName])||'normal';
      root.querySelectorAll('[data-style]').forEach(tile=>tile.setAttribute('aria-pressed',String(tile.dataset.style===style)));
      $('#fontColor').value=css.color;$('#backColor').value=css.backgroundColor==='rgba(0, 0, 0, 0)'?'transparent':css.backgroundColor;
      colorPickers.forEach(picker=>picker.sync());
    });
  }
  return {attach,sync};
}
