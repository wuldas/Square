# HTML5 elements in Square's core element tree

Scope: [WHATWG HTML Standard, conforming-elements index](https://html.spec.whatwg.org/multipage/indices.html#elements-3), last updated **2026-09-22**. This frozen list has **113** HTML elements; it excludes MathML `math`, SVG vocabulary, custom elements, and obsolete `param`. “Supported” below means recognized, represented, laid out/painted where visual, and safely exported—not a browser engine. SVG keeps its existing namespace and rendering path; Square controls keep theirs.

## Naming and template syntax

| Source | Meaning |
| --- | --- |
| `<button>` (any ASCII casing, e.g. `<Button>`) | HTML button when XHTML is the template's default namespace |
| `<Button>` with a `urn:square:ui` default, or `<ui:Button>` | Existing Square Button |
| `<html:button>` | HTML button via the `html:` prefix declared on the root template |
| unprefixed nested `<template>` (XHTML default) | Inert `HTMLTemplateElement`, retained but not painted; Square fragment wrapper is `<Fragment>` / `<ui:Fragment>` |
| `<html:template>` | Inert HTML template element, retained but not painted |
| top-level `<template>` / `<script>` / `<style>` sections | Existing component markup / C# code / component CSS; not HTML elements |

Both `.sqx` and `.sqv` resolve through the same catalog. Unprefixed names resolve first against the template's default namespace — root `xmlns`, else project `SquareDefaultElementNamespace`, else XHTML — then accept a unique candidate from any namespace; zero candidates is `SQXE003` and ambiguity is `SQXE004`. HTML names are ASCII case-insensitive and normalize to lowercase; Square retains its built-in case aliases, while SVG and package local names match exactly. Prefixes work only when declared on the root `<template>`; `html:`, `ui:` and `svg:` cannot bind to other URIs. HTML void elements are exactly `area base br col embed hr img input link meta source track wbr`: `<br>` and `<br/>` finish immediately. `selectedcontent` is empty **but not void** and takes the normal closing/self-closing rule. Other HTML tags require matched closing tags or template self-closing syntax; Square does not recover HTML optional end tags. SVG descendants stay in SVG. Unknown/obsolete names diagnose rather than silently render.

## Support matrix

A row is one conforming tag. **Native** column describes desktop behavior; for unchanged group defaults, sections/grouping are block boxes, phrasing is an inherited inline box, table nodes use CSS table roles, and media elements have a replaced box. Explicit class, inline style and component CSS override low-priority HTML UA styles. **Web** emits the corresponding literal semantic tag and escaped ordered text except where an active behavior is expressly disabled. In every “disabled” row the **element exists but that behavior is unavailable**; this is not a claim of playback, execution, document loading, or plugin support.

Native HTML flow preserves block-to-inline margins, places Square `Button` inline beside HTML/SVG content, and sizes checkable inputs as 13px widgets. The Square Button UA font matches Chromium's Arial 13.3333px default; installed fonts can still change text metrics between platforms.

| Tag | Semantic group | Native display / behavior | Web output / limit |
| --- | --- | --- | --- |
| `html` | Document | Document root; merged into one shell, never nested | Head metadata into the single Web head; inert on desktop |
| `head` | Document | No desktop box; safe metadata only in Web head | Head metadata into the single Web head; inert on desktop |
| `body` | Document | Block document content; single Web body | Head metadata into the single Web head; inert on desktop |
| `title` | Document | No box; head title when options.Title absent | Head metadata into the single Web head; inert on desktop |
| `base` | Document | Element exists but base URL changes unavailable; never export as active base | Disabled active behavior; omit active Web payload + diagnostic |
| `link` | Document | Element exists but external stylesheet loading unavailable; no active stylesheet export | Disabled active behavior; omit active Web payload + diagnostic |
| `meta` | Document | No box; safe name/charset in head; refresh prohibited | Head metadata into the single Web head; inert on desktop |
| `style` | Document | Element exists but inline stylesheet injection unavailable | Disabled active behavior; omit active Web payload + diagnostic |
| `script` | Document | Element exists but user script execution unavailable; never export content or src | Disabled active behavior; omit active Web payload + diagnostic |
| `noscript` | Document | Visible fallback content; no script execution | Head metadata into the single Web head; inert on desktop |
| `template` | Document | Inert subtree retained in tree, not painted or executed | Head metadata into the single Web head; inert on desktop |
| `article` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `aside` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `footer` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `header` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `hgroup` | Sections | Heading grouping block | Native semantic tag, children in order |
| `main` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `nav` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `section` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `search` | Sections | block; headings use heading font/spacing | Native semantic tag, children in order |
| `address` | Sections | Italic contact block | Native semantic tag, children in order |
| `h1` | Sections | Largest bold heading with margins | Native semantic tag, children in order |
| `h2` | Sections | Bold heading with margins | Native semantic tag, children in order |
| `h3` | Sections | Bold heading with margins | Native semantic tag, children in order |
| `h4` | Sections | Bold heading with margins | Native semantic tag, children in order |
| `h5` | Sections | Bold heading with margins | Native semantic tag, children in order |
| `h6` | Sections | Smallest bold heading with margins | Native semantic tag, children in order |
| `div` | Grouping | block with paragraph/list spacing | Native semantic tag, children in order |
| `p` | Grouping | Paragraph margins; inline line wrapping | Native semantic tag, children in order |
| `blockquote` | Grouping | Indented quotation block | Native semantic tag, children in order |
| `pre` | Grouping | Monospace preformatted line breaks and indentation | Native semantic tag, children in order |
| `hr` | Grouping | Painted horizontal divider | Native semantic tag, children in order |
| `figure` | Grouping | Block media group with margins | Native semantic tag, children in order |
| `figcaption` | Grouping | Caption below/above figure | Native semantic tag, children in order |
| `ol` | Grouping | Numbered nested list; start/reversed honored | Native semantic tag, children in order |
| `ul` | Grouping | Bulleted nested list | Native semantic tag, children in order |
| `menu` | Grouping | Bulleted list | Native semantic tag, children in order |
| `li` | Grouping | Visible marker and nested content | Native semantic tag, children in order |
| `dl` | Grouping | Definition list block | Native semantic tag, children in order |
| `dt` | Grouping | Definition term | Native semantic tag, children in order |
| `dd` | Grouping | Indented definition description | Native semantic tag, children in order |
| `a` | Phrasing | Link focus/activation only for safe href | Native semantic tag, escaped text in order |
| `abbr` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `b` | Phrasing | Bold | Native semantic tag, escaped text in order |
| `bdi` | Phrasing | Isolated text direction within supported text shaping | Native semantic tag, escaped text in order |
| `bdo` | Phrasing | Direction override within supported text shaping | Native semantic tag, escaped text in order |
| `br` | Phrasing | Forced line break; void | Native semantic tag, escaped text in order |
| `cite` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `code` | Phrasing | Monospace | Native semantic tag, escaped text in order |
| `data` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `del` | Phrasing | Struck-through | Native semantic tag, escaped text in order |
| `dfn` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `em` | Phrasing | Italic | Native semantic tag, escaped text in order |
| `i` | Phrasing | Italic | Native semantic tag, escaped text in order |
| `ins` | Phrasing | Underlined | Native semantic tag, escaped text in order |
| `kbd` | Phrasing | Monospace | Native semantic tag, escaped text in order |
| `mark` | Phrasing | Highlighted background follows shaped inline text bounds | Native semantic tag, escaped text in order |
| `q` | Phrasing | Quoted inline text | Native semantic tag, escaped text in order |
| `rp` | Phrasing | Ruby fallback inline | Native semantic tag, escaped text in order |
| `rt` | Phrasing | Ruby annotation inline | Native semantic tag, escaped text in order |
| `ruby` | Phrasing | Ruby grouping inline | Native semantic tag, escaped text in order |
| `s` | Phrasing | Struck-through | Native semantic tag, escaped text in order |
| `samp` | Phrasing | Monospace | Native semantic tag, escaped text in order |
| `small` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `span` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `strong` | Phrasing | Bold | Native semantic tag, escaped text in order |
| `sub` | Phrasing | Lowered small text | Native semantic tag, escaped text in order |
| `sup` | Phrasing | Raised small text | Native semantic tag, escaped text in order |
| `time` | Phrasing | inline; inherited font and whitespace | Native semantic tag, escaped text in order |
| `u` | Phrasing | Underlined | Native semantic tag, escaped text in order |
| `var` | Phrasing | Italic | Native semantic tag, escaped text in order |
| `wbr` | Phrasing | Optional wrap opportunity; void | Native semantic tag, escaped text in order |
| `table` | Table | CSS table roles; auto-width border box follows its measured grid | Native table tag; cells preserve spans |
| `caption` | Table | Table caption box | Native table tag; cells preserve spans |
| `colgroup` | Table | Column group configuration, no duplicate painted box | Native table tag; cells preserve spans |
| `col` | Table | Void column configuration, no independent box | Native table tag; cells preserve spans |
| `thead` | Table | CSS table role | Native table tag; cells preserve spans |
| `tbody` | Table | CSS table role | Native table tag; cells preserve spans |
| `tfoot` | Table | CSS table role | Native table tag; cells preserve spans |
| `tr` | Table | CSS table role | Native table tag; cells preserve spans |
| `th` | Table | Bold heading cell; colspan/rowspan geometry | Native table tag; cells preserve spans |
| `td` | Table | Table cell; colspan/rowspan geometry | Native table tag; cells preserve spans |
| `button` | Forms | Focusable styled host paints real child content, not nested Square Button | Native semantic tag and safe attributes |
| `input` | Forms | Native text/password/number, checkbox/radio/button proxy; text input host paints one inset UA frame, proxy text uses the host content box and font, author border takes precedence; other types fall back to text; no desktop network submit; void | Native semantic tag and safe attributes |
| `textarea` | Forms | Native multiline proxy, preserves source whitespace and value | Native semantic tag and safe attributes |
| `select` | Forms | Single-selection proxy populated by option/optgroup (group labels not painted) | Native semantic tag and safe attributes |
| `option` | Forms | Selection data, not a duplicate independent box | Native semantic tag and safe attributes |
| `optgroup` | Forms | Selection group data, not duplicate independent box | Native semantic tag and safe attributes |
| `datalist` | Forms | Candidate tree retained; desktop suggestion popup unavailable (element exists, behavior unavailable) | Browser-native suggestions from safe exported tag |
| `output` | Forms | Inline calculated-value host; no browser calculation engine | Native semantic tag and safe attributes |
| `label` | Forms | for/nested focus and activation | Native semantic tag and safe attributes |
| `form` | Forms | Block grouping; desktop network submit unavailable; Web native submit | Native semantic tag and safe attributes |
| `fieldset` | Forms | Grouped controls with border | Native semantic tag and safe attributes |
| `legend` | Forms | Fieldset caption | Native semantic tag and safe attributes |
| `progress` | Forms | Painted progress gauge | Native semantic tag and safe attributes |
| `meter` | Forms | Painted bounded meter gauge | Native semantic tag and safe attributes |
| `details` | Forms | Disclosure; summary toggles open | Native semantic tag and safe attributes |
| `summary` | Forms | Focusable disclosure heading | Native semantic tag and safe attributes |
| `dialog` | Forms | Visible only when open; no browser modal API | Native semantic tag and safe attributes |
| `selectedcontent` | Forms | Empty non-void selected display slot; normal closing required | Native semantic tag and safe attributes |
| `slot` | Forms | Inline slot fallback; no shadow DOM distribution | Native semantic tag and safe attributes |
| `iframe` | Media | “Embedded content disabled”; document loading unavailable | Disabled active behavior; omit active Web payload + diagnostic |
| `object` | Media | “Embedded content disabled” plus text fallback; plugin loading unavailable | Disabled active behavior; omit active Web payload + diagnostic |
| `embed` | Media | Void “Embedded content disabled”; plugin loading unavailable | Disabled active behavior; omit active Web payload + diagnostic |
| `img` | Media | Static Square Image proxy; alt fallback; void | Native tag only when safe; never load active embeds |
| `picture` | Media | Select its single img; no network source negotiation | Native tag only when safe; never load active embeds |
| `map` | Media | Image-map area grouping, no independent box | Native tag only when safe; never load active embeds |
| `area` | Media | Void safe-link hit area in associated map | Native tag only when safe; never load active embeds |
| `audio` | Media | Static “Playback unavailable” region; decoding unavailable | Native tag only when safe; never load active embeds |
| `video` | Media | Poster if safe; static “Playback unavailable” region; decoding unavailable | Native tag only when safe; never load active embeds |
| `source` | Media | Media source configuration only; void; no decoder | Native tag only when safe; never load active embeds |
| `track` | Media | Caption track configuration only; void; no decoder | Native tag only when safe; never load active embeds |
| `canvas` | Media | Blank region with visible fallback; Canvas 2D/WebGL unavailable | Native tag only when safe; never load active embeds |

## Attribute, style and URL boundaries

HTML attributes are case-normalized on HTML elements alone and exposed through `GetAttribute` / `HasAttribute` / `SetAttribute` / `RemoveAttribute` and an on-demand read-only snapshot. `id`, `class`, and `style` use Square's existing identity/class/style paths; other HTML attributes sync with the property store for selectors, `:attr` and bindings. Presence denotes true for standard boolean attributes (`checked`, `disabled`, `open` etc.); false/null removes them, while an ordinary attribute with value `""` remains present. Numeric formatting is invariant. `[checked]` and `[data-state="on"]` match HTML attributes without changing Square control selector behavior. HTML completion suggests standard global names and registered observed attributes, not Square's `TextContent`/`IsDisabled` aliases.

Static `onclick="..."` or any HTML `on*="..."` attribute is compile error `SQXE007`; only `@click` / `onClick={CSharpHandler}` is a Square event. Export also rejects `on*`, `srcdoc`, malformed attribute names, unsafe URL schemes in `href/src/poster/action/formaction/cite`, any unsafe `srcset` candidate, reserved `data-square-*` attributes supplied by packages, and every `is` attribute: no trusted client registration is authorized in this release. HTML attribute names/values and text are encoded. No user-authored script or injected style is emitted. `html:style`, stylesheet `html:link`, and `html:base` neither load on desktop nor change Web CSS/base URL; use component-file `<style>` and `HtmlExportOptions.AdditionalCss`. Safe `meta name`/`charset` enters head; `http-equiv=refresh` and arbitrary HTTP-equivalent directives do not.

URL validation rejects control characters before URI parsing: browser-stripped tabs/newlines cannot turn an apparently relative value into a `javascript:` URL. Every `srcset` candidate must pass the same check.

## Layout, substitution and export

Normal-flow layout flattens nested inline HTML runs in document order, wraps them across lines while keeping individual CSS inheritance, and leaves inline-block atomic. Mixed text inside HTML is a real `Square.UI.Text` DOM node in `ChildNodes` — not a UIElement control; layout measures, wraps, paints, binds and selects those text fragments in place, and export emits encoded *bare text*; text inside Square UI controls retains their existing export path. `white-space: normal/pre/pre-wrap` governs whitespace; `pre` and `textarea` preserve newlines and indentation; only pure template indentation outside them can disappear. Lists show bullets/numbers, nested numbering, `ol start/reversed`; tables honor cell spans. `br` breaks a line and `wbr` allows a break.

For form/image hosts, a private visual sidecar Square control paints and handles focus/state; it is not a DOM child and stays invisible to `ChildNodes`, `Children`, DOM queries and export. `input`/`textarea`/`select` synchronize value, checked, and selected bidirectionally; `label` activates its control; `details` toggles `open`; button paints its actual children. Image and video-poster rendering use Square Image; `picture` picks its one `img`. Audio/video decoding, browser canvas drawing, embedded documents/plugins, network form submission **on desktop**, and script execution are unavailable. Web forms retain browser-native submission behavior; Square's interactive bridge processes only existing click/input/change and updates HTML host state. `noscript` fallback remains visible. Safe map/area hit regions do not fetch content.

Desktop does not provide a file picker, color/date/range widgets, multi-select, datalist popup, required/pattern/maxlength enforcement, or native form submission; those attributes/elements remain in the tree and the corresponding Web tags are exported safely. Unsupported input types use the text proxy rather than posing as specialized widgets. Media decoders and active embeds remain disabled with labeled placeholders.

With `IncludeDocument=true`, a single `<!doctype html><html><head>…</head><body>…</body></html>` shell merges a lone HTML document root even through its generated component wrapper. Title precedence is explicit `HtmlExportOptions.Title`, then HTML `head/title`, then Square `Document.Title`/root name; charset, viewport, title and shell nodes are not duplicated. An ordinary fragment receives an automatic shell; `IncludeDocument=false` emits a fragment. An HTML document element nested in ordinary content is diagnosed and not emitted as nested html/head/body. Framework-owned interactive bridge script may remain; template-authored `script` never reaches the response.

## Acceptance fixtures

`.sqx` and `.sqv` template body (top-level sections are unchanged):

```xml
<template xmlns="http://www.w3.org/1999/xhtml" xmlns:ui="urn:square:ui">
  <article><h2>Square HTML</h2><p>Hello <strong>世界</strong> !<br>next</p>
    <ul><li>first</li><li>second</li></ul>
    <table><tr><td colspan="2">wide</td></tr></table>
    <img src="assets/example.png" alt="example"><label><input type="checkbox" checked>agree</label>
    <svg><circle r="8" /></svg>
    <ui:Button Text="Square control" />
  </article>
</template>
```

Expected: native ordered text and post-`br` line, checked checkbox, real image/list/table geometry and SVG painting; `doc.CreateElement("button")` creates an XHTML `HTMLButtonElement` while `doc.CreateComponentElement("urn:square:ui", "Button")` creates Square Button. Web output contains a real `<article><h2>` and `<p>Hello <strong>世界</strong> !<br>next</p>`, a void `<input … checked>`, and intact SVG without a duplicate proxy node. Both dialects compile. `<html:template>` remains inert; `<html:script>` never executes. A static `onclick="alert(1)"` fails SQXE007; runtime `href="javascript:alert(1)"` is omitted with an export diagnostic. Inspect actual DOM after browser parsing, not only response strings. Capture desktop screenshot using `--html-regression --screenshot <temporary PNG>`; exercise the Web sample endpoints — including `/html-elements`, which mixes HTML, SVG, UI and safe custom-element static representations — in a real browser and inspect form state and console. Existing PascalCase UI and SVG pages must not regress.
