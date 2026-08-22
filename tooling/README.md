# Square IDE tooling

Phase A provides shared TextMate syntax support for `.sqx` and `.sqv` in VS Code, Visual Studio 2022, and Rider.

## Shared grammar

```bash
cd tooling/square-language
npm install
npm test
```

The shared source files are:

- `square-language/syntaxes/sqx.tmLanguage.json`
- `square-language/syntaxes/sqv.tmLanguage.json`
- `square-language/language-configuration.json`

## VS Code

```bash
cd tooling/vscode-square
npm install
npm run package
```

Output: `artifacts/square-language-support.vsix`.

## Visual Studio 2022

```bash
cd tooling/visualstudio-square
dotnet build Square.VisualStudio.csproj -c Release
```

Output: `tooling/visualstudio-square/bin/Release/Square.LanguageSupport.VisualStudio.vsix`.

## Rider

```bash
cd tooling/rider-square
./gradlew buildPlugin
```

The default build resolves Rider 2025.2.3. To use an installed Rider instead:

```bash
JAVA_HOME="C:/path/to/Rider/jbr" ./gradlew buildPlugin -PlocalRiderPath="C:/path/to/Rider"
```

Output: `tooling/rider-square/build/distributions/square-language-support-rider-0.2.0.zip`.

## Current boundary

This phase includes file recognition, TextMate highlighting, embedded C#/CSS scopes, brackets, folding, and snippets where supported by the host. The shared Language Server adds diagnostics plus syntax-tree completion, hover, symbols, definition navigation, and semantic tokens.
