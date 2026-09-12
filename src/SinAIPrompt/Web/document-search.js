export function createDocumentSearch(getDocument,changed){
  let worker=null,pending=null,timer=null,current=null,mapped=null,revision=0,operation=0,cached=null;
  function stop(message='Search changed.'){clearTimeout(timer);worker?.terminate();worker=null;pending?.({error:message});pending=null;}
  function clear(){operation++;stop();current=null;cached=null;getDocument()?.defaultView.CSS.highlights?.delete('sin-search');}
  function invalidate(){revision++;mapped=null;clear();}
  function textMap(doc){
    const nodes=[];let text='';
    const block=/^(P|DIV|H[1-6]|LI|TR|PRE|BLOCKQUOTE|SECTION|ARTICLE)$/;
    function visit(node){
      if(node.nodeType===3){nodes.push({node,start:text.length,end:text.length+node.length});text+=node.data;return;}
      if(node.nodeType!==1||node.matches('script,style,noscript,[hidden],[data-sin-runtime]')||getComputedStyle(node).display==='none')return;
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
    const requestId=++operation,map=mapped??=textMap(doc);
    const cacheKey=JSON.stringify([options.query,options.matchCase,options.wholeWord,options.regex,options.replacement]);
    const result=cached?.key===cacheKey?cached.result:await find({...options,text:map.text});
    if(doc!==getDocument()||version!==revision||requestId!==operation)return {count:0,index:0};
    if(result.error){doc.defaultView.CSS.highlights?.delete('sin-search');current=null;return {count:0,index:0,message:result.error};}
    cached={key:cacheKey,result};
    const matches=result.matches,ranges=[];
    for(let i=0;i<matches.length;i++){
      ranges.push(rangeFor(doc,map.nodes,matches[i]));
      if(i&&i%2000===0){await new Promise(resolve=>setTimeout(resolve,0));if(requestId!==operation)return {count:0,index:0};}
    }
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
    if(['next','previous','replace','select'].includes(action)){
      index=action==='select'?options.target-1:index+(action==='previous'?-1:1);
      if(index<0||index>=matches.length){if(options.wrap)index=action==='previous'?matches.length-1:0;else return {count:matches.length,index:0,message:'Reached the end of the search'};}
      const range=matchRange(index);
      if(range){
        const selection=doc.getSelection();selection.removeAllRanges();selection.addRange(range);
        const rect=range.getBoundingClientRect();
        if(action==='select')selection.collapseToStart();
        doc.defaultView.scrollTo({top:doc.defaultView.scrollY+rect.top-doc.defaultView.innerHeight/2,behavior:'smooth'});
        current={key,index};
      }
    }
    const registry=doc.defaultView.CSS.highlights;
    if(registry){const highlight=new doc.defaultView.Highlight();for(const range of ranges)if(range)highlight.add(range);registry.set('sin-search',highlight);}
    const results=matches.map((m,i)=>{
      const end=m.start+m.length,nextLine=map.text.indexOf('\n',end);
      return {index:i+1,before:map.text.slice(Math.max(0,m.start-45,map.text.lastIndexOf('\n',m.start-1)+1),m.start),match:map.text.slice(m.start,m.start+Math.min(m.length,100)).replaceAll('\n',' '),after:map.text.slice(end,Math.min(end+70,nextLine<0?map.text.length:nextLine))};
    });
    return {count:matches.length,index:index+1,results};
  }
  return {run,invalidate};
}
