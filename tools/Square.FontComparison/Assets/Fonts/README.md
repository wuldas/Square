# Font assets

The conformance tool uses committed font files so Chromium and every Square backend consume identical bytes.

| Family | Version/source | License |
|---|---|---|
| Square Inter | Inter 4.1 static TTF files from https://github.com/rsms/inter/releases/tag/v4.1 | `Inter-LICENSE.txt` |
| Square Noto Sans SC | Noto Sans CJK Simplified Chinese OTF files from https://github.com/notofonts/noto-cjk | `NotoSansCJK-LICENSE.txt` |

`fonts.json` records the CSS face descriptors and SHA-256 for every file. The comparison tool verifies these hashes before launching a renderer.
