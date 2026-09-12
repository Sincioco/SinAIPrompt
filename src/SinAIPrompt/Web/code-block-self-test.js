import {codeBlockHtml} from './code-blocks.js';
import {htmlToMarkdown} from './markdown.js';
import {parseHtml} from './document.js';
import {outputBounds,renderPng} from './annotation-model.js';
import {prepareRichSourceHighlight} from './source-highlighting.js';

export async function runCodeBlockTests(check){
  const saved=window.editor.html(),doc=()=>document.querySelector('#document').contentDocument;
  const delay=()=>new Promise(resolve=>setTimeout(resolve,40));
  const code='const first = 1;\nconst second = 2;\nconsole.log(first, second);';
  try{
    for(const [language,sample] of Object.entries({csharp:'class Prompt { string Name = @"sample"; }',tsql:"SELECT COUNT(*) FROM Prompts WHERE Name = 'sample';",html:'<a href="sample">&amp;</a>',css:'.card { color: #156082; background:url(https://example.invalid/a.png); }',javascript:'export class Prompt { name = `sample`; }',typescript:'const name: string = "sample";',json:'{"name":"sample","count":3,"valid":true}',java:'public class Prompt { String name = "sample"; }'})){
      const colored=prepareRichSourceHighlight(sample,language),parsed=parseHtml('<pre>'+colored.html+'</pre>');
      check(colored.highlighted&&parsed.querySelector('pre').textContent===sample&&parsed.querySelectorAll('.rich-source-token-string,.rich-source-token-keyword,.rich-source-token-type,.rich-source-token-tag,.rich-source-token-property').length>0,language+' coloring preserves exact source and assigns language tokens');
      if(language==='typescript')check(parsed.querySelector('.rich-source-token-type')?.textContent==='string','TypeScript built-in types use the Visual Studio type color');
      if(language==='css')check(!parsed.querySelector('.rich-source-token-comment'),'An unquoted CSS URL is not mistaken for a line comment');
    }
    const state={width:800,height:500,blankCanvas:true,background:'none',objects:[{type:'rectangle',x:15,y:13,width:781,height:193,fill:'#fff',stroke:'none',strokeWidth:0}]};
    let bounds=outputBounds(state),png=await renderPng(state);
    check(bounds.x===15&&bounds.y===13&&png.width===781&&png.height===193,'Transparent starter canvas exports the actual content bounds without 800 by 500 padding');
    state.background='#fff';bounds=outputBounds(state);
    check(bounds.x===0&&bounds.width===800&&bounds.height===500,'An intentional solid canvas retains its requested dimensions');
    for(const mode of ['full','preview','collapsed']){
      await window.editor.load('<p>Before</p>'+codeBlockHtml(code,'javascript',mode,2,'Example & caption')+'<p>After</p>');
      const block=doc().querySelector('pre[data-sin-code]'),details=doc().querySelector('details');
      check(block.textContent===code&&!!details===(mode!=='full'),'Code display '+mode+' retains the entire source');
      if(details){
        check(!details.open,'Expandable code starts closed');
        const preview=details.querySelector('[data-sin-code-preview]');
        check(mode==='preview'?preview.textContent===code.split('\n').slice(0,2).join('\n'):details.querySelector('summary').textContent==='Example & caption','Code preview and caption match the chosen display');
        details.querySelector('summary').click();await delay();
        check(details.open&&block.getBoundingClientRect().height>0&&(!preview||doc().defaultView.getComputedStyle(preview).display==='none'),'Clicking the native disclosure reveals full code and hides the duplicate preview');
      }
      const markdown=await htmlToMarkdown(doc(),()=>{throw Error('Unexpected image');});
      check(markdown.includes('```javascript\n'+code+'\n```')&&markdown.split('const first').length===2,'Markdown exports full '+mode+' code once');
      const serialized=window.editor.html();await window.editor.load(serialized);
      check(!doc().querySelector('details')?.open&&doc().querySelector('pre[data-sin-code]').textContent===code,'Initial code display and complete content survive save and reload');
    }
    const editing=window.editor.pasteCode(doc().querySelector('pre[data-sin-code]'));await delay();
    let dialog=document.querySelector('dialog');dialog.querySelector('[name=caption]').value='';dialog.querySelector('[value=ok]').click();
    check(dialog.open,'Collapsed code requires a caption before it can be inserted');
    dialog.querySelector('[name=caption]').value='Revised';dialog.querySelector('[name=caption]').dispatchEvent(new Event('input'));dialog.querySelector('[value=ok]').click();await editing;
    check(doc().querySelectorAll('details').length===1&&doc().querySelector('summary').textContent==='Revised','Editing a collapsed block replaces its wrapper without nesting or duplicating code');
    window.editor.command('undo');check(doc().querySelector('summary').textContent==='Example & caption','Editing collapsed code supports native Undo');
    const inert=parseHtml(codeBlockHtml('<script>window.bad=1</script>','html'));
    check(!inert.querySelector('script')&&inert.querySelector('code').textContent.includes('<script>'),'Highlighted code stays escaped and cannot execute document scripts');
  }finally{await window.editor.load(saved);}
}
