import Foundation
import Glibc

enum Status: String, Codable {
    case passed
    case failed
}

struct LegacyReceipt: Decodable {
    let schemaVersion: Int
    let id: String
    let status: Status
}

struct Receipt: Codable {
    let schemaVersion: Int
    let artifactId: String
    let status: Status
}

enum ReceiptError: Error {
    case usage
    case unsupportedSchema(Int)
    case missingArtifactId
}

func upgrade(_ legacy: LegacyReceipt) throws -> Receipt {
    guard legacy.schemaVersion == 1 else {
        throw ReceiptError.unsupportedSchema(legacy.schemaVersion)
    }
    guard !legacy.id.isEmpty else { throw ReceiptError.missingArtifactId }
    return Receipt(schemaVersion: 2, artifactId: legacy.id, status: legacy.status)
}

func run(_ arguments: [String]) throws {
    guard let command = arguments.first else { throw ReceiptError.usage }
    let decoder = JSONDecoder()
    switch command {
    case "upgrade" where arguments.count == 3:
        let input = try Data(contentsOf: URL(fileURLWithPath: arguments[1]))
        let current = try upgrade(decoder.decode(LegacyReceipt.self, from: input))
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        try encoder.encode(current).write(to: URL(fileURLWithPath: arguments[2]), options: .atomic)
    case "check" where arguments.count == 2:
        let input = try Data(contentsOf: URL(fileURLWithPath: arguments[1]))
        let receipt = try decoder.decode(Receipt.self, from: input)
        guard receipt.schemaVersion == 2 else { throw ReceiptError.unsupportedSchema(receipt.schemaVersion) }
        guard !receipt.artifactId.isEmpty else { throw ReceiptError.missingArtifactId }
        print(receipt.artifactId)
    default:
        throw ReceiptError.usage
    }
}

do {
    try run(Array(CommandLine.arguments.dropFirst()))
} catch {
    let message = Data("receipt: \(error)\n".utf8)
    FileHandle.standardError.write(message)
    exit(2)
}
