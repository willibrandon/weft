# weft

Durable terminal sessions and a multiplexer for all work.

weft keeps terminal blocks alive inside long-lived sessions on a local server. Close the
window, reconnect from anywhere, and pick up where you left off. Every action is a command,
so people and software drive it the same way.

```console
dotnet tool install --global weft
weft
```

While attached, click **Help** in the bottom bar to see commands and shortcuts.
**F1** opens the same window. Click **Close** or press **Esc** to return to your terminal.

Design: [docs/design.md](docs/design.md).

## Build

Install a compatible .NET 11 SDK, including previews.

```console
dotnet build Weft.slnx
dotnet test --solution Weft.slnx
```
