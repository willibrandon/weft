# weft

Durable terminal sessions and a multiplexer for all work.

weft keeps terminal blocks alive inside long-lived sessions on a local server. Close the
window, reconnect from anywhere, and pick up where you left off. Every action is a command,
so people and software drive it the same way.

```console
dotnet tool install --global weft
weft
```

Design: [docs/design.md](docs/design.md).

## Build

```console
dotnet build Weft.slnx
dotnet test --solution Weft.slnx
```
