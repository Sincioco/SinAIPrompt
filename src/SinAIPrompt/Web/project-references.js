import {parseHtml} from './document.js';

// A move supplies exact HTML files and whole companion asset folders. Resolve
// against the old document before rewriting; never search/replace its text.
export function rewriteProjectReferences(html,oldDocumentUrl,newDocumentUrl,moves=[]) {
  const oldDocument=new URL(oldDocumentUrl),newDocument=new URL(newDocumentUrl),doc=parseHtml(html);
  const mappings=[{oldUrl:oldDocument.href,newUrl:newDocument.href},...moves].map(move=>({
    from:new URL(move.oldUrl),to:new URL(move.newUrl),directory:!!move.directory
  })).sort((a,b)=>parts(b.from).length-parts(a.from).length);
  let changed=false;
  const local=(url,document)=>url.hostname==='sin-document.local'
    ?new URL(url.pathname.slice(1)+url.search+url.hash,new URL('.',document)):url;
  const resolve=(value,base,document)=>local(new URL(value,base),document);
  function moved(url) {
    if(url.protocol!=='file:')return url;
    const source=parts(url);
    for(const mapping of mappings){
      const prefix=parts(mapping.from);
      if(!sameHost(url,mapping.from)||(!mapping.directory&&(prefix.length!==source.length||url.pathname.endsWith('/')!==mapping.from.pathname.endsWith('/')))||prefix.length>source.length)continue;
      if(!prefix.every((part,index)=>part===source[index]))continue;
      const result=new URL(mapping.to);
      if(mapping.directory){
        const tail=url.pathname.split('/').slice(prefix.length);
        result.pathname=mapping.to.pathname.replace(/\/$/,'')+(tail.length?'/'+tail.join('/'):'');
      }
      result.search=url.search;result.hash=url.hash;return result;
    }
    return url;
  }
  function rebase(value,oldBase,newBase) {
    if(!value.trim())return value;
    try {
      const resolved=resolve(value,oldBase,oldDocument);
      if(resolved.protocol!=='file:')return value;
      const target=moved(resolved),current=resolve(value,newBase,newDocument);
      if(sameUrl(target,current)&&!value.includes('sin-document.local'))return value;
      return reference(target,newBase);
    }catch{return value;} // Malformed unrelated references remain unchanged.
  }
  const base=doc.querySelector('base[href]');
  let oldBase=oldDocument,newBase=newDocument;
  if(base){
    try {
      oldBase=resolve(base.getAttribute('href'),oldDocument,oldDocument);
      const value=rebase(base.getAttribute('href'),oldDocument,newDocument);
      if(value!==base.getAttribute('href')){base.setAttribute('href',value);changed=true;}
      newBase=resolve(value,newDocument,newDocument);
    }catch{/* An invalid base falls back to the document URL. */}
  }
  const rewrite=value=>rebase(value,oldBase,newBase);
  function visit(root) {
    for(const element of root.querySelectorAll('*')){
      for(const name of ['src','href','xlink:href','poster','background','data','action','formaction','cite','data-sin-original-source']){
        if(!element.hasAttribute(name)||element.localName==='base'||name==='data'&&element.localName!=='object')continue;
        const before=element.getAttribute(name),after=rewrite(before);
        if(before!==after){element.setAttribute(name,after);changed=true;}
      }
      for(const name of ['srcset','imagesrcset']){
        if(!element.hasAttribute(name))continue;
        const before=element.getAttribute(name),after=rewriteSrcset(before,rewrite);
        if(before!==after){element.setAttribute(name,after);changed=true;}
      }
      for(const name of ['style','fill','stroke','filter','clip-path','mask','marker-start','marker-mid','marker-end']){
        if(!element.hasAttribute(name))continue;
        const before=element.getAttribute(name),after=rewriteCss(before,rewrite);
        if(before!==after){element.setAttribute(name,after);changed=true;}
      }
      if(element.localName==='style'){
        const before=element.textContent,after=rewriteCss(before,rewrite);
        if(before!==after){element.textContent=after;changed=true;}
      }
      if(element.localName==='template')visit(element.content);
    }
  }
  visit(doc);
  if(!changed)return html;
  const doctype=doc.doctype?new XMLSerializer().serializeToString(doc.doctype)+'\n':'';
  const newline=html.match(/\r\n|\r|\n/)?.[0]||'\n';
  return (doctype+doc.documentElement.outerHTML).replace(/\r\n?|\n/g,newline);
}

function parts(url) {
  return url.pathname.replace(/\/$/,'').split('/').map(part=>{
    try{return decodeURIComponent(part).toLowerCase();}catch{return part.toLowerCase();}
  });
}
function sameHost(a,b){return a.protocol===b.protocol&&a.hostname.toLowerCase()===b.hostname.toLowerCase();}
function sameUrl(a,b){return sameHost(a,b)&&parts(a).join('/')===parts(b).join('/')&&a.pathname.endsWith('/')===b.pathname.endsWith('/')&&a.search===b.search&&a.hash===b.hash;}
function reference(target,base) {
  const folder=new URL('.',base),prefix=parts(folder),path=parts(target);
  // References outside the new document/base folder use file URIs. WebView's
  // virtual document root cannot represent parent folders with ../ traversal.
  if(sameHost(target,folder)&&prefix.length<=path.length&&(prefix.length<path.length||target.pathname.endsWith('/'))&&prefix.every((part,index)=>part===path[index])){
    let relative=target.pathname.split('/').slice(prefix.length).join('/')||'./';
    if(relative.split('/')[0].includes(':'))relative='./'+relative;
    return relative+target.search+target.hash;
  }
  return target.href;
}

function rewriteSrcset(source,rewrite) {
  let index=0,cursor=0,result='';
  while(index<source.length){
    while(index<source.length&&/[\s,]/.test(source[index]))index++;
    const start=index;
    while(index<source.length&&!/\s/.test(source[index]))index++;
    let end=index;while(end>start&&source[end-1]===',')end--;
    if(end>start){result+=source.slice(cursor,start)+rewrite(source.slice(start,end));cursor=end;}
    if(end<index)continue;
    let depth=0;
    while(index<source.length){
      const character=source[index++];
      if(character==='(')depth++;else if(character===')')depth=Math.max(0,depth-1);
      else if(character===','&&!depth)break;
    }
  }
  return result+source.slice(cursor);
}

// Match actual CSS URL/import tokens while skipping comments and ordinary
// quoted strings (for example content:"url(example.png)").
function rewriteCss(css,rewrite) {
  const tokens=/\/\*[\s\S]*?\*\/|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|\burl\(\s*(?:"((?:\\[\s\S]|[^"\\])*)"|'((?:\\[\s\S]|[^'\\])*)'|((?:\\[\s\S]|[^\\)])*?))\s*\)|@import\s+(?:"((?:\\[\s\S]|[^"\\])*)"|'((?:\\[\s\S]|[^'\\])*)')/gi;
  return css.replace(tokens,(token,double,single,bare,importDouble,importSingle)=>{
    const value=double??single??bare??importDouble??importSingle;
    if(value===undefined)return token;
    const decoded=cssValue(value),updated=rewrite(decoded);
    if(updated===decoded)return token;
    const quoted=updated.replace(/\\/g,'\\\\').replace(/"/g,'\\"').replace(/[\r\n\f]/g,character=>'\\'+character.charCodeAt(0).toString(16)+' ');
    return importDouble!==undefined||importSingle!==undefined?'@import "'+quoted+'"':'url("'+quoted+'")';
  });
}
function cssValue(value) {
  return value.replace(/\\([0-9a-f]{1,6})(?:\r\n|[ \t\r\n\f])?|\\(\r\n|[\s\S])/gi,(_,hex,character)=>{
    if(!hex)return /[\r\n\f]/.test(character)?'':character;
    const number=parseInt(hex,16);return String.fromCodePoint(number>0&&number<=0x10ffff&&!(number>=0xd800&&number<=0xdfff)?number:0xfffd);
  });
}
