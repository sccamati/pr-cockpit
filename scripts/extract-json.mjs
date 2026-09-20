// "Return only JSON" is a request to a language model, not a guarantee, and the CLI writes
// prose of its own to stdout — a rejected `--model` prints "There's an issue with the
// selected model…" there, and notices land on the same stream.
//
// Those messages can carry an object that is not an answer, e.g.
//
//   [claude-code:unrecognized_model] {"model":"...","query_source":"sdk"}
//
// A failing CLI run exits non-zero and the backend catches it on the exit code, so that
// particular message never reaches here. But taking the first object on the stream would
// mean that anything printed alongside a *successful* run could be passed on as the
// answer, and the backend would report "AI returned an invalid Summary" with nothing in
// the log. So a candidate counts only if it is the shape the backend asked for; anything
// else passes through untouched and the backend logs what really arrived.
//
// ponytail: brace counting outside string literals, not a JSON parser tried over every
// prefix. It handles a ```json fence, a sentence either side, and a banner in front.

/** The one field every answer carries, for both the summary and the per-file task. */
function looksLikeAnAnswer(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value) && 'sentences' in value;
}

/** The balanced object starting at `start`, or null if the braces never close. */
function objectAt(text, start) {
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
  return null;
}

export function extractJson(text) {
  for (let start = text.indexOf('{'); start >= 0; start = text.indexOf('{', start + 1)) {
    const candidate = objectAt(text, start);
    if (candidate === null) break;   // nothing after this point can close either
    try {
      if (looksLikeAnAnswer(JSON.parse(candidate))) return candidate;
    } catch {
      // Not JSON after all — keep looking further along the stream.
    }
  }
  // Nothing here is an answer, so hand back what actually arrived.
  return text;
}
