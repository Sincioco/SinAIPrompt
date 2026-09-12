export const native = !!window.chrome?.webview;
const requests = new Map();
if (native) window.chrome.webview.addEventListener('message', ({data}) => {
  const pending = requests.get(data.id);
  if (!pending) return;
  requests.delete(data.id);
  data.error ? pending.reject(new Error(data.error)) : pending.resolve(data.result);
});
export function send(type, data = {}) { window.chrome?.webview?.postMessage({type, ...data}); }
export function request(type, data = {}) {
  if (!native) {
    if (type === 'screen-capture') return Promise.reject(new Error('Screen Capture is available in the desktop application.'));
    if (type === 'annotation-mode') return Promise.resolve();
    if (type === 'annotation-copy' || type === 'annotation-paste') return Promise.reject(new Error('Object clipboard is available in the desktop application.'));
    if (type === 'templates-load') return Promise.resolve(JSON.parse(localStorage.getItem('sin.templates') || '[]'));
    if (type === 'templates-save') { localStorage.setItem('sin.templates', JSON.stringify(data.templates)); return Promise.resolve(); }
    if (type === 'save-image') return Promise.resolve(data.data);
    if (type === 'export-template') { download('Object.pmt-template.json', data.contents, 'application/json'); return Promise.resolve(); }
    if (type === 'read-image') return fetch(data.source).then(r => { if (!r.ok) throw Error('Image unavailable: ' + data.source); return r.blob(); }).then(blobData);
  }
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => { requests.set(id, {resolve, reject}); send(type, {...data, id}); });
}
export function blobData(blob) { return new Promise((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.onerror = reject; reader.readAsDataURL(blob); }); }
export function download(name, contents, type) { const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([contents], {type})); a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 1000); }
export const escapeHtml = text => String(text).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
export function report(error) { const bar = document.querySelector('#notice'); bar.textContent = error?.message || String(error); bar.hidden = false; }
export function ask(title, contents, buttons = [{value:'ok', label:'Apply'}]) {
  return new Promise(resolve => {
    const dialog = document.createElement('dialog'); dialog.className = 'form-dialog';
    dialog.innerHTML = `<form method="dialog"><header><h2>${escapeHtml(title)}</h2></header><div class="dialog-content">${contents}</div><footer><button value="cancel" formnovalidate>Cancel</button>${buttons.map(b => `<button class="primary" value="${b.value}">${escapeHtml(b.label)}</button>`).join('')}</footer></form>`;
    document.body.append(dialog); dialog.showModal();
    dialog.addEventListener('close', () => { const values = Object.fromEntries(new FormData(dialog.querySelector('form'))); resolve({choice:dialog.returnValue, values}); dialog.remove(); }, {once:true});
  });
}
