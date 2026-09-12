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
    check([...document.querySelectorAll('[data-image-action]')].map(b=>b.textContent).join('|')==='Edit Image|Resize Image|View Full Screen|View Externally'&&!document.querySelector('dialog[open]'),'Image actions are a four-option dropdown, not a dialog');
    await realClick(document.querySelector('[data-image-action=fullscreen]'));
    await wait(()=>document.fullscreenElement?.classList.contains('media-fullscreen'));
    const layout=await request('test-annotation-layout');
    check(layout.expanded&&!layout.backgroundEnabled&&document.querySelector('.media-fullscreen img').src.startsWith('https://sin-document.local/'),'Image fullscreen expands the native host while preserving its source');
    await document.exitFullscreen();
    await wait(async()=>!(await request('test-annotation-layout')).expanded);
    check(!document.querySelector('.media-fullscreen'),'Leaving fullscreen removes the temporary viewer and restores the editor');
    const video={id:'AbcD_123-xy',title:'Title & <test>',author:'Example channel',image:png};
    await window.editor.load(youtubeCard(video)+'<script>window.documentScriptRan=true</script>');
    check(doc().querySelector('figcaption strong').textContent===video.title&&doc().querySelector('figcaption span').textContent===video.author&&!doc().defaultView.documentScriptRan,'YouTube cards preserve escaped title/author while document scripts remain disabled');
    doc().querySelector('[data-video-action=play]').click();
    await wait(()=>!!document.querySelector('.youtube-player iframe'));
    const player=document.querySelector('.youtube-player iframe');
    check(player.parentElement.parentElement===document.body&&player.src.startsWith('https://www.youtube.com/embed/AbcD_123-xy?')&&player.allowFullscreen,'Inline playback uses a controlled outer iframe with fullscreen permission');
    check(!window.editor.html().includes('youtube-player')&&!doc().querySelector('iframe'),'Runtime player controls do not enter saved HTML or weaken the document sandbox');
    await realClick(document.querySelector('.video-controls button'));
    await wait(()=>document.fullscreenElement?.classList.contains('youtube-player'));
    await delay();await delay();
    check(document.querySelector('.youtube-player iframe')===player&&(await request('test-annotation-layout')).expanded,'Video fullscreen preserves the active player while expanding the native host');
    await document.exitFullscreen();await wait(async()=>!(await request('test-annotation-layout')).expanded);
    document.querySelector('.video-controls button:last-child').click();
    check(!document.querySelector('.youtube-player'),'Closing video playback removes the runtime player');
    const persisted=window.editor.html();await window.editor.load(persisted);
    check(doc().querySelector('[data-sin-youtube]').dataset.sinYoutube===video.id&&doc().images[0].src===png,'YouTube metadata and a local thumbnail survive save and reload');
  }finally{
    if(document.fullscreenElement)await document.exitFullscreen();
    window.editor.setImageStorage('');await window.editor.load(saved);
  }
}
