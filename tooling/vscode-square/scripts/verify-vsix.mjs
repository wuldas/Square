import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const vsixPath = path.resolve(process.argv[2] ?? '');
assert.ok(process.argv[2], 'Usage: node scripts/verify-vsix.mjs <path-to-vsix>');
const archive = fs.readFileSync(vsixPath);
const entries = readCentralDirectory(archive);

for (const required of [
  'extension/out/extension.js',
  'extension/server/Square.LanguageServer.dll',
  'extension/server/Square.LanguageServer.runtimeconfig.json',
  'extension/syntaxes/sqx.tmLanguage.json',
  'extension/syntaxes/sqv.tmLanguage.json',
]) {
  assert.ok(entries.has(required), `${required} must be present in ${path.basename(vsixPath)}`);
}

assert.equal(
  [...entries].filter(name => name.includes('/node_modules/')).length,
  0,
  'The bundled extension must not depend on node_modules files',
);

console.log('VS Code VSIX archive verification passed');

function readCentralDirectory(buffer) {
  const eocdSignature = 0x06054b50;
  const centralSignature = 0x02014b50;
  const minimumEocd = 22;
  const searchStart = Math.max(0, buffer.length - 0xffff - minimumEocd);
  let eocd = -1;
  for (let index = buffer.length - minimumEocd; index >= searchStart; index--) {
    if (buffer.readUInt32LE(index) === eocdSignature) {
      eocd = index;
      break;
    }
  }
  assert.ok(eocd >= 0, 'VSIX end-of-central-directory record was not found');

  const entryCount = buffer.readUInt16LE(eocd + 10);
  let offset = buffer.readUInt32LE(eocd + 16);
  const names = new Set();
  for (let entry = 0; entry < entryCount; entry++) {
    assert.equal(buffer.readUInt32LE(offset), centralSignature, 'Invalid VSIX central-directory entry');
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const nameStart = offset + 46;
    names.add(buffer.toString('utf8', nameStart, nameStart + nameLength));
    offset = nameStart + nameLength + extraLength + commentLength;
  }
  return names;
}
