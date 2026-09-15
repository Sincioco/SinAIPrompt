// Moves existing controls, preserving their identities, handlers and selection.
export function createRibbonOverflow(root){
  const strip=root.querySelector('.ribbon-groups'),groups=[...strip.children];
  const more=document.createElement('button');more.id='moreRibbon';more.textContent='⋯';more.title='More ribbon tools';more.setAttribute('aria-label','More ribbon tools');more.hidden=true;
  const popup=document.createElement('div');popup.className='ribbon-overflow';popup.setAttribute('popover','auto');popup.setAttribute('aria-label','More ribbon tools');
  popup.id='ribbonOverflow';more.setAttribute('popovertarget',popup.id);more.setAttribute('popovertargetaction','toggle');
  const row=document.createElement('div');row.className='ribbon-main';strip.before(row);row.append(strip,more);root.append(popup);
  const styles=groups.find(group=>group.classList.contains('styles-group'));
  const tools=groups.find(group=>group.classList.contains('tools-group')),toolRow=tools.querySelector('.tools-controls'),buttons=[...toolRow.children];
  const extraTools=tools.cloneNode(false),extraRow=toolRow.cloneNode(false);
  extraTools.append(extraRow,tools.querySelector('.group-caption').cloneNode(true));
  const toolWidth=count=>count*72+Math.max(0,count-1)*3+16;
  let wrap=true,pending=0,lastWidth=-1;
  function layout(){
    pending=0;if(!root.clientWidth)return;
    styles.style.width='';
    tools.style.width=toolWidth(buttons.length)+'px';
    const widths=groups.map(group=>parseFloat(getComputedStyle(group).width));
    const available=row.clientWidth-(widths.reduce((sum,width)=>sum+width,0)>row.clientWidth&&!wrap?34:0);
    let used=0,overflow=false,moved=false;
    for(const group of groups.filter(group=>group!==tools)){
      let width=widths[groups.indexOf(group)];
      // Keep every style visible before giving the lower-priority Tools any space.
      if(!wrap&&group===styles&&!overflow&&available-used>=150){width=Math.min(width,available-used);styles.style.width=width+'px';}
      overflow=!wrap&&(overflow||used+width>available);
      if(wrap&&used+width>available)used=0;
      const parent=overflow?popup:strip;
      if(group.parentElement!==parent){parent.append(group);moved=true;}
      if(!overflow)used+=width;
    }
    let fit=overflow?0:buttons.length;
    while(fit>0&&used+toolWidth(fit)>available)fit--;
    if(wrap&&fit===0)fit=Math.min(buttons.length,Math.max(1,Math.floor((available-13)/75)));
    const firstParent=fit>0?strip:popup,restParent=wrap?strip:popup;
    if(tools.parentElement!==firstParent){firstParent.append(tools);moved=true;}
    tools.style.width=toolWidth(fit||buttons.length)+'px';
    for(const [index,button] of buttons.entries()){
      const parent=fit>0&&index>=fit?extraRow:toolRow;
      const offset=parent===extraRow?index-fit:index;
      if(parent.children[offset]!==button){parent.insertBefore(button,parent.children[offset]||null);moved=true;}
    }
    if(fit>0&&fit<buttons.length){
      extraTools.style.width=toolWidth(buttons.length-fit)+'px';
      if(extraTools.parentElement!==restParent||restParent.lastElementChild!==extraTools){restParent.append(extraTools);moved=true;}
    }else if(extraTools.parentElement){extraTools.remove();moved=true;}
    more.hidden=wrap||!overflow&&fit===buttons.length;
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
