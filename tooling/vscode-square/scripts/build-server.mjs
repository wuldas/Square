import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const extensionRoot = path.resolve(import.meta.dirname, '..');
const repositoryRoot = path.resolve(extensionRoot, '..', '..');
const output = path.join(extensionRoot, 'server');
fs.rmSync(output, { recursive: true, force: true });
fs.mkdirSync(output, { recursive: true });

const project = path.join(repositoryRoot, 'tools', 'Square.LanguageServer', 'Square.LanguageServer.csproj');
const result = spawnSync('dotnet', [
  'publish', project,
  '-c', 'Release',
  '--no-restore',
  '-p:DebugType=None',
  '-p:DebugSymbols=false',
  '-o', output,
], { cwd: repositoryRoot, stdio: 'inherit', shell: process.platform === 'win32' });

if (result.status !== 0) process.exit(result.status ?? 1);
console.log(`Published Square Language Server to ${output}`);
