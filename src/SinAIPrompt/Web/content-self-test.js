import {youtubeCard} from './youtube.js';
import {videoMode} from './video-presentation.js';

export async function runContentTests(check){
  const saved=window.editor.html(),doc=()=>document.querySelector('#document').contentDocument;
  const delay=()=>new Promise(resolve=>setTimeout(resolve,60));
  const click=selector=>document.querySelector(selector).click();
  try{
    await window.editor.load('<p data-sin-style="title">Document title</p><h1>First section</h1><p>Body</p><h2>Subsection</h2>');
    check(JSON.stringify(window.editor.outline.items().map(x=>[x.text,x.level]))===JSON.stringify([['Document title',0],['First section',1],['Subsection',2]]),'Content View includes Title, Heading and Heading2 in document order');
    window.editor.outline.jump(2);check(doc().querySelector('h2').contains(doc().getSelection().anchorNode),'Content View moves the caret to the chosen section');
    const original=window.editor.html();window.editor.setReadOnly(true);window.editor.command('insertText','Blocked edit');
    check(!doc().body.isContentEditable&&window.editor.html()===original&&document.querySelector('[data-native-command=lock]').disabled&&!document.querySelector('[data-native-command=unlock]').disabled,'Read-only disables editing and exposes Unlock without changing saved markup');
    check(window.editor.outline.items().length===3,'Locked documents retain their Title, Heading and Heading2 outline');
    window.editor.setReadOnly(false);
    const video={id:'AbcD_123-xy',title:'Reference title',author:'Example channel',image:'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=='};
    await window.editor.load(youtubeCard(video)+'<p>Following text stays here</p>');
    doc().querySelector('figcaption').click();
    for(const mode of ['url','inline','card','embed']){
      click('[data-video-tool=mode]');click(`[data-video-mode=${mode}]`);await delay();
      const item=doc().querySelector('[data-sin-youtube]');
      check(item&&videoMode(item)===mode&&doc().body.textContent.includes('Following text stays here'),`YouTube ${mode} presentation replaces the selection and preserves neighboring text`);
      if(mode==='url')check(!document.querySelector('[data-video-tool=copy-url]').hidden&&document.querySelector('[data-video-tool=more]').hidden,'URL toolbar offers Copy URL without a Delete menu');
      if(mode==='card')check(item.querySelector('img').getAttribute('src')===video.image&&item.textContent.includes(video.author),'Card presentation retains the thumbnail and author');
    }
    window.editor.command('undo');await delay();check(videoMode(doc().querySelector('[data-sin-youtube]'))==='card','YouTube presentation changes use native Undo');
    await window.editor.load('<p>Before <a href="https://youtu.be/AbcD_123-xy">Linked title</a> after</p>');
    const range=doc().createRange();range.setStart(doc().querySelector('a').firstChild,3);range.collapse(true);doc().getSelection().removeAllRanges();doc().getSelection().addRange(range);doc().dispatchEvent(new Event('selectionchange'));await delay();
    check(!document.querySelector('.video-selection').hidden&&document.querySelector('[data-mode-label]').textContent==='URL','Caret inside an ordinary YouTube hyperlink opens the URL toolbar');
    click('[data-video-tool=unlink]');await delay();check(!doc().querySelector('a')&&doc().body.textContent==='Before Linked title after','Unlink preserves ordinary text and surrounding paragraph');
  }finally{window.editor.setReadOnly(false);await window.editor.load(saved);}
}
