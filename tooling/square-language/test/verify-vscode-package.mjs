import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..', '..');
const extensionRoot = path.join(root, 'vscode-square');
const sharedRoot = path.join(root, 'square-language');
const manifest = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'package.json'), 'utf8'));
assert.ok(manifest.activationEvents.includes('onLanguage:sqx'));
assert.ok(manifest.activationEvents.includes('onLanguage:sqv'));
assert.ok(manifest.contributes.configuration);
assert.ok(manifest.contributes.configuration.properties['square.languageServer.path']);
assert.ok(manifest.contributes.configuration.properties['square.languageServer.args']);
assert.match(manifest.main, /out[\\/]extension\.js$/);

const languages = new Map(manifest.contributes.languages.map(language => [language.id, language]));
assert.deepEqual(languages.get('sqx').extensions, ['.sqx']);
assert.deepEqual(languages.get('sqv').extensions, ['.sqv']);
assert.equal(languages.get('sqx').configuration, './language-configuration.json');
assert.equal(languages.get('sqv').configuration, './language-configuration.json');

const grammars = new Map(manifest.contributes.grammars.map(grammar => [grammar.language, grammar]));
assert.equal(grammars.get('sqx').scopeName, 'source.sqx');
assert.equal(grammars.get('sqv').scopeName, 'source.sqv');
assert.equal(grammars.get('sqx').embeddedLanguages['source.cs'], 'csharp');
assert.equal(grammars.get('sqx').embeddedLanguages['source.css'], 'css');
assert.equal(grammars.get('sqv').embeddedLanguages['source.cs'], 'csharp');
assert.equal(grammars.get('sqv').embeddedLanguages['source.css'], 'css');

function digest(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

for (const file of ['language-configuration.json', 'syntaxes/sqx.tmLanguage.json', 'syntaxes/sqv.tmLanguage.json']) {
  const shared = file.startsWith('syntaxes/') ? path.join(sharedRoot, file) : path.join(sharedRoot, file);
  const packaged = path.join(extensionRoot, file);
  assert.equal(digest(packaged), digest(shared), `${file} must be copied from the shared source`);
}

const snippets = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'snippets', 'square.json'), 'utf8'));
assert.ok(snippets['Square component']);
assert.ok(snippets['Square Vue component']);
assert.match(fs.readFileSync(path.join(extensionRoot, 'LICENSE.txt'), 'utf8'), /MIT License/);
assert.ok(fs.existsSync(path.join(extensionRoot, 'src', 'extension.ts')));
assert.ok(fs.existsSync(path.join(extensionRoot, 'tsconfig.json')));
assert.ok(fs.existsSync(path.join(extensionRoot, 'server', 'Square.LanguageServer.dll')));
assert.ok(fs.existsSync(path.join(extensionRoot, 'server', 'Square.LanguageServer.runtimeconfig.json')));

console.log('VS Code package verification passed');
