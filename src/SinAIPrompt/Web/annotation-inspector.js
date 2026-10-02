import {isLine,clamp,imageClip,resize} from './annotation-model.js';
import {setCropInsets,setCornerRadii} from './annotation-crop.js';

// The inspector adapts controls to explicit objects. The dialog owns the scene,
// selection and undo transaction surrounding each edit.
export function createAnnotationInspector(dialog) {
  const $=s=>dialog.querySelector(s),$$=s=>[...dialog.querySelectorAll(s)];
  const appearance=['stroke','fill','noStroke','noFill','strokeWidth','arrowSize','opacity'];
  const style=()=>({stroke:$('#noStroke').checked?'none':$('#stroke').value,fill:$('#noFill').checked?'none':$('#fill').value,strokeWidth:clamp($('#strokeWidth').value,1,80),arrowSize:clamp($('#arrowSize').value,4,160),opacity:clamp($('#opacity').value,0,100)/100,outlineVisible:!$('#noStroke').checked});

  function sync(objects,cropMode) {
    const o=objects.length===1?objects[0]:null,locked=objects.length>0&&objects.every(o=>o.locked);
    appearance.forEach(key=>$('#'+key).disabled=locked);
    $$('#geometry input,#endpoints input,#imageCrop input,#imageCrop button,#textFields input,#textFields textarea,#textFields button').forEach(el=>el.disabled=!!o?.locked);
    $('#geometry').hidden=!o||isLine(o);$('#endpoints').hidden=!o||!isLine(o);$('#imageCrop').hidden=o?.type!=='embedded-image';$('#textFields').hidden=o?.type!=='textbox';
    if(!o)return;
    if(o.stroke && o.stroke!=='none')$('#stroke').value=o.stroke;if(o.fill && o.fill!=='none')$('#fill').value=o.fill;
    $('#noStroke').checked=o.stroke==='none'||o.outlineVisible===false;$('#noFill').checked=!o.fill||o.fill==='none';$('#strokeWidth').value=o.strokeWidth||4;$('#arrowSize').value=o.arrowSize||20;$('#opacity').value=Math.round((o.opacity??1)*100);
    $$('[data-geometry]').forEach(el=>el.value=Math.round(o[el.dataset.geometry]||0));$$('[data-endpoint]').forEach(el=>el.value=Math.round(o[el.dataset.endpoint]||0));
    if(o.type==='embedded-image') {
      const c=imageClip(o),insets={top:c.y-o.y,left:c.x-o.x,right:o.x+o.width-c.x-c.width,bottom:o.y+o.height-c.y-c.height};
      $$('[data-crop-value]').forEach(el=>el.value=Math.round(insets[el.dataset.cropValue]));
      $$('[data-radius]').forEach(el=>el.value=o.cropCornerRadii?.[el.dataset.radius]??o.cropCornerRadius??0);
    }
    if(o.type==='textbox'){$('#shapeText').value=o.text||'';$('#shapeFontSize').value=o.fontSize||20;$('#textColor').value=o.textColor||'#20252c';}
    $('[data-action=crop]').classList.toggle('active',cropMode);
  }

  function edit(objects,el) {
    if(appearance.includes(el.id)){objects.filter(o=>!o.locked).forEach(o=>Object.assign(o,style()));return;}
    const o=objects.length===1?objects[0]:null;
    if(!o||o.locked)return;
    if(el.dataset.geometry){const b={x:o.x,y:o.y,width:o.width,height:o.height};b[el.dataset.geometry]=Number(el.value)||0;b.width=Math.max(1,b.width);b.height=Math.max(1,b.height);resize(o,b);}
    if(el.dataset.endpoint)o[el.dataset.endpoint]=Number(el.value)||0;
    if(el.dataset.cropValue)setCropInsets(o,Object.fromEntries($$('[data-crop-value]').map(el=>[el.dataset.cropValue,Number(el.value)||0])));
    if(el.dataset.radius)setCornerRadii(o,el.dataset.radius,el.value,$('#syncCorners').checked);
    if(el.id==='shapeText')o.text=el.value;if(el.id==='shapeFontSize')o.fontSize=clamp(el.value,6,200);if(el.id==='textColor')o.textColor=el.value;
  }
  return {style,sync,edit};
}
