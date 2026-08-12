import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..', '..');
const extensionRoot = path.join(root, 'visualstudio-square');
const csproj = fs.readFileSync(path.join(extensionRoot, 'Square.VisualStudio.csproj'), 'utf8');
const manifest = fs.readFileSync(path.join(extensionRoot, 'source.extension.vsixmanifest'), 'utf8');
const pkgdef = fs.readFileSync(path.join(extensionRoot, 'Square.pkgdef'), 'utf8');

assert.match(csproj, /Microsoft\.VSSDK\.BuildTools/);
assert.match(csproj, /Microsoft\.VsSDK\.targets/);
assert.match(csproj, /AfterTargets="Build"[^>]*DependsOnTargets="CreateVsixContainer"/s);
assert.match(csproj, /<IntermediateOutputPath>obj\\\$\(Configuration\)\\<\/IntermediateOutputPath>/);
assert.match(csproj, /<GeneratePkgDefFile>false<\/GeneratePkgDefFile>/);
assert.match(csproj, /<TargetVsixContainerName>Square\.LanguageSupport\.VisualStudio\.vsix<\/TargetVsixContainerName>/);
assert.match(csproj, /\.\.\\square-language\\syntaxes\\sqx\.tmLanguage\.json/);
assert.match(csproj, /\.\.\\square-language\\syntaxes\\sqv\.tmLanguage\.json/);
assert.match(csproj, /<Link>Grammars\\sqx\.tmLanguage\.json<\/Link>/);
assert.match(csproj, /<Link>Grammars\\sqv\.tmLanguage\.json<\/Link>/);
assert.match(csproj, /<Link>language-configuration\.json<\/Link>/);

assert.match(manifest, /Version="\[17\.0,18\.0\)"/);
assert.match(manifest, /Microsoft\.VisualStudio\.Component\.CoreEditor/);
assert.match(manifest, /Path="Square\.pkgdef"/);
assert.match(manifest, /Path="Grammars"/);

assert.match(pkgdef, /TextMate\\Repositories/);
assert.match(pkgdef, /source\.sqx/);
assert.match(pkgdef, /source\.sqv/);
assert.match(pkgdef, /language-configuration\.json/);

console.log('Visual Studio package verification passed');
