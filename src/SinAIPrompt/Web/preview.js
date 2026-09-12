import {attachFileDrop} from './file-drop.js';
import {send} from './bridge.js';

// Only this local shell runs scripts. The document remains sandboxed and read-only.
const frame=document.querySelector('#preview');
attachFileDrop(document);
frame.addEventListener('load',()=>attachFileDrop(frame.contentDocument));
window.chrome.webview.addEventListener('message',({data})=>{if(typeof data.html==='string')frame.srcdoc=data.html;});
send('preview-ready');
