// Selection chrome lives outside the editable iframe and never enters saved HTML.
export function createImageSelection(frame,changed,onSelection){
  const overlay=document.createElement('div');overlay.className='image-selection';overlay.hidden=true;
  overlay.innerHTML=['nw','ne','se','sw'].map(corner=>`<button data-image-resize="${corner}" aria-label="Resize Image ${corner.toUpperCase()}" title="Drag to resize proportionately"></button>`).join('');
  document.body.append(overlay);
  let image=null,gesture=null,observer=null,shield=null;
  function position(){
    if(!image?.isConnected){select(null);return;}
    const r=image.getBoundingClientRect(),f=frame.getBoundingClientRect();
    overlay.style.clipPath=`inset(${Math.max(-6,-r.top)}px -6px ${Math.max(-6,r.bottom-f.height)}px -6px)`;
    Object.assign(overlay.style,{left:f.left+r.left+'px',top:f.top+r.top+'px',width:r.width+'px',height:r.height+'px'});
    overlay.hidden=r.bottom<0||r.top>f.height;
  }
  function select(next){
    image?.removeAttribute('data-sin-selected');observer?.disconnect();observer=null;
    image=next;overlay.hidden=!image;onSelection(image);
    if(image){
      image.dataset.sinSelected='1';observer=new ResizeObserver(position);observer.observe(image);position();
      const doc=image.ownerDocument,range=doc.createRange();range.selectNode(image);
      doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);
    }
  }
  function attach(doc){
    select(null);
    doc.defaultView.addEventListener('scroll',()=>{if(image)position();},{passive:true});
    doc.defaultView.addEventListener('resize',()=>{if(image)position();});
    doc.addEventListener('input',()=>{if(image&&!gesture)position();});
    doc.addEventListener('dragstart',event=>{if(event.target===image)event.preventDefault();});
  }
  overlay.addEventListener('pointerdown',event=>{
    if(event.button!==0||!image)return;
    event.preventDefault();event.stopPropagation();
    gesture={x:event.clientX,y:event.clientY,rect:image.getBoundingClientRect(),style:image.getAttribute('style'),corner:event.target.dataset.imageResize};
    // Keep a drag in the parent document when the pointer crosses the iframe.
    shield=document.createElement('div');shield.className='image-resize-shield';
    shield.style.cursor=getComputedStyle(event.target).cursor;document.body.append(shield);shield.setPointerCapture(event.pointerId);
  });
  document.addEventListener('pointermove',event=>{
    if(!gesture||!image)return;
    const {rect,corner}=gesture,dx=(event.clientX-gesture.x)*(corner.includes('w')?-1:1),dy=(event.clientY-gesture.y)*(corner.includes('n')?-1:1);
    const scale=Math.max(1/rect.width,Math.abs(dx/rect.width)>=Math.abs(dy/rect.height)?1+dx/rect.width:1+dy/rect.height);
    image.style.width=Math.max(1,Math.round(rect.width*scale))+'px';image.style.height=Math.max(1,Math.round(rect.height*scale))+'px';position();
  });
  function restore(){if(gesture.style==null)image.removeAttribute('style');else image.setAttribute('style',gesture.style);}
  document.addEventListener('pointerup',event=>{
    if(!gesture||!image)return;
    const doc=image.ownerDocument,index=[...doc.images].indexOf(image),replacement=image.cloneNode(true);
    replacement.removeAttribute('data-sin-selected');restore();gesture=null;
    doc.body.focus();const range=doc.createRange();range.selectNode(image);doc.getSelection().removeAllRanges();doc.getSelection().addRange(range);
    doc.execCommand('insertHTML',false,replacement.outerHTML);select(doc.images[index]);changed();
    shield?.remove();shield=null;
  });
  document.addEventListener('pointercancel',()=>{if(gesture&&image){restore();gesture=null;position();}shield?.remove();shield=null;});
  return {select,attach,get selected(){return image;}};
}
