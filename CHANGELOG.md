# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres loosely to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `MsPaintFile` library: read and write MS Paint `.paint` files (HEIF/MIAF
  container with `unci` layer items and an `iovl` overlay composition).
- Paint.NET v5 file-type plugin: registers `.paint` for Open and Save.
- Round-trip preservation of canvas background fill via Paint.NET's
  `Document.CustomHeaders` (the format stores it as a document-level property
  that has no native Paint.NET equivalent).
- Reverse-engineered format spec (`PAINT-FORMAT-SPEC.md`) covering box layout,
  property semantics, and pixel byte ordering.
- Test suite: 20 tests including box reader, iovl payload, unci decode,
  truncation fuzz, and a Save/Load round-trip integrity check.

### Known limitations
- Layer names, per-layer visibility, and per-layer opacity are not yet
  round-tripped through `.paint` (MS Paint's UI doesn't surface them and we
  haven't located the encoding yet — see spec §3.4).
- Layers smaller than the canvas are rejected on save (`NotSupportedException`).
  Paint.NET always uses canvas-sized layers, so this is currently a no-op
  restriction; will be relaxed when needed.
- Status reflects MS Paint Insider preview builds (~11.2508). Microsoft may
  alter the format before stable release.
