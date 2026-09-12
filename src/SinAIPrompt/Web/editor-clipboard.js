import {native,request} from './bridge.js';
import {pasteSafeHtml,portableHtml,parseHtml} from './document.js';
import {captureFormat} from './text-formatting.js';
import {pasteMarkdown} from './markdown.js';
import {pasteYouTube,editVideoAtoms} from './youtube.js';

export async function clipboardCommand(doc,name,{changed,insertImage}) {
  const selection=doc.getSelection();if(!selection?.rangeCount)return;
  const range=selection.getRangeAt(0).cloneRange();
  if(!native){doc.execCommand(name);changed();return;}
  if(name==='copy'||name==='cut'){
    if(range.collapsed)return;
    const wrapper=doc.createElement('div');wrapper.append(range.cloneContents());
    wrapper.querySelectorAll('[data-sin-selected]').forEach(el=>el.removeAttribute('data-sin-selected'));
    Object.assign(wrapper.style,captureFormat(doc));
    const text=range.toString();
    const hasImages=!!wrapper.querySelector('img');
    const video=wrapper.childNodes.length===1&&wrapper.firstElementChild?.matches('figure[data-sin-youtube]');
    const fragment=video?wrapper.firstElementChild.outerHTML+'<p><br></p>':wrapper.outerHTML;
    const html=hasImages?parseHtml(await portableHtml(fragment,doc.baseURI,true)).body.innerHTML:fragment;
    await request('editor-copy',{html,text,internalHtml:hasImages?fragment:null});
    if(name==='copy')return;
    // Cut only after Windows accepted the copy, using the original selection.
  }else{
    const data=await request('editor-paste');if(!data)return;
    if(!range.startContainer.isConnected)return;
    doc.body.focus();selection.removeAllRanges();selection.addRange(range);
    if(!data.image&&pasteYouTube(doc,data.text,changed))return;
    if(await pasteMarkdown(doc,data.markdown??data.text??'',changed,data.base??'',data.markdown!=null))return;
    if(data.html){
      const markup=pasteSafeHtml(data.html),fragment=parseHtml(markup);
      const image=fragment.images.length===1?fragment.images[0]:null;
      // Photos supplies file:// image HTML. Read and store its pixels through the
      // same native path as other image pastes; the sandbox cannot display that URL.
      if(!data.preserveImageMarkup&&image?.getAttribute('src')&&!fragment.body.textContent.trim()){
        await insertImage(image.getAttribute('src'));return;
      }
      editVideoAtoms(doc,()=>doc.execCommand('insertHTML',false,markup));
    }
    else if(data.image){await insertImage(data.image);return;}
    else if(data.text)editVideoAtoms(doc,()=>doc.execCommand('insertText',false,data.text));
    changed();return;
  }
  if(!range.startContainer.isConnected)return;
  doc.body.focus();selection.removeAllRanges();selection.addRange(range);
  editVideoAtoms(doc,()=>doc.execCommand('delete'));changed();
}
