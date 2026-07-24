/**
 * emit-runtime.mjs
 *
 * Uses the Pagefind Node.js API to emit pagefind.js and wasm.*.pagefind
 * into the specified output directory.
 *
 * Usage:
 *   node emit-runtime.mjs <output-dir>
 */

import { createIndex } from 'pagefind';
import { resolve } from 'node:path';
import { mkdirSync } from 'node:fs';

const outputDir = resolve(process.argv[2] ?? 'wwwroot/pagefind');
mkdirSync(outputDir, { recursive: true });

const { index, errors } = await createIndex({ language: 'en' });
if (errors.length > 0) {
  console.error('Pagefind createIndex errors:', errors);
  process.exit(1);
}

await index.addHTMLFile({
  url: '/_pagefind_bootstrap/',
  content: '<html lang="en"><body data-pagefind-body><h1>bootstrap</h1></body></html>',
});

const { errors: writeErrors } = await index.writeFiles({ outputPath: outputDir });
if (writeErrors.length > 0) {
  console.error('Pagefind writeFiles errors:', writeErrors);
  process.exit(1);
}

console.log(`Pagefind runtime written to: ${outputDir}`);
