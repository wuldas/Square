# Changelog

## 0.3.0

- Completion now uses the Square syntax tree for tags, control-flow, attributes, events, Vue directives, and CSS classes.
- Added semantic tokens for built-in controls, directives, and events.
- Fold nested template tags and expose CSS color decorations through the Language Server.
- Keep `</script>` and `</style>` highlighted instead of swallowing them as C#/CSS.

## 0.2.0

- Added the initial Square Language Server client.
- Added live diagnostics for `.sqx` and `.sqv` documents.
- Bundled the framework-dependent `.NET` Language Server in the VSIX.

## 0.1.0

- Add SQX and SQV syntax highlighting.
- Add embedded C# and CSS scopes.
- Add language configuration and starter snippets.
