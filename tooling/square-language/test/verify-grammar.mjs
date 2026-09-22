import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import textmate from 'vscode-textmate';
import oniguruma from 'vscode-oniguruma';

const { Registry, parseRawGrammar } = textmate;
const { loadWASM, OnigScanner, OnigString } = oniguruma;

const require = createRequire(import.meta.url);
const root = path.resolve(import.meta.dirname, '..');
const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm'));
await loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));

const stubs = {
  'source.cs': {
    scopeName: 'source.cs',
    patterns: [
      { name: 'keyword.control.cs', match: '\\b(?:private|public|class|new|return|void|bool|string)\\b' },
      { name: 'string.quoted.double.cs', begin: '"', end: '"' }
    ]
  },
  'source.css': {
    scopeName: 'source.css',
    patterns: [
      { name: 'support.type.property-name.css', match: '\\b(?:display|gap|color|opacity|background|content)\\b(?=\\s*:)' },
      { name: 'entity.other.attribute-name.class.css', match: '\\.[A-Za-z_-][A-Za-z0-9_-]*' }
    ]
  }
};

function grammarPath(scopeName) {
  if (scopeName === 'source.sqx') return path.join(root, 'syntaxes', 'sqx.tmLanguage.json');
  if (scopeName === 'source.sqv') return path.join(root, 'syntaxes', 'sqv.tmLanguage.json');
  return null;
}

const registry = new Registry({
  onigLib: Promise.resolve({
    createOnigScanner: patterns => new OnigScanner(patterns),
    createOnigString: value => new OnigString(value)
  }),
  loadGrammar: async scopeName => {
    if (stubs[scopeName]) return parseRawGrammar(JSON.stringify(stubs[scopeName]), scopeName + '.json');
    const file = grammarPath(scopeName);
    if (!file) return null;
    return parseRawGrammar(fs.readFileSync(file, 'utf8'), file);
  }
});

function fixture(name) {
  return fs.readFileSync(path.join(root, 'test', 'fixtures', name), 'utf8');
}

async function tokenize(scopeName, text) {
  const grammar = await registry.loadGrammar(scopeName);
  assert.ok(grammar, `Grammar ${scopeName} should load`);
  let ruleStack = null;
  const result = [];
  for (const line of text.split(/\r?\n/)) {
    const tokenized = grammar.tokenizeLine(line, ruleStack);
    ruleStack = tokenized.ruleStack;
    result.push(tokenized.tokens.map(token => ({
      text: line.slice(token.startIndex, token.endIndex),
      scopes: token.scopes
    })));
  }
  return result.flat();
}

function scopesFor(tokens, exactText) {
  const token = tokens.find(candidate => candidate.text === exactText);
  assert.ok(token, `Expected token ${JSON.stringify(exactText)}`);
  return token.scopes;
}

function tokensFor(tokens, exactText) {
  return tokens.filter(candidate => candidate.text === exactText);
}

function assertTagScopes(tokens, name, scope, label) {
  const tags = tokensFor(tokens, name);
  assert.ok(tags.length >= 2, `Expected opening and closing ${name} tags to be tokenized`);
  assert.ok(tags.every(token => token.scopes.includes(scope)),
    `Every ${label} ${name} tag should be scoped ${scope}`);
}

const sqx = await tokenize('source.sqx', fixture('basic.sqx'));
assert.ok(scopesFor(sqx, 'template').includes('entity.name.tag.section.sqx'));
assert.ok(scopesFor(sqx, 'Button').includes('entity.name.tag.sqx'));
assert.ok(scopesFor(sqx, 'Show').includes('entity.name.tag.directive.sqx'));
assertTagScopes(sqx, 'Show', 'entity.name.tag.directive.sqx', 'directive');
assertTagScopes(sqx, 'Slot', 'entity.name.tag.directive.sqx', 'slot');
assertTagScopes(sqx, 'Button', 'entity.name.tag.sqx', 'component');
assertTagScopes(sqx, 'View', 'entity.name.tag.sqx', 'component');
assert.ok(scopesFor(sqx, 'when').includes('entity.other.attribute-name.directive.sqx'));
assert.ok(scopesFor(sqx, 'onClick').includes('entity.other.attribute-name.event.sqx'));
assert.ok(scopesFor(sqx, 'private').includes('keyword.control.cs'));
assert.ok(scopesFor(sqx, 'display').includes('support.type.property-name.css'));
const sqxEmbedded = await tokenize('source.sqx', fixture('embedded-csharp.sqx'));
assert.ok(scopesFor(sqxEmbedded, 'private').includes('keyword.control.cs'));
assert.ok(scopesFor(sqxEmbedded, 'script').includes('entity.name.tag.section.sqx'));
const closingScript = sqxEmbedded.filter(token => token.text === 'script');
assert.ok(closingScript.length >= 2, 'Opening and closing script tags should both be scoped');
assert.ok(closingScript.every(token => token.scopes.includes('entity.name.tag.section.sqx')));
assert.ok(sqxEmbedded.some(token => token.text === '</' && token.scopes.includes('punctuation.definition.tag.begin.sqx')));
assert.ok(sqxEmbedded.some(token => token.text === '{' && token.scopes.includes('punctuation.section.block.begin.cs')));
assert.ok(sqxEmbedded.some(token => token.text === '}' && token.scopes.includes('punctuation.section.block.end.cs')));
assert.ok(scopesFor(sqxEmbedded, 'Prop').includes('support.type.attribute.cs'));
assert.ok(scopesFor(sqxEmbedded, 'Required').includes('entity.other.attribute-name.cs'));

const sqxCss = await tokenize('source.sqx', fixture('embedded-css.sqx'));
const cssTokens = sqxCss.filter(token => token.text.includes('8px'));
assert.ok(cssTokens.length > 0, 'Expected CSS dimension token');
assert.ok(cssTokens.every(token => token.scopes.includes('source.css')));
assert.ok(cssTokens.every(token => !token.scopes.includes('source.cs')));

const sqv = await tokenize('source.sqv', fixture('basic.sqv'));
assert.ok(scopesFor(sqv, '@click').includes('entity.other.attribute-name.event.sqv'));
assert.ok(scopesFor(sqv, 'stop').includes('storage.modifier.sqv'));
assert.ok(scopesFor(sqv, 'v-if').includes('entity.other.attribute-name.directive.sqv'));
assert.ok(scopesFor(sqv, '#header').includes('entity.other.attribute-name.slot.sqv'));
// 带值指令：值必须是 C# 作用域，且不得吞掉同行的结束标签
assert.ok(scopesFor(sqv, 'v-if').includes('entity.other.attribute-name.directive.sqv'));
assert.ok(scopesFor(sqv, 'Visible').includes('meta.attribute.directive.sqv'));
assert.ok(scopesFor(sqv, 'Visible').includes('source.cs'));
assert.ok(scopesFor(sqv, 'v-else').includes('entity.other.attribute-name.directive.sqv'));
assert.ok(scopesFor(sqv, ':key').includes('entity.other.attribute-name.binding.sqv'));
assert.ok(sqv.every(token => !(token.scopes.includes('string.quoted.double.sqv') && token.text.includes('</'))),
  'Closing tags must not be swallowed by a string scope');
assertTagScopes(sqv, 'Slot', 'entity.name.tag.directive.sqv', 'slot');
assertTagScopes(sqv, 'Button', 'entity.name.tag.sqv', 'component');

// 嵌套/内联插槽 <template v-slot:…> 不得提前结束 #template-section：
// 首尾两个 template 令牌是段标签，中间的都是普通组件标签；其后内容仍应位于模板段内。
const sqvTemplates = tokensFor(sqv, 'template');
assert.ok(sqvTemplates.length >= 4, 'Expected section open, nested slot templates and section close');
assert.ok(sqvTemplates[0].scopes.includes('entity.name.tag.section.sqv'));
assert.ok(sqvTemplates[sqvTemplates.length - 1].scopes.includes('entity.name.tag.section.sqv'));
for (const token of sqvTemplates.slice(1, -1)) {
  assert.ok(token.scopes.includes('entity.name.tag.sqv'),
    `Nested template tag should use the component tag scope: ${token.scopes.join(' ')}`);
  assert.ok(!token.scopes.includes('entity.name.tag.section.sqv'),
    'Nested template tag must not use the section tag scope');
}
for (const name of ['Slot', 'SlotCard', 'View']) {
  const tags = tokensFor(sqv, name);
  assert.ok(tags.length >= 2, `Expected opening and closing ${name} tags after a nested slot template`);
  assert.ok(tags.every(token => token.scopes.includes('meta.section.template.sqv')),
    `Content after a nested slot template must stay inside the template section (${name})`);
}
assert.ok(scopesFor(sqv, 'private').includes('keyword.control.cs'));
assert.ok(scopesFor(sqv, 'display').includes('support.type.property-name.css'));

for (const [scopeName, name] of [['source.sqx', 'malformed.sqx'], ['source.sqv', 'malformed.sqv']]) {
  const malformed = await tokenize(scopeName, fixture(name));
  assert.ok(malformed.some(token => token.text === 'style' && token.scopes.some(scope => scope.includes('section'))),
    `${name} should recover highlighting at the style section`);
}

const configuration = JSON.parse(fs.readFileSync(path.join(root, 'language-configuration.json'), 'utf8'));
assert.deepEqual(configuration.brackets, [['<', '>'], ['{', '}'], ['[', ']'], ['(', ')']]);
assert.deepEqual(configuration.comments.blockComment, ['<!--', '-->']);

console.log('Square grammar verification passed');
