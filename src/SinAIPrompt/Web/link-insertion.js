import {ask,escapeHtml,request,report} from './bridge.js';
import {youtubeId,youtubeCard} from './youtube.js';

export async function insertLink({getDocument,saveSelection,command}){
  saveSelection();const doc=getDocument(),selected=doc.getSelection().toString();
  let preview=null,video=null;
  const answer=await ask('Insert Link',`
    <label class="stacked-field">Address <input name="url" type="url" required autofocus placeholder="https://…"></label>
    <label><input name="urlOnly" type="checkbox"> URL only, no thumbnail</label>
    <div data-youtube-options hidden><label><input name="embedVideo" type="checkbox" checked> Embed YouTube video</label>
      <label><input name="saveThumbnail" type="checkbox"> Save the YouTube thumbnail locally in this document</label></div>
    <label class="stacked-field">Name (optional) <input name="name" placeholder="${escapeHtml(selected||'Use the page title or address')}"></label>
    <p class="hint">YouTube cards include playback options and video information. Other available thumbnails are saved inside this document.</p>
    <div data-link-progress hidden role="status"><progress aria-label="Retrieving thumbnail"></progress> Retrieving thumbnail…</div>`,
    [{value:'ok',label:'Insert Link'}],dialog=>{
      const form=dialog.querySelector('form'),address=form.elements.url;
      address.oninput=()=>{address.setCustomValidity('');dialog.querySelector('[data-youtube-options]').hidden=!youtubeId(address.value);};
      form.elements.urlOnly.onchange=()=>{for(const input of dialog.querySelectorAll('[data-youtube-options] input'))input.disabled=form.elements.urlOnly.checked;};
      form.addEventListener('submit',async event=>{
        if(event.submitter?.value!=='ok')return;
        let url;try{url=new URL(address.value);}catch{return;}
        if(!['http:','https:','mailto:','tel:','ftp:','file:'].includes(url.protocol)){
          event.preventDefault();address.setCustomValidity('Enter a web, email, telephone or file address.');address.reportValidity();return;
        }
        if(form.elements.urlOnly.checked||!['http:','https:'].includes(url.protocol))return;
        event.preventDefault();dialog.querySelector('[data-link-progress]').hidden=false;
        const submit=dialog.querySelector('[value=ok]');submit.disabled=true;
        // Cancel remains available. A late result cannot insert into a closed dialog.
        for(const input of form.querySelectorAll('input'))input.readOnly=true;
        try{
          if(youtubeId(url.href)){
            video=await request('youtube-preview',{url:url.href,saveThumbnail:form.elements.saveThumbnail.checked});
            if(!video)video={id:youtubeId(url.href),title:'YouTube video',author:'YouTube',image:`https://i.ytimg.com/vi/${youtubeId(url.href)}/hqdefault.jpg`};
            preview=video;
          }else preview=await request('link-preview',{url:url.href});
        }catch(error){
          if(youtubeId(url.href)){report(error);submit.disabled=false;dialog.querySelector('[data-link-progress]').hidden=true;for(const input of form.querySelectorAll('input'))input.readOnly=false;return;}
          preview=null;
        }
        if(dialog.open)dialog.close('ok');
      });
    });
  if(answer.choice!=='ok'||doc!==getDocument())return;
  const url=answer.values.url.trim(),name=answer.values.name.trim();
  if(video&&answer.values.embedVideo)command('insertHTML',youtubeCard(video,name||selected));
  else if(!preview?.image&&!name&&selected)command('createLink',url);
  else{
    const label=escapeHtml(name||selected||preview?.title||url),href=escapeHtml(url);
    const image=preview?.image?`<img src="${escapeHtml(preview.image)}" ${preview.image.startsWith('data:')?'data-sin-storage="inline"':''} alt="${label}" style="width:480px;max-width:100%;height:auto"><br>`:'';
    command('insertHTML',`<a href="${href}">${image}${label}</a>`);
  }
  if(!answer.values.urlOnly&&!preview&&/^https?:/i.test(url))report('No thumbnail was available. The text link was inserted.');
}
