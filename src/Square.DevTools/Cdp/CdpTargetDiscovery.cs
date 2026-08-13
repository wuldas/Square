using System.Globalization;
using System.Text;

namespace Square.DevTools;

internal static class CdpTargetDiscovery
{
    public static string SerializeVersion(DevToolsServer server)
    {
        var websocket = server.CdpWebSocketAddress;
        return $"{{\"Browser\":\"Square.DevTools/0.1\",\"Protocol-Version\":\"1.3\"," +
               $"\"User-Agent\":\"Square.DevTools\",\"V8-Version\":\"\",\"WebKit-Version\":\"\"," +
               $"\"webSocketDebuggerUrl\":\"{Escape(websocket)}\"}}";
    }

    public static string SerializeList(DevToolsServer server)
    {
        var websocket = server.CdpWebSocketAddress;
        var frontend = $"/devtools/inspector.html?ws=127.0.0.1:{server.Port}/devtools/page/{server.TargetId}";
        return $"[{{\"description\":\"Square application target\",\"devtoolsFrontendUrl\":\"{Escape(frontend)}\"," +
               $"\"id\":\"{Escape(server.TargetId)}\",\"title\":\"Square DevTools\",\"type\":\"page\"," +
               $"\"url\":\"square://application/{server.TargetId}\",\"webSocketDebuggerUrl\":\"{Escape(websocket)}\"}}]";
    }

    public static string SerializeProtocol()
    {
        return "{\"version\":{\"major\":\"1\",\"minor\":\"3\"},\"domains\":[" +
               "{\"domain\":\"DOM\",\"commands\":[" +
               "{\"name\":\"enable\"},{\"name\":\"disable\"},{\"name\":\"getDocument\"}," +
               "{\"name\":\"getFlattenedDocument\"},{\"name\":\"requestChildNodes\"}," +
               "{\"name\":\"describeNode\"},{\"name\":\"getAttributes\"},{\"name\":\"getNodeForLocation\"}," +
               "{\"name\":\"getBoxModel\"},{\"name\":\"getOuterHTML\"},{\"name\":\"resolveNode\"}," +
               "{\"name\":\"setInspectedNode\"},{\"name\":\"pushNodesByBackendIdsToFrontend\"}]," +
               "\"events\":[{\"name\":\"documentUpdated\"},{\"name\":\"setChildNodes\"}]}," +
               "{\"domain\":\"Runtime\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"},{\"name\":\"getProperties\"},{\"name\":\"callFunctionOn\"},{\"name\":\"releaseObject\"}]," +
               "\"events\":[{\"name\":\"executionContextCreated\"}]}," +
               "{\"domain\":\"Page\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"},{\"name\":\"getFrameTree\"},{\"name\":\"getResourceTree\"},{\"name\":\"getNavigationHistory\"},{\"name\":\"getLayoutMetrics\"},{\"name\":\"captureScreenshot\"},{\"name\":\"startScreencast\"},{\"name\":\"stopScreencast\"},{\"name\":\"addScriptToEvaluateOnNewDocument\"}]}," +
               "{\"domain\":\"Target\",\"commands\":[{\"name\":\"getTargetInfo\"},{\"name\":\"setDiscoverTargets\"},{\"name\":\"setAutoAttach\"}]}," +
               "{\"domain\":\"CSS\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"},{\"name\":\"getComputedStyleForNode\"},{\"name\":\"getMatchedStylesForNode\"},{\"name\":\"getAnimatedStylesForNode\"},{\"name\":\"getInlineStylesForNode\"},{\"name\":\"getPlatformFontsForNode\"},{\"name\":\"getEnvironmentVariables\"},{\"name\":\"trackComputedStyleUpdates\"},{\"name\":\"takeComputedStyleUpdates\"}]}," +
               "{\"domain\":\"Overlay\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"},{\"name\":\"hideHighlight\"},{\"name\":\"setInspectMode\"},{\"name\":\"highlightNode\"},{\"name\":\"highlightRect\"}]," +
               "\"events\":[{\"name\":\"inspectNodeRequested\"}]}," +
               "{\"domain\":\"Inspector\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"}]}," +
               "{\"domain\":\"Console\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"}]}," +
               "{\"domain\":\"Log\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"}]}," +
               "{\"domain\":\"Network\",\"commands\":[{\"name\":\"enable\"},{\"name\":\"disable\"}]}]}";
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(ch))
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(ch);
                    break;
            }
        }
        return builder.ToString();
    }
}
