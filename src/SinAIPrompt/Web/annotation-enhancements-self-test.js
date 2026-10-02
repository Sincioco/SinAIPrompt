import {annotate} from './annotation-ui.js';
import {request} from './bridge.js';

export async function runAnnotationEnhancementTests(check,png) {
  const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  const $=selector=>document.querySelector('dialog.annotation '+selector);
  const click=selector=>$(selector).click();
  async function until(predicate,name){for(let i=0;i<100;i++){if(await predicate())return;await delay(30);}throw Error('Timed out: '+name);}
  async function open(state){const result=annotate(state);await until(()=>$('[data-tool=select]'),'annotation enhancement dialog');$('#canvasZoom').value='100';$('#canvasZoom').dispatchEvent(new Event('change',{bubbles:true}));await delay(30);return {result};}
  const point=(x,y)=>new DOMPoint(x,y).matrixTransform($('#canvas').getScreenCTM());
  const mouse=(type,p,button='left')=>request('test-mouse',{parameters:{type,x:p.x,y:p.y,button:type==='mouseMoved'?'none':button,buttons:type==='mouseReleased'?0:button==='right'?2:1,clickCount:1}});
  async function drag(from,to){await mouse('mousePressed',from);await mouse('mouseMoved',to);await mouse('mouseReleased',to);}
  async function context(selector){const node=$(selector);node.scrollIntoView({block:'nearest'});const b=node.getBoundingClientRect(),p={x:b.x+b.width/2,y:b.y+b.height/2};await mouse('mousePressed',p,'right');await mouse('mouseReleased',p,'right');await until(()=>$('.annotation-menu').matches(':popover-open'),'object context menu');}
  const select=(key,add=false)=>$(`[data-layer="${key}"]`).dispatchEvent(new MouseEvent('click',{bubbles:true,shiftKey:add}));
  const change=(selector,value)=>{const input=$(selector);if(input.type==='checkbox')input.checked=value;else input.value=String(value);input.dispatchEvent(new Event('change',{bubbles:true}));};
  const key=(key,ctrlKey=false)=>$('#canvas').dispatchEvent(new KeyboardEvent('keydown',{key,ctrlKey,bubbles:true,cancelable:true}));
  const order=()=>[...document.querySelectorAll('dialog.annotation [data-layer]')].map(el=>el.dataset.layer).reverse();

  const drawing=await open({version:1,width:400,height:260,blankCanvas:true,objects:[],background:'none'});
  for(const tool of ['arrow','line','rectangle','circle','text']){
    click(`[data-tool=${tool}]`);await drag(point(30,30),point(110,80));await drag(point(150,100),point(210,150));
    check($(`[data-tool=${tool}]`).classList.contains('active')&&!$('#canvas [data-handle]')&&document.querySelectorAll('dialog.annotation [data-layer]').length===2,'Drawing '+tool+' twice keeps that drawing tool active without misleading resize handles');
    click('[data-action=undo]');click('[data-action=undo]');
  }
  click('[data-action=cancel]');await drawing.result;

  const fixtures=[
    {id:'lock-image',name:'Lock image',type:'embedded-image',source:png,x:0,y:0,width:180,height:100,visible:true,imageClip:{x:5,y:8,width:160,height:80},cropCornerRadii:{topLeft:8,topRight:6,bottomLeft:4,bottomRight:2}},
    {id:'lock-text',name:'Lock text',type:'textbox',text:'Keep this text',x:210,y:5,width:140,height:75,fontSize:18,textColor:'#223344',fill:'none',stroke:'#abcdef',strokeWidth:2,visible:true},
    {id:'move-box',name:'Move box',type:'rectangle',x:20,y:150,width:100,height:60,fill:'#ffeeaa',stroke:'#778899',strokeWidth:2,visible:true},
    {id:'lock-line',name:'Lock line',type:'line',x1:175,y1:130,x2:325,y2:150,stroke:'#882233',strokeWidth:4,visible:true},
    {id:'move-circle',name:'Move circle',type:'circle',x:210,y:190,width:60,height:50,fill:'#aaccff',stroke:'#123456',strokeWidth:2,visible:true}
  ];
  const session=await open({version:1,width:400,height:260,objects:structuredClone(fixtures),background:'none'});
  // Lock while the PNG conversion is awaiting image decode. The result must not
  // write its stale snapshot back into the newly locked scene object.
  click('[data-action=applyCrop]');click('[data-action=lock]');
  await until(()=>!$('.annotation-error').textContent,'pending crop after locking');
  check($('#canvas [data-object="lock-image"]').dataset.locked==='true'&&$('#canvas image').getAttribute('href')===png&&$('[data-action=applyCrop]').disabled,'Locking during Apply Crop rejects the pending change and keeps crop controls disabled');
  check(!$('#canvas [data-handle]')&&$('[data-geometry=x]').disabled&&$('#opacity').disabled,'Locked objects have no edit handles and their inspector is disabled');
  change('[data-geometry=x]',88);change('[data-crop-value=left]',42);change('[data-radius=topLeft]',22);
  change('#opacity',12);change('#strokeWidth',22);change('[data-visible="lock-image"]',false);
  click('[data-action=resetAllCrops]');key('ArrowRight');key('Delete');key('Backspace');
  const beforeImage=$('#canvas [data-object="lock-image"]').innerHTML;
  await drag(point(60,40),point(90,65));
  check($('#canvas [data-object="lock-image"]').innerHTML===beforeImage,'Locked image ignores pointer movement, crop, property, visibility and deletion changes');

  select('lock-text');await context('[data-layer="lock-text"]');click('[data-annotation-command=lock]');
  change('#shapeText','Changed');change('#shapeFontSize',60);change('#textColor','#ff0000');
  $('#canvas [data-object="lock-text"]').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));
  check($('#shapeText').disabled&&document.activeElement!==$('#shapeText'),'Locked text remains protected from editing and double-click focus');
  select('lock-line');click('[data-action=lock]');change('[data-endpoint=x2]',400);
  check(!$('#canvas [data-end]'),'Locked line has no endpoint handles');

  select('lock-image');select('lock-text',true);
  for(const format of ['png','svg']){
    await context('[data-layer="lock-image"]');
    check(document.querySelectorAll('dialog.annotation .layer.selected').length===2,'Right-clicking a selected object retains the multi-selection for '+format.toUpperCase());
    click(`[data-annotation-command=${format}]`);
    const expected=format==='png'?'PNG':'image/svg+xml';
    await until(async()=>{
      const formats=await request('test-clipboard-formats')||[],data=await request('annotation-paste');
      return formats.includes(expected)&&data?.objects&&JSON.parse(data.objects).objects.map(o=>o.id).join(',')==='lock-image,lock-text';
    },'direct '+format+' clipboard');
    check(!document.querySelector('dialog.form-dialog')&&JSON.parse((await request('annotation-paste')).objects).objects.length===2,'Right-click Copy '+format.toUpperCase()+' copies both locked objects without an extra format prompt');
  }
  await context('#canvas [data-object="move-circle"]');
  check(document.querySelectorAll('dialog.annotation .layer.selected').length===1&&$('[data-layer="move-circle"]').classList.contains('selected'),'Right-clicking an unselected canvas object selects that object');
  $('.annotation-menu').dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}));
  check(document.querySelector('dialog.annotation').open&&!$('.annotation-menu').matches(':popover-open'),'Escape closes the object context menu while keeping annotation open');

  select('lock-image');select('move-circle',true);const beforeOrder=order();
  click('[data-action=back]');check(order()[0]==='lock-image'&&order()[1]==='lock-text'&&order()[3]==='lock-line','Mixed-selection reorder keeps locked objects in their exact layer slots');
  click('[data-action=undo]');check(order().join(',')===beforeOrder.join(','),'Layer ordering remains undoable');
  key('a',true);key('ArrowRight');change('#opacity',50);
  check($('#canvas [data-object="move-box"] rect').getAttribute('x')==='21'&&$('#canvas [data-object="move-box"]').getAttribute('opacity')==='0.5'&&$('#canvas [data-object="lock-text"]').getAttribute('opacity')==='1','Mixed-selection keyboard and appearance edits change only unlocked objects');
  await drag(point(60,170),point(80,185));
  check(Number($('#canvas [data-object="move-box"] rect').getAttribute('x'))>21&&$('#canvas [data-object="lock-text"] rect').getAttribute('x')==='210','Dragging a mixed selection moves only unlocked objects');
  key('Delete');check(order().join(',')==='lock-image,lock-text,lock-line','Deleting a mixed selection retains every locked object');click('[data-action=undo]');
  select('lock-image');click('[data-action=duplicate]');
  check(order().length===6&&$('#canvas [data-object="lock-image"]').dataset.locked==='true'&&$('#canvas [data-object="'+$('.layer.selected').dataset.layer+'"]').dataset.locked==='false','Duplicating a locked object creates an editable copy without changing the original');click('[data-action=undo]');

  select('lock-image');select('lock-text',true);await context('[data-layer="lock-text"]');click('[data-annotation-command=unlock]');
  check($('#canvas [data-object="lock-image"]').dataset.locked==='false'&&$('#canvas [data-object="lock-text"]').dataset.locked==='false','Context Unlock releases every selected object');
  click('[data-action=undo]');check($('#canvas [data-object="lock-text"]').dataset.locked==='true','Unlock is undoable');
  click('[data-action=redo]');check($('#canvas [data-object="lock-text"]').dataset.locked==='false','Unlock supports redo');
  click('[data-action=lock]');click('[data-action=apply]');const result=await session.result;
  check(fixtures.filter(o=>o.id.startsWith('lock-')).every(original=>JSON.stringify(result.state.objects.find(o=>o.id===original.id))===JSON.stringify({...original,locked:true})),'Every locked object retains all original properties through selection mutations and PNG apply');
  const reopened=await open(JSON.parse(JSON.stringify(result.state)));select('lock-text');
  check($('#shapeText').disabled&&$('#canvas [data-object="lock-text"]').dataset.locked==='true','Serialized annotation state restores object locks on reopening');
  click('[data-action=unlock]');change('#shapeText','Unlocked and editable');
  check($('#canvas [data-object="lock-text"] text').textContent==='Unlocked and editable','Unlock restores normal object editing');
  click('[data-action=cancel]');await reopened.result;
}
