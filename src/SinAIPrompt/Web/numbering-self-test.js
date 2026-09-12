import {setListNumber} from './list-numbering.js';
import {request} from './bridge.js';

export async function runNumberingTests(check){
  const original=window.editor.html();
  let doc;
  const load=async html=>{await window.editor.load(html);await window.editor.ready();doc=document.querySelector('#document').contentDocument;};
  const caret=(selector,end=true)=>{doc.body.focus();const range=doc.createRange();range.selectNodeContents(typeof selector==='string'?doc.querySelector(selector):selector);range.collapse(!end);doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);doc.dispatchEvent(new Event('selectionchange'));};
  const enter=async()=>{await request('test-key',{parameters:{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r'}});await request('test-key',{parameters:{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13}});};
  const numbers=selector=>{const list=typeof selector==='string'?doc.querySelector(selector):selector;let next=list.hasAttribute('start')?list.start:1;return [...list.children].filter(el=>el.tagName==='LI').map(li=>{if(li.hasAttribute('value'))next=li.value;return next++;});};
  const lastList=()=>[...doc.querySelectorAll('ol')].at(-1);
  const options=async mode=>{
    document.querySelector('#listNumbering').click();
    for(let i=0;i<100&&!document.querySelector(`dialog button[value="${mode}"]`);i++)await new Promise(resolve=>setTimeout(resolve,10));
    const button=document.querySelector(`dialog button[value="${mode}"]`),dialog=button.closest('dialog');
    const closed=new Promise(resolve=>dialog.addEventListener('close',()=>setTimeout(resolve,0),{once:true}));
    button.click();await closed;
  };
  try{
    await load('<ol><li>One</li><li>Two</li></ol><h2>Next section</h2><ol id="second"><li>Three</li><li value="3">Four</li><li value="3">Five</li></ol>');
    caret('#second li');setListNumber(doc,'continue');
    check(numbers('#second').join() === '3,4,5','Continue Previous List repairs all following duplicated numbers in one operation');
    caret('#second li:last-child');await enter();
    check(numbers('#second').join()==='3,4,5,6','Enter after a continued numbered item increments instead of repeating');
    doc.execCommand('insertText',false,'Six');await enter();doc.execCommand('insertText',false,'Seven');
    check(numbers('#second').join()==='3,4,5,6,7','Repeated Enter and typing continues the section sequentially');
    caret('#second li');await enter();doc.execCommand('insertText',false,'Inserted');
    check(numbers('#second').join()==='3,4,5,6,7,8','Enter directly on the continuation anchor removes the copied explicit value');
    doc.execCommand('undo');doc.execCommand('redo');
    check(numbers('#second').join()==='3,4,5,6,7,8','Undo and Redo of Enter preserve consecutive numbers');

    await load('<ol start="5"><li>Five</li><li>Six</li></ol><h2>Section two</h2><ul id="second" style="list-style-type:circle"><li><b>Seven</b></li><li>Eight</li></ul><h2>Section three</h2><p id="third">Nine</p>');
    caret('#second li');await options('continue');
    check(doc.querySelector('#second')?.tagName==='OL'&&numbers('#second').join()==='7,8','The actual Continue dialog converts round bullets to numbers in one click');
    check(doc.defaultView.getComputedStyle(doc.querySelector('#second li')).listStyleType==='decimal','Converted bullets visually use decimal markers');
    caret('#second li');await options('continue');
    check(numbers('#second').join()==='7,8'&&doc.querySelector('#second b')?.textContent==='Seven','Continue is idempotent and preserves inline formatting');
    caret('#third');await options('continue');
    check(lastList()?.querySelector('li')?.value===9&&!doc.querySelector('ul'),'Continue on a plain paragraph creates the next numbered section without a circle bullet');
    await request('test-capture',{name:'numbered-sections'});
    const persisted=window.editor.html();await load(persisted);
    const heading=doc.createElement('h2');heading.textContent='New section after reopening';doc.body.append(heading);
    const paragraph=doc.createElement('p');paragraph.id='new-section';paragraph.textContent='Ten';doc.body.append(paragraph);
    caret('#new-section');window.editor.command('insertOrderedList');
    check(numbers(lastList())[0]===10,'Reopening HTML retains continuation mode for a newly inserted numbered section');
    caret(lastList().querySelector('li'));await enter();
    check(numbers(lastList()).join()==='10,11','Enter in a newly created section after reopening increments');

    await load('<ol start="8"><li id="first">Eight</li><li>Nine</li><li>Ten</li></ol><h2>Next</h2><ol id="second"><li>Eleven</li><li>Twelve</li></ol>');
    caret('#second li');setListNumber(doc,'continue');
    caret('#first');setListNumber(doc,'restart');
    check(numbers('ol').join()==='1,2,3'&&numbers('#second').join()==='4,5','Restart renumbers later sections linked by Continue');
    caret('#second li');setListNumber(doc,'set',42);await enter();
    check(numbers('#second').join()==='42,43,44','Custom start values increment naturally with Enter');
    caret('#second li');setListNumber(doc,'continue');
    check(numbers('#second').join()==='4,5,6','Continue repairs a custom start and its following items');
    await load('<ol><li>Outer one<ol start="4"><li>Nested four</li><li>Nested five</li></ol></li><li>Outer two</li></ol><h2>Next</h2><ol id="second"><li>Outer three</li></ol>');
    caret('#second li');setListNumber(doc,'continue');
    check(numbers('#second')[0]===3,'Nested numbering does not change the next outer-section number');
  }finally{await load(original);}
}
