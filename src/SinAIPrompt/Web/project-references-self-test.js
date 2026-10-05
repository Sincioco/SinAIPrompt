import {parseHtml} from './document.js';
import {rewriteProjectReferences} from './project-references.js';

export function runProjectReferenceTests(check) {
  const oldUrl='file:///D:/Prompts/Sheet%20%231.html',newUrl='file:///D:/Projects/Actors/Sheet%20%231.html';
  const folder={oldUrl:'file:///D:/Prompts/Sheet%20%231/',newUrl:'file:///D:/Projects/Actors/Sheet%20%231/',directory:true};
  const other={oldUrl:'file:///D:/Prompts/Other%20%232.html',newUrl:'file:///D:/Projects/Other/Other%20%232.html',directory:false};
  const rewrite=html=>rewriteProjectReferences(html,oldUrl,newUrl,[folder,other]);
  const paired='<!doctype html>\r\n<html><body><img src="Sheet%20%231/front%20view.png?x=1#face"></body></html>';
  check(rewrite(paired)===paired,'Moving an HTML file with its matching image folder preserves unchanged relative references and original bytes');

  const source=`<!doctype html><html><head><title>Sheet #1/front.png</title></head><body>
    <img id="paired" src="file:///d:/prompts/SHEET%20%231/front%20view.png?size=2&amp;quality=1#face">
    <img id="outside" src="../Shared/a%23b.png?size=2#point" data-sin-original-source="../Shared/a%23b.png">
    <img id="prefix" src="Sheet%20%231-backup/icon.png">
    <a id="other" href="Other%20%232.html#head">Other #2</a><a id="self" href="${oldUrl}?print=1#head">Self</a>
    <a id="hash" href="#head">Local</a><a id="mail" href="mailto:author@example.invalid">Mail</a>
    <img id="remote" src="https://cdn.example.invalid/Sheet%20%231/front.png"><img id="data" src="data:image/png;base64,AA==">
    <video poster="Sheet%20%231/poster.png"><source src="../Shared/movie.mp4"></video>
    <object data="../Shared/example.pdf"></object><div id="literal" data="Sheet #1/front.png">Sheet #1/front.png</div>
    <script>window.__projectMoveExecuted = true; const image = "Sheet #1/front.png";</script>
    <pre>&lt;img src="Sheet #1/front.png"&gt;</pre><img id="invalid" src="http://[">
    <template><img src="../Shared/template.png"></template>
  </body></html>`;
  const doc=parseHtml(rewrite(source)),get=(id,name='src')=>doc.getElementById(id).getAttribute(name);
  check(get('paired')==='Sheet%20%231/front%20view.png?size=2&quality=1#face',
    'Absolute moved image references compare Windows paths without case sensitivity and retain encoded names, query and fragment');
  check(get('outside')==='file:///D:/Shared/a%23b.png?size=2#point'&&get('outside','data-sin-original-source')==='file:///D:/Shared/a%23b.png',
    'Outside images and original-source metadata retain their old targets through absolute local references');
  check(get('prefix')==='file:///D:/Prompts/Sheet%20%231-backup/icon.png','Asset folder mapping respects directory boundaries');
  check(get('other','href')===other.newUrl+'#head'&&get('self','href')==='Sheet%20%231.html?print=1#head',
    'Batch links and absolute self links follow their moved HTML targets');
  check(get('hash','href')==='#head'&&get('mail','href')==='mailto:author@example.invalid'&&
    get('remote')==='https://cdn.example.invalid/Sheet%20%231/front.png'&&get('data')==='data:image/png;base64,AA==',
    'Fragment, mail, remote and embedded-data references keep their original meaning');
  check(doc.querySelector('video').getAttribute('poster')==='Sheet%20%231/poster.png'&&
    doc.querySelector('source').getAttribute('src')==='file:///D:/Shared/movie.mp4'&&doc.querySelector('object').getAttribute('data')==='file:///D:/Shared/example.pdf',
    'Moved HTML preserves video poster, media source and object resource references');
  check(doc.getElementById('literal').textContent==='Sheet #1/front.png'&&get('literal','data')==='Sheet #1/front.png'&&
    doc.querySelector('pre').textContent==='<img src="Sheet #1/front.png">'&&doc.querySelector('script').textContent.includes('const image = "Sheet #1/front.png"')&&
    window.__projectMoveExecuted!==true,'Reference rewriting preserves ordinary text, code and inline scripts without executing document scripts');
  check(get('invalid')==='http://['&&doc.querySelector('template').content.querySelector('img').getAttribute('src')==='file:///D:/Shared/template.png',
    'Invalid unrelated URLs remain unchanged while inert template resources are rebased');

  const sets=parseHtml(rewrite('<img srcset="data:image/png;base64,AA== 1x, ../Shared/retina.png 2x, Sheet%20%231/wide.png 800w"><link imagesrcset="../Shared/preload.png 1x">'));
  check(sets.querySelector('img').getAttribute('srcset')==='data:image/png;base64,AA== 1x, file:///D:/Shared/retina.png 2x, Sheet%20%231/wide.png 800w'&&
    sets.querySelector('link').getAttribute('imagesrcset')==='file:///D:/Shared/preload.png 1x',
    'Responsive image candidates preserve data-URI commas and descriptors while rebasing local candidates');
  const css='/* url(../Shared/comment.png) */ .a{content:"url(../Shared/text.png)";background:url("../Shared/back.png")} @import "../Shared/theme.css"; .b{mask:url(#shape)}';
  const styled=parseHtml(rewrite('<style>'+css+'</style><p style="background:url(../Shared/inline.png)"></p><svg><image href="../Shared/vector.png"></image><use xlink:href="#shape"></use></svg>'));
  check(styled.querySelector('style').textContent.includes('url("file:///D:/Shared/back.png")')&&styled.querySelector('style').textContent.includes('@import "file:///D:/Shared/theme.css"')&&
    styled.querySelector('p').getAttribute('style').includes('file:///D:/Shared/inline.png')&&styled.querySelector('image').getAttribute('href')==='file:///D:/Shared/vector.png',
    'Style sheets, inline CSS, CSS imports and SVG images retain their local resource targets');
  check(styled.querySelector('style').textContent.includes('/* url(../Shared/comment.png) */')&&styled.querySelector('style').textContent.includes('content:"url(../Shared/text.png)"')&&
    styled.querySelector('style').textContent.includes('mask:url(#shape)')&&styled.querySelector('use').getAttribute('xlink:href')==='#shape',
    'CSS comments, content strings and fragment references are not blind text replacements');
  const escaped=parseHtml(rewrite('<style>.a{background:url(../Shared/space\\20 name.png)}</style>'));
  check(escaped.querySelector('style').textContent.includes('file:///D:/Shared/space%20name.png'),'CSS escaped spaces resolve to the original local file');

  const outsideBase=parseHtml(rewrite('<base href="../Shared/"><img src="front.png"><a href="#face">Face</a>'));
  check(outsideBase.querySelector('base').getAttribute('href')==='file:///D:/Shared/'&&outsideBase.querySelector('img').getAttribute('src')==='front.png'&&
    new URL(outsideBase.querySelector('a').getAttribute('href'),outsideBase.querySelector('base').getAttribute('href')).href==='file:///D:/Shared/#face',
    'An explicit outside base preserves its resolved folder and relative or fragment references');
  const insideBase='<base href="Sheet%20%231/"><img src="front.png"><a href="../Sheet%20%231.html#face">Face</a>';
  check(rewrite(insideBase)===insideBase,'An explicit base inside the moved image folder keeps unchanged references byte-for-byte');
  const remoteBase='<base href="https://cdn.example.invalid/assets/"><img src="front.png"><img src="//images.example.invalid/a.png"><a href="#face">Face</a>';
  check(rewrite(remoteBase)===remoteBase,'An explicit remote base leaves its relative and protocol-relative resources unchanged');
  const virtual=parseHtml(rewrite('<img src="https://sin-document.local/Sheet%20%231/front.png">'));
  check(virtual.querySelector('img').getAttribute('src')==='Sheet%20%231/front.png','Editor virtual references become portable moved image references');
  const windowsLines=rewrite('<!doctype html>\r\n<html>\r\n<body><img src="../Shared/a.png"></body>\r\n</html>');
  check(windowsLines.startsWith('<!DOCTYPE html>\r\n')&&!/(?<!\r)\n/.test(windowsLines),'Changed HTML retains Windows CRLF line endings');
}
