// Regex runs outside the UI thread. The caller terminates slow expressions.
self.onmessage=({data:{text,query,matchCase,wholeWord,regex,replacement}})=>{
  try{
    let pattern=regex?query:query.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
    if(wholeWord)pattern=`(?<![\\p{L}\\p{N}_])(?:${pattern})(?![\\p{L}\\p{N}_])`;
    const expression=new RegExp(pattern,'gu'+(matchCase?'':'i')),matches=[];
    for(const match of text.matchAll(expression)){
      if(!match[0].length)continue;
      matches.push({start:match.index,length:match[0].length,text:replacement==null?null:regex?replacement.replace(/\$(\$|&|\d{1,2}|<[^>]+>)/g,(token,key)=>key==='$'?'$':key==='&'?match[0]:key[0]==='<'?match.groups?.[key.slice(1,-1)]??'':match[Number(key)]??token):replacement});
    }
    self.postMessage({matches});
  }catch(error){self.postMessage({error:'Invalid regular expression: '+error.message});}
};
