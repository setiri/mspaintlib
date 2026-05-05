# mspaintlib

A standalone .NET library and Paint.NET v5 plugin for reading MS Paint's `.paint` project files.

The format is a HEIF/MIAF container with uncompressed (`unci`) image items composed via an `iovl` overlay. See [PAINT-FORMAT-SPEC.md](PAINT-FORMAT-SPEC.md) for the full reverse-engineered spec.

## Status

**Phase 1 — read-only.** `PaintDocument.Load` parses `.paint` files; the Paint.NET plugin registers them for *Open* only. Save support is Phase 2.

## Layout

```
src/MsPaintFile/        # the library (no native deps)
src/MsPaintFileType/    # the Paint.NET v5 plugin
tests/MsPaintFile.Tests # xUnit + FluentAssertions
samples/                # .paint fixtures (provided locally)
```

## Build

```
dotnet build
dotnet test
```

The plugin project references Paint.NET assemblies via the `PaintDotNetPath` MSBuild property. Default is `C:\Program Files\paint.net`. Override per-developer by setting the env var or passing `/p:PaintDotNetPath=...` to MSBuild.

## Phase 1 release checklist

- [ ] All unit tests green
- [ ] Integration test against at least one real `.paint` sample passes
- [ ] Truncation fuzz test passes (every random truncation throws `PaintFormatException`, never an unhandled exception)
- [ ] Manual gate: drop `MsPaintFileType.dll` into `…\paint.net\FileTypes\`, open a sample, visually compare layer stack against MS Paint
