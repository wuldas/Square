import fs from 'node:fs';
import path from 'node:path';

const extensionRoot = path.resolve(import.meta.dirname, '..');
const sharedRoot = path.resolve(extensionRoot, '..', 'square-language');
const files = [
  ['language-configuration.json', 'language-configuration.json'],
  ['syntaxes/sqx.tmLanguage.json', 'syntaxes/sqx.tmLanguage.json'],
  ['syntaxes/sqv.tmLanguage.json', 'syntaxes/sqv.tmLanguage.json']
];

for (const [source, destination] of files) {
  const destinationPath = path.join(extensionRoot, destination);
  fs.mkdirSync(path.dirname(destinationPath), { recursive: true });
  fs.copyFileSync(path.join(sharedRoot, source), destinationPath);
}
