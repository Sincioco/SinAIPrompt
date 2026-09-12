import {escapeHtml,request,report} from './bridge.js';

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
  return `<figure data-sin-youtube="${video.id}" contenteditable="false" style="width:480px;max-width:100%;margin:1em 0;border:1px solid #d1d9e0;border-radius:6px;overflow:hidden;background:#fff;color:#1f2328">
    <a data-video-surface href="${href}" data-video-action="play" aria-label="Play ${title}" style="display:block;aspect-ratio:16/9;min-height:200px;background:#111"><img src="${escapeHtml(video.image)}" ${video.image.startsWith('data:')?'data-sin-storage="inline"':''} alt="${title}" style="display:block;width:100%;height:100%;object-fit:contain"></a>
    <div style="display:flex;gap:16px;align-items:center;min-height:32px;padding:0 10px;font:13px/1.5 'Segoe UI',sans-serif"><a href="${href}" data-video-action="play">▶ Play</a><a href="${href}" data-video-action="fullscreen">Full Screen</a><a href="${href}" data-video-action="external">Open In Browser</a></div>
    <figcaption style="padding:10px;font:14px/1.5 'Segoe UI',sans-serif"><strong>${title}</strong><br><span>${escapeHtml(video.author||'YouTube')}</span></figcaption></figure><p><br></p>`;
}

// Only this trusted outer-page owner creates players. The editable iframe keeps
// its no-scripts sandbox; persisted cards contain metadata and ordinary links.
export function createYouTubePlayer(frame){
  let card=null,player=null,observer=null;
  function close(){observer?.disconnect();observer=null;player?.remove();player=null;card=null;}
  function position(){
    if(!card?.isConnected){close();return;}
    if(document.fullscreenElement)return;
    const rect=card.querySelector('[data-video-surface]').getBoundingClientRect(),host=frame.getBoundingClientRect();
    Object.assign(player.style,{left:host.left+rect.left+'px',top:host.top+rect.top+'px',width:rect.width+'px',height:rect.height+32+'px',
      clipPath:`inset(${Math.max(0,-rect.top)}px 0 ${Math.max(0,rect.bottom+32-host.height)}px 0)`});
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
      else if(action)play(target,action==='fullscreen');
    });
    doc.addEventListener('dblclick',event=>{if(event.target.closest('figure[data-sin-youtube]'))event.stopImmediatePropagation();});
    doc.defaultView.addEventListener('scroll',()=>{if(card)position();},{passive:true});
    doc.defaultView.addEventListener('resize',()=>{if(card)position();});
    doc.addEventListener('input',()=>{if(card)position();});
  }
  document.addEventListener('fullscreenchange',()=>{if(card)position();});
  return {attach,close};
}
