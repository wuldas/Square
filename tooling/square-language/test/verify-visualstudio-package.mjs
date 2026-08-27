import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..', '..');
const extensionRoot = path.join(root, 'visualstudio-square');
const csproj = fs.readFileSync(path.join(extensionRoot, 'Square.VisualStudio.csproj'), 'utf8');
const manifest = fs.readFileSync(path.join(extensionRoot, 'source.extension.vsixmanifest'), 'utf8');
const pkgdef = fs.readFileSync(path.join(extensionRoot, 'Square.pkgdef'), 'utf8');

assert.match(csproj, /Microsoft\.VSSDK\.BuildTools/);
assert.match(csproj, /Microsoft\.VisualStudio\.LanguageServer\.Client/);
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
assert.match(csproj, /Square\.LanguageServer\.csproj/);
assert.match(csproj, /<Link>Server\\%\(Filename\)%\(Extension\)<\/Link>/);

const languageClient = fs.readFileSync(
  path.join(extensionRoot, 'LanguageServer', 'SquareLanguageClient.cs'),
  'utf8',
);
assert.match(languageClient, /ILanguageClient/);
assert.match(languageClient, /Export\(typeof\(ILanguageClient\)\)/);
assert.match(languageClient, /ContentType\(SquareContentTypes\.Sqx\)/);
assert.match(languageClient, /ContentType\(SquareContentTypes\.Sqv\)/);
assert.match(languageClient, /Square\.LanguageServer\.dll/);
const contentTypes = fs.readFileSync(
  path.join(extensionRoot, 'LanguageServer', 'SquareContentTypes.cs'),
  'utf8',
);
assert.match(contentTypes, /CodeRemoteContentDefinition\.CodeRemoteContentTypeName/);
assert.match(contentTypes, /FileExtension\("\.sqx"\)/);
assert.match(contentTypes, /FileExtension\("\.sqv"\)/);

assert.match(manifest, /Version="\[17\.0,18\.0\)"/);
assert.match(manifest, /Microsoft\.VisualStudio\.Component\.CoreEditor/);
assert.match(manifest, /Path="Square\.pkgdef"/);
assert.match(manifest, /Path="Grammars"/);
assert.match(manifest, /Microsoft\.VisualStudio\.MefComponent/);

assert.match(pkgdef, /TextMate\\Repositories/);
assert.match(pkgdef, /Editors\\\{8B382828-6202-11D1-8870-0000F87579D2\}\\Extensions/i);
assert.match(pkgdef, /"sqx"=dword:00000032/i);
assert.match(pkgdef, /"sqv"=dword:00000032/i);
assert.match(pkgdef, /source\.sqx/);
assert.match(pkgdef, /source\.sqv/);
assert.match(pkgdef, /language-configuration\.json/);

console.log('Visual Studio package verification passed');
