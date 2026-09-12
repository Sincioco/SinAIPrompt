// Checked against Visual Studio 2026's light editor and installed
// EditorColors/LanguageServices pkgdef files. Java uses IntelliJ IDEA Default.
export const RICH_SOURCE_TEXT_TYPES=Object.freeze([
  {value:'',label:'None'},{value:'csharp',label:'C#'},{value:'tsql',label:'T-SQL'},
  {value:'html',label:'HTML'},{value:'css',label:'CSS'},{value:'javascript',label:'JavaScript'},
  {value:'typescript',label:'TypeScript'},{value:'json',label:'JSON'},{value:'java',label:'JAVA'}
].map(Object.freeze));
const words=text=>new Set(text.split(/\s+/));
const keywords={
  csharp:words('abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly record ref required return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while yield init get set value partial global file not and or with when where select from group into orderby join let on equals ascending descending'),
  javascript:words('async await break case catch class const continue debugger default delete do else export extends false finally for function get if import in instanceof let new null of return set static super switch this throw true try typeof undefined var void while with yield'),
  typescript:words('abstract any as assert asserts async await bigint boolean break case catch class const constructor continue debugger declare default delete do else enum export extends false finally for from function get if implements import in infer instanceof interface is keyof let module namespace never new null number object of override private protected public readonly require return satisfies set static string super switch symbol this throw true try type typeof undefined unique unknown var void while with yield'),
  java:words('abstract assert boolean break byte case catch char class const continue default do double else enum exports extends final finally float for if implements import instanceof int interface long module native new null open opens package permits private protected provides public record requires return sealed short static strictfp super switch synchronized this throw throws to transient transitive true false try uses var void volatile while with yield'),
  tsql:words('add all alter and any as asc authorization backup begin between break browse bulk by cascade case check checkpoint close clustered coalesce collate column commit constraint contains continue convert create cross current current_date current_time current_timestamp cursor database dbcc deallocate declare default delete deny desc distinct distributed double drop else end escape except exec execute exists exit external fetch file fillfactor for foreign freetext from full function go goto grant group having holdlock identity if in index inner insert intersect into is join key kill left like merge national nocheck nonclustered not null nullif of off offset on open option or order outer over percent plan primary print procedure public raiserror read readtext reconfigure references replication restore restrict return revoke right rollback rowcount rule save schema select session_user set shutdown some statistics system_user table then to top tran transaction trigger truncate try_convert tsequal union unique update use user values varying view waitfor when where while with within writetext'),
};
const controls=words('break case catch continue default do else finally for foreach goto if return switch throw try while yield export import from');
const typescriptTypes=words('any bigint boolean never number object string symbol unknown void');
const sqlFunctions=words('abs avg cast ceiling concat count datediff dateadd datename datepart floor format getdate isnull left len lower ltrim max min newid replace right round row_number rtrim substring sum upper');
export const RICH_SOURCE_KEYWORD_COUNTS=Object.freeze(Object.fromEntries(Object.entries(keywords).map(([name,set])=>[name,set.size])));

export const sourceColors=`
.rich-source-token-keyword{color:#0000ff}.rich-source-token-control{color:#8f08c4}
.rich-source-token-comment{color:#008000;font-style:normal}.rich-source-token-string{color:#a31515}
.rich-source-token-verbatim{color:#800000}.rich-source-token-number{color:#000000}
.rich-source-token-type{color:#2b91af}.rich-source-token-function{color:#74531f}
.rich-source-token-identifier{color:#1f377f}.rich-source-token-property{color:#2e75b6}
.rich-source-token-tag,.rich-source-token-selector{color:#800000}.rich-source-token-attribute{color:#ff0000}
.rich-source-token-delimiter,.rich-source-token-entity{color:#0000ff}.rich-source-token-preprocessor{color:#808080}
:is([data-sin-code=html],[data-sin-code-preview=html]) .rich-source-token-string{color:#0000ff}
:is([data-sin-code=css],[data-sin-code-preview=css]) .rich-source-token-property{color:#ff0000}
:is([data-sin-code=css],[data-sin-code-preview=css]) :is(.rich-source-token-number,.rich-source-token-string,.rich-source-token-keyword){color:#0000ff}
:is([data-sin-code=tsql],[data-sin-code-preview=tsql]) .rich-source-token-string{color:#ff0000}
:is([data-sin-code=tsql],[data-sin-code-preview=tsql]) .rich-source-token-function{color:#ff00ff}
:is([data-sin-code=java],[data-sin-code-preview=java]) :is(.rich-source-token-keyword,.rich-source-token-control){color:#000080;font-weight:bold}
:is([data-sin-code=java],[data-sin-code-preview=java]) .rich-source-token-string{color:#008000;font-weight:bold}
:is([data-sin-code=java],[data-sin-code-preview=java]) .rich-source-token-comment{color:#808080;font-style:italic}
:is([data-sin-code=java],[data-sin-code-preview=java]) .rich-source-token-number{color:#0000ff}
:is([data-sin-code=java],[data-sin-code-preview=java]) :is(.rich-source-token-type,.rich-source-token-function,.rich-source-token-identifier){color:#000}
`;
const escape=text=>String(text).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
const token=(text,type)=>type?`<span class="rich-source-token-${type}">${escape(text)}</span>`:escape(text);

export function prepareRichSourceHighlight(source,textType,options={}){
  let text=String(source||''),type=String(textType||'').trim().toLowerCase();
  const plain=error=>({error,highlighted:false,html:'',text});
  if(!type)return plain('');
  if(text.length>250000)return plain('Source is too large to color code safely, so it is being shown as plain text.');
  if(!RICH_SOURCE_TEXT_TYPES.some(t=>t.value===type))return plain('The selected text type cannot be color coded, so it is being shown as plain text.');
  if(type==='json'){
    try{const parsed=JSON.parse(text);if(options.formatJson)text=JSON.stringify(parsed,null,2);}
    catch{return plain('JSON is invalid, so it is being shown as plain text.');}
  }
  return {error:'',highlighted:true,html:type==='html'?highlightHtml(text):highlightCode(text,type),text};
}

function highlightHtml(text){
  return text.split(/(<!--[\s\S]*?(?:-->|$)|<![^>]*>|<\/?[a-zA-Z][^>]*>)/g).map(part=>{
    if(part.startsWith('<!--'))return token(part,'comment');
    if(!part.startsWith('<'))return part.split(/(&[\w#]+;)/g).map(t=>token(t,/^&[\w#]+;$/.test(t)?'entity':'')).join('');
    let first=true;
    return (part.match(/"[^"]*"|'[^']*'|[a-zA-Z_:][\w:.-]*|[\s\S]/g)||[]).map(piece=>{
      if(/^['"]/.test(piece))return token(piece,'string');
      if(/^[a-zA-Z_:]/.test(piece)){const kind=first?'tag':'attribute';first=false;return token(piece,kind);}
      return token(piece,/[<>/=!]/.test(piece)?'delimiter':'');
    }).join('');
  }).join('');
}

function stringEnd(text,start,quote,doubled=false){
  for(let i=start+1;i<text.length;i++){
    if(!doubled&&text[i]==='\\'){i++;continue;}
    if(text[i]===quote){if(doubled&&text[i+1]===quote){i++;continue;}return i+1;}
  }
  return text.length;
}

// Standalone lexical coloring preserves source without resolving project symbols
// or running a compiler. Escaped spans cannot execute the pasted code.
function highlightCode(text,type){
  let html='',i=0,previous='',cssDepth=0,cssValue=false;
  const types=new Set([...text.matchAll(/\b(?:class|interface|enum|struct|record)\s+(\w+)/g)].map(m=>m[1]));
  while(i<text.length){
    const rest=text.slice(i),ch=text[i];
    if(rest.startsWith('/*')||!['css','json'].includes(type)&&rest.startsWith(type==='tsql'?'--':'//')){
      const block=rest.startsWith('/*'),end=text.indexOf(block?'*/':'\n',i+2),finish=end<0?text.length:end+(block?2:0);
      html+=token(text.slice(i,finish),'comment');i=finish;continue;
    }
    if(type==='csharp'&&ch==='#'&&(i===0||text[i-1]==='\n')){
      const end=text.indexOf('\n',i);html+=token(text.slice(i,end<0?text.length:end),'preprocessor');i=end<0?text.length:end;continue;
    }
    const prefix=type==='csharp'?/^(?:\$@|@\$|@|\$)?("{3,}|"|')/.exec(rest):null;
    if(prefix||/['"`]/.test(ch)){
      const quote=prefix?.[1]||ch,start=i+(prefix?.[0].length||1)-quote.length;
      const verbatim=prefix?.[0].includes('@'),raw=quote.length>=3;
      let finish=raw?text.indexOf(quote,start+quote.length):stringEnd(text,start,quote,verbatim||type==='tsql');
      finish=finish<0?text.length:raw?finish+quote.length:finish;
      const value=text.slice(i,finish),property=type==='json'&&/^\s*:/.test(text.slice(finish));
      html+=token(value,property?'property':verbatim?'verbatim':'string');i=finish;continue;
    }
    if(/\d/.test(ch)||ch==='.'&&/\d/.test(text[i+1]||'')){
      const value=/^(?:0[xX][\da-fA-F_]+|0[bB][01_]+|\d*\.?\d[\d_]*(?:\.\d*)?(?:[eE][+-]?\d+)?)(?:[uUlLfFdDmMnN]+)?/.exec(rest)?.[0]||ch;
      html+=token(value,'number');i+=value.length;continue;
    }
    if(/[A-Za-z_$]/.test(ch)||type==='css'&&/[#.\-@]/.test(ch)){
      const value=(type==='css'?/^[\w$#.\-@]+/:/^[A-Za-z_$][\w$]*/).exec(rest)?.[0]||ch;
      const word=type==='tsql'?value.toLowerCase():value,tail=text.slice(i+value.length);
      let kind='';
      if(type==='css')kind=cssDepth===0?'selector':!cssValue&&/^\s*:/.test(tail)?'property':'keyword';
      else if(type==='json')kind=/^(true|false|null)$/.test(value)?'keyword':'';
      else if(type==='typescript'&&typescriptTypes.has(word))kind='type';
      else if(keywords[type]?.has(word))kind=type!=='tsql'&&controls.has(word)?'control':'keyword';
      else if(type==='tsql')kind=sqlFunctions.has(word)&&/^\s*\(/.test(tail)?'function':'';
      else if(types.has(value)||['class','interface','enum','struct','record','new'].includes(previous))kind='type';
      else if(type!=='java'&&/^\s*\(/.test(tail))kind='function';
      else if(['javascript','typescript'].includes(type))kind='identifier';
      html+=token(value,kind);previous=value;i+=value.length;continue;
    }
    if(type==='css'){
      if(ch==='{'){cssDepth++;cssValue=false;}if(ch==='}')cssDepth=Math.max(0,cssDepth-1);
      if(ch===':')cssValue=true;if(ch===';')cssValue=false;
    }
    html+=escape(ch);i++;
  }
  return html;
}
