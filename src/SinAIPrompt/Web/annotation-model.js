import {escapeHtml} from './bridge.js';
import {loadImage} from './document.js';

export const clone = value => structuredClone(value);
export const id = () => crypto.randomUUID();
export const isLine = o => ['arrow','line'].includes(o.type);
export const clamp = (v,min,max) => Math.min(max,Math.max(min,Number(v)||0));
export function imageClip(o) {
  if (o.cropVisible === false || !o.imageClip) return {x:o.x,y:o.y,width:o.width,height:o.height};
  const c=o.imageClip;
  const x=clamp(c.x,o.x,o.x+o.width-1), y=clamp(c.y,o.y,o.y+o.height-1);
  return {x,y,width:clamp(c.width,1,o.x+o.width-x),height:clamp(c.height,1,o.y+o.height-y)};
}
export function bounds(o, visual=true) {
  if (isLine(o)) { const p=visual?Math.max(o.strokeWidth||1,o.type==='arrow'?(o.arrowSize||18):0)/2:0; return {x:Math.min(o.x1,o.x2)-p,y:Math.min(o.y1,o.y2)-p,width:Math.abs(o.x2-o.x1)+p*2,height:Math.abs(o.y2-o.y1)+p*2}; }
  if (o.type==='embedded-image' && visual) return imageClip(o);
  const p=visual && o.stroke!=='none' && o.outlineVisible!==false?(o.strokeWidth||0)/2:0;
  return {x:o.x-p,y:o.y-p,width:o.width+2*p,height:o.height+2*p};
}
export function union(items) {
  if(!items.length) return {x:0,y:0,width:800,height:500};
  const x=Math.min(...items.map(o=>o.x)),y=Math.min(...items.map(o=>o.y));
  return {x,y,width:Math.max(1,Math.max(...items.map(o=>o.x+o.width))-x),height:Math.max(1,Math.max(...items.map(o=>o.y+o.height))-y)};
}
export function outputBounds(state) {
  const visible=state.objects.filter(o=>o.visible!==false).map(o=>bounds(o));
  // A transparent starter canvas is a workspace, not padding in the exported image.
  if(state.blankCanvas&&(!visible.length||(state.background&&state.background!=='none')))
    visible.push({x:0,y:0,width:state.width,height:state.height});
  return union(visible);
}
export function move(o,dx,dy) {
  if(isLine(o)){o.x1+=dx;o.x2+=dx;o.y1+=dy;o.y2+=dy;}
  else {o.x+=dx;o.y+=dy;if(o.imageClip){o.imageClip.x+=dx;o.imageClip.y+=dy;}}
}
export function resize(o,b) {
  const sx=b.width/o.width,sy=b.height/o.height;
  if(o.imageClip) o.imageClip={x:b.x+(o.imageClip.x-o.x)*sx,y:b.y+(o.imageClip.y-o.y)*sy,width:o.imageClip.width*sx,height:o.imageClip.height*sy};
  Object.assign(o,b);
}
function roundPath(b,r={}) {
  const max=Math.min(b.width,b.height)/2;
  const [tl,tr,br,bl]=['topLeft','topRight','bottomRight','bottomLeft'].map(k=>clamp(r[k],0,max));
  const {x,y,width:w,height:h}=b;
  return `M${x+tl},${y}H${x+w-tr}Q${x+w},${y} ${x+w},${y+tr}V${y+h-br}Q${x+w},${y+h} ${x+w-br},${y+h}H${x+bl}Q${x},${y+h} ${x},${y+h-bl}V${y+tl}Q${x},${y} ${x+tl},${y}Z`;
}
export function objectSvg(o, interactive=false) {
  const attr=interactive?`data-object="${escapeHtml(o.id)}"`:'';
  const stroke=o.outlineVisible===false?'none':o.stroke||'none';
  const style=`fill="${escapeHtml(o.fill||'none')}" stroke="${escapeHtml(stroke)}" stroke-width="${Number(o.strokeWidth)||0}"`;
  let content='';
  if(o.type==='embedded-image') {
    const clip=imageClip(o), key='clip-'+o.id.replace(/[^a-z0-9]/gi,'');
    content=`<defs><clipPath id="${key}"><path d="${roundPath(clip,o.cropCornerRadii||{topLeft:o.cropCornerRadius,topRight:o.cropCornerRadius,bottomRight:o.cropCornerRadius,bottomLeft:o.cropCornerRadius})}"/></clipPath></defs><image href="${escapeHtml(o.source)}" x="${o.x}" y="${o.y}" width="${o.width}" height="${o.height}" preserveAspectRatio="none" clip-path="url(#${key})"/>`;
  } else if(o.type==='rectangle') content=`<rect x="${o.x}" y="${o.y}" width="${o.width}" height="${o.height}" ${style}/>`;
  else if(o.type==='circle') content=`<ellipse cx="${o.x+o.width/2}" cy="${o.y+o.height/2}" rx="${o.width/2}" ry="${o.height/2}" ${style}/>`;
  else if(isLine(o)) {
    const a=Math.atan2(o.y2-o.y1,o.x2-o.x1),head=Math.min(o.arrowSize||18,Math.hypot(o.x2-o.x1,o.y2-o.y1));
    const endX=o.x2-(o.type==='arrow'?Math.cos(a)*head*.75:0),endY=o.y2-(o.type==='arrow'?Math.sin(a)*head*.75:0);
    content=`<line x1="${o.x1}" y1="${o.y1}" x2="${endX}" y2="${endY}" stroke="${escapeHtml(stroke)}" stroke-width="${o.strokeWidth}" stroke-linecap="round"/>`;
    if(o.type==='arrow') content+=`<polygon points="${o.x2},${o.y2} ${o.x2-Math.cos(a)*head+Math.sin(a)*head*.45},${o.y2-Math.sin(a)*head-Math.cos(a)*head*.45} ${o.x2-Math.cos(a)*head-Math.sin(a)*head*.45},${o.y2-Math.sin(a)*head+Math.cos(a)*head*.45}" fill="${escapeHtml(stroke)}"/>`;
  } else if(o.type==='textbox') {
    content=`<rect x="${o.x}" y="${o.y}" width="${o.width}" height="${o.height}" ${style}/>`;
    content+=`<text x="${o.x+8}" y="${o.y+(o.fontSize||20)+5}" font-family="${escapeHtml(o.fontFamily||'Segoe UI')}" font-size="${o.fontSize||20}" fill="${escapeHtml(o.textColor||'#20252c')}">${String(o.text||'Text').split('\n').map((l,i)=>`<tspan x="${o.x+8}" dy="${i?1.25*(o.fontSize||20):0}">${escapeHtml(l)}</tspan>`).join('')}</text>`;
  }
  if(interactive) {
    if(isLine(o)) content+=`<line x1="${o.x1}" y1="${o.y1}" x2="${o.x2}" y2="${o.y2}" stroke="transparent" stroke-width="${Math.max(12,o.strokeWidth)}" pointer-events="stroke"/>`;
    else {const b=bounds(o);content+=`<rect x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}" fill="transparent" stroke="none"/>`;}
  }
  return `<g ${attr} opacity="${o.visible===false?0:(o.opacity??1)}">${content}</g>`;
}
export function sceneSvg(state,b=outputBounds(state)) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${Math.ceil(b.width)}" height="${Math.ceil(b.height)}" viewBox="${b.x} ${b.y} ${b.width} ${b.height}">${state.background && state.background!=='none'?`<rect x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}" fill="${escapeHtml(state.background)}"/>`:''}${state.objects.filter(o=>o.visible!==false).map(o=>objectSvg(o)).join('')}</svg>`;
}
export async function renderPng(state) {
  const b=outputBounds(state), svg=sceneSvg(state,b);
  if(b.width*b.height>100000000) throw Error('The canvas exceeds 100 megapixels. Reduce its dimensions before applying.');
  const url=URL.createObjectURL(new Blob([svg],{type:'image/svg+xml'}));
  try {
    const image=await loadImage(url), canvas=document.createElement('canvas');canvas.width=Math.ceil(b.width);canvas.height=Math.ceil(b.height);
    canvas.getContext('2d').drawImage(image,0,0); const data=canvas.toDataURL('image/png');
    if(data==='data:,') throw Error('The canvas could not be rendered at this size.');
    return {data,width:canvas.width,height:canvas.height,bounds:b};
  } finally {URL.revokeObjectURL(url);}
}
