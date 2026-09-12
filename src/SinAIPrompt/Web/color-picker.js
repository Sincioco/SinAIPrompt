import {escapeHtml} from './bridge.js';

const theme = [
  ['White','#ffffff',['#f2f2f2','#d9d9d9','#bfbfbf','#a6a6a6','#808080']],
  ['Black','#000000',['#808080','#595959','#404040','#262626','#0d0d0d']],
  ['Light gray','#e8e8e8',['#d0cece','#aeaaaa','#757171','#514d4d','#343232']],
  ['Dark Blue','#0e2841',['#dbe5f1','#b8cce4','#8ea9c1','#0b2034','#071421']],
  ['Blue','#156082',['#c0e6f5','#83cceb','#46b1e1','#0f4761','#0a3041']],
  ['Orange','#e97132',['#fbe2d5','#f7c6ac','#f2aa84','#be5014','#80350e']],
  ['Green','#196b24',['#c1f0c8','#84e291','#47d45a','#13511b','#0c3612']],
  ['Teal','#0f9ed5',['#caedfb','#95dcf7','#61cbf3','#0b769f','#084f6a']],
  ['Purple','#a02b93',['#f2ceef','#e59edd','#d86dcc','#78206e','#501549']],
  ['Light Green','#4ea72e',['#d9f2d0','#b2e5a0','#8ed973','#3a7d22','#275417']]
];
const standard = ['#c00000','#ff0000','#ffc000','#ffff00','#92d050','#00b050','#00b0f0','#0070c0','#002060','#7030a0'];

// The caller owns the color value; each picker owns only its temporary popup.
export function createColorPicker(button,{getValue=()=>button.value,onChange,emptyLabel='Automatic',emptyValue='#20252c'}={}) {
  const label=button.getAttribute('aria-label')||button.title||'Color';
  button.classList.add('color-picker');button.type='button';
  button.setAttribute('aria-haspopup','dialog');button.setAttribute('aria-expanded','false');
  if(!button.querySelector('.color-sample'))button.insertAdjacentHTML('beforeend','<span class="color-sample" aria-hidden="true"></span><span class="color-arrow" aria-hidden="true">⌄</span>');
  let popup=null;
  function sync(){
    const value=getValue()||emptyValue;
    button.style.setProperty('--picked-color',['none','transparent'].includes(value)?'transparent':value);
    button.classList.toggle('no-color',['none','transparent'].includes(value));
    button.title=label+': '+value;
  }
  function close(focus=false){
    if(!popup)return;
    const old=popup;popup=null;old.remove();button.setAttribute('aria-expanded','false');
    if(focus)button.focus({preventScroll:true});
  }
  function pick(value){onChange(value);sync();close();}
  const swatch=(value,name)=>`<button type="button" class="swatch" data-color="${value}" style="--swatch:${value}" title="${escapeHtml(name)} (${value})" aria-label="${escapeHtml(name)} ${value}"></button>`;
  function open(){
    if(popup){close();return;}
    popup=document.createElement('div');popup.className='color-palette';popup.setAttribute('popover','auto');
    popup.setAttribute('role','dialog');popup.setAttribute('aria-label',label+' Palette');
    popup.innerHTML=`<button type="button" class="palette-automatic" data-color="${emptyValue}"><span class="automatic-sample" style="--swatch:${emptyValue==='none'?'transparent':emptyValue}"></span>${escapeHtml(emptyLabel)}</button>
      <h4>Theme Colors</h4><div class="palette-grid theme-colors">${theme.map(([name,color])=>swatch(color,name)).join('')}</div>
      <div class="palette-grid theme-shades">${[0,1,2,3,4].map(row=>theme.map(([name,,shades])=>swatch(shades[row],name+' Shade '+(row+1))).join('')).join('')}</div>
      <h4>Standard Colors</h4><div class="palette-grid standard-colors">${standard.map(value=>swatch(value,'Standard')).join('')}</div>
      <details class="custom-color"><summary>More Colors…</summary><label>Hex Color <input aria-label="Custom Hex Color" placeholder="#RRGGBB" maxlength="7" spellcheck="false"></label><button type="button" data-custom>Apply Color</button><p role="alert" hidden>Enter a hex color such as #156082.</p></details>`;
    (button.closest('dialog,[popover]')||document.body).append(popup);
    popup.addEventListener('mousedown',event=>{if(event.target.closest('button'))event.preventDefault();});
    popup.addEventListener('click',event=>{
      const color=event.target.closest('[data-color]')?.dataset.color;
      if(color){pick(color);return;}
      if(event.target.closest('[data-custom]')){
        const input=popup.querySelector('input'),value=input.value.trim();
        if(/^#[\da-f]{6}$/i.test(value))pick(value.toLowerCase());
        else {popup.querySelector('[role=alert]').hidden=false;input.focus();}
      }
    });
    popup.addEventListener('keydown',event=>{
      event.stopPropagation();
      if(event.key==='Escape'){event.preventDefault();close(true);return;}
      if(event.target.matches('input')&&event.key==='Enter'){event.preventDefault();popup.querySelector('[data-custom]').click();return;}
      const buttons=[...popup.querySelectorAll('[data-color]')],index=buttons.indexOf(document.activeElement);
      const delta={ArrowRight:1,ArrowLeft:-1,ArrowDown:10,ArrowUp:-10}[event.key];
      if(index>=0&&delta){event.preventDefault();buttons[Math.max(0,Math.min(buttons.length-1,index+delta))].focus();}
    });
    popup.addEventListener('toggle',event=>{if(event.newState==='closed'&&popup===event.target)close();});
    popup.showPopover();
    const anchor=button.getBoundingClientRect(),bounds=popup.getBoundingClientRect();
    popup.style.left=Math.max(8,Math.min(anchor.left,innerWidth-bounds.width-8))+'px';
    popup.style.top=Math.max(8,Math.min(anchor.bottom+4,innerHeight-bounds.height-8))+'px';
    button.setAttribute('aria-expanded','true');popup.querySelector('button').focus({preventScroll:true});
  }
  button.addEventListener('click',open);sync();
  return {sync,close};
}
