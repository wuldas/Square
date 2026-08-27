import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { inflateRawSync } from 'node:zlib';

const vsixPath = path.resolve(process.argv[2] ?? '');
assert.ok(process.argv[2], 'Usage: node verify-vsix.mjs <path-to-vsix>');
const archive = fs.readFileSync(vsixPath);
const entries = readCentralDirectory(archive);

for (const required of [
  'Square.VisualStudio.dll',
  'Square.pkgdef',
  'Grammars/sqx.tmLanguage.json',
  'Grammars/sqv.tmLanguage.json',
  'language-configuration.json',
  'Server/Square.LanguageServer.dll',
  'Server/Square.LanguageServer.deps.json',
  'Server/Square.LanguageServer.runtimeconfig.json',
]) {
  assert.ok(entries.has(required), `${required} must be present in ${path.basename(vsixPath)}`);
}

const rootAssemblies = [...entries.keys()]
  .filter(name => !name.includes('/') && name.toLowerCase().endsWith('.dll'));
assert.deepEqual(rootAssemblies, ['Square.VisualStudio.dll']);

const manifest = readEntry(archive, entries.get('extension.vsixmanifest')).toString('utf8');
assert.match(manifest, /Version="0\.2\.0"/);
assert.match(
  manifest,
  /Asset Type="Microsoft\.VisualStudio\.MefComponent" Path="Square\.VisualStudio\.dll"/,
);

console.log('Visual Studio VSIX archive verification passed');

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
  const records = new Map();
  for (let entry = 0; entry < entryCount; entry++) {
    assert.equal(buffer.readUInt32LE(offset), centralSignature, 'Invalid VSIX central-directory entry');
    const compression = buffer.readUInt16LE(offset + 10);
    const compressedSize = buffer.readUInt32LE(offset + 20);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const localOffset = buffer.readUInt32LE(offset + 42);
    const nameStart = offset + 46;
    const name = buffer.toString('utf8', nameStart, nameStart + nameLength);
    records.set(name, { compression, compressedSize, localOffset });
    offset = nameStart + nameLength + extraLength + commentLength;
  }
  return records;
}

function readEntry(buffer, record) {
  assert.ok(record, 'Required VSIX entry was not found');
  assert.equal(buffer.readUInt32LE(record.localOffset), 0x04034b50, 'Invalid VSIX local entry');
  const nameLength = buffer.readUInt16LE(record.localOffset + 26);
  const extraLength = buffer.readUInt16LE(record.localOffset + 28);
  const dataStart = record.localOffset + 30 + nameLength + extraLength;
  const compressed = buffer.subarray(dataStart, dataStart + record.compressedSize);
  if (record.compression === 0) return compressed;
  if (record.compression === 8) return inflateRawSync(compressed);
  throw new Error(`Unsupported VSIX compression method ${record.compression}`);
}
