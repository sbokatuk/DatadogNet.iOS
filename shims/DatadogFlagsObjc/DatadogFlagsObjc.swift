// A hand-written Objective-C projection of DatadogFlags, which Datadog ships as Swift only.
//
// Nothing here adds behaviour. Every member forwards to the Swift API; the work is entirely in
// reshaping what Swift can express and @objc cannot: generics, enums with associated values,
// Result-typed completions, and structs.

import Foundation
import DatadogFlags
import DatadogInternal

// MARK: - Enums
//
// Swift enums reach Objective-C only when they are Int-backed, so each is restated. FlagsClientState
// and FlagEvaluationError are both simple cases with no payload, so this is lossless.

@objc(DDFlagsClientState)
public enum DDFlagsClientState: Int {
    case notReady = 0
    case ready = 1
    case reconciling = 2
    case stale = 3
    case error = 4

    init(_ state: FlagsClientState) {
        switch state {
        case .notReady: self = .notReady
        case .ready: self = .ready
        case .reconciling: self = .reconciling
        case .stale: self = .stale
        case .error: self = .error
        @unknown default: self = .notReady
        }
    }
}

/// The evaluation error, with a `none` case standing in for Swift's `nil`.
@objc(DDFlagEvaluationError)
public enum DDFlagEvaluationError: Int {
    case none = 0
    case providerNotReady = 1
    case flagNotFound = 2
    case typeMismatch = 3

    init(_ error: FlagEvaluationError?) {
        switch error {
        case .none: self = .none
        case .some(.providerNotReady): self = .providerNotReady
        case .some(.flagNotFound): self = .flagNotFound
        case .some(.typeMismatch): self = .typeMismatch
        @unknown default: self = .none
        }
    }
}

// MARK: - AnyValue
//
// AnyValue is an enum with associated values, which cannot cross into Objective-C at all. It maps
// naturally onto the Foundation object graph instead, which is also what a C# caller wants: it
// arrives as NSString/NSNumber/NSDictionary/NSArray/NSNull and needs no further translation.

enum DDAnyValue {
    static func toObjC(_ value: AnyValue) -> Any {
        switch value {
        case .string(let value): return value as NSString
        case .bool(let value): return NSNumber(value: value)
        case .int(let value): return NSNumber(value: value)
        case .double(let value): return NSNumber(value: value)
        case .dictionary(let value): return value.mapValues(toObjC) as NSDictionary
        case .array(let value): return value.map(toObjC) as NSArray
        case .null: return NSNull()
        @unknown default: return NSNull()
        }
    }

    static func fromObjC(_ value: Any) -> AnyValue {
        switch value {
        case let value as NSNumber:
            // NSNumber erases Bool, so the CFBoolean check is the only way to keep `true` from
            // arriving as the integer 1 - which would change a boolean flag's type and make the
            // SDK report a typeMismatch against a perfectly good flag.
            if CFGetTypeID(value) == CFBooleanGetTypeID() {
                return .bool(value.boolValue)
            }

            let type = String(cString: value.objCType)
            return (type == "d" || type == "f") ? .double(value.doubleValue) : .int(value.intValue)

        case let value as NSString: return .string(value as String)
        case let value as NSDictionary:
            var result: [String: AnyValue] = [:]
            for (key, element) in value {
                if let key = key as? String { result[key] = fromObjC(element) }
            }
            return .dictionary(result)

        case let value as NSArray: return .array(value.map(fromObjC))
        default: return .null
        }
    }
}

// MARK: - FlagDetails
//
// FlagDetails<T> is generic, so it cannot be exposed as-is. Flattening the value to `Any` collapses
// the five generic instantiations into one class, and loses nothing a C# caller can act on.

@objc(DDFlagDetails)
public final class DDFlagDetails: NSObject {
    @objc public let key: String
    @objc public let value: Any
    @objc public let variant: String?
    @objc public let reason: String?
    @objc public let allocationKey: String?
    @objc public let error: DDFlagEvaluationError

    init<T: FlagValue & Equatable>(_ details: FlagDetails<T>, value: Any) {
        self.key = details.key
        self.value = value
        self.variant = details.variant
        self.reason = details.reason
        self.allocationKey = details.allocationKey
        self.error = DDFlagEvaluationError(details.error)
    }
}

@objc(DDFlagSnapshot)
public final class DDFlagSnapshot: NSObject {
    @objc public let value: Any
    @objc public let variant: String
    @objc public let reason: String

    init(_ snapshot: FlagSnapshot) {
        self.value = DDAnyValue.toObjC(snapshot.value)
        self.variant = snapshot.variant
        self.reason = snapshot.reason
    }
}

// MARK: - Configuration

@objc(DDFlagsConfiguration)
public final class DDFlagsConfiguration: NSObject {
    @objc public var gracefulModeEnabled: Bool = true
    @objc public var customFlagsEndpoint: URL?
    @objc public var customFlagsHeaders: [String: String]?
    @objc public var customExposureEndpoint: URL?
    @objc public var trackExposures: Bool = true
    @objc public var customEvaluationEndpoint: URL?
    @objc public var trackEvaluations: Bool = true
    @objc public var evaluationFlushInterval: TimeInterval = 10
    @objc public var rumIntegrationEnabled: Bool = true

    var swift: Flags.Configuration {
        Flags.Configuration(
            gracefulModeEnabled: gracefulModeEnabled,
            customFlagsEndpoint: customFlagsEndpoint,
            customFlagsHeaders: customFlagsHeaders,
            customExposureEndpoint: customExposureEndpoint,
            trackExposures: trackExposures,
            customEvaluationEndpoint: customEvaluationEndpoint,
            trackEvaluations: trackEvaluations,
            evaluationFlushInterval: evaluationFlushInterval,
            rumIntegrationEnabled: rumIntegrationEnabled
        )
    }
}

// MARK: - Entry point
//
// `Flags` is a caseless enum used as a namespace, which has no Objective-C equivalent. A final class
// with static members reads the same from C#.

@objc(DDFlags)
public final class DDFlags: NSObject {
    @objc public static func enable() {
        Flags.enable()
    }

    @objc public static func enable(with configuration: DDFlagsConfiguration) {
        Flags.enable(with: configuration.swift)
    }
}

// MARK: - Listener registration
//
// The Swift API takes a listener object and hands back nothing, so unsubscribing means holding on
// to the same instance. Returning a token instead removes a way to leak.

@objc(DDFlagsStateSubscription)
public final class DDFlagsStateSubscription: NSObject {
    private weak var observable: (any FlagsStateObservable)?
    private let listener: any FlagsStateListener

    init(observable: any FlagsStateObservable, listener: any FlagsStateListener) {
        self.observable = observable
        self.listener = listener
    }

    @objc public func cancel() {
        observable?.removeListener(listener)
        observable = nil
    }
}

private final class BlockStateListener: FlagsStateListener {
    private let handler: (DDFlagsClientState) -> Void

    init(handler: @escaping (DDFlagsClientState) -> Void) {
        self.handler = handler
    }

    func flagsStateDidChange(_ newState: FlagsClientState) {
        handler(DDFlagsClientState(newState))
    }
}

// MARK: - Client

@objc(DDFlagsClient)
public final class DDFlagsClient: NSObject {
    private let client: any FlagsClientProtocol

    private init(_ client: any FlagsClientProtocol) {
        self.client = client
    }

    @objc public static func shared() -> DDFlagsClient {
        DDFlagsClient(FlagsClient.shared())
    }

    @objc public static func shared(named name: String) -> DDFlagsClient {
        DDFlagsClient(FlagsClient.shared(named: name))
    }

    @objc public static func create(named name: String) -> DDFlagsClient {
        DDFlagsClient(FlagsClient.create(name: name))
    }

    @objc public var currentState: DDFlagsClientState {
        DDFlagsClientState(client.state.currentState)
    }

    // MARK: Values

    @objc public func boolValue(forKey key: String, defaultValue: Bool) -> Bool {
        client.getBooleanValue(key: key, defaultValue: defaultValue)
    }

    @objc public func stringValue(forKey key: String, defaultValue: String) -> String {
        client.getStringValue(key: key, defaultValue: defaultValue)
    }

    // Int rather than Swift's Int, so the width is the platform's and not a surprise on 32-bit.
    @objc public func integerValue(forKey key: String, defaultValue: Int) -> Int {
        client.getIntegerValue(key: key, defaultValue: defaultValue)
    }

    @objc public func doubleValue(forKey key: String, defaultValue: Double) -> Double {
        client.getDoubleValue(key: key, defaultValue: defaultValue)
    }

    @objc public func objectValue(forKey key: String, defaultValue: Any) -> Any {
        DDAnyValue.toObjC(
            client.getObjectValue(key: key, defaultValue: DDAnyValue.fromObjC(defaultValue)))
    }

    // MARK: Details

    @objc public func boolDetails(forKey key: String, defaultValue: Bool) -> DDFlagDetails {
        let details = client.getBooleanDetails(key: key, defaultValue: defaultValue)
        return DDFlagDetails(details, value: NSNumber(value: details.value))
    }

    @objc public func stringDetails(forKey key: String, defaultValue: String) -> DDFlagDetails {
        let details = client.getStringDetails(key: key, defaultValue: defaultValue)
        return DDFlagDetails(details, value: details.value as NSString)
    }

    @objc public func integerDetails(forKey key: String, defaultValue: Int) -> DDFlagDetails {
        let details = client.getIntegerDetails(key: key, defaultValue: defaultValue)
        return DDFlagDetails(details, value: NSNumber(value: details.value))
    }

    @objc public func doubleDetails(forKey key: String, defaultValue: Double) -> DDFlagDetails {
        let details = client.getDoubleDetails(key: key, defaultValue: defaultValue)
        return DDFlagDetails(details, value: NSNumber(value: details.value))
    }

    @objc public func objectDetails(forKey key: String, defaultValue: Any) -> DDFlagDetails {
        let details = client.getObjectDetails(
            key: key, defaultValue: DDAnyValue.fromObjC(defaultValue))
        return DDFlagDetails(details, value: DDAnyValue.toObjC(details.value))
    }

    // MARK: Evaluation context

    /// Sets the evaluation context.
    /// - Note: `Result<Void, FlagsError>` has no Objective-C form; the completion takes an
    ///   `NSError?` instead, which is also what a C# `Task` wants.
    @objc public func setEvaluationContext(
        targetingKey: String,
        attributes: [String: Any],
        completion: @escaping (NSError?) -> Void
    ) {
        let context = FlagsEvaluationContext(
            targetingKey: targetingKey,
            attributes: attributes.mapValues(DDAnyValue.fromObjC))

        client.setEvaluationContext(context) { result in
            switch result {
            case .success:
                completion(nil)
            case .failure(let error):
                completion(error as NSError)
            }
        }
    }

    // MARK: State

    @objc public func addStateListener(
        _ handler: @escaping (DDFlagsClientState) -> Void
    ) -> DDFlagsStateSubscription {
        let observable = client.state
        let listener = BlockStateListener(handler: handler)
        observable.addListener(listener)
        return DDFlagsStateSubscription(observable: observable, listener: listener)
    }

    // MARK: Snapshot

    @objc public func snapshot() -> [String: DDFlagSnapshot]? {
        client.snapshot()?.assignments.mapValues(DDFlagSnapshot.init)
    }
}
