import {send} from './bridge.js';

// Report actual popup rectangles so native navigation stays visible beside them.
// Removed popups can lose their queued toggle event, so also observe DOM removals.
export function observeEditorChrome(toolbar){
  let pending=0,previous='';
  const observed=new Set(),resize=new ResizeObserver(schedule);
  function sync(){
    pending=0;const open=[...document.querySelectorAll('dialog[open],:popover-open')];
    for(const popup of observed)if(!open.includes(popup)){resize.unobserve(popup);observed.delete(popup);}
    for(const popup of open)if(!observed.has(popup)){observed.add(popup);resize.observe(popup);}
    const state={modal:open.some(popup=>popup.matches(':modal')),rects:open.map(popup=>{
      const {x,y,width,height}=popup.getBoundingClientRect();return {x,y,width,height};
    })},key=JSON.stringify(state);
    if(key!==previous){previous=key;send('chrome-overlay',state);}
  }
  function schedule(){if(!pending)pending=requestAnimationFrame(sync);}
  new ResizeObserver(()=>{send('ribbon-height',{height:toolbar.getBoundingClientRect().height});schedule();}).observe(toolbar);
  document.addEventListener('toggle',schedule,true);
  window.addEventListener('resize',schedule);
  document.addEventListener('scroll',schedule,true);
  new MutationObserver(records=>{if(records.some(record=>record.removedNodes.length))schedule();}).observe(document.body,{childList:true,subtree:true});
  schedule();
}
