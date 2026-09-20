// Minimal Ai:Summary:Executable adapter: reads one JSON request on stdin
// ({ instruction, context, model }), runs it through the local Claude Code CLI
// (`claude -p`, using its existing login — no API key needed), writes the
// model's raw stdout ({ schemaVersion: 2, sentences: [...], criticalFiles: [...] })
// to our stdout. The instruction carries the contract, so this file only pipes.
//
// ponytail: shells out to the CLI instead of calling the Anthropic API
// directly — one dependency less, but slower and tied to the CLI being
// installed and logged in on this machine.
// ponytail: the instruction goes through a temp file (--system-prompt-file),
// not a --system-prompt argument — cmd.exe's shell:true concatenation mangles
// quotes/braces in a plain argument, a temp file sidesteps that entirely.

import { spawn } from 'node:child_process';
import { writeFile, unlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { extractJson } from './extract-json.mjs';

async function main() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  const input = Buffer.concat(chunks).toString('utf8').replace(/^﻿/, '');
  const request = JSON.parse(input);

  const instructionFile = join(tmpdir(), `pr-cockpit-summary-${randomUUID()}.txt`);
  await writeFile(instructionFile, request.instruction, 'utf8');
  try {
    const output = await runClaude(instructionFile, request.model, JSON.stringify(request.context));
    // The model is asked for bare JSON; what it sends is another matter.
    process.stdout.write(extractJson(output));
  } finally {
    await unlink(instructionFile).catch(() => {});
  }
}

function runClaude(instructionFile, model, contextJson) {
  return new Promise((resolve, reject) => {
    const args = ['-p', '--system-prompt-file', instructionFile, '--output-format', 'text'];
    if (model) args.push('--model', model);

    // claude's npm global bin dir isn't on PATH here, so we point straight at
    // its shim instead of adding a system-wide PATH entry for one script.
    const claudeCli = `${process.env.APPDATA}\\npm\\claude.cmd`;
    const claude = spawn(claudeCli, args, { stdio: ['pipe', 'pipe', 'pipe'], shell: true });

    let stdout = '';
    let stderr = '';
    claude.stdout.on('data', chunk => { stdout += chunk; });
    claude.stderr.on('data', chunk => { stderr += chunk; });
    claude.on('error', reject);
    claude.on('close', code => {
      if (code !== 0) {
        reject(new Error(`claude CLI exited with ${code}: ${stderr}`));
        return;
      }
      resolve(stdout);
    });

    claude.stdin.write(contextJson);
    claude.stdin.end();
  });
}

main().catch(error => {
  process.stderr.write(`${error?.stack ?? error}\n`);
  process.exitCode = 1;
});
