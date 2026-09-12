import {escapeHtml} from './bridge.js';
import {toPng} from './document.js';
import {setDocumentStyle} from './document-styles.js';

const safeUrl=value=>/^(?:javascript|vbscript|data):/i.test(value.trim())?'':value.trim();
export const looksLikeMarkdown=text=>/^(?:#{1,6}\s|\s*[-+*]\s|\s*\d+[.)]\s|>\s|```|~~~)|\*\*\S|!\[[^\]]*\]\(/m.test(text);
export async function pasteMarkdown(doc,text,changed,base='',force=false){
  if(doc.body.textContent.trim()||doc.body.querySelector('img,pre,table')||(!force&&!looksLikeMarkdown(text)))return false;
  const fragment=doc.createElement('div');fragment.innerHTML=markdownToHtml(text,base);
  for(const image of fragment.querySelectorAll('img[src]')){image.src=(await toPng(new URL(image.getAttribute('src'),base||doc.baseURI).href)).data;image.dataset.sinStorage='inline';}
  // Only touch the empty document after all referenced images have loaded.
  if(doc.body.textContent.trim()||doc.body.querySelector('img,pre,table'))return false;
  setDocumentStyle(doc,'modern');doc.body.focus();const range=doc.createRange();range.selectNodeContents(doc.body);
  const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);doc.execCommand('insertHTML',false,fragment.innerHTML);changed();return true;
}
export function markdownToHtml(text,base=''){
  const references=new Map();
  const lines=text.replace(/\r\n?/g,'\n').replace(/^\uFEFF/,'').split('\n').filter(line=>{
    const match=/^\s*\[([^\]]+)\]:\s*(\S+)(?:\s+"([^"]*)")?\s*$/.exec(line);
    if(!match)return true;references.set(match[1].toLowerCase(),match[2]);return false;
  });
  function inline(value){
    const tokens=[];const token=html=>`\uE000${tokens.push(html)-1}\uE001`;
    value=value.replace(/(`+)([\s\S]*?)\1/g,(_,ticks,code)=>token(`<code>${escapeHtml(code.replace(/\n/g,' '))}</code>`));
    value=value.replace(/\\([\\`*{}\[\]()#+\-.!_>|])/g,(_,char)=>token(escapeHtml(char)));
    value=value.replace(/(!?)\[([^\]]*)\](?:\((<[^>]*>|[^\s)]*)(?:\s+"[^"]*")?\)|\[([^\]]*)\])/g,(_,image,label,url,reference)=>{
      url=safeUrl(url?.replace(/^<|>$/g,'')??references.get((reference||label).toLowerCase())??'');
      if(image&&base&&url){try{url=new URL(url,base).href;}catch{}}
      return token(image?`<img src="${escapeHtml(url)}" alt="${escapeHtml(label)}">`:`<a href="${escapeHtml(url)}">${inline(label)}</a>`);
    });
    value=escapeHtml(value).replace(/\*\*([^\n]+?)\*\*|__([^\n]+?)__/g,(_,a,b)=>`<strong>${a??b}</strong>`)
      .replace(/~~([^\n]+?)~~/g,'<s>$1</s>').replace(/\*([^*\n]+?)\*|_([^_\n]+?)_/g,(_,a,b)=>`<em>${a??b}</em>`)
      .replace(/ {2}\n/g,'<br>').replace(/\n/g,' ');
    return value.replace(/\uE000(\d+)\uE001/g,(_,i)=>tokens[Number(i)]);
  }
  const item=/^(\s*)([-+*]|\d+[.)])\s+(.*)$/;
  function blocks(input){
    let output='',i=0;
    while(i<input.length){
      const line=input[i];if(!line.trim()){i++;continue;}
      if(/^\s*<!--.*-->\s*$/.test(line)){i++;continue;}
      let match;
      if((match=/^\s*(`{3,}|~{3,})(\S*)\s*$/.exec(line))){
        const fence=match[1],language=match[2],code=[];i++;
        while(i<input.length&&!new RegExp('^\\s*'+fence[0]+'{'+fence.length+',}\\s*$').test(input[i]))code.push(input[i++]);
        if(i<input.length)i++;
        output+=`<pre data-sin-code="${escapeHtml(language||'text')}"><code>${escapeHtml(code.join('\n'))}</code></pre>`;continue;
      }
      if((match=/^(#{1,6})\s+(.+?)(?:\s+#+)?$/.exec(line))){output+=`<h${match[1].length}>${inline(match[2])}</h${match[1].length}>`;i++;continue;}
      if(i+1<input.length&&/^\s*(?:=+|-+)\s*$/.test(input[i+1])){const level=input[i+1].trim()[0]==='='?1:2;output+=`<h${level}>${inline(line)}</h${level}>`;i+=2;continue;}
      if(/^\s*(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$/.test(line)){output+='<hr>';i++;continue;}
      if(/^\s*>/.test(line)){
        const quote=[];while(i<input.length&&/^\s*>/.test(input[i]))quote.push(input[i++].replace(/^\s*> ?/,''));
        output+=`<blockquote>${blocks(quote)}</blockquote>`;continue;
      }
      if((match=item.exec(line))){
        const indent=match[1].length,ordered=/\d/.test(match[2]),tag=ordered?'ol':'ul';
        let expected=ordered?parseInt(match[2]):1;output+=`<${tag}${ordered&&expected!==1?` start="${expected}"`:''}>`;
        while(i<input.length){
          const entry=item.exec(input[i]);if(!entry||entry[1].length!==indent||/\d/.test(entry[2])!==ordered)break;
          const number=parseInt(entry[2]),content=[entry[3]],padding=entry[1].length+entry[2].length+1;i++;
          while(i<input.length){
            if(!input[i].trim()){if(i+1<input.length&&/^\s+/.test(input[i+1])){content.push('');i++;continue;}break;}
            if(input[i].match(/^\s*/)[0].length<=indent)break;
            content.push(input[i++].slice(Math.min(padding,input[i-1].match(/^\s*/)[0].length)));
          }
          const checked=/^\[([ xX])\]\s+/.exec(content[0]);if(checked)content[0]=(checked[1]===' '?'☐ ':'☑ ')+content[0].slice(checked[0].length);
          output+=`<li${ordered&&number!==expected?` value="${number}"`:''}>${blocks(content)}</li>`;expected=number+1;
          while(i<input.length&&!input[i].trim()&&item.test(input[i+1]||''))i++;
        }
        output+=`</${tag}>`;continue;
      }
      if(i+1<input.length&&line.includes('|')&&/^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$/.test(input[i+1])){
        const cells=row=>row.trim().replace(/^\||\|$/g,'').split(/(?<!\\)\|/).map(cell=>cell.trim());
        const headings=cells(line),alignments=cells(input[i+1]);output+='<table><thead><tr>'+headings.map((cell,n)=>`<th${alignments[n]?.endsWith(':')?` style="text-align:${alignments[n].startsWith(':')?'center':'right'}"`:''}>${inline(cell)}</th>`).join('')+'</tr></thead><tbody>';i+=2;
        while(i<input.length&&input[i].includes('|')&&input[i].trim())output+='<tr>'+cells(input[i++]).map(cell=>`<td>${inline(cell)}</td>`).join('')+'</tr>';
        output+='</tbody></table>';continue;
      }
      const paragraph=[line];i++;
      while(i<input.length&&input[i].trim()&&!/^(?:#{1,6}\s|\s*>|\s*```|\s*~~~)/.test(input[i])&&!item.test(input[i])&&!(i+1<input.length&&/^\s*(?:=+|-+)\s*$/.test(input[i+1])))paragraph.push(input[i++]);
      output+=`<p>${inline(paragraph.join('\n'))}</p>`;
    }
    return output;
  }
  return blocks(lines);
}

export async function htmlToMarkdown(doc,saveImage){
  const clone=doc.body.cloneNode(true),sources=new Map(),codeBlocks=[];
  for(const image of clone.querySelectorAll('img')){
    const source=image.getAttribute('src');if(!source)continue;
    const absolute=new URL(source,doc.baseURI).href;
    if(!sources.has(absolute))sources.set(absolute,await saveImage((await toPng(absolute)).data));
    image.setAttribute('src',sources.get(absolute));
  }
  const escape=text=>text.replace(/\\/g,'\\\\').replace(/([`*_[\]<>])/g,'\\$1');
  const destination=url=>safeUrl(url).replace(/ /g,'%20').replace(/\(/g,'%28').replace(/\)/g,'%29');
  const children=node=>[...node.childNodes].map(write).join('');
  function write(node){
    if(node.nodeType===3)return escape(node.data.replace(/\s+/g,' '));
    if(node.nodeType!==1||node.matches('script,style,noscript,[data-sin-runtime]'))return '';
    const tag=node.tagName,content=()=>children(node);
    const heading=/^H([1-6])$/.exec(tag)?.[1]??({heading:1,heading2:2,title:1}[node.dataset.sinStyle]);
    if(heading)return '\n\n'+'#'.repeat(Number(heading))+' '+content().trim()+'\n\n';
    if(tag==='PRE'){
      const code=node.textContent.replace(/\n$/,''),ticks='`'.repeat(Math.max(3,...[...code.matchAll(/`+/g)].map(m=>m[0].length+1)));
      codeBlocks.push(`${ticks}${node.dataset.sinCode==='text'?'':node.dataset.sinCode||''}\n${code}\n${ticks}`);
      return `\n\n\uE002${codeBlocks.length-1}\uE003\n\n`;
    }
    if(tag==='CODE'){const ticks='`'.repeat(Math.max(1,...[...node.textContent.matchAll(/`+/g)].map(m=>m[0].length+1)));return ticks+' '+node.textContent+' '+ticks;}
    if(tag==='BR')return '  \n';
    if(tag==='HR')return '\n\n---\n\n';
    if(tag==='IMG')return `![${escape(node.getAttribute('alt')||'')}](${destination(node.getAttribute('src')||'')})`;
    if(tag==='A')return `[${content()}](${destination(node.getAttribute('href')||'')})`;
    if(tag==='STRONG'||tag==='B')return '**'+content()+'**';
    if(tag==='EM'||tag==='I')return '*'+content()+'*';
    if(tag==='S'||tag==='DEL'||tag==='STRIKE')return '~~'+content()+'~~';
    if(tag==='BLOCKQUOTE')return '\n\n'+content().trim().split('\n').map(line=>'> '+line).join('\n')+'\n\n';
    if(tag==='OL'||tag==='UL'){
      const items=[...node.children].filter(el=>el.tagName==='LI');let number=node.hasAttribute('start')?node.start:node.reversed?items.length:1;
      return '\n\n'+items.map(item=>{
        const discontinuity=tag==='OL'&&item!==items[0]&&(node.reversed||item.hasAttribute('value')&&item.value!==number);
        if(item.hasAttribute('value'))number=item.value;const prefix=tag==='OL'?`${number}. `:'- ';number+=node.reversed?-1:1;
        return (discontinuity?'\n<!-- list break -->\n\n':'')+children(item).trim().split('\n').map((line,i)=>(i?' '.repeat(prefix.length):prefix)+line).join('\n');
      }).join('\n')+'\n\n';
    }
    if(tag==='TABLE'){
      const rows=[...node.rows].map(row=>[...row.cells].map(cell=>children(cell).trim().replace(/\n/g,'<br>').replace(/\|/g,'\\|')));
      if(!rows.length)return '';const columns=Math.max(...rows.map(row=>row.length));
      const row=values=>'| '+Array.from({length:columns},(_,i)=>values[i]||'').join(' | ')+' |';
      return '\n\n'+row(rows[0])+'\n'+row(Array(columns).fill('---'))+'\n'+rows.slice(1).map(row).join('\n')+'\n\n';
    }
    if(/^(P|DIV|SECTION|ARTICLE)$/.test(tag))return '\n\n'+content().trim()+'\n\n';
    return content();
  }
  return children(clone).replace(/\n[ \t]+\n/g,'\n\n').replace(/\n{3,}/g,'\n\n').trim().replace(/\uE002(\d+)\uE003/g,(_,i)=>codeBlocks[Number(i)])+'\n';
}
