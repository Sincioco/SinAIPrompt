import {ask,escapeHtml,request,report} from './bridge.js';

export async function insertLink({getDocument,saveSelection,command}){
  saveSelection();const doc=getDocument(),selected=doc.getSelection().toString();
  let preview=null;
  const answer=await ask('Insert Link',`
    <label class="stacked-field">Address <input name="url" type="url" required autofocus placeholder="https://…"></label>
    <label><input name="urlOnly" type="checkbox"> URL only, no thumbnail</label>
    <label class="stacked-field">Name (optional) <input name="name" placeholder="${escapeHtml(selected||'Use the page title or address')}"></label>
    <p class="hint">Available thumbnails are saved inside this document. If none is available, the address is inserted as a text link.</p>
    <div data-link-progress hidden role="status"><progress aria-label="Retrieving thumbnail"></progress> Retrieving thumbnail…</div>`,
    [{value:'ok',label:'Insert Link'}],dialog=>{
      const form=dialog.querySelector('form'),address=form.elements.url;
      address.oninput=()=>address.setCustomValidity('');
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
        try{preview=await request('link-preview',{url:url.href});}catch{preview=null;}
        if(dialog.open)dialog.close('ok');
      });
    });
  if(answer.choice!=='ok'||doc!==getDocument())return;
  const url=answer.values.url.trim(),name=answer.values.name.trim();
  if(!preview?.image&&!name&&selected)command('createLink',url);
  else{
    const label=escapeHtml(name||selected||preview?.title||url),href=escapeHtml(url);
    const image=preview?.image?`<img src="${escapeHtml(preview.image)}" data-sin-storage="inline" alt="${label}" style="width:480px;max-width:100%;height:auto"><br>`:'';
    command('insertHTML',`<a href="${href}">${image}${label}</a>`);
  }
  if(!answer.values.urlOnly&&!preview&&/^https?:/i.test(url))report('No thumbnail was available. The text link was inserted.');
}
