export function createDocumentSearch(getDocument,changed){
  let worker=null,pending=null,timer=null,current=null,mapped=null,revision=0;
  function stop(message='Search changed.'){clearTimeout(timer);worker?.terminate();worker=null;pending?.({error:message});pending=null;}
  function clear(){stop();current=null;getDocument()?.defaultView.CSS.highlights?.delete('sin-search');}
  function invalidate(){revision++;mapped=null;clear();}
  function textMap(doc){
    const nodes=[];let text='';
    const block=/^(P|DIV|H[1-6]|LI|TR|PRE|BLOCKQUOTE|SECTION|ARTICLE)$/;
    function visit(node){
      if(node.nodeType===3){nodes.push({node,start:text.length,end:text.length+node.length});text+=node.data;return;}
      if(node.nodeType!==1||node.matches('script,style,noscript,[hidden]')||getComputedStyle(node).display==='none')return;
      if(node.tagName==='BR'){text+='\n';return;}
      if(block.test(node.tagName)&&text&&!text.endsWith('\n'))text+='\n';
      for(const child of node.childNodes)visit(child);
      if(block.test(node.tagName)&&text&&!text.endsWith('\n'))text+='\n';
    }
    visit(doc.body);return {text,nodes};
  }
  function rangeFor(doc,nodes,match){
    function at(offset,end){let low=0,high=nodes.length;while(low<high){const mid=(low+high)>>1;if(end?nodes[mid].end<offset:nodes[mid].end<=offset)low=mid+1;else high=mid;}return nodes[low];}
    const start=at(match.start,false),end=at(match.start+match.length,true);
    if(!start||!end)return null;
    const range=doc.createRange();range.setStart(start.node,Math.max(0,match.start-start.start));range.setEnd(end.node,match.start+match.length-end.start);return range;
  }
  function find(data){
    stop();return new Promise(resolve=>{
      pending=resolve;worker=new Worker(new URL('./search-worker.js',import.meta.url));
      timer=setTimeout(()=>stop('Search took too long. Try a simpler expression.'),750);
      worker.onmessage=event=>{clearTimeout(timer);worker.terminate();worker=null;pending=null;resolve(event.data);};
      worker.onerror=()=>stop('Search could not run.');worker.postMessage(data);
    });
  }
  async function run(options){
    const doc=getDocument(),version=revision;
    if(options.action==='clear'){clear();return {count:0,index:0};}
    if(!options.query){clear();return {count:0,index:0};}
    const map=mapped??=textMap(doc),result=await find({...options,text:map.text});
    if(doc!==getDocument()||version!==revision)return {count:0,index:0};
    if(result.error)return {count:0,index:0,message:result.error};
    const matches=result.matches,ranges=matches.slice(0,options.action==='replaceAll'?undefined:2000).map(m=>rangeFor(doc,map.nodes,m));
    const matchRange=index=>ranges[index]??(matches[index]?rangeFor(doc,map.nodes,matches[index]):null);
    const key=JSON.stringify([options.query,options.matchCase,options.wholeWord,options.regex]);
    if(current?.key!==key)current=null;
    const action=options.action;
    if(action==='replaceAll'){
      for(let i=ranges.length-1;i>=0;i--)if(ranges[i]){const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(ranges[i]);doc.execCommand('insertText',false,matches[i].text);}
      changed();clear();return {count:matches.length,index:0,message:`Replaced ${matches.length} occurrences`};
    }
    if(action==='replace'&&current&&matchRange(current.index)){
      const i=current.index,selection=doc.getSelection();selection.removeAllRanges();selection.addRange(matchRange(i));doc.execCommand('insertText',false,matches[i].text);changed();clear();
      return run({...options,action:'next'});
    }
    let index=current?.index??-1;
    if(action==='next'||action==='previous'||action==='replace'){
      index+=action==='previous'?-1:1;
      if(index<0||index>=matches.length){if(options.wrap)index=action==='previous'?matches.length-1:0;else return {count:matches.length,index:0,message:'Reached the end of the search'};}
      const range=matchRange(index);
      if(range){const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);range.startContainer.parentElement?.scrollIntoView({block:'center'});current={key,index};}
    }
    const registry=doc.defaultView.CSS.highlights;
    if(registry){registry.delete('sin-search');registry.set('sin-search',new doc.defaultView.Highlight(...ranges.filter(Boolean).slice(0,2000)));}
    return {count:matches.length,index:index+1};
  }
  return {run,invalidate};
}
