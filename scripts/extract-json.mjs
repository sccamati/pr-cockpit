// "Return only JSON" is a request to a language model, not a guarantee, and the CLI is
// free to print a notice of its own on the same stream. The backend accepts exactly one
// JSON object, so pull it out of whatever came back rather than passing the noise on and
// turning it into a 502 that says nothing.
//
// ponytail: brace counting outside string literals, not a JSON parser tried over every
// prefix. It handles a ```json fence, a sentence before or after, and a CLI banner. It
// would not handle two separate top-level objects in one answer; if that ever shows up,
// the backend log now prints what came back, so it will be recognisable.
export function extractJson(text) {
  const start = text.indexOf('{');
  if (start < 0) return text;
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let index = start; index < text.length; index++) {
    const char = text[index];
    if (inString) {
      if (escaped) escaped = false;
      else if (char === '\\') escaped = true;
      else if (char === '"') inString = false;
      continue;
    }
    if (char === '"') inString = true;
    else if (char === '{') depth++;
    else if (char === '}' && --depth === 0) return text.slice(start, index + 1);
  }
  // Unbalanced: hand back the original so the backend logs what actually arrived.
  return text;
}
