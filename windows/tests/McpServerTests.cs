using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using YTray.Core;

namespace YTray.Tests
{
    [TestClass]
    public class McpServerTests
    {
        [TestMethod]
        public async Task McpContractExposesBrowserToolsAndRejectsLocalFiles()
        {
            CollectionAssert.AreEqual(new[]
            {
                "list_browser_runtimes",
                "list_browser_instances",
                "open_browser_instance",
                "focus_browser_instance",
                "stop_browser_instance",
            }, McpServer.ToolNames.ToArray());
            Assert.IsTrue(McpServer.IsAllowedStartUrl("https://example.com/path"));
            Assert.IsTrue(McpServer.IsAllowedStartUrl("chrome://newtab"));
            Assert.IsFalse(McpServer.IsAllowedStartUrl("https:missing-host"));
            Assert.IsFalse(McpServer.IsAllowedStartUrl("file:///C:/Users/test/secret.txt"));
            Assert.IsFalse(McpServer.IsAllowedStartUrl("javascript:alert(1)"));

            var directory = Path.Combine(Path.GetTempPath(), "ytray-mcp-test-" + Guid.NewGuid());
            try
            {
                using var store = new InstanceStore(directory, false, false);
                using var server = new McpServer(store);
                Assert.IsTrue(server.Start());
                Assert.IsTrue(store.IsMcpAvailable);
                StringAssert.Contains(store.McpStatusMessage, McpServer.Endpoint);
                using var client = new HttpClient();
                var initialize = await client.PostAsync(McpServer.Endpoint, Json(@"{
                  'jsonrpc':'2.0','id':1,'method':'initialize',
                  'params':{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'test','version':'1'}}
                }"));
                Assert.IsTrue(initialize.IsSuccessStatusCode);
                var initializeResult = (JObject)JObject.Parse(
                    await initialize.Content.ReadAsStringAsync())["result"]!;
                Assert.AreEqual("2025-11-25", initializeResult.Value<string>("protocolVersion"));

                var listRequest = new HttpRequestMessage(HttpMethod.Post, McpServer.Endpoint)
                {
                    Content = Json(@"{
                      'jsonrpc':'2.0','id':2,'method':'tools/list','params':{
                        '_meta':{
                          'io.modelcontextprotocol/protocolVersion':'2026-07-28',
                          'io.modelcontextprotocol/clientCapabilities':{}
                        }
                      }
                    }")
                };
                listRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
                listRequest.Headers.TryAddWithoutValidation("Mcp-Method", "tools/list");
                var listed = await client.SendAsync(listRequest);
                var names = JObject.Parse(await listed.Content.ReadAsStringAsync())["result"]?["tools"]?
                    .Select(tool => (string)tool["name"]!).ToArray();
                CollectionAssert.AreEqual(McpServer.ToolNames.ToArray(), names);

                server.Stop();
                Assert.IsFalse(store.IsMcpAvailable);
                Assert.IsTrue(server.Start());
                store.SetMcpAutoStart(false);
                Assert.IsFalse(store.IsMcpAutoStartEnabled);
                using var restored = new InstanceStore(directory, false, false);
                Assert.IsFalse(restored.IsMcpAutoStartEnabled);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static StringContent Json(string value) =>
            new StringContent(value.Replace('\'', '"'), Encoding.UTF8, "application/json");
    }
}
