// Read from a document saved by the running Word instance on 2026-09-12.
// Word stores paragraph spacing in twips and automatic line spacing in 240ths.
const bodyFont='Aptos, "Segoe UI", Arial, sans-serif';
const displayFont='"Aptos Display", Aptos, "Segoe UI", Arial, sans-serif';
const common={fontWeight:'400',fontStyle:'normal',color:'#000000',textAlign:'left',textIndent:'0',letterSpacing:'normal',marginLeft:'0',marginRight:'0',paddingBottom:'0',borderBottom:'none',orphans:'2',widows:'2'};
export const wordStyles = [
  {id:'normal',name:'Normal',tag:'p',next:'normal',css:{...common,fontFamily:bodyFont,fontSize:'12pt',lineHeight:String(278/240),marginTop:'0pt',marginBottom:'8pt'}},
  {id:'no-spacing',name:'No Spacing',tag:'p',next:'no-spacing',css:{...common,fontFamily:bodyFont,fontSize:'12pt',lineHeight:'1',marginTop:'0pt',marginBottom:'0pt'}},
  {id:'heading',name:'Heading',tag:'h1',next:'normal',css:{...common,fontFamily:displayFont,fontSize:'20pt',color:'#0f4761',lineHeight:String(278/240),marginTop:'18pt',marginBottom:'4pt',breakAfter:'avoid',breakInside:'avoid'}},
  {id:'heading2',name:'Heading2',tag:'h2',next:'normal',css:{...common,fontFamily:displayFont,fontSize:'16pt',color:'#0f4761',lineHeight:String(278/240),marginTop:'8pt',marginBottom:'4pt',breakAfter:'avoid',breakInside:'avoid'}},
  {id:'title',name:'Title',tag:'p',next:'normal',css:{...common,fontFamily:displayFont,fontSize:'28pt',lineHeight:'1',marginTop:'0pt',marginBottom:'4pt',letterSpacing:'-.5pt'}}
];
// Modern matches the supplied AI-Instructions.html reference. The document,
// rather than a global preference or toolbar instance, owns its style mode.
const modernCommon={...common,fontFamily:'"Segoe UI", Arial, sans-serif',fontSize:'16px',color:'#1f2328',lineHeight:'1.65',marginTop:'0',marginBottom:'1em'};
const modernHeading={...modernCommon,fontWeight:'600',lineHeight:'1.3',marginBottom:'.65em',paddingBottom:'.3em',borderBottom:'1px solid #d1d9e0',breakAfter:'avoid'};
export const modernStyles = [
  {id:'normal',name:'Normal',tag:'p',next:'normal',css:modernCommon},
  {id:'no-spacing',name:'No Spacing',tag:'p',next:'no-spacing',css:{...modernCommon,lineHeight:'1',marginBottom:'0'}},
  {id:'heading',name:'Heading',tag:'h1',next:'normal',css:{...modernHeading,fontSize:'22pt',marginTop:'1.75em'}},
  {id:'heading2',name:'Heading2',tag:'h2',next:'normal',css:{...modernHeading,fontSize:'28pt',marginTop:'1.7em'}},
  {id:'title',name:'Title',tag:'p',next:'normal',css:{...modernHeading,fontSize:'34pt',marginTop:'0'}}
];
export const documentStyleMode=doc=>doc?.documentElement.dataset.sinStyleMode==='modern'?'modern':'office';
export const stylesFor=doc=>documentStyleMode(doc)==='modern'?modernStyles:wordStyles;
export const styleById=(id,doc)=>stylesFor(doc).find(style=>style.id===id);
export const cssText=properties=>Object.entries(properties).map(([key,value])=>`${key.replace(/[A-Z]/g,ch=>'-'+ch.toLowerCase())}:${value}`).join(';');
export const wordDocumentStyles=`body{font-family:${bodyFont};font-size:12pt;color:#000;line-height:${278/240};font-kerning:normal;font-variant-ligatures:common-ligatures contextual}p{margin:0 0 8pt}h1{${cssText(styleById('heading').css)}}h2{${cssText(styleById('heading2').css)}}[data-sin-style=title]+[data-sin-style=title]{margin-top:-4pt}`;

const modernDocumentStyles=`
html{font-size:16px;line-height:1.65;color:#1f2328;background:#fff}
body{box-sizing:border-box;width:100%;max-width:980px;margin:0 auto;padding:48px 40px 64px;font-family:"Segoe UI",Arial,sans-serif;font-size:16px;line-height:1.65;color:#1f2328}
p{margin:0 0 1em}
ol,ul{margin:0 0 1em;padding-left:2em}
li+li{margin-top:.35em}
h1{${cssText(modernStyles[2].css)}}
h2{${cssText(modernStyles[3].css)}}
h3{font-weight:600;line-height:1.3;color:#1f2328;margin:1.4em 0 .65em;font-size:1.2em}
body>:first-child:is(h1,[data-sin-style=heading]){margin-top:0!important}
[data-sin-style=title]+[data-sin-style=title]{margin-top:0}
code{padding:.16em .35em;border-radius:4px;font-family:Consolas,"Cascadia Mono","Courier New",monospace;font-size:.875em;background:#eff1f3;white-space:break-spaces;overflow-wrap:anywhere}
pre[data-sin-code] code{padding:0;background:transparent}
@media(max-width:640px){body{padding:24px 20px 40px}}
@media print{body{max-width:none;padding:0}p,li{orphans:3;widows:3}}
`;

export function setDocumentStyle(doc,mode) {
  mode=mode==='modern'?'modern':'office';
  doc.documentElement.dataset.sinStyleMode=mode;
  let sheet=doc.querySelector('style[data-sin-theme]');
  if(!sheet){sheet=doc.createElement('style');sheet.dataset.sinTheme='1';doc.head.append(sheet);}
  sheet.textContent=mode==='modern'?modernDocumentStyles:wordDocumentStyles+`
    body{width:auto;max-width:none;margin:32px;padding:0}
    ol,ul{margin:0 0 8pt;padding-left:2em}li+li{margin-top:0}
    h3{font-family:${displayFont};font-size:14pt;font-weight:400;color:#0f4761;line-height:${278/240};margin:8pt 0 4pt}
    code{padding:0;border-radius:0;background:transparent}`;
  for(const block of doc.body.querySelectorAll('[data-sin-style]')){
    const preset=styleById(block.dataset.sinStyle,doc);
    if(preset)Object.assign(block.style,preset.css);
  }
}
