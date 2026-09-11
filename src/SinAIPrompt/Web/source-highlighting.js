export const RICH_SOURCE_TEXT_TYPES = Object.freeze([
  Object.freeze({ value: "", label: "None" }),
  Object.freeze({ value: "csharp", label: "C#" }),
  Object.freeze({ value: "tsql", label: "T-SQL" }),
  Object.freeze({ value: "html", label: "HTML" }),
  Object.freeze({ value: "css", label: "CSS" }),
  Object.freeze({ value: "javascript", label: "JavaScript" }),
  Object.freeze({ value: "typescript", label: "TypeScript" }),
  Object.freeze({ value: "json", label: "JSON" }),
  Object.freeze({ value: "java", label: "JAVA" })
]);

const richSourceLanguageDefinitions = Object.freeze({
  csharp: languageDefinition([
    "abstract", "as", "async", "await", "base", "bool", "break", "case", "catch", "class",
    "const", "continue", "decimal", "default", "delegate", "do", "else", "enum", "false", "finally",
    "for", "foreach", "if", "interface", "namespace", "new", "null", "public", "return", "using"
  ], { lineComment: "//", blockComment: ["/*", "*/"] }),
  tsql: languageDefinition([
    "select", "from", "where", "join", "inner", "left", "right", "full", "on", "group",
    "by", "order", "having", "insert", "into", "values", "update", "set", "delete", "create",
    "alter", "drop", "table", "procedure", "as", "begin", "end", "null", "and", "or"
  ], { lineComment: "--", blockComment: ["/*", "*/"], caseInsensitive: true }),
  html: languageDefinition([
    "html", "head", "body", "title", "meta", "link", "script", "style", "div", "span",
    "p", "a", "img", "table", "thead", "tbody", "tr", "th", "td", "ul",
    "ol", "li", "form", "label", "input", "button", "select", "option", "section", "header"
  ], { blockComment: ["<!--", "-->"], caseInsensitive: true }),
  css: languageDefinition([
    "display", "position", "top", "right", "bottom", "left", "width", "height", "margin", "padding",
    "border", "background", "color", "font", "grid", "flex", "gap", "align-items", "justify-content", "overflow",
    "opacity", "transform", "transition", "animation", "content", "cursor", "visibility", "z-index", "box-shadow", "min-width"
  ], { blockComment: ["/*", "*/"], caseInsensitive: true }),
  javascript: languageDefinition([
    "async", "await", "break", "case", "catch", "class", "const", "continue", "default", "delete",
    "do", "else", "export", "extends", "false", "finally", "for", "function", "if", "import",
    "let", "new", "null", "return", "switch", "throw", "true", "try", "typeof", "while"
  ], { lineComment: "//", blockComment: ["/*", "*/"], stringQuotes: ["'", "\"", "`"] }),
  typescript: languageDefinition([
    "any", "as", "async", "await", "boolean", "break", "case", "catch", "class", "const",
    "constructor", "continue", "declare", "default", "else", "enum", "export", "extends", "false", "for",
    "function", "implements", "import", "interface", "let", "namespace", "new", "null", "private", "public"
  ], { lineComment: "//", blockComment: ["/*", "*/"], stringQuotes: ["'", "\"", "`"] }),
  java: languageDefinition([
    "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const",
    "continue", "default", "do", "double", "else", "enum", "extends", "final", "finally", "float",
    "for", "if", "implements", "import", "instanceof", "int", "interface", "long", "new", "package"
  ], { lineComment: "//", blockComment: ["/*", "*/"] })
});

export const RICH_SOURCE_KEYWORD_COUNTS = Object.freeze(Object.fromEntries(
  Object.entries(richSourceLanguageDefinitions).map(([name, definition]) => [name, definition.keywords.size])
));

const richSourceHighlightLimit = 250000;

export function prepareRichSourceHighlight(source, textType, options = {}) {
  let text = String(source || "");
  const type = String(textType || "").trim().toLowerCase();
  if (!type) return { error: "", highlighted: false, html: "", text };

  if (text.length > richSourceHighlightLimit) {
    return {
      error: "Source is too large to color code safely, so it is being shown as plain text.",
      highlighted: false,
      html: "",
      text
    };
  }

  if (type === "json") {
    try {
      const parsed = JSON.parse(text);
      if (options.formatJson) text = JSON.stringify(parsed, null, 2);
      return {
        error: "",
        highlighted: true,
        html: richSourceHighlightHtml(text, jsonLanguageDefinition, true),
        text
      };
    } catch {
      return {
        error: "JSON is invalid, so it is being shown as plain text.",
        highlighted: false,
        html: "",
        text
      };
    }
  }

  const definition = richSourceLanguageDefinitions[type];
  if (!definition) {
    return {
      error: "The selected text type cannot be color coded, so it is being shown as plain text.",
      highlighted: false,
      html: "",
      text
    };
  }

  return {
    error: "",
    highlighted: true,
    html: richSourceHighlightHtml(text, definition, false),
    text
  };
}

const jsonLanguageDefinition = languageDefinition(["false", "null", "true"], {
  stringQuotes: ["\""]
});

function languageDefinition(keywords, options = {}) {
  return Object.freeze({
    blockComment: options.blockComment || null,
    caseInsensitive: Boolean(options.caseInsensitive),
    keywords: new Set(keywords),
    lineComment: options.lineComment || "",
    stringQuotes: options.stringQuotes || ["'", "\""]
  });
}

function richSourceHighlightHtml(source, definition, jsonMode) {
  let html = "";
  let index = 0;

  while (index < source.length) {
    const blockStart = definition.blockComment?.[0] || "";
    if (blockStart && source.startsWith(blockStart, index)) {
      const blockEnd = definition.blockComment[1];
      const endIndex = source.indexOf(blockEnd, index + blockStart.length);
      const finish = endIndex < 0 ? source.length : endIndex + blockEnd.length;
      html += richSourceTokenHtml(source.slice(index, finish), "comment");
      index = finish;
      continue;
    }

    if (definition.lineComment && source.startsWith(definition.lineComment, index)) {
      const endIndex = source.indexOf("\n", index + definition.lineComment.length);
      const finish = endIndex < 0 ? source.length : endIndex;
      html += richSourceTokenHtml(source.slice(index, finish), "comment");
      index = finish;
      continue;
    }

    const character = source[index];
    if (definition.stringQuotes.includes(character)) {
      const finish = richSourceStringEnd(source, index, character);
      const value = source.slice(index, finish);
      const remainder = source.slice(finish);
      const tokenType = jsonMode && /^\s*:/.test(remainder) ? "property" : "string";
      html += richSourceTokenHtml(value, tokenType);
      index = finish;
      continue;
    }

    if (/\d/.test(character)) {
      const match = source.slice(index).match(/^-?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?/i);
      if (match) {
        html += richSourceTokenHtml(match[0], "number");
        index += match[0].length;
        continue;
      }
    }

    if (/[A-Za-z_$]/.test(character)) {
      const match = source.slice(index).match(/^[A-Za-z_$][A-Za-z0-9_$-]*/);
      const word = match?.[0] || character;
      const keyword = definition.caseInsensitive ? word.toLowerCase() : word;
      html += definition.keywords.has(keyword)
        ? richSourceTokenHtml(word, "keyword")
        : escapeRichSourceText(word);
      index += word.length;
      continue;
    }

    html += escapeRichSourceText(character);
    index += 1;
  }

  return html;
}

function richSourceStringEnd(source, start, quote) {
  let escaped = false;
  for (let index = start + 1; index < source.length; index += 1) {
    const character = source[index];
    if (escaped) {
      escaped = false;
      continue;
    }
    if (character === "\\") {
      escaped = true;
      continue;
    }
    if (character === quote) return index + 1;
  }
  return source.length;
}

function richSourceTokenHtml(value, type) {
  return `<span class="rich-source-token-${type}">${escapeRichSourceText(value)}</span>`;
}

function escapeRichSourceText(value) {
  return String(value || "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}
