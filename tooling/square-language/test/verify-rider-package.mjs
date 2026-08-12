import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..', '..');
const extensionRoot = path.join(root, 'rider-square');
const build = fs.readFileSync(path.join(extensionRoot, 'build.gradle.kts'), 'utf8');
const pluginXml = fs.readFileSync(path.join(extensionRoot, 'src', 'main', 'resources', 'META-INF', 'plugin.xml'), 'utf8');
const provider = fs.readFileSync(path.join(extensionRoot, 'src', 'main', 'java', 'com', 'wuldas', 'square', 'SquareTextMateBundleProvider.java'), 'utf8');

assert.match(build, /org\.jetbrains\.intellij\.platform/);
assert.match(build, /rider\("2025\.2\.3"\)/);
assert.match(build, /bundledPlugin\("org\.jetbrains\.plugins\.textmate"\)/);
assert.match(build, /square-language[\\/]syntaxes/);
assert.match(build, /vscode-square[\\/]LICENSE\.txt/);
assert.match(pluginXml, /org\.jetbrains\.plugins\.textmate/);
assert.match(pluginXml, /textmate\.bundleProvider/);
assert.match(pluginXml, /com\.intellij\.modules\.lsp/);
assert.match(pluginXml, /platform\.lsp\.serverSupportProvider/);
assert.match(provider, /TextMateBundleProvider/);
assert.match(provider, /Square\.tmBundle/);
const lspProvider = fs.readFileSync(path.join(extensionRoot, 'src', 'main', 'java', 'com', 'wuldas', 'square', 'SquareLspServerSupportProvider.java'), 'utf8');
assert.match(lspProvider, /LspServerSupportProvider/);
assert.match(lspProvider, /ProjectWideLspServerDescriptor/);
assert.match(lspProvider, /sqx/);
assert.match(lspProvider, /sqv/);
assert.match(lspProvider, /Square\.LanguageServer\.dll/);

console.log('Rider package verification passed');
