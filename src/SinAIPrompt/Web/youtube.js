import {ask,escapeHtml,request,report} from './bridge.js';

export function youtubeId(address){
  try{
    const url=new URL(address);if(!['https:','http:'].includes(url.protocol)||url.username||url.password)return null;
    const parts=url.pathname.split('/').filter(Boolean);let id='';
    if(['youtu.be','www.youtu.be'].includes(url.hostname))id=parts[0];
    else if(['youtube.com','www.youtube.com','m.youtube.com','youtube-nocookie.com','www.youtube-nocookie.com'].includes(url.hostname))
      id=['shorts','embed','live'].includes(parts[0])?parts[1]:url.searchParams.get('v');
    return /^[\w-]{11}$/.test(id||'')?id:null;
  }catch{return null;}
}

export function youtubeCard(video,name=''){
  if(!/^[\w-]{11}$/.test(video.id))throw Error('Invalid YouTube video.');
  const href=`https://www.youtube.com/watch?v=${video.id}`,title=escapeHtml(name||video.title||'YouTube video');
  return `<figure data-sin-youtube="${video.id}" data-sin-video-align="center" contenteditable="false" style="box-sizing:border-box;width:100%;max-width:100%;margin:1em auto;padding:8px;border:1px solid #d1d9e0;border-radius:12px;background:#fff;color:#1f2328">
    <figcaption title="Select video frame" style="cursor:default;padding:0 0 8px;font:14px/1.5 'Segoe UI',sans-serif;color:#065fd4"><strong>▶ <span data-video-title>${title}</span></strong><span data-video-author style="display:none">${escapeHtml(video.author||'YouTube')}</span></figcaption>
    <div data-video-surface style="position:relative;aspect-ratio:16/9;min-height:200px;border-radius:6px;overflow:hidden;background:#111">
      <a href="${href}" data-video-action="play" aria-label="Play ${title}" style="display:block;width:100%;height:100%;color:white"><img src="${escapeHtml(video.image)}" ${video.image.startsWith('data:')?'data-sin-storage="inline"':''} alt="${title}" style="display:block;width:100%;height:100%;object-fit:cover"><span style="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:68px;height:48px;border-radius:14px;background:#f03;color:#fff;font:28px/48px Arial;text-align:center">▶</span></a>
      <div style="position:absolute;bottom:8px;left:10px;right:10px;display:flex;gap:12px;align-items:center;font:14px/28px 'Segoe UI',sans-serif"><a href="${href}" data-video-action="share" title="Share video link" style="color:white;text-decoration:none;background:#000a;border-radius:20px;padding:2px 10px">↗ Share</a><a href="${href}" data-video-action="external" title="Save to Watch Later on YouTube" style="color:white;text-decoration:none;background:#000a;border-radius:20px;padding:2px 10px">◷</a><a href="${href}" data-video-action="external" style="margin-left:auto;color:white;text-decoration:none;background:#000a;border-radius:20px;padding:2px 12px">Watch on YouTube</a></div>
    </div></figure><p><br></p>`;
}

export function pasteYouTube(doc,text,changed){
  const id=youtubeId(text?.trim());if(!id||/\s/.test(text.trim()))return false;
  const video={id,title:'YouTube video',author:'YouTube',image:`https://i.ytimg.com/vi/${id}/hqdefault.jpg`};
  editVideoAtoms(doc,()=>doc.execCommand('insertHTML',false,youtubeCard(video)));changed();
  // Insert immediately; metadata cannot delay typing or move the current caret.
  const cards=[...doc.querySelectorAll('figure[data-sin-youtube]')];
  const selection=doc.getSelection(),card=cards.findLast(node=>node.compareDocumentPosition(selection.anchorNode)&Node.DOCUMENT_POSITION_FOLLOWING);
  const original=card?.outerHTML;
  request('youtube-preview',{url:text.trim(),saveThumbnail:false}).then(preview=>{
    if(!preview||doc.defaultView?.frameElement?.contentDocument!==doc||!card?.isConnected||card.outerHTML!==original)return;
    card.querySelector('[data-video-title]').textContent=preview.title;
    card.querySelector('[data-video-author]').textContent=preview.author;
    card.querySelector('img').alt=preview.title;
    card.querySelector('[data-video-action=play]').setAttribute('aria-label','Play '+preview.title);changed();
  }).catch(()=>{});
  return true;
}

// Chromium disables native replacement when it descends into a populated,
// noneditable figure. Keep video atoms empty during the synchronous command, then
// restore their children on the original nodes retained by native Undo.
export function editVideoAtoms(doc,action){
  const saved=[];
  // Adjacent floating cards must also remain atomic during Chromium's paragraph
  // merge; otherwise it can absorb the neighboring figure during replacement.
  for(const card of doc.querySelectorAll('figure[data-sin-youtube]')){
    const contents=doc.createDocumentFragment();while(card.firstChild)contents.append(card.firstChild);
    const style=card.getAttribute('style');
    card.setAttribute('style','display:inline-block;float:none;width:1px;height:1px;margin:0;padding:0;border:0');
    saved.push({card,contents,style});
  }
  try{return action();}
  finally{
    for(const {card,contents,style} of saved){
      card.append(contents);
      if(style==null)card.removeAttribute('style');else card.setAttribute('style',style);
    }
  }
}

// Only this trusted outer-page owner creates players. The editable iframe keeps
// its no-scripts sandbox; persisted cards contain metadata and ordinary links.
export function createYouTubePlayer(frame,onSelect){
  let card=null,player=null,observer=null;
  function close(){observer?.disconnect();observer=null;player?.remove();player=null;card=null;}
  function position(){
    if(!card?.isConnected){close();return;}
    if(document.fullscreenElement)return;
    const surface=card.querySelector('[data-video-surface]');if(!surface)return;
    const rect=surface.getBoundingClientRect(),host=frame.getBoundingClientRect();
    Object.assign(player.style,{left:host.left+rect.left+'px',top:host.top+rect.top+'px',width:rect.width+'px',height:rect.height+'px',
      clipPath:`inset(${Math.max(0,-rect.top)}px 0 ${Math.max(0,rect.bottom-host.height)}px 0)`});
  }
  function play(target,fullscreen=false){
    const id=target.dataset.sinYoutube;if(!/^[\w-]{11}$/.test(id)||!target.querySelector('[data-video-surface]'))return;
    if(card!==target){
      close();card=target;target.scrollIntoView({block:'nearest'});
      player=document.createElement('div');player.className='youtube-player';
      const video=document.createElement('iframe');video.title='YouTube video player';video.allowFullscreen=true;
      video.allow='autoplay; encrypted-media; picture-in-picture; fullscreen';video.referrerPolicy='strict-origin-when-cross-origin';
      video.src=`https://www.youtube.com/embed/${id}?autoplay=1&playsinline=1&origin=${encodeURIComponent(location.origin)}`;
      const controls=document.createElement('div');controls.className='video-controls';
      for(const [label,action] of [['Full Screen',()=>player.requestFullscreen().catch(report)],['Open In Browser',()=>request('open-video',{video:id}).catch(report)],['Close Video',()=>{if(document.fullscreenElement)document.exitFullscreen().then(close);else close();}]]){
        const button=document.createElement('button');button.textContent=label;button.onclick=action;controls.append(button);
      }
      player.append(video,controls);document.body.append(player);observer=new ResizeObserver(position);observer.observe(target);position();
    }
    if(fullscreen)player.requestFullscreen().catch(report);
  }
  function attach(doc){
    close();
    doc.addEventListener('click',event=>{
      const target=event.target.closest('figure[data-sin-youtube]');if(!target||!/^[-\w]{11}$/.test(target.dataset.sinYoutube))return;
      event.preventDefault();event.stopImmediatePropagation();
      const action=event.target.closest('[data-video-action]')?.dataset.videoAction;
      if(action==='external')request('open-video',{video:target.dataset.sinYoutube}).catch(report);
      else if(action==='share')shareVideo(target.dataset.sinYoutube).catch(report);
      else if(action){onSelect(null);play(target,action==='fullscreen');}
      else onSelect(target);
    });
    doc.addEventListener('dblclick',event=>{if(event.target.closest('figure[data-sin-youtube]'))event.stopImmediatePropagation();});
    doc.defaultView.addEventListener('scroll',()=>{if(card)position();},{passive:true});
    doc.defaultView.addEventListener('resize',()=>{if(card)position();});
    doc.addEventListener('input',()=>requestAnimationFrame(()=>{if(card)position();}));
  }
  document.addEventListener('fullscreenchange',()=>{if(card)position();});
  return {attach,close};
}

async function shareVideo(id){
  const url=`https://www.youtube.com/watch?v=${id}`;
  const answer=await ask('Share YouTube video',`<label class="stacked-field">Video link <input name="url" readonly value="${url}"></label>`,[{value:'copy',label:'Copy Link'}]);
  if(answer.choice==='copy')await request('editor-copy',{html:`<a href="${url}">${url}</a>`,text:url});
}
