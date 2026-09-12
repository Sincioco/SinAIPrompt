import {youtubeId,youtubeCard} from './youtube.js';
import {request} from './bridge.js';

export async function runMediaTests(check){
  const saved=window.editor.html(),frame=document.querySelector('#document'),doc=()=>frame.contentDocument;
  const delay=()=>new Promise(resolve=>setTimeout(resolve,30));
  const wait=async predicate=>{for(let n=0;n<120;n++){if(await predicate())return;await delay();}throw Error('Media action did not finish');};
  const realClick=async element=>{
    const r=element.getBoundingClientRect(),x=r.x+r.width/2,y=r.y+r.height/2;
    for(const type of ['mousePressed','mouseReleased'])await request('test-mouse',{parameters:{type,x,y,button:'left',buttons:type==='mousePressed'?1:0,clickCount:1}});
  };
  try{
    check(youtubeId('https://youtu.be/AbcD_123-xy?t=3')==='AbcD_123-xy'&&youtubeId('https://youtube.com/shorts/AbcD_123-xy')==='AbcD_123-xy'&&youtubeId('https://youtube.com.evil.invalid/watch?v=AbcD_123-xy')===null,'YouTube cards recognize valid links and reject lookalike hosts');
    const canvas=document.createElement('canvas');canvas.width=100;canvas.height=60;canvas.getContext('2d').fillRect(0,0,100,60);const png=canvas.toDataURL();
    await window.editor.load('<p>Media fixture</p>');
    window.editor.setImageStorage('separate');await window.editor.insertImage(png);
    check(doc().images[0].dataset.sinStorage==='separate'&&!document.querySelector('dialog[open]'),'Always-store preference inserts a separate image without asking');
    const source=doc().images[0].getAttribute('src');
    window.editor.setImageStorage('inline');window.editor.command('selectAll');window.editor.command('delete');await window.editor.insertImage(png);
    check(doc().images[0].src.startsWith('data:image/png;')&&doc().images[0].dataset.sinStorage==='inline','Always-embed preference wins even when the same image already has a separate file');
    window.editor.command('selectAll');window.editor.command('delete');await window.editor.insertImage(png,'separate');
    check(doc().images[0].getAttribute('src')===source,'An explicit separate insertion overrides the default and reuses the existing image');
    doc().images[0].click();
    check([...document.querySelectorAll('[data-image-action]')].map(b=>b.textContent).join('|')==='Edit Image|Resize Image|Rename Image|Delete Image|View Full Screen|View Externally'&&!document.querySelector('dialog[open]'),'Image actions are a six-option dropdown, not a dialog');
    await realClick(document.querySelector('[data-image-action=fullscreen]'));
    await wait(()=>document.fullscreenElement?.classList.contains('media-fullscreen'));
    const layout=await request('test-annotation-layout');
    check(layout.expanded&&!layout.backgroundEnabled&&document.querySelector('.media-fullscreen img').src.startsWith('https://sin-document.local/'),'Image fullscreen expands the native host while preserving its source');
    await document.exitFullscreen();
    await wait(async()=>!(await request('test-annotation-layout')).expanded);
    check(!document.querySelector('.media-fullscreen'),'Leaving fullscreen removes the temporary viewer and restores the editor');
    const video={id:'AbcD_123-xy',title:'Title & <test>',author:'Example channel',image:png};
    await window.editor.load(youtubeCard(video)+'<script>window.documentScriptRan=true</script>');
    check(doc().querySelector('[data-video-title]').textContent===video.title&&doc().querySelector('[data-video-author]').textContent===video.author&&!doc().defaultView.documentScriptRan,'YouTube cards preserve escaped title/author while document scripts remain disabled');
    const card=()=>doc().querySelector('figure[data-sin-youtube]'),tool=name=>document.querySelector(`[data-video-tool=${name}]`).click();
    check(card().style.width==='100%'&&card().style.marginLeft==='auto'&&card().querySelector('[data-video-action=play] span').style.left==='50%','New video cards fill the document and center the play button');
    card().querySelector('figcaption').click();await delay();
    check(!document.querySelector('.video-selection').hidden&&document.querySelectorAll('[data-video-resize]').length===2&&!document.querySelector('.youtube-player'),'Clicking the frame selects two side handles without starting playback');
    const beforeResize=card().getBoundingClientRect(),handle=document.querySelector('[data-video-resize=right]'),hr=handle.getBoundingClientRect();
    for(const [type,x] of [['mousePressed',hr.x+hr.width/2],['mouseMoved',hr.x-80],['mouseReleased',hr.x-80]])
      await request('test-mouse',{parameters:{type,x,y:hr.y+hr.height/2,button:'left',buttons:type==='mouseReleased'?0:1,clickCount:1}});
    await delay();const resized=card().getBoundingClientRect();
    check(resized.width<beforeResize.width-50&&Math.abs(resized.x+resized.width/2-beforeResize.x-beforeResize.width/2)<2,'Dragging a video side handle resizes proportionately around its center');
    window.editor.command('undo');await delay();
    check(Math.abs(card().getBoundingClientRect().width-beforeResize.width)<2&&card().contentEditable==='false'&&card().querySelector('img').src===png,'Video resizing is one undoable edit');
    for(const value of ['left','center','right','wrap-left','wrap-right']){
      card().querySelector('figcaption').click();tool('align');document.querySelector(`[data-video-align=${value}]`).click();
      check(card().dataset.sinVideoAlign===value&&(!value.startsWith('wrap-')||card().style.float===value.slice(5)),'Video '+value+' persists its alignment and wrapping');
    }
    card().querySelector('figcaption').click();tool('edit');document.querySelector('dialog [name=title]').value='Renamed <video>';document.querySelector('dialog button[value=ok]').click();await delay();
    check(card().querySelector('[data-video-title]').textContent==='Renamed <video>'&&card().querySelector('img').src===png,'Editing a video title preserves the local thumbnail and escapes text');
    tool('edit');document.querySelector('dialog [name=url]').value='https://youtu.be/ZyxW_987-ab';document.querySelector('dialog button[value=cancel]').click();await delay();
    check(card().dataset.sinYoutube===video.id,'Canceling a video URL edit preserves its identity');
    tool('edit');document.querySelector('dialog [name=url]').value='https://youtu.be/ZyxW_987-ab';document.querySelector('dialog button[value=ok]').click();await delay();
    check(card().dataset.sinYoutube==='ZyxW_987-ab'&&card().querySelector('img').src.includes('/ZyxW_987-ab/'),'Editing the URL updates the video and its thumbnail reference');
    window.editor.command('undo');await delay();card().querySelector('figcaption').click();
    tool('more');tool('copy');await wait(async()=>(await request('editor-paste'))?.html?.includes('data-sin-youtube'));
    tool('more');tool('delete');check(!card(),'Video Delete removes the whole card');
    window.editor.command('undo');await delay();check(card()?.dataset.sinYoutube===video.id,'Undo restores a deleted video');
    const end=doc().createRange();end.selectNodeContents(doc().body.lastElementChild);end.collapse(false);doc().getSelection().removeAllRanges();doc().getSelection().addRange(end);doc().dispatchEvent(new Event('selectionchange'));
    await window.editor.command('paste');
    check(doc().querySelectorAll('figure[data-sin-youtube]').length===2&&doc().querySelectorAll('figure[data-sin-youtube]')[1].querySelector('img').src===png,'Copy and paste add a second video with the original identity, local thumbnail and alignment');
    card().querySelector('figcaption').click();await window.editor.command('cut');
    check(doc().querySelectorAll('figure[data-sin-youtube]').length===1,'Cut removes a selected video after Windows accepts its copy');
    window.editor.command('undo');await delay();check(doc().querySelectorAll('figure[data-sin-youtube]').length===2&&card().querySelector('img').src===png,'Undo restores a cut video with its content intact');
    card().querySelector('figcaption').click();const neighbor=doc().querySelectorAll('figure')[1].outerHTML;await window.editor.command('paste');
    check(doc().querySelectorAll('figure[data-sin-youtube]').length===2&&card().querySelector('img').src===png,'Pasting over a selected video replaces the complete atom');
    check(doc().querySelectorAll('figure')[1].outerHTML===neighbor,'Replacing one wrapped video leaves its neighbor unchanged');
    await window.editor.load(youtubeCard(video));
    doc().querySelector('[data-video-action=play]').click();
    await wait(()=>!!document.querySelector('.youtube-player iframe'));
    const player=document.querySelector('.youtube-player iframe');
    check(player.parentElement.parentElement===document.body&&player.src.startsWith('https://www.youtube.com/embed/AbcD_123-xy?')&&player.allowFullscreen,'Inline playback uses a controlled outer iframe with fullscreen permission');
    check(!window.editor.html().includes('youtube-player')&&!doc().querySelector('iframe'),'Runtime player controls do not enter saved HTML or weaken the document sandbox');
    const caret=doc().createRange();caret.selectNodeContents(doc().body.lastElementChild);caret.collapse(false);doc().getSelection().removeAllRanges();doc().getSelection().addRange(caret);doc().dispatchEvent(new Event('selectionchange'));
    window.editor.command('insertText','Text beside a playing video');await delay();
    check(document.querySelector('.youtube-player iframe')===player&&card().querySelector('img').src===png,'Editing beside a playing video preserves the player and card contents');
    doc().defaultView.scrollBy(0,Math.max(0,card().getBoundingClientRect().bottom-frame.clientHeight+20));await delay();
    await realClick(document.querySelector('.video-controls button'));
    await wait(()=>document.fullscreenElement?.classList.contains('youtube-player'));
    await delay();await delay();
    check(document.querySelector('.youtube-player iframe')===player&&(await request('test-annotation-layout')).expanded,'Video fullscreen preserves the active player while expanding the native host');
    await document.exitFullscreen();await wait(async()=>!(await request('test-annotation-layout')).expanded);
    document.querySelector('.video-controls button:last-child').click();
    check(!document.querySelector('.youtube-player'),'Closing video playback removes the runtime player');
    const persisted=window.editor.html();await window.editor.load(persisted);
    check(doc().querySelector('[data-sin-youtube]').dataset.sinYoutube===video.id&&doc().images[0].src===png,'YouTube metadata and a local thumbnail survive save and reload');
    await window.editor.load('<p>Paste here</p>');window.editor.command('selectAll');
    await request('editor-copy',{html:'<a href="https://youtu.be/AbcD_123-xy">https://youtu.be/AbcD_123-xy</a>',text:'https://youtu.be/AbcD_123-xy'});
    await window.editor.command('paste');
    check(card()?.dataset.sinYoutube===video.id&&card().style.width==='100%','Pasting a YouTube URL from Windows automatically inserts a full-width card');
    window.editor.command('undo');check(!card(),'Undo removes an automatically embedded video');
  }finally{
    if(document.fullscreenElement)await document.exitFullscreen();
    window.editor.setImageStorage('');await window.editor.load(saved);
  }
}
