# API reference

pagefind-net exposes a small, focused API surface across two packages.

## Pagefind.Net

The indexer package. All types are in the `Pagefind.Net` namespace.

| Type | Description |
|---|---|
| [`PagefindIndex`](pagefind-index.md) | Accumulates documents and writes the index to disk. |
| [`PagefindRecord`](pagefind-record.md) | A single searchable document. |
| [`WeightedSegment`](pagefind-record.md#weightedsegment) | A text segment with a search weight. |
| [`PagefindAnchor`](pagefind-record.md#pagefindanchor) | A heading anchor for sub-page deep links. |
| `PagefindIndexOptions` | Configuration for language and tokenisation. |

## Pagefind.Net.Frontend

The runtime delivery package. All types are in the `Pagefind.Net.Frontend` namespace.

| Type | Description |
|---|---|
| [`PagefindFrontend`](frontend.md) | Static class for extracting the JS/WASM runtime. |
