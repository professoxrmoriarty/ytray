import Foundation
import Darwin

/// Current-user IPC used by first-party desktop clients. Independent from MCP.
final class NativeControlServer {
    private weak var store: InstanceStore?
    private let socketURL: URL
    private let queue = DispatchQueue(label: "io.yaklang.ytray.native-control")
    private var descriptor: Int32 = -1

    @MainActor
    init(store: InstanceStore) {
        self.store = store
        socketURL = store.applicationDirectory.appendingPathComponent("control.sock")
    }

    func start() {
        guard descriptor < 0 else { return }
        let path = socketURL.path
        let pathBytes = Array(path.utf8CString)
        var address = sockaddr_un()
        guard pathBytes.count <= MemoryLayout.size(ofValue: address.sun_path) else {
            DiagnosticLog.error("native-control", NSError(domain: "YTray", code: 1,
                userInfo: [NSLocalizedDescriptionKey: "原生控制 Socket 路径过长"]))
            return
        }

        let server = socket(AF_UNIX, SOCK_STREAM, 0)
        guard server >= 0 else { return }
        unlink(path)
        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        address.sun_family = sa_family_t(AF_UNIX)
        withUnsafeMutableBytes(of: &address.sun_path) { buffer in
            buffer.copyBytes(from: pathBytes)
        }
        let bound = withUnsafePointer(to: &address) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.bind(server, $0, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        guard bound == 0, listen(server, 4) == 0 else {
            close(server)
            return
        }
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: path)
        descriptor = server
        queue.async { [weak self] in self?.acceptLoop(server) }
    }

    func stop() {
        let server = descriptor
        descriptor = -1
        if server >= 0 {
            shutdown(server, SHUT_RDWR)
            close(server)
        }
        unlink(socketURL.path)
    }

    private func acceptLoop(_ server: Int32) {
        while descriptor == server {
            let client = accept(server, nil, nil)
            guard client >= 0 else { continue }
            var noPipe: Int32 = 1
            setsockopt(client, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout.size(ofValue: noPipe)))
            var timeout = timeval(tv_sec: 3, tv_usec: 0)
            setsockopt(client, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout.size(ofValue: timeout)))
            guard let request = Self.readRequest(client) else {
                close(client)
                continue
            }
            Task { @MainActor [weak self] in
                let response = self?.handle(request) ?? Self.failure("YTray 正在退出")
                Self.write(response, to: client)
                close(client)
            }
        }
    }

    private static func readRequest(_ client: Int32) -> [String: Any]? {
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while data.count <= 64 * 1024 {
            let count = recv(client, &buffer, buffer.count, 0)
            guard count > 0 else { return nil }
            data.append(contentsOf: buffer.prefix(count))
            if let newline = data.firstIndex(of: 10) {
                return try? JSONSerialization.jsonObject(with: Data(data[..<newline])) as? [String: Any]
            }
        }
        return nil
    }

    @MainActor
    private func handle(_ request: [String: Any]) -> Data {
        guard let store else { return Self.failure("YTray 正在退出") }
        switch request["action"] as? String {
        case "list_history":
            let instances: [[String: Any]] = store.historyInstances
                .sorted { $0.startedAt > $1.startedAt }
                .map { instance in
                    [
                        "id": instance.id.uuidString,
                        "name": instance.name,
                        "runtime": [instance.runtimeName, instance.runtimeVersion]
                            .compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " "),
                        "status": instance.status.rawValue,
                        "startUrl": instance.startURL,
                        "pageTitle": instance.lastPageTitle ?? "",
                        "pageUrl": instance.lastPageURL ?? "",
                        "badge": instance.dockBadge ?? "",
                        "startedAt": Int(instance.startedAt.timeIntervalSince1970 * 1000),
                    ]
                }
            return Self.encode(["ok": true, "instances": instances])
        case "claim_launch_approval":
            guard let rawID = request["id"] as? String, let id = UUID(uuidString: rawID) else {
                return Self.failure("浏览器实例 ID 无效")
            }
            guard let requestID = (request["requestId"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines),
                  !requestID.isEmpty, requestID.utf8.count <= 160,
                  requestID.rangeOfCharacter(from: .controlCharacters) == nil else {
                return Self.failure("配对请求 ID 无效")
            }
            let result = store.claimYakitLaunch(id, requestID: requestID)
            return Self.encode(["ok": true, "approved": result.approved, "reason": result.reason])
        case "restore_history":
            guard !store.isLaunching else { return Self.failure("YTray 正在启动另一个浏览器实例") }
            guard let value = request["id"] as? String, let id = UUID(uuidString: value),
                  let history = store.historyInstances.first(where: { $0.id == id }) else {
                return Self.failure("没有找到可恢复的浏览器实例")
            }
            store.restoreHistory(history)
            return Self.encode(["ok": true])
        default:
            return Self.failure("不支持的原生控制命令")
        }
    }

    private static func failure(_ message: String) -> Data {
        encode(["ok": false, "error": message])
    }

    private static func encode(_ value: [String: Any]) -> Data {
        (try? JSONSerialization.data(withJSONObject: value)) ?? Data(#"{"ok":false,"error":"编码失败"}"#.utf8)
    }

    private static func write(_ response: Data, to client: Int32) {
        var payload = response
        payload.append(10)
        payload.withUnsafeBytes { bytes in
            var sent = 0
            while sent < bytes.count {
                let count = Darwin.send(client, bytes.baseAddress!.advanced(by: sent), bytes.count - sent, 0)
                if count <= 0 { return }
                sent += count
            }
        }
    }
}
