// Own only the temporary context menu. Scene selection and commands stay with
// the annotation dialog, and clipboard conversion stays with its adapter.
export function connectAnnotationMenu(dialog,{getSelection,selectObject,copy,setLocked,onError}) {
  const menu=document.createElement('div');menu.className='annotation-menu';menu.popover='auto';
  menu.setAttribute('role','menu');menu.setAttribute('aria-label','Selected Objects');
  menu.innerHTML='<button role="menuitem" data-annotation-command="png">Copy Image (PNG)</button><button role="menuitem" data-annotation-command="svg">Copy Vector (SVG)</button><hr><button role="menuitem" data-annotation-command="lock">Lock Selected Objects</button><button role="menuitem" data-annotation-command="unlock">Unlock Selected Objects</button>';
  dialog.append(menu);
  const close=()=>{menu.hidePopover();dialog.querySelector('#canvas').focus({preventScroll:true});};
  dialog.addEventListener('contextmenu',event=>{
    const object=event.target.closest('[data-object],[data-layer]');
    if(!object&&!event.target.closest('.canvas-viewport'))return;
    event.preventDefault();event.stopPropagation();
    if(object)selectObject(object.dataset.object||object.dataset.layer);
    const objects=getSelection();if(!objects.length)return;
    menu.querySelector('[data-annotation-command=lock]').disabled=objects.every(o=>o.locked);
    menu.querySelector('[data-annotation-command=unlock]').disabled=objects.every(o=>!o.locked);
    menu.showPopover();
    const b=menu.getBoundingClientRect();
    menu.style.left=Math.max(4,Math.min(event.clientX,innerWidth-b.width-4))+'px';
    menu.style.top=Math.max(4,Math.min(event.clientY,innerHeight-b.height-4))+'px';
    menu.querySelector('button').focus({preventScroll:true});
  });
  menu.addEventListener('click',async event=>{
    const command=event.target.closest('button')?.dataset.annotationCommand;if(!command)return;
    event.stopPropagation();close();
    try {if(command==='lock'||command==='unlock')setLocked(command==='lock');else await copy(command);}
    catch(error){onError(error);}
  });
  menu.addEventListener('keydown',event=>{
    event.stopPropagation();
    if(event.key==='Escape'){event.preventDefault();close();}
    if(event.key==='ArrowDown'||event.key==='ArrowUp'){
      event.preventDefault();const buttons=[...menu.querySelectorAll('button:not(:disabled)')],index=buttons.indexOf(document.activeElement);
      buttons[(index+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length].focus();
    }
  });
}
