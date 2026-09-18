import {parseHtml,ensureStyle,portableHtml,serialize} from './document.js';

// Parse detached documents; source scripts never enter a live browsing context.
export async function combineDocuments(sources){
  const output=parseHtml('');output.body.replaceChildren();ensureStyle(output);
  const styles=new Set([...output.querySelectorAll('style')].map(style=>style.textContent));
  for(const [index,source] of sources.entries()){
    const input=parseHtml(source.html);
    const base=new URL(input.querySelector('base[href]')?.getAttribute('href')||source.base,source.base).href;
    input.querySelectorAll('script,iframe,object,embed,meta[http-equiv]').forEach(element=>element.remove());
    for(const element of input.querySelectorAll('*'))for(const attribute of [...element.attributes])
      if(/^on/i.test(attribute.name)||/^(href|src|action)$/i.test(attribute.name)&&/^\s*javascript:/i.test(attribute.value))element.removeAttribute(attribute.name);
    // Resolve every source against its own folder before combining it with others.
    input.querySelectorAll('base').forEach(element=>element.remove());
    const baseElement=input.createElement('base');baseElement.href=base;input.head.prepend(baseElement);
    const portable=parseHtml(await portableHtml(serialize(input),base,true));
    for(const style of portable.querySelectorAll('head style'))if(!styles.has(style.textContent)){
      styles.add(style.textContent);output.head.append(output.importNode(style,true));
    }
    const prefix=`sin-combined-${index+1}-`;
    for(const element of portable.body.querySelectorAll('[id]'))element.id=prefix+element.id;
    for(const link of portable.body.querySelectorAll('a[href]')){
      const href=link.getAttribute('href');
      link.setAttribute('href',href.startsWith('#')?'#'+prefix+href.slice(1):new URL(href,base).href);
    }
    const section=output.createElement('section');section.dataset.sinCombined=String(index+1);
    if(portable.body.hasAttribute('style'))section.setAttribute('style',portable.body.getAttribute('style'));
    section.append(...[...portable.body.childNodes].map(node=>output.importNode(node,true)));output.body.append(section);
  }
  let number=1;
  for(const list of output.querySelectorAll('ol')){
    if(list.parentElement.closest('ol,ul'))continue;
    list.removeAttribute('reversed');list.start=number;list.type='1';list.style.listStyleType='decimal';
    for(const item of list.children)if(item.tagName==='LI'){
      item.removeAttribute('value');item.removeAttribute('data-sin-list-mode');item.removeAttribute('data-sin-list-id');number++;
    }
  }
  return serialize(output);
}
