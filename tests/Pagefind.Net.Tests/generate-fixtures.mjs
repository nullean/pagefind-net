/**
 * generate-fixtures.mjs
 *
 * Generates fixture data for the parity tests using the official pagefind
 * Node.js API. Run once from this directory after `npm install`:
 *
 *   cd tests/Pagefind.Net.Tests
 *   npm install
 *   node generate-fixtures.mjs
 *
 * Outputs (committed to the repo, read by the [Skip]-free parity tests):
 *   Fixtures/reference-pagefind/   — pagefind output for Corpus/
 *   Fixtures/tokenizer-parity.json — { input, expected[] } test cases
 *   Fixtures/stemmer-parity.csv    — input,expected_stem pairs via pagefind
 */

import { createIndex } from 'pagefind';
import { gunzipSync } from 'zlib';
import { mkdirSync, writeFileSync, readFileSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const fixturesDir = resolve(__dirname, 'Fixtures');
const corpusDir   = resolve(__dirname, 'Corpus');

mkdirSync(fixturesDir, { recursive: true });

// ── Minimal CBOR decoder (pagefind subset: arrays, ints, text strings) ────────

function decodeCbor(buf, offset = 0) {
  const byte = buf[offset];
  const mt   = (byte >> 5) & 0x7;
  const ai   = byte & 0x1f;
  offset++;
  let len;
  if (ai <= 23)      len = ai;
  else if (ai === 24) len = buf[offset++];
  else if (ai === 25) { len = (buf[offset] << 8) | buf[offset + 1]; offset += 2; }
  else if (ai === 26) { len = buf.readUInt32BE(offset); offset += 4; }
  if (mt === 0) return { v: len, o: offset };
  if (mt === 1) return { v: -(1 + len), o: offset };
  if (mt === 3) return { v: buf.slice(offset, offset + len).toString('utf8'), o: offset + len };
  if (mt === 4) {
    const arr = [];
    for (let i = 0; i < len; i++) { const r = decodeCbor(buf, offset); arr.push(r.v); offset = r.o; }
    return { v: arr, o: offset };
  }
  return { v: null, o: offset };
}

/** Decodes a framed pagefind file: gunzip → skip 12-byte magic → CBOR decode. */
function decodeFramed(fileContent) {
  const dec = gunzipSync(Buffer.from(fileContent)).slice(12); // skip "pagefind_dcd"
  return decodeCbor(dec).v;
}

/** Decodes a framed pf_index file and returns the word list. */
function extractWords(fileContent) {
  const data = decodeFramed(fileContent);
  return data[0].map(entry => entry[0]); // [word, postings, variants] → word
}

/**
 * Decodes a pf_index into a structured word→postings map:
 * { word: { pageIndex, locs: number[], metaLocs: number[] }[] }
 */
function extractFullIndex(fileContent) {
  const data = decodeFramed(fileContent);
  const words = {};
  for (const entry of data[0]) {
    const [word, postings, variants] = entry;
    const decoded = [];
    let prevPage = 0;
    for (const posting of postings) {
      const [pageDelta, locs, metaLocs] = posting;
      prevPage += pageDelta;
      decoded.push({ pageIndex: prevPage, locs, metaLocs });
    }
    words[word] = { postings: decoded, variants: variants ?? [] };
  }
  return words;
}

/**
 * Decodes a pf_meta into a structured object:
 * { version, pages: [{hash, wordCount}], indexChunks: [{from, to, hash}], ... }
 */
function extractMeta(fileContent) {
  const data = decodeFramed(fileContent);
  return {
    version: data[0],
    pages: data[1].map(p => ({ hash: p[0], wordCount: p[1] })),
    indexChunks: data[2].map(c => ({ from: c[0], to: c[1], hash: c[2] })),
    filterChunks: data[3],
    sorts: data[4],
    metaFields: data[5],
  };
}

/**
 * Decodes a pf_fragment: gunzip → skip magic → parse JSON.
 */
function extractFragment(fileContent) {
  const dec = gunzipSync(Buffer.from(fileContent)).slice(12);
  return JSON.parse(dec.toString('utf8'));
}

/** Indexes a single HTML string with pagefind and returns the indexed word list. */
async function indexAndGetWords(html) {
  const { index, errors } = await createIndex({ language: 'en' });
  if (errors.length) throw new Error('createIndex: ' + errors.join(', '));
  await index.addHTMLFile({ url: '/test/', content: html });
  const { files, errors: fe } = await index.getFiles();
  if (fe.length) throw new Error('getFiles: ' + fe.join(', '));
  for (const f of files) {
    if (f.path.includes('.pf_index')) return extractWords(f.content);
  }
  return [];
}

// ── 1. Reference corpus output ─────────────────────────────────────────────────

console.log('Generating Fixtures/reference-pagefind/ …');
const refDir = resolve(fixturesDir, 'reference-pagefind');
mkdirSync(refDir, { recursive: true });
{
  const { index, errors } = await createIndex({ language: 'en' });
  if (errors.length) throw new Error(errors.join(', '));

  for (const [slug, file] of [
    ['/getting-started/', 'getting-started.html'],
    ['/configuration/',   'configuration.html'],
    ['/api-reference/',   'api-reference.html'],
  ]) {
    const html = readFileSync(resolve(corpusDir, file), 'utf8');
    const { errors: ae } = await index.addHTMLFile({ url: slug, content: html });
    if (ae.length) console.warn('addHTMLFile errors:', ae);
  }

  const { errors: we } = await index.writeFiles({ outputPath: refDir });
  if (we.length) throw new Error(we.join(', '));
}
console.log('  done.');

// ── 1b. Decoded reference data from addCustomRecord (for deep parity tests) ───
//
// Uses addCustomRecord so that both the official pagefind and the .NET port
// index the *exact same* content with weight 1. This isolates the encoding
// layer from HTML parsing / weight assignment differences.

console.log('Generating Fixtures/reference-decoded/ …');
const decodedDir = resolve(fixturesDir, 'reference-decoded');
mkdirSync(decodedDir, { recursive: true });

// These records are shared between generate-fixtures.mjs and IndexParityTests.cs.
// Keep them in sync — the .NET test defines identical PagefindRecord objects.
// Uses real PascalCase content to verify full tokenizer parity (compound splitting).
const customRecords = [
  {
    url: '/getting-started/',
    content: 'Welcome to pagefind-net. Install the NuGet package and call WriteAsync.',
    language: 'en',
    meta: { title: 'Getting Started' },
  },
  {
    url: '/configuration/',
    content: 'Configure PagefindIndexOptions: Language, IncludeCharacters, WeightedSegments.',
    language: 'en',
    meta: { title: 'Configuration' },
  },
  {
    url: '/api-reference/',
    content: 'PagefindIndex, PagefindRecord, WeightedSegment, PagefindAnchor — full API reference.',
    language: 'en',
    meta: { title: 'API Reference' },
  },
];
{
  const { index, errors } = await createIndex({ language: 'en' });
  if (errors.length) throw new Error(errors.join(', '));

  for (const rec of customRecords) {
    const { errors: ae } = await index.addCustomRecord(rec);
    if (ae.length) console.warn('addCustomRecord errors:', ae);
  }

  const { files, errors: ge } = await index.getFiles();
  if (ge.length) throw new Error(ge.join(', '));

  // Decode and dump index chunks
  const mergedIndex = {};
  for (const f of files) {
    if (f.path.includes('.pf_index')) {
      const words = extractFullIndex(f.content);
      Object.assign(mergedIndex, words);
    }
  }
  writeFileSync(resolve(decodedDir, 'index-words.json'),
    JSON.stringify(mergedIndex, null, 2));
  console.log(`  index-words.json: ${Object.keys(mergedIndex).length} words`);

  // Decode and dump fragments
  const fragments = [];
  for (const f of files) {
    if (f.path.includes('.pf_fragment')) {
      fragments.push(extractFragment(f.content));
    }
  }
  fragments.sort((a, b) => a.url.localeCompare(b.url));
  writeFileSync(resolve(decodedDir, 'fragments.json'),
    JSON.stringify(fragments, null, 2));
  console.log(`  fragments.json: ${fragments.length} fragments`);

  // Decode and dump meta
  for (const f of files) {
    if (f.path.includes('.pf_meta')) {
      const meta = extractMeta(f.content);
      writeFileSync(resolve(decodedDir, 'meta.json'),
        JSON.stringify(meta, null, 2));
      console.log(`  meta.json: ${meta.pages.length} pages, ${meta.indexChunks.length} chunks`);
    }
  }

  // Dump entry.json
  for (const f of files) {
    if (f.path.endsWith('pagefind-entry.json')) {
      const entry = JSON.parse(Buffer.from(f.content).toString('utf8'));
      writeFileSync(resolve(decodedDir, 'entry.json'),
        JSON.stringify(entry, null, 2));
      console.log('  entry.json');
    }
  }
}
console.log('  done.');

// ── 1c. Decoded reference data from addHTMLFile (for full HTML parity tests) ──
//
// Indexes the corpus HTML with addHTMLFile and dumps decoded structures.
// The .NET AddHtmlRecord test constructs equivalent PagefindRecords from
// the same HTML and compares index output against these references.

console.log('Generating Fixtures/reference-html-decoded/ …');
const htmlDecodedDir = resolve(fixturesDir, 'reference-html-decoded');
mkdirSync(htmlDecodedDir, { recursive: true });
{
  const { index, errors } = await createIndex({ language: 'en' });
  if (errors.length) throw new Error(errors.join(', '));

  for (const [slug, file] of [
    ['/getting-started/', 'getting-started.html'],
    ['/configuration/',   'configuration.html'],
    ['/api-reference/',   'api-reference.html'],
  ]) {
    const html = readFileSync(resolve(corpusDir, file), 'utf8');
    await index.addHTMLFile({ url: slug, content: html });
  }

  const { files, errors: ge } = await index.getFiles();
  if (ge.length) throw new Error(ge.join(', '));

  const mergedIndex = {};
  for (const f of files) {
    if (f.path.includes('.pf_index')) {
      Object.assign(mergedIndex, extractFullIndex(f.content));
    }
  }
  writeFileSync(resolve(htmlDecodedDir, 'index-words.json'),
    JSON.stringify(mergedIndex, null, 2));
  console.log(`  index-words.json: ${Object.keys(mergedIndex).length} words`);

  const fragments = [];
  for (const f of files) {
    if (f.path.includes('.pf_fragment')) {
      fragments.push(extractFragment(f.content));
    }
  }
  fragments.sort((a, b) => a.url.localeCompare(b.url));
  writeFileSync(resolve(htmlDecodedDir, 'fragments.json'),
    JSON.stringify(fragments, null, 2));
  console.log(`  fragments.json: ${fragments.length} fragments`);

  for (const f of files) {
    if (f.path.includes('.pf_meta')) {
      const meta = extractMeta(f.content);
      writeFileSync(resolve(htmlDecodedDir, 'meta.json'),
        JSON.stringify(meta, null, 2));
      console.log(`  meta.json: ${meta.pages.length} pages, ${meta.indexChunks.length} chunks`);
    }
  }

  for (const f of files) {
    if (f.path.endsWith('pagefind-entry.json')) {
      const entry = JSON.parse(Buffer.from(f.content).toString('utf8'));
      writeFileSync(resolve(htmlDecodedDir, 'entry.json'),
        JSON.stringify(entry, null, 2));
      console.log('  entry.json');
    }
  }
}
console.log('  done.');

// ── 2. Tokenizer parity fixture ────────────────────────────────────────────────
//
// We only include inputs where every token is stable under English Snowball
// stemming (stem == token), so the pagefind pf_index word list directly gives
// us the expected tokenizer output.  The expected arrays are sourced from
// pagefind's actual output — this is a true parity fixture.

console.log('Generating Fixtures/tokenizer-parity.json …');
{
  // Inputs chosen so all tokens are stem-stable (stem == token).
  const inputs = [
    'hello world',         // basic split + lowercase
    'Hello WORLD',         // uppercase normalisation
    'foo.bar baz',         // compound split: emits joined form + parts
    'hello! world?',       // strip punctuation
    'hello   world',       // multiple spaces
    'café',                // NFD + strip Mn (diacritic-free)
    'html css json',       // stem-stable words
    'index',               // stable
  ];

  const fixture = [];
  for (const input of inputs) {
    const html     = `<html lang="en"><body data-pagefind-body>${input}</body></html>`;
    const expected = (await indexAndGetWords(html)).sort();
    fixture.push({ input, expected });
    console.log(`  "${input}" → [${expected.join(', ')}]`);
  }

  writeFileSync(resolve(fixturesDir, 'tokenizer-parity.json'),
    JSON.stringify(fixture, null, 2));
}
console.log('  done.');

// ── 3. Stemmer parity fixture ──────────────────────────────────────────────────
//
// For each word: index it alone, read the stem from the pagefind pf_index.

console.log('Generating Fixtures/stemmer-parity.csv …');
{
  const words = [
    'running', 'jumps', 'easily', 'fishing', 'generously', 'troubled',
    'index', 'indexing', 'indexes', 'create', 'creates', 'created', 'creating',
    'configure', 'language', 'install', 'installation',
    'package', 'packages', 'search', 'searches', 'searching',
    'record', 'records', 'weight', 'weighted', 'anchor', 'anchors',
    'generate', 'generated', 'generation',
  ];

  const pairs = [];
  for (const word of words) {
    const html  = `<html lang="en"><body data-pagefind-body>${word}</body></html>`;
    const found = await indexAndGetWords(html);
    const stem  = found[0] ?? word;
    pairs.push([word, stem]);
  }

  const csv = ['input,expected', ...pairs.map(([a, b]) => `${a},${b}`)].join('\n');
  writeFileSync(resolve(fixturesDir, 'stemmer-parity.csv'), csv);
}
console.log('  done.');

console.log('\nAll fixtures written to:', fixturesDir);
