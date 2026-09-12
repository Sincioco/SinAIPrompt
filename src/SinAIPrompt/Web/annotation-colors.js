import {createColorPicker} from './color-picker.js';

// Adapt the palette to the inspector's existing values and change events.
// Scene updates and undo remain owned by the annotation dialog.
export function connectAnnotationColors(dialog) {
  const pickers=[['stroke','noStroke'],['fill','noFill'],['textColor',null],['canvasColor','canvasTransparent']].map(([id,transparentId])=>{
    const button=dialog.querySelector('#'+id),transparent=transparentId?dialog.querySelector('#'+transparentId):null;
    return createColorPicker(button,{
      getValue:()=>transparent?.checked?'none':button.value,
      emptyLabel:transparent?'No Color':'Automatic',emptyValue:transparent?'none':'#000000',
      onChange(value){
        if(transparent)transparent.checked=value==='none';
        if(value!=='none')button.value=value;
        button.dispatchEvent(new Event('change',{bubbles:true}));
      }
    });
  });
  return {sync:()=>pickers.forEach(picker=>picker.sync())};
}
