using System.Net;
using System.Text;

namespace Square.FontComparison;

internal static class ReportWriter
{
    public static void Write(string outputDirectory, ComparisonReport report)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Square font conformance</title><style>
            :root{color-scheme:light;font-family:Inter,Segoe UI,sans-serif;background:#eef1f5;color:#172033}
            *{box-sizing:border-box}body{margin:0}.shell{max-width:1500px;margin:auto;padding:28px}
            h1{margin:0 0 8px;font-size:28px}.meta{color:#667085;margin-bottom:24px}
            .renderer{background:white;border:1px solid #d8dee8;border-radius:14px;margin:0 0 24px;overflow:hidden}
            .renderer-head{display:flex;align-items:center;gap:16px;padding:18px 22px;background:#172033;color:white}
            .renderer-head h2{margin:0;flex:1}.count{font-variant-numeric:tabular-nums}.cases{padding:8px 18px 20px}
            details{border-bottom:1px solid #e7eaf0;padding:12px 0}summary{cursor:pointer;display:flex;gap:12px;align-items:center}
            .id{font-family:Consolas,monospace;flex:1}.status{font-weight:700;text-transform:uppercase}
            .pass{color:#067647}.fail{color:#b42318}.probe{color:#6941c6}
            .metrics{display:grid;grid-template-columns:repeat(4,minmax(130px,1fr));gap:10px;margin:14px 0}
            .metric{padding:10px;background:#f7f8fa;border-radius:8px;font-variant-numeric:tabular-nums}
            .shots{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}.shot{border:1px solid #d8dee8;border-radius:10px;padding:10px;overflow:auto}
            .shot h4{margin:0 0 8px}.shot img{display:block;max-width:none;background:white;image-rendering:auto}
            .failures{color:#b42318;font-family:Consolas,monospace}@media(max-width:800px){.shell{padding:14px}.metrics,.shots{grid-template-columns:1fr}}
            </style></head><body><main class="shell">
            """);
        html.Append("<h1>Square / Chromium 字体一致性</h1><div class=\"meta\">Chromium ")
            .Append(Encode(report.ChromiumVersion)).Append(" · generated ")
            .Append(Encode(report.GeneratedAt.ToString("u"))).Append("</div>");

        foreach (var renderer in report.Renderers)
        {
            html.Append("<section class=\"renderer\"><header class=\"renderer-head\"><h2>")
                .Append(Encode(renderer.Renderer)).Append("</h2><span class=\"count\">pass ")
                .Append(renderer.Passed).Append(" · fail ").Append(renderer.Failed)
                .Append(" · probe ").Append(renderer.Probes).Append("</span></header><div class=\"cases\">");
            foreach (var item in renderer.Cases)
            {
                html.Append("<details").Append(item.Status == "fail" ? " open" : "")
                    .Append("><summary><span class=\"id\">").Append(Encode(item.Id))
                    .Append("</span><span class=\"status ").Append(item.Status).Append("\">")
                    .Append(item.Status).Append("</span></summary><div class=\"metrics\">");
                AddMetric(html, "width delta", item.WidthDelta);
                AddMetric(html, "height delta", item.HeightDelta);
                AddMetric(html, "container x delta", item.XDelta);
                AddMetric(html, "container y delta", item.YDelta);
                AddMetric(html, "baseline delta", item.BaselineDelta);
                AddMetric(html, "max char x delta", item.MaxCharacterXDelta);
                AddMetric(html, "mask IoU", item.MaskIoU, "");
                AddMetric(html, "mean ink delta", item.MeanInkDelta, " / 255");
                AddMetric(html, "high delta", item.HighDeltaRatio * 100, "%");
                html.Append("</div>");
                if (item.Failures.Length > 0)
                    html.Append("<p class=\"failures\">").Append(Encode(string.Join(" · ", item.Failures))).Append("</p>");
                html.Append("<div class=\"shots\"><div class=\"shot\"><h4>Chromium</h4><img src=\"")
                    .Append(Encode(item.ChromiumScreenshot)).Append("\"></div><div class=\"shot\"><h4>")
                    .Append(Encode(renderer.Renderer)).Append("</h4><img src=\"")
                    .Append(Encode(item.SquareScreenshot)).Append("\"></div><div class=\"shot\"><h4>Diff</h4><img src=\"")
                    .Append(Encode(item.DiffScreenshot)).Append("\"></div></div></details>");
            }
            html.Append("</div></section>");
        }
        html.Append("</main></body></html>");
        File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html.ToString(), Encoding.UTF8);
    }

    private static void AddMetric(StringBuilder html, string label, float value, string suffix = " px")
        => html.Append("<div class=\"metric\"><strong>").Append(label).Append("</strong><br>")
            .Append(value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(suffix).Append("</div>");

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
