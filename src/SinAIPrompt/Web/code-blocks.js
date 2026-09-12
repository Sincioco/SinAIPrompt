import {ask,escapeHtml,report} from './bridge.js';
import {normalizeIndent} from './document.js';
import {prepareRichSourceHighlight,RICH_SOURCE_TEXT_TYPES} from './source-highlighting.js';

// Each block owns its code and initial display mode in portable, script-free HTML.
export function codeBlockHtml(text,language,mode='full',lines=10,caption=''){
  const highlight=prepareRichSourceHighlight(text,language);
  const pre=`<pre data-sin-code="${escapeHtml(language)}" contenteditable="false"><code>${highlight.html||escapeHtml(text)}</code></pre>`;
  if(highlight.error)report(highlight.error);
  if(mode==='full')return pre;
  const count=Math.max(1,Math.floor(Number(lines)||10));
  const preview=mode==='preview'?text.split('\n').slice(0,count).join('\n'):'';
  const previewHtml=preview?`<pre data-sin-code-preview="${escapeHtml(language)}"><code>${prepareRichSourceHighlight(preview,language).html||escapeHtml(preview)}</code></pre>`:'';
  const label=mode==='collapsed'?caption.trim():`Show all ${text.split('\n').length} lines`;
  return `<details data-sin-code-display="${mode}" data-sin-code-lines="${count}" contenteditable="false"><summary>${escapeHtml(label)}${previewHtml}</summary>${pre}</details>`;
}

export async function editCodeBlock(existing,{getDocument,saveSelection,restoreSelection,command}){
  saveSelection();const doc=getDocument(),wrapper=existing?.closest('details[data-sin-code-display]');
  const language=existing?.dataset.sinCode||'csharp',mode=wrapper?.dataset.sinCodeDisplay||'full';
  const caption=mode==='collapsed'?wrapper.querySelector('summary')?.textContent||'':'';
  const options=RICH_SOURCE_TEXT_TYPES.filter(t=>t.value).map(t=>`<option value="${t.value}" ${t.value===language?'selected':''}>${escapeHtml(t.value==='tsql'?'SQL / T-SQL':t.label)}</option>`).join('');
  const answer=await ask(existing?'Edit Code Block':'Paste Code',`
    <label>Language <select name="language">${options}</select></label>
    <textarea name="code" aria-label="Code" spellcheck="false" autofocus>${escapeHtml(existing?.textContent||'')}</textarea>
    <label>Display <select name="display"><option value="full">Show entire code</option><option value="preview">Show first N lines</option><option value="collapsed">Initially collapsed</option></select></label>
    <label data-code-lines>Lines to show <input name="lines" type="number" min="1" max="10000" step="1" value="${Number(wrapper?.dataset.sinCodeLines)||10}"></label>
    <label class="stacked-field" data-code-caption>Caption <input name="caption" value="${escapeHtml(caption)}" placeholder="Describe this code"></label>
    <p class="hint">Common indentation is trimmed. Expandable blocks also work in saved HTML without scripts. Double-click a block to edit it.</p>`,
    [{value:'ok',label:existing?'Update Code':'Insert Code'}],dialog=>{
      const select=dialog.querySelector('[name=display]');select.value=mode;
      const update=()=>{
        dialog.querySelector('[data-code-lines]').hidden=select.value!=='preview';
        dialog.querySelector('[name=lines]').disabled=select.value!=='preview';
        dialog.querySelector('[data-code-caption]').hidden=select.value!=='collapsed';
        dialog.querySelector('[name=caption]').required=select.value==='collapsed';
      };
      select.onchange=update;update();
      dialog.querySelector('form').addEventListener('submit',event=>{
        const input=dialog.querySelector('[name=caption]');
        if(event.submitter?.value==='ok'&&select.value==='collapsed'&&!input.value.trim()){
          event.preventDefault();input.setCustomValidity('Enter a caption for the collapsed code.');input.reportValidity();
        }
      });
      dialog.querySelector('[name=caption]').oninput=event=>event.target.setCustomValidity('');
    });
  if(answer.choice!=='ok'||doc!==getDocument()||existing&&!existing.isConnected)return;
  const text=normalizeIndent(answer.values.code),previous=[...doc.querySelectorAll('pre[data-sin-code]')];
  const index=existing?previous.indexOf(existing):-1;
  if(existing){
    // Chromium cannot replace a selection whose code is hidden by <details>.
    if(wrapper)wrapper.open=true;
    restoreSelection();const range=doc.createRange();range.selectNode(wrapper||existing);
    doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);saveSelection();
  }
  command('insertHTML',codeBlockHtml(text,answer.values.language,answer.values.display,answer.values.lines,answer.values.caption)+(existing?'':'<p><br></p>'));
  const after=[...doc.querySelectorAll('pre[data-sin-code]')],inserted=index>=0?after[index]:after.find(block=>!previous.includes(block));
  if(!inserted)return;
  const block=inserted.closest('details[data-sin-code-display]')||inserted;
  let next=block.nextElementSibling;
  if(!next||next.tagName!=='P'){next=doc.createElement('p');next.innerHTML='<br>';block.after(next);}
  const range=doc.createRange();range.selectNodeContents(next);range.collapse(true);
  doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);saveSelection();
}
