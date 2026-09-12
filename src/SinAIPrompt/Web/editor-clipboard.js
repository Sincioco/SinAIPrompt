import {native,request} from './bridge.js';
import {pasteSafeHtml,portableHtml,parseHtml} from './document.js';
import {captureFormat} from './text-formatting.js';
import {pasteMarkdown} from './markdown.js';

export async function clipboardCommand(doc,name,{changed,insertImage}) {
  const selection=doc.getSelection();if(!selection?.rangeCount)return;
  const range=selection.getRangeAt(0).cloneRange();
  if(!native){doc.execCommand(name);changed();return;}
  if(name==='copy'||name==='cut'){
    if(range.collapsed)return;
    const wrapper=doc.createElement('div');wrapper.append(range.cloneContents());
    Object.assign(wrapper.style,captureFormat(doc));
    const text=range.toString();
    const html=wrapper.querySelector('img')?parseHtml(await portableHtml(wrapper.outerHTML,doc.baseURI)).body.innerHTML:wrapper.outerHTML;
    await request('editor-copy',{html,text});
    if(name==='copy')return;
    // Cut only after Windows accepted the copy, using the original selection.
  }else{
    const data=await request('editor-paste');if(!data)return;
    if(!range.startContainer.isConnected)return;
    doc.body.focus();selection.removeAllRanges();selection.addRange(range);
    if(await pasteMarkdown(doc,data.markdown??data.text??'',changed,data.base??'',data.markdown!=null))return;
    if(data.html)doc.execCommand('insertHTML',false,pasteSafeHtml(data.html));
    else if(data.image){await insertImage(data.image);return;}
    else if(data.text)doc.execCommand('insertText',false,data.text);
    changed();return;
  }
  if(!range.startContainer.isConnected)return;
  doc.body.focus();selection.removeAllRanges();selection.addRange(range);
  doc.execCommand('delete');changed();
}
