# Square Language Support for Rider

Provides TextMate-based syntax highlighting and initial Language Server support for Square `.sqx` and `.sqv` component files. The plugin manifest declares Rider 2025.2+ compatibility; the current local artifact has also passed JetBrains Plugin Verifier against Rider 2026.2.

The plugin reuses the same grammar files as the VS Code and Visual Studio extensions. The plugin bundles and starts the shared Square Language Server for diagnostics, syntax-tree completion, hover, document symbols, semantic tokens, and component definition navigation.

To build against an installed Rider without downloading the default Rider 2025.2.3 SDK archive:

```bash
JAVA_HOME="C:/path/to/Rider/jbr" ./gradlew buildPlugin -PlocalRiderPath="C:/path/to/Rider"
```
