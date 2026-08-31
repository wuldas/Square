# Square Language Support

Provides syntax highlighting and basic editing support for Square `.sqx` and `.sqv` component files.

## Included

- SQX and SQV file recognition
- TextMate syntax highlighting
- Embedded C# highlighting in `<script>` and template expressions
- Embedded CSS highlighting in `<style>`
- Bracket matching, auto-closing, folding, and component snippets
- Language Server integration for live SQX/SQV diagnostics, completion, hover, symbols, and semantic tokens

## Language Server

The extension starts the bundled `Square.LanguageServer` through `dotnet` for `.sqx` and `.sqv` documents.

Configuration:

- `square.languageServer.enabled`: enable or disable the client
- `square.languageServer.path`: override the server executable, for example `dotnet`
- `square.languageServer.args`: arguments passed to the configured executable

When `square.languageServer.path` is empty, the extension starts the bundled server DLL. Completion uses the shared Square syntax tree: tags, control-flow directives, attributes, events, Vue directives, and CSS class names. Inside `<style>`, completion covers Square CSS properties, property-specific values, built-in control selectors, template class/ID selectors, pseudo selectors, custom properties, and supported at-rules. Hover, folding, CSS color decorations, document symbols, definition navigation, and semantic tokens are also provided.
