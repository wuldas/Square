# Square.Extensions.Terminal

`Square.Extensions.Terminal` provides a dependency-free ANSI/VT terminal model and a Square `TerminalView` control suitable for SSH or child-process transports.

## Registration

```csharp
using Square.Extensions.Terminal;

TerminalRegistration.RegisterDefaults();
```

The registration adds the `TerminalView` element tag.

## Transport Integration

Remote output is fed into the parser. Local keyboard, text input, and paste data are emitted through `Input`.

```csharp
var terminal = new TerminalView(120, 32, maxScrollback: 5000);
terminal.Input += (_, e) => sshStream.Write(e.Data);

terminal.Feed(remoteText);
terminal.Resize(140, 40);
```

`Input` carries terminal data as a .NET string. The transport is responsible for applying its negotiated character encoding when writing bytes.

## Core API

- `TerminalBuffer`: visible grid, cursor, scrolling region, bounded scrollback, editing operations, resize, and snapshots.
- `TerminalScreen`: primary and alternate buffers, saved cursor state, cursor visibility, and current style.
- `AnsiVtParser`: stateful parser accepting `string` or `ReadOnlySpan<char>` input.
- `TerminalCell` and `TerminalStyle`: character, attributes, and default/indexed/RGB colors.
- `TerminalView`: Square painting, scrolling, selection, copy support, key translation, and transport input events.

The parser supports CR, LF, BS, TAB, ESC save/restore, common CSI cursor/edit/scroll commands, DEC private modes `?25` and `?1049`, and required SGR attributes and color forms.
