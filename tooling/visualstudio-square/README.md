# Square Language Support for Visual Studio

TextMate grammar and Language Server packaging for `.sqx` and `.sqv` files in Visual Studio 2022 17.14 or later.

The extension provides file recognition, TextMate highlighting, folding, bracket matching, embedded C#/CSS highlighting, and the shared Square Language Server for diagnostics, syntax-tree completion, hover, symbols, semantic tokens, component navigation, and CSS color support.

The VSIX bundles the framework-dependent `.NET 10` language server and starts it through `dotnet` when an SQX or SQV document is opened.
