import {setListNumber} from './list-numbering.js';
import {markdownToHtml,htmlToMarkdown,pasteMarkdown} from './markdown.js';
import {parseHtml} from './document.js';

export async function runDocumentToolsTests(check){
  const original=window.editor.html();
  const load=async html=>{await window.editor.load(html);await window.editor.ready();return document.querySelector('#document').contentDocument;};
  try{
    let doc=await load('<p>Alpha <b>beta</b> gamma</p><p>alpha beta</p><p data-secret="beta">other</p>');
    let result=await window.editor.search({query:'alpha beta',wrap:true,action:'next'});
    check(result.count===2&&result.index===1&&doc.getSelection().toString()==='Alpha beta','Visual search matches text across inline formatting');
    result=await window.editor.search({query:'alpha beta',wrap:true,action:'next'});
    check(result.index===2,'Visual Find Next advances and reports the match position');
    check((await window.editor.search({query:'beta',action:'count'})).count===2,'Visual search excludes HTML attributes');
    check((await window.editor.search({query:'Alpha',matchCase:true,action:'count'})).count===1,'Visual search supports Match Case');
    check((await window.editor.search({query:'alpha\\s+beta',regex:true,action:'count'})).count===2,'Visual search supports regular expressions');
    check((await window.editor.search({query:'[',regex:true,action:'count'})).message.includes('Invalid'),'Invalid regex reports an error without changing the document');
    result=await window.editor.search({query:'beta',replacement:'delta',action:'replaceAll'});
    check(result.count===2&&doc.querySelector('b').textContent==='delta'&&doc.querySelector('[data-secret]').dataset.secret==='beta','Visual replacement preserves formatting and HTML attributes');
    check(!window.editor.html().includes('sin-search'),'Search highlighting is absent from saved HTML');
    doc=await load('<p>match</p>'.repeat(2600));
    result=await window.editor.search({query:'match',action:'count'});
    check(result.results.length===2600&&doc.defaultView.CSS.highlights.get('sin-search').size===2600,'Find lists and highlights all results beyond the former 2000-match limit');
    result=await window.editor.search({query:'match',action:'select',target:2600});
    check(result.index===2600&&result.results[0].before===''&&result.results[0].after==='','Find navigates to a distant result and keeps excerpts within each paragraph');
    doc=await load('<p>'+'a'.repeat(5000)+'!</p>');
    check((await window.editor.search({query:'(a+)+$',regex:true,action:'count'})).message?.includes('too long'),'Slow regular expressions are terminated outside the editor UI thread');
    doc=await load('<ol start="5"><li>First</li><li>Second</li></ol><p>Between</p><ol><li>Third</li><li>Fourth</li><li>Fifth</li></ol>');
    function selectItem(index){const range=doc.createRange();range.selectNodeContents(doc.querySelectorAll('li')[index]);range.collapse(true);const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);}
    selectItem(2);setListNumber(doc,'continue');
    check(doc.querySelectorAll('li')[2].value===7,'Numbering continues from the previous numbered list');
    selectItem(3);setListNumber(doc,'restart');
    check(doc.querySelectorAll('li')[3].value===1,'Numbering restarts at the current list item');
    selectItem(4);setListNumber(doc,'set',42);
    check(doc.querySelectorAll('li')[4].value===42,'Numbering accepts an arbitrary starting value');
    doc.execCommand('undo');check(doc.querySelectorAll('li')[4].getAttribute('value')!=='42','Numbering changes support Undo');
    doc=await load('<ol start="9"><li>Only</li><li>Following</li></ol>');
    selectItem(0);setListNumber(doc,'restart');
    check(doc.querySelector('li').value===1&&doc.querySelectorAll('li').length===2,'Numbering restarts the first list without losing its items');
    selectItem(1);setListNumber(doc,'set',42);
    const numberedMarkdown=await htmlToMarkdown(doc,()=>{}),numberedRoundTrip=parseHtml(markdownToHtml(numberedMarkdown));
    check(numberedRoundTrip.querySelectorAll('ol')[1]?.start===42,'Markdown preserves an arbitrary number inside a list by starting a new numbered block');

    const markdown='# Example\n\n**Bold** and *italic* with `code`.\n\n4. Four\n5. Five\n   - Nested\n\n> Quote\n\n```js\nconst x = 1;\n\n\nconsole.log(x);\n```\n\n| Name | Value |\n| --- | --- |\n| A | B |\n\n[Link](https://example.invalid)';
    const parsed=parseHtml(markdownToHtml(markdown));
    check(parsed.querySelector('h1')?.textContent==='Example'&&parsed.querySelector('strong')?.textContent==='Bold'&&parsed.querySelector('ol')?.start===4&&parsed.querySelector('ol ul li')?.textContent==='Nested','Markdown parses headings, emphasis, numbered lists, and nested lists');
    check(parsed.querySelector('pre')?.textContent.includes('\n\n\n')&&parsed.querySelectorAll('table tr').length===2,'Markdown preserves fenced code and tables');
    check(!parseHtml(markdownToHtml('<script>alert(1)</script>')).querySelector('script'),'Pasted Markdown cannot introduce executable HTML');
    doc=await load('');
    check(await pasteMarkdown(doc,markdown,()=>{}),'An empty document accepts Markdown paste');
    check(doc.documentElement.dataset.sinStyleMode==='modern'&&doc.querySelector('h1').textContent==='Example','Markdown paste uses Modern styling');
    check(!await pasteMarkdown(doc,'# Unexpected replacement',()=>{}),'Markdown autoformat does not replace an existing document');
    const exported=await htmlToMarkdown(doc,()=>{throw Error('Unexpected image');});
    check(exported.includes('# Example')&&exported.includes('4. Four')&&exported.includes('**Bold**')&&exported.includes('```js')&&exported.includes('| Name | Value |'),'Markdown export retains headings, numbering, emphasis, code, links and tables');
    check(exported.includes('const x = 1;\n\n\nconsole.log(x);'),'Markdown export preserves blank lines inside fenced code');
  }finally{await load(original);}
}
