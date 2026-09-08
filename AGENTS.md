# AGENTS.md

`weft` targets .NET 10 and C# 14. Read `CONTRIBUTING.md` before changing code, and
keep `docs/design.md` and `docs/progress.md` current as work lands.

## Required conventions

- Use `Weft.*` namespaces and assemblies.
- Put exactly one class, interface, enum, record, struct, or delegate in each C# file.
- Document every public or internal type and member with triple-slash XML documentation.
- Write every XML `<summary>` as exactly three lines: opening tag, text, closing tag.
- Follow the dotnet/runtime C# coding style: Allman braces, four-space indentation,
  `_camelCase` instance fields, `s_camelCase` static fields, PascalCase constants,
  explicit visibility, no `this.`, language keywords over BCL type names, and `var`
  only when the type is apparent on the right-hand side.
- Use Central Package Management as the single package-version source.
- Support development with any compatible .NET 10 SDK. Do not add or retain a
  `global.json` SDK pin or an exact `dotnet-version` value.
- Do not hard-code versions for SDKs, runtimes, tools, GitHub Actions, or editor
  dependencies. Package manifests may retain versions only where the package
  manager requires them.
- Never use a version pin to work around a CI, network, registry, release, or
  runner failure. Diagnose the failure instead.
- Keep nullable references, analyzers, deterministic builds, and warnings-as-errors enabled.
- Keep every product assembly Native AOT compatible; the `weft` executable is published with Native AOT.
- Direct dependencies are limited to packages owned by Microsoft or the .NET Foundation,
  StreamJsonRpc, and Hex1b. Use System.CommandLine for CLI parsing and Hex1b for terminal UI.
- Never add telemetry.
- Implement repository automation only as .NET file-based C# apps under `scripts/`.
  Do not add shell, PowerShell, batch, or command scripts.

## Testing

- Use MSTest 4 and Microsoft.Testing.Platform.
- Run tests with `dotnet test --solution Weft.slnx`; never use `--no-build` and never pass `--nologo`, which the test host rejects.
- Never change test parallelization settings to work around test or CI failures.
  Diagnose and fix the underlying product, process-lifecycle, or test-isolation defect.
- Use real processes, pseudo-terminals, streams, Unix-domain sockets, and files.
- Never use a mocking library or a hand-written substitute for a production service.
- Synthetic data is allowed only for malformed or hostile input coverage and must
  still pass through a real transport or file boundary.
- The installed `hex1b` tool may be used to drive the built executable end to end.

## Reference material

- Reference repositories for hex1b, sibling projects, other multiplexers, and agent
  runtimes are listed in `docs/references.md`, a local, uncommitted file. Treat every
  reference repository as read-only. Never modify one.
- `docs/progress.md` is a local, uncommitted work tracker. `docs/design.md` is committed.
