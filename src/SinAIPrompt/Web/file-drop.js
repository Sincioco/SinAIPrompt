import {native} from './bridge.js';

// WebView owns drops over its surface; forward actual Windows file objects to the host.
export function attachFileDrop(target){
  target.addEventListener('dragover',event=>{if(native&&event.dataTransfer.types.includes('Files'))event.preventDefault();},true);
  target.addEventListener('drop',event=>{
    if(!native)return;
    const files=[...event.dataTransfer.files].filter(file=>/\.(html?|md)$/i.test(file.name));
    if(!files.length)return;
    event.preventDefault();event.stopImmediatePropagation();
    window.chrome.webview.postMessageWithAdditionalObjects({type:'open-files'},files);
  },true);
}
