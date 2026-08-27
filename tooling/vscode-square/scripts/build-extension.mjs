import fs from 'node:fs';
import path from 'node:path';
import { build } from 'esbuild';

const extensionRoot = path.resolve(import.meta.dirname, '..');
const outputDirectory = path.join(extensionRoot, 'out');
fs.rmSync(outputDirectory, { recursive: true, force: true });
fs.mkdirSync(outputDirectory, { recursive: true });

await build({
  entryPoints: [path.join(extensionRoot, 'src', 'extension.ts')],
  outfile: path.join(outputDirectory, 'extension.js'),
  bundle: true,
  platform: 'node',
  format: 'cjs',
  target: 'node20',
  external: ['vscode'],
  sourcemap: true,
  sourcesContent: false,
  tsconfig: path.join(extensionRoot, 'tsconfig.json'),
  logLevel: 'info',
});
