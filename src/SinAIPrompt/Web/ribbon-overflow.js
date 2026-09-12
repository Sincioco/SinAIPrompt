// Moves existing groups, preserving control identities, handlers and editor selection.
export function createRibbonOverflow(root){
  const strip=root.querySelector('.ribbon-groups'),groups=[...strip.children];
  const more=document.createElement('button');more.id='moreRibbon';more.textContent='⋯';more.title='More ribbon tools';more.setAttribute('aria-label','More ribbon tools');more.hidden=true;
  const popup=document.createElement('div');popup.className='ribbon-overflow';popup.setAttribute('popover','auto');popup.setAttribute('aria-label','More ribbon tools');
  popup.id='ribbonOverflow';more.setAttribute('popovertarget',popup.id);more.setAttribute('popovertargetaction','toggle');
  const row=document.createElement('div');row.className='ribbon-main';strip.before(row);row.append(strip,more);root.append(popup);
  const styles=groups.find(group=>group.classList.contains('styles-group'));
  let wrap=true,pending=0,lastWidth=-1;
  function layout(){
    pending=0;if(!root.clientWidth)return;
    styles.style.width='';
    const widths=groups.map(group=>parseFloat(getComputedStyle(group).width));
    const available=row.clientWidth-(widths.reduce((sum,width)=>sum+width,0)>row.clientWidth&&!wrap?34:0);
    let used=0,overflow=false,moved=false;
    for(const group of groups){
      let width=widths[groups.indexOf(group)];
      // Keep every style visible before giving the lower-priority Tools any space.
      if(!wrap&&group===styles&&!overflow&&available-used>=150){width=Math.min(width,available-used);styles.style.width=width+'px';}
      overflow=!wrap&&(overflow||used+width>available);
      const parent=overflow?popup:strip;
      if(group.parentElement!==parent){parent.append(group);moved=true;}
      if(!overflow)used+=width;
    }
    more.hidden=!overflow;
    if(moved)popup.hidePopover();
  }
  function schedule(){if(!pending)pending=requestAnimationFrame(layout);}
  // Native invoker ownership prevents light-dismiss on pointerdown followed by
  // an onclick reopening the same popup. Escape/outside clicks remain native.
  popup.addEventListener('beforetoggle',event=>{
    if(event.newState==='open'){popup.style.top=more.getBoundingClientRect().bottom+4+'px';popup.style.right='8px';}
  });
  popup.addEventListener('toggle',event=>more.setAttribute('aria-expanded',String(event.newState==='open')));
  new ResizeObserver(()=>{if(row.clientWidth!==lastWidth){lastWidth=row.clientWidth;schedule();}}).observe(row);
  return enabled=>{if(wrap===enabled)return;wrap=enabled;root.classList.toggle('ribbon-nowrap',!wrap);schedule();};
}
