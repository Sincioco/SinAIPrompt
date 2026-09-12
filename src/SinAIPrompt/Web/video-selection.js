import {ask,escapeHtml,request,report} from './bridge.js';
import {youtubeId,editVideoAtoms} from './youtube.js';
import {videoMode,videoData,videoMarkup,videoIcon} from './video-presentation.js';

// Owns one editor's video selection and gestures. All chrome stays outside saved HTML.
export function createVideoSelection(frame,{changed,command,saveSelection,stopPlayback,clearImage}){
  const overlay=document.createElement('div');overlay.className='video-selection';overlay.hidden=true;
  overlay.innerHTML=`<button data-video-resize="left" aria-label="Resize video left"></button><button data-video-resize="right" aria-label="Resize video right"></button>
    <div class="video-toolbar" role="toolbar" aria-label="Video options">
      <button data-video-tool="mode" title="Video display mode">${videoIcon('mode')}<span data-mode-label>Embed</span>⌄</button><button data-video-tool="align" title="Video alignment" aria-label="Video alignment">${videoIcon('align')}⌄</button>
      <button data-video-tool="edit" title="Edit URL and Title" aria-label="Edit video">${videoIcon('edit')}</button>
      <button data-video-tool="unlink" title="Unlink" aria-label="Unlink">${videoIcon('unlink')}</button>
      <button data-video-tool="external" title="Open on YouTube" aria-label="Open video on YouTube">${videoIcon('external')}</button>
      <button data-video-tool="copy-url" title="Copy URL" aria-label="Copy URL">${videoIcon('copy')}</button>
      <button data-video-tool="more" title="More video options" aria-label="More video options">${videoIcon('more')}</button>
      <div class="video-menu" data-video-menu="mode" hidden>${['URL','Inline','Card','Embed'].map(mode=>`<button data-video-mode="${mode.toLowerCase()}">${mode}</button>`).join('')}</div>
      <div class="video-menu" data-video-menu="align" hidden>${['Align Left','Align Center','Align Right','Wrap Left','Wrap Right'].map((label,i)=>`<button data-video-align="${['left','center','right','wrap-left','wrap-right'][i]}">${label}</button>`).join('')}</div>
      <div class="video-menu" data-video-menu="more" hidden><button data-video-tool="copy">Copy</button><button data-video-tool="delete">Delete</button></div>
    </div>`;
  document.body.append(overlay);
  let card=null,observer=null,gesture=null,shield=null;
  const toolbar=overlay.querySelector('.video-toolbar');
  function hideMenus(){overlay.querySelectorAll('[data-video-menu]').forEach(menu=>menu.hidden=true);}
  function position(){
    if(!card?.isConnected){select(null);return;}
    const r=card.getBoundingClientRect(),f=frame.getBoundingClientRect();
    overlay.hidden=r.bottom<0||r.top>f.height||!!document.fullscreenElement;
    Object.assign(overlay.style,{left:f.left+r.left+'px',top:f.top+r.top+'px',width:r.width+'px',height:r.height+'px',
      clipPath:`inset(${Math.max(-6,-r.top)}px -8px ${Math.min(-50,r.bottom-f.height)}px -8px)`});
    toolbar.style.top=Math.min(r.height+6,f.height-r.top-44)+'px';
    toolbar.style.left=Math.max(0,Math.min(r.width/2-145,innerWidth-f.left-r.left-300))+'px';
  }
  function select(next,preserveCaret=false){
    if(gesture)cancelDrag();
    observer?.disconnect();observer=null;card=next;overlay.hidden=!card;hideMenus();
    if(!card)return;
    clearImage();if(!preserveCaret)selectRange();
    const mode=videoMode(card),locked=!card.ownerDocument.body.isContentEditable;
    toolbar.querySelector('[data-mode-label]').textContent=mode==='url'?'URL':mode[0].toUpperCase()+mode.slice(1);
    for(const action of ['align','more','copy-url','unlink'])toolbar.querySelector(`[data-video-tool=${action}]`).hidden=action==='align'?['url','inline'].includes(mode):action==='more'?mode==='url':action==='copy-url'?mode!=='url':!['url','inline'].includes(mode);
    for(const action of ['mode','edit','unlink','align','delete'])toolbar.querySelector(`[data-video-tool=${action}]`).disabled=locked;
    overlay.querySelectorAll('[data-video-resize]').forEach(handle=>handle.hidden=locked||['url','inline'].includes(mode));
    observer=new ResizeObserver(position);observer.observe(card);position();
  }
  function selectRange(){
    if(!card?.isConnected)return;
    const doc=card.ownerDocument,range=doc.createRange();range.selectNode(card);
    doc.body.focus();doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);saveSelection();
  }
  function ownsSelection(){
    if(!card?.isConnected)return false;
    const selection=card.ownerDocument.getSelection();if(!selection?.rangeCount)return false;
    const range=selection.getRangeAt(0),node=card.ownerDocument.createRange();node.selectNode(card);
    if(videoMode(card)==='url'&&card.contains(selection.anchorNode))return true;
    return range.compareBoundaryPoints(Range.START_TO_START,node)===0&&range.compareBoundaryPoints(Range.END_TO_END,node)===0;
  }
  function replace(markup){
    if(!card?.isConnected)return;
    if(!card.ownerDocument.body.isContentEditable)return;
    const doc=card.ownerDocument,all=[...doc.querySelectorAll('[data-sin-youtube],a[href]')].filter(node=>node.matches('[data-sin-youtube]')||!node.closest('figure')&&youtubeId(node.href)),index=all.indexOf(card);
    editCard(()=>doc.execCommand('insertHTML',false,markup+'<p><br></p>'));changed();
    select([...doc.querySelectorAll('[data-sin-youtube],a[href]')].filter(node=>node.matches('[data-sin-youtube]')||!node.closest('figure')&&youtubeId(node.href))[index]||null);
  }
  function editCard(action){
    stopPlayback();selectRange();editVideoAtoms(card.ownerDocument,action);
  }
  function remove(){if(!card.ownerDocument.body.isContentEditable)return;editCard(()=>card.ownerDocument.execCommand('delete'));changed();select(null);}
  function align(value){
    const copy=card.cloneNode(true);copy.dataset.sinVideoAlign=value;
    const wrap=value.startsWith('wrap-'),side=value.replace('wrap-','');
    Object.assign(copy.style,{float:wrap?side:'none',marginTop:'1em',marginBottom:'1em',
      marginLeft:side==='left'?'0':'auto',marginRight:side==='right'?'0':'auto'});
    if(wrap){copy.style.width=Math.min(card.getBoundingClientRect().width,availableWidth()*.6)+'px';copy.style[side==='left'?'marginRight':'marginLeft']='1em';}
    replace(copy.outerHTML);
  }
  function availableWidth(){
    const parent=card.parentElement,css=card.ownerDocument.defaultView.getComputedStyle(parent);
    return Math.max(200,parent.clientWidth-parseFloat(css.paddingLeft)-parseFloat(css.paddingRight));
  }
  async function edit(){
    const target=card,old=videoData(target),oldId=old.id,title=old.title;
    stopPlayback();
    const answer=await ask('Edit YouTube video',`<label class="stacked-field">YouTube URL <input name="url" type="url" required value="https://www.youtube.com/watch?v=${oldId}"></label><label class="stacked-field">Title <input name="title" value="${escapeHtml(title)}"></label>`,[{value:'ok',label:'Save'}],dialog=>{
      const input=dialog.querySelector('[name=url]');
      input.oninput=()=>input.setCustomValidity(youtubeId(input.value)?'':'Enter a valid YouTube video URL.');
    });
    if(answer.choice!=='ok'||!target.isConnected)return;
    const id=youtubeId(answer.values.url);if(!id)return;
    const same=id===oldId,video={id,title:answer.values.title.trim()||'YouTube video',author:same?old.author:'YouTube',image:same?old.image:`https://i.ytimg.com/vi/${id}/hqdefault.jpg`};
    const template=document.createElement('template');template.innerHTML=videoMarkup(video,videoMode(target));
    const copy=template.content.firstElementChild;
    for(const property of ['width','float','marginLeft','marginRight'])copy.style[property]=target.style[property];
    copy.dataset.sinVideoAlign=target.dataset.sinVideoAlign||'center';
    card=target;replace(copy.outerHTML);
  }
  overlay.addEventListener('mousedown',event=>event.preventDefault());
  overlay.addEventListener('click',event=>{
    if(!card)return;
    const button=event.target.closest('button');if(!button)return;
    const action=button.dataset.videoTool,alignment=button.dataset.videoAlign;
    if(button.dataset.videoMode){const video=videoData(card);replace(videoMarkup(video,button.dataset.videoMode));return;}
    if(alignment){align(alignment);return;}
    if(action==='align'||action==='more'||action==='mode'){
      const menu=overlay.querySelector(`[data-video-menu=${action}]`),open=menu.hidden;hideMenus();menu.hidden=!open;return;
    }
    hideMenus();
    if(action==='edit')edit().catch(report);
    if(action==='external')request('open-video',{video:videoData(card).id}).catch(report);
    if(action==='copy-url'){const url=`https://www.youtube.com/watch?v=${videoData(card).id}`;request('editor-copy',{html:escapeHtml(url),text:url}).catch(report);}
    if(action==='unlink'){editCard(()=>card.ownerDocument.execCommand('unlink'));changed();select(null);}
    if(action==='copy'){selectRange();Promise.resolve(command('copy')).catch(report);}
    if(action==='delete')remove();
  });
  overlay.addEventListener('pointerdown',event=>{
    if(event.button!==0||!card||!event.target.dataset.videoResize)return;
    event.preventDefault();hideMenus();stopPlayback();
    gesture={x:event.clientX,width:card.getBoundingClientRect().width,style:card.getAttribute('style'),side:event.target.dataset.videoResize,max:availableWidth()};
    shield=document.createElement('div');shield.className='image-resize-shield';shield.style.cursor='ew-resize';
    document.body.append(shield);shield.setPointerCapture(event.pointerId);
  });
  document.addEventListener('pointermove',event=>{
    if(!gesture||!card)return;
    const centered=(card.dataset.sinVideoAlign||'center')==='center',dx=(event.clientX-gesture.x)*(gesture.side==='left'?-1:1);
    card.style.width=Math.min(gesture.max,Math.max(Math.min(374,gesture.max),gesture.width+dx*(centered?2:1)))+'px';position();
  });
  function cancelDrag(){if(card?.isConnected&&gesture)card.setAttribute('style',gesture.style||'');gesture=null;shield?.remove();shield=null;}
  document.addEventListener('pointerup',()=>{
    if(!gesture||!card)return;
    const markup=card.outerHTML;cancelDrag();replace(markup);
  });
  document.addEventListener('pointercancel',()=>{cancelDrag();if(card)position();});
  document.addEventListener('fullscreenchange',()=>{if(card)position();});
  document.addEventListener('pointerdown',event=>{if(!overlay.contains(event.target)&&!event.target.closest('dialog'))hideMenus();});
  function attach(doc){
    select(null);
    doc.addEventListener('pointerdown',event=>{if(!event.target.closest('figure[data-sin-youtube]'))select(null);});
    doc.addEventListener('keydown',event=>{
      if(!card)return;
      if(!ownsSelection()){select(null);return;}
      if(event.key==='Escape'){select(null);return;}
      if(['Delete','Backspace'].includes(event.key)){event.preventDefault();remove();}
      else if(event.key.startsWith('Arrow'))select(null);
    });
    doc.addEventListener('selectionchange',()=>{if(card&&!gesture&&!ownsSelection())select(null);});
    function linkAt(node){const element=node?.nodeType===1?node:node?.parentElement,link=element?.closest('a[href]');return link&&!link.closest('figure')&&youtubeId(link.href)?link:null;}
    doc.addEventListener('click',event=>{const link=linkAt(event.target);if(link){event.preventDefault();event.stopImmediatePropagation();select(link,videoMode(link)==='url');}});
    doc.addEventListener('selectionchange',()=>{const link=linkAt(doc.getSelection()?.anchorNode);if(link&&link!==card)select(link,true);});
    doc.addEventListener('input',()=>requestAnimationFrame(()=>{if(card&&!gesture)position();}));
    doc.defaultView.addEventListener('scroll',()=>{if(card)position();},{passive:true});
    doc.defaultView.addEventListener('resize',()=>{if(card)position();});
  }
  return {select,attach};
}
