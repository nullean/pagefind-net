/**
 * emit-runtime.mjs
 *
 * Uses the Pagefind Node.js API to emit pagefind.js and wasm.*.pagefind
 * into the specified output directory. The pagefind-net data files are
 * written separately by the .NET example; this script only provides the
 * query runtime.
 *
 * Usage:
 *   node emit-runtime.mjs <output-dir>
 *   e.g.: node emit-runtime.mjs ../../wwwroot/pagefind
 */

import { createIndex } from 'pagefind';
import { join, resolve } from 'node:path';
import { mkdirSync } from 'node:fs';

const outputDir = resolve(process.argv[2] ?? 'wwwroot/pagefind');
mkdirSync(outputDir, { recursive: true });

// Create a minimal index — we only want the frontend runtime files.
// Pagefind's writeFiles emits pagefind.js + wasm.*.pagefind alongside the data.
// We will overlay our .NET-generated data files afterwards.
const { index, errors } = await createIndex({});
if (errors.length > 0) {
  console.error('Pagefind createIndex errors:', errors);
  process.exit(1);
}

// Add a dummy page so the index has at least one entry (required to emit wasm).
await index.addHTMLFile({
  url: '/_pagefind_bootstrap/',
  content: '<html><body data-pagefind-body><h1>bootstrap</h1></body></html>',
});

const { errors: writeErrors } = await index.writeFiles({ outputPath: outputDir });
if (writeErrors.length > 0) {
  console.error('Pagefind writeFiles errors:', writeErrors);
  process.exit(1);
}

console.log(`Pagefind runtime written to: ${outputDir}`);
console.log('(pagefind.js and wasm.*.pagefind are now available)');
