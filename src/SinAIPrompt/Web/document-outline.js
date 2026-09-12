// Outline indexes are transient; navigation adds no IDs or markup to saved HTML.
export function createDocumentOutline(getDocument){
  const nodes=()=>[...getDocument()?.body.querySelectorAll('h1,h2,[data-sin-style=title],[data-sin-style=heading],[data-sin-style=heading2]')||[]]
    .filter(node=>!node.closest('figure,pre,[contenteditable=false]:not(body)'));
  return {
    items:()=>nodes().map((node,index)=>({index,text:node.textContent.trim()||'(Untitled section)',level:node.dataset.sinStyle==='title'?0:node.dataset.sinStyle==='heading2'||node.tagName==='H2'?2:1})),
    jump(index){const node=nodes()[index];if(!node)return;node.scrollIntoView({behavior:'smooth',block:'start'});const doc=node.ownerDocument,range=doc.createRange();range.selectNodeContents(node);range.collapse(true);doc.body.focus();doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);}
  };
}
