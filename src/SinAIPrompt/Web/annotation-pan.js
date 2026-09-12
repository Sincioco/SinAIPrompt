// View-only movement: no scene coordinates, selection or history are changed.
export function connectCanvasPan(viewport,svg,enabled){
  let offset={x:0,y:0},drag=null;
  viewport.addEventListener('pointerdown',event=>{
    if(event.button!==0||!enabled())return;
    event.preventDefault();event.stopImmediatePropagation();
    drag={x:event.clientX,y:event.clientY,left:offset.x,top:offset.y};
    viewport.setPointerCapture(event.pointerId);viewport.classList.add('panning');
  },true);
  viewport.addEventListener('pointermove',event=>{
    if(!drag)return;
    event.preventDefault();event.stopImmediatePropagation();
    offset={x:drag.left+event.clientX-drag.x,y:drag.top+event.clientY-drag.y};
    svg.style.transform=`translate(${offset.x}px,${offset.y}px)`;
  },true);
  for(const type of ['pointerup','pointercancel','lostpointercapture'])viewport.addEventListener(type,event=>{
    if(!drag)return;
    drag=null;viewport.classList.remove('panning');event.stopImmediatePropagation();
    if(viewport.hasPointerCapture(event.pointerId))viewport.releasePointerCapture(event.pointerId);
  },true);
}
