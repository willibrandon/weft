# Contributing

Install any compatible .NET 11 SDK, including previews. Native AOT publishing on Linux
also needs `clang` and the zlib development headers.

Then run:

```console
dotnet restore Weft.slnx
dotnet build Weft.slnx
dotnet test --solution Weft.slnx
dotnet run --file scripts/Verify-Repository.cs
dotnet format Weft.slnx whitespace --verify-no-changes --no-restore
dotnet format Weft.slnx style --verify-no-changes --no-restore
```

Tests use MSTest 4 on Microsoft.Testing.Platform. Always run `dotnet test` and
never use `--no-build`. Do not pass `--nologo` to `dotnet test`: it is forwarded to the
test host, which rejects it and reports zero tests. Product tests exercise real processes, pseudo-terminals,
Unix-domain sockets, and files. Mocking libraries and hand-written substitutes for
production services are prohibited.

Each C# file contains one type. Every public or internal type and member has
triple-slash XML documentation, and each `<summary>` uses exactly three lines:
an opening tag, one text line, and a closing tag. Code follows the dotnet/runtime
C# coding style, enforced by `.editorconfig` and `dotnet format`.

Builds explicitly enable all code-style analyzers and treat their findings as errors.
CI follows the rolling .NET 11 preview channel and records `dotnet --info` so its SDK
can be compared with a local build. Only the preview SDK notice NETSDK1057 is suppressed.

The current preview CLI native dispatcher looks for `dotnet-format` outside the SDK
directory. If formatting reports a missing tool DLL, run the same command with
`DOTNET_CLI_ENABLEAOT=false` to use the SDK's managed dispatcher. The CI formatting
steps use this setting; it does not affect Native AOT publishing of weft.

Repository automation is implemented only as .NET file-based C# apps under
`scripts/`. Shell, PowerShell, batch, and command scripts are not used.

Design decisions live in `docs/design.md` and work tracking in `docs/progress.md`.
Update both alongside code changes.
