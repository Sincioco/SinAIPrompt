import {request} from './bridge.js';

// Word-style integration scenarios: real editable DOM, native undo and clipboard.
export async function runRibbonTests(check) {
  const saved=window.editor.html(),doc=()=>document.querySelector('#document').contentDocument;
  const delay=()=>new Promise(resolve=>setTimeout(resolve,40));
  const click=selector=>document.querySelector(selector).click();
  function select(selector,start=null,end=start){
    const block=doc().querySelector(selector),range=doc().createRange();range.selectNodeContents(block);
    if(start!==null){const node=doc().createTreeWalker(block,NodeFilter.SHOW_TEXT).nextNode();range.setStart(node,start);range.setEnd(node,end);}
    const selection=doc().getSelection();selection.removeAllRanges();selection.addRange(range);
    doc().body.focus();doc().dispatchEvent(new Event('selectionchange'));
  }
  async function load(html='<p id="sample">Style reference sample</p><p id="after">Other paragraph</p>'){await window.editor.load(html);select('#sample',2);}
  function style(id){click('.style-strip [data-style="'+id+'"]');}
  try {
    check(document.querySelector('#growFont svg')&&document.querySelector('#shrinkFont svg')&&document.querySelector('#fontColor svg'),'Font size and font color controls use drawn Office-style icons');
    const editorButton=document.querySelector('#insertImage'),styles=document.querySelector('.styles-group');
    check(editorButton.closest('.editor-group')?.previousElementSibling===styles,'Image Editor is grouped immediately to the right of Styles');
    const toolbar=document.querySelector('#toolbar'),originalWidth=toolbar.style.width;
    toolbar.style.width='1800px';await delay();const styleWidth=styles.getBoundingClientRect().width;
    toolbar.style.width='2200px';await delay();
    check(Math.abs(styles.getBoundingClientRect().width-styleWidth)<1&&styleWidth<650,'Styles stops growing after the five named presets');
    toolbar.style.width=originalWidth;
    await load();
    const beforeLink=window.editor.html();
    for(const address of ['', 'invalid address']){
      click('#link');
      document.querySelector('dialog [name=url]').value=address;
      click('dialog button[value=ok]');
      check(document.querySelector('dialog[open]')&&window.editor.html()===beforeLink,'Insert Link still validates an empty or invalid URL');
      click('dialog button[value=cancel]');await delay();
      check(!document.querySelector('dialog[open]')&&window.editor.html()===beforeLink,'Cancel dismisses Insert Link without validating or changing the document');
    }
    select('#sample',0,5);click('#link');
    document.querySelector('dialog [name=url]').value='https://example.invalid/reference';click('dialog button[value=ok]');await delay();
    check(doc().querySelector('a')?.textContent==='Style'&&doc().querySelector('a').getAttribute('href')==='https://example.invalid/reference','Apply inserts a valid link at the saved selection');
    await window.editor.load('');
    let css=doc().defaultView.getComputedStyle(doc().querySelector('p'));
    check(doc().documentElement.dataset.sinStyleMode==='modern'&&css.fontFamily.includes('Segoe UI')&&css.fontSize==='16px'&&css.lineHeight==='26.4px'&&css.marginBottom==='16px','New documents default to the reference Modern font, line spacing and paragraph spacing');
    select('p');doc().execCommand('insertText',false,'Modern first paragraph');
    await request('test-key',{parameters:{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r'}});
    await request('test-key',{parameters:{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13}});
    doc().execCommand('insertText',false,'Modern second paragraph');
    css=doc().defaultView.getComputedStyle(doc().body.lastElementChild);
    check(doc().body.children.length===2&&css.marginBottom==='16px'&&css.lineHeight==='26.4px','Enter in a Modern document keeps its paragraph spacing');
    window.editor.command('insertHTML','<ul><li>First bullet</li><li id="secondBullet">Second bullet</li></ul>');
    css=doc().defaultView.getComputedStyle(doc().querySelector('#secondBullet'));
    check(Math.abs(parseFloat(css.marginTop)-5.6)<.05&&css.lineHeight==='26.4px','Modern bullets use the reference item and line spacing');
    select('p',2);style('heading');
    const heading=doc().querySelector('p'),content=doc().body.textContent;
    css=doc().defaultView.getComputedStyle(heading);
    check(Math.abs(parseFloat(css.fontSize)-22*4/3)<.05&&css.fontWeight==='600'&&css.color==='rgb(31, 35, 40)'&&css.borderBottomWidth==='1px','Modern Heading uses 22 points with the reference color, weight and divider');
    for(const [id,points] of [['title',34],['heading2',18],['heading',22]]){
      style(id);await delay();
      check(Math.abs(parseFloat(doc().defaultView.getComputedStyle(doc().querySelector('p')).fontSize)-points*4/3)<.05&&Number(document.querySelector('#fontSize').value)===points,'Modern '+id+' applies the requested point size and displays it in the font box');
    }
    check(document.querySelector('#fontSize').type==='number'&&!document.querySelector('#fontSize').hasAttribute('list'),'Font size retains numeric spin controls without the extra datalist arrow');
    check(document.querySelector('#backColor svg path')&&!document.querySelector('#backColor').textContent.includes('▰'),'Highlight uses a recognizable marker icon instead of the block glyph');
    document.querySelector('#documentStyle').value='office';document.querySelector('#documentStyle').dispatchEvent(new Event('change'));
    css=doc().defaultView.getComputedStyle(doc().querySelector('p'));
    check(css.fontFamily.includes('Aptos Display')&&css.color==='rgb(15, 71, 97)'&&css.borderBottomStyle==='none'&&doc().body.textContent===content,'MS Office Style updates styled paragraphs without losing content');
    document.querySelector('#documentStyle').value='modern';document.querySelector('#documentStyle').dispatchEvent(new Event('change'));
    const modern=window.editor.html();await window.editor.load(modern);
    check(doc().documentElement.dataset.sinStyleMode==='modern'&&document.querySelector('#documentStyle').value==='modern'&&doc().body.textContent===content&&doc().defaultView.getComputedStyle(doc().querySelector('p')).fontWeight==='600','Modern styling and toolbar choice survive HTML save and reload');
    check([...document.querySelectorAll('.file-group [data-native-command]')].map(button=>button.textContent).join(',')==='New,Save,Save As,Close'&&document.querySelector('#insertImage').getAttribute('aria-label')==='Image Editor','File ribbon contains New, Save, Save As, Close and a picture-edit icon');
    check(document.querySelector('.editor-group').nextElementSibling?.contains(document.querySelector('#screenCapture')),'Screen Capture follows Image Editor on the main ribbon');
    await load();
    check(!document.querySelector('input[type=color]')&&document.querySelectorAll('.style-strip [data-style]').length===5,'Ribbon exposes five Word paragraph styles and replaces native color inputs');
    const fontCss=await request('editor-fonts');
    for(const family of ['Aptos','Aptos Display'])if(fontCss.includes("font-family:'"+family+"'")){
      const faces=await doc().fonts.load('16px "'+family+'"');
      check(faces.length>0&&faces.every(face=>face.status==='loaded'),'The editor loads the actual local Office '+family+' font');
    }
    check(!window.editor.html().includes('sin-office-fonts.local'),'Saved HTML excludes the local Office font adapter');
    const expected=[['normal','P',16,0,32/3,278/240,'rgb(0, 0, 0)','Aptos'],['no-spacing','P',16,0,0,1,'rgb(0, 0, 0)','Aptos'],['heading','P',80/3,24,16/3,278/240,'rgb(15, 71, 97)','Aptos Display'],['heading2','P',64/3,32/3,16/3,278/240,'rgb(15, 71, 97)','Aptos Display'],['title','P',112/3,0,16/3,1,'rgb(0, 0, 0)','Aptos Display']];
    for(const [id,tag,size,before,after,line,color,font] of expected){
      await load();select('#sample',0,5);style(id);
      const block=doc().querySelector('#sample'),css=doc().defaultView.getComputedStyle(block),near=(a,b)=>Math.abs(parseFloat(a)-b)<.05;
      check(block.tagName===tag&&block.dataset.sinStyle===id&&block.textContent==='Style reference sample'&&doc().querySelector('#after').textContent==='Other paragraph','Applying '+id+' to a selected word styles its entire paragraph without changing adjacent text');
      check(near(css.fontSize,size)&&near(css.marginTop,before)&&near(css.marginBottom,after)&&near(css.lineHeight,size*line)&&css.color===color&&css.fontFamily.includes(font)&&css.fontWeight==='400','Word '+id+' font, color, point size, spacing and weight match the measured preset');
      if(id==='title')check(near(css.letterSpacing,-2/3),'Title uses Word’s condensed half-point character spacing');
      window.editor.command('undo');check(doc().querySelector('#sample')?.tagName==='P'&&!doc().querySelector('#sample')?.dataset.sinStyle,'Undo restores the paragraph before '+id);
    }
    await load();
    for(const id of ['heading','title','heading2','no-spacing','normal']){style(id);check(doc().body.children.length===2&&doc().querySelector('#sample')?.dataset.sinStyle===id,'Changing an existing style to '+id+' preserves the paragraph structure');}
    await window.editor.load('<p id="sample"><br></p><p id="after">Other paragraph</p>');
    select('#sample');doc().getSelection().collapseToStart();doc().dispatchEvent(new Event('selectionchange'));style('title');
    doc().execCommand('insertText',false,'New title');
    check(doc().querySelector('#sample')?.textContent==='New title'&&doc().querySelector('#after').textContent==='Other paragraph','Applying Title on an empty line keeps typing in that line');
    await load();
    const unchanged=window.editor.html();
    document.querySelector('[data-style=heading]').dispatchEvent(new MouseEvent('mouseover',{bubbles:true}));
    check(doc().querySelector('style[data-sin-style-preview]')&&window.editor.html()===unchanged,'Hover previews a style without changing saved HTML');
    document.querySelector('.style-strip').dispatchEvent(new MouseEvent('mouseleave'));
    check(!doc().querySelector('style[data-sin-style-preview]'),'Leaving the gallery removes the temporary preview');
    style('heading');select('#sample');doc().getSelection().collapseToEnd();doc().dispatchEvent(new Event('selectionchange'));
    await request('test-key',{parameters:{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r'}});
    await request('test-key',{parameters:{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13}});
    await delay();
    const next=doc().querySelector('#sample').nextElementSibling;
    check(next?.tagName==='P'&&next.dataset.sinStyle==='normal'&&next.textContent===''&&next.contains(doc().getSelection().anchorNode),'Enter after Heading creates Normal and keeps the caret on the new paragraph');
    doc().execCommand('insertText',false,'Next paragraph');
    check(next.textContent==='Next paragraph'&&parseFloat(doc().defaultView.getComputedStyle(next).fontSize)===16,'Typing after Heading uses Normal’s 12-point font');
    await load();select('#sample');
    click('#fontColor');check(document.querySelectorAll('.color-palette .swatch').length===70,'Office palette offers ten theme columns with shades and ten standard colors');
    click('.color-palette [data-color="#e97132"]');
    check(doc().querySelector('#sample').innerHTML.includes('233, 113, 50')&&!document.querySelector('.color-palette'),'Choosing a palette color formats the saved editor selection and closes the palette');
    select('#sample');click('#backColor');click('.color-palette [data-color="#ffff00"]');
    check(doc().querySelector('#sample').innerHTML.includes('255, 255, 0'),'Highlight palette applies the selected color');
    select('#sample');click('#backColor');click('.color-palette [data-color=transparent]');
    check(!doc().querySelector('#sample').innerHTML.includes('255, 255, 0'),'No Color removes text highlighting');
    await load();select('#sample');
    document.querySelector('#fontSize').value='18';document.querySelector('#fontSize').dispatchEvent(new Event('change'));
    await delay();click('#growFont');await delay();
    check(doc().querySelector('#sample').innerHTML.includes('20pt'),'Grow font advances to the next Word point size');
    click('#shrinkFont');await delay();check(doc().querySelector('#sample').innerHTML.includes('18pt'),'Shrink font returns to the previous Word point size');
    select('#sample');click('#formatPainter');select('#after');doc().dispatchEvent(new PointerEvent('pointerup'));
    check(doc().querySelector('#after').innerHTML.includes('24px')&&document.querySelector('#formatPainter').getAttribute('aria-pressed')==='false','Format Painter applies the source formatting and stops after one selection');
    await load();select('#sample',5);
    document.querySelector('#fontSize').value='23';document.querySelector('#fontSize').dispatchEvent(new Event('change'));
    doc().execCommand('insertText',false,' inserted');
    check(doc().querySelector('#sample').innerHTML.includes('23pt'),'Changing font size at the caret uses the requested points for subsequent typing');
    await load('<p id="sample"><strong>Copy 中文 café</strong></p><p id="after">Destination</p>');select('#sample');
    await window.editor.command('copy');const copied=await request('editor-paste');
    check(copied.html.includes('Copy 中文 café')&&copied.html.includes('strong')&&!copied.html.includes('StartFragment:'),'Windows HTML clipboard preserves Unicode text and rich formatting');
    select('#after');await window.editor.command('paste');
    check(doc().body.lastElementChild.textContent==='Copy 中文 café','Ribbon paste inserts Windows HTML at the saved selection');
    select('body > :last-child');await window.editor.command('cut');check(doc().body.lastElementChild.textContent==='','Cut removes text after copying it');
    window.editor.command('undo');check(doc().body.lastElementChild.textContent==='Copy 中文 café','Native undo restores cut text');
  } finally {await window.editor.load(saved);}
}
