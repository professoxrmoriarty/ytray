import XCTest
@testable import YTray

final class MCPServerTests: XCTestCase {
    func testMCPContractExposesBrowserToolsAndRejectsLocalFiles() {
        XCTAssertEqual(MCPServer.toolNames, [
            "list_browser_runtimes",
            "list_browser_instances",
            "open_browser_instance",
            "focus_browser_instance",
            "stop_browser_instance",
        ])
        XCTAssertTrue(MCPServer.isAllowedStartURL("https://example.com/path"))
        XCTAssertTrue(MCPServer.isAllowedStartURL("chrome://newtab"))
        XCTAssertFalse(MCPServer.isAllowedStartURL("https:missing-host"))
        XCTAssertFalse(MCPServer.isAllowedStartURL("file:///Users/test/secret.txt"))
        XCTAssertFalse(MCPServer.isAllowedStartURL("javascript:alert(1)"))
    }

    func testMCPAutoStartDefaultsOnAndPersistsOff() throws {
        XCTAssertTrue(LaunchSettings().mcpAutoStart)
        var settings = LaunchSettings()
        settings.mcpAutoStart = false
        let restored = try JSONDecoder().decode(LaunchSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertFalse(restored.mcpAutoStart)
    }

    func testYakitAutoConnectDefaultsOnAndPersistsOff() throws {
        XCTAssertTrue(LaunchSettings().autoConnectYakit)
        var settings = LaunchSettings()
        settings.autoConnectYakit = false
        let restored = try JSONDecoder().decode(LaunchSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertFalse(restored.autoConnectYakit)
    }
}
