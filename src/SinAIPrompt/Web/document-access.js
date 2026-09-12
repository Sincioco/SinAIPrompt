// Read-only is native file state; this owner reflects it on editor interaction.
export function createDocumentAccess(root,getDocument){
  let locked=false;
  function apply(){
    const doc=getDocument();if(doc)doc.body.contentEditable=String(!locked);
    document.body.dataset.readOnly=String(locked);
    for(const control of root.querySelectorAll('button,input,select')){
      const action=control.dataset.nativeCommand||control.dataset.cmd||control.id;
      control.disabled=locked&&!['copy','save','saveAs','saveAll','new','close','unlock','source','moreRibbon'].includes(action)||action==='unlock'&&!locked;
    }
  }
  function attach(doc){
    apply();
    for(const type of ['beforeinput','paste','cut','drop','dragstart','dblclick'])doc.addEventListener(type,event=>{if(locked){event.preventDefault();event.stopImmediatePropagation();}},true);
  }
  root.addEventListener('click',event=>{if(locked&&event.target.closest('button:disabled')){event.preventDefault();event.stopImmediatePropagation();}},true);
  return {get locked(){return locked;},set(value){locked=!!value;apply();},attach};
}
