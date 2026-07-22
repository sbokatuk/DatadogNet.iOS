using DatadogInternal;
using Foundation;
using ObjCRuntime;

namespace DatadogInternal
{
	// @interface DatadogURLSessionDelegate : NSObject <NSURLSessionDataDelegate>
	[BaseType (typeof(NSObject), Name = "_TtC15DatadogInternal25DatadogURLSessionDelegate")]
	interface DatadogURLSessionDelegate : INSUrlSessionDataDelegate
	{
		// -(instancetype _Nonnull)initWithAdditionalFirstPartyHosts:(NSSet<NSString *> * _Nonnull)additionalFirstPartyHosts;
		[Export ("initWithAdditionalFirstPartyHosts:")]
		NativeHandle Constructor (NSSet<NSString> additionalFirstPartyHosts);

		// Objective Sharpie also emitted three URLSession overloads here, for the selectors
		//
		//     URLSession:task:didFinishCollectingMetrics:
		//     URLSession:dataTask:didReceiveData:
		//     URLSession:task:didCompleteWithError:
		//
		// They are deliberately not bound. INSUrlSessionDataDelegate, which this interface derives
		// from, already declares all three - as DidFinishCollectingMetrics, DidReceiveData and
		// DidCompleteWithError - so binding them again registers each selector twice. That is not a
		// compile error: it fails at startup, when the runtime registers the assembly, and takes
		// down *every* app that loads the package before a line of its own code runs:
		//
		//     Could not register the selector 'URLSession:task:didFinishCollectingMetrics:' of the
		//     member 'DatadogInternal.DatadogURLSessionDelegate.URLSession' because the selector is
		//     already registered on the member 'DidFinishCollectingMetrics'.
		//
		// The methods are still callable through the inherited names, and the native class still
		// implements the selectors - dropping the duplicates removes nothing but the collision.
	}

	// @protocol __URLSessionDelegateProviding <NSURLSessionDelegate>
	/*
  Check whether adding [Model] to this declaration is appropriate.
  [Model] is used to generate a C# class that implements this protocol,
  and might be useful for protocols that consumers are supposed to implement,
  since consumers can subclass the generated class instead of implementing
  the generated interface. If consumers are not supposed to implement this
  protocol, then [Model] is redundant and will generate code that will never
  be used.
*/[Protocol (Name = "_TtP15DatadogInternal29__URLSessionDelegateProviding_")]
	[BaseType(typeof(NSObject))]
	interface __URLSessionDelegateProviding : INSUrlSessionDelegate
	{
		[Wrap ("WeakDdURLSessionDelegate"), Abstract]
		DatadogURLSessionDelegate DdURLSessionDelegate { get; }

		// @required @property (readonly, nonatomic, strong) DatadogURLSessionDelegate * _Nonnull ddURLSessionDelegate;
		[Abstract]
		[NullAllowed, Export ("ddURLSessionDelegate", ArgumentSemantic.Strong)]
		NSObject WeakDdURLSessionDelegate { get; }
	}

	// @interface DatadogInternal_Swift_716 (DatadogURLSessionDelegate) <__URLSessionDelegateProviding>
	[Protocol, Model]
	[BaseType (typeof(DatadogURLSessionDelegate))]
	interface DatadogURLSessionDelegate_DatadogInternal_Swift_716 : __URLSessionDelegateProviding
	{
		[Wrap ("WeakDdURLSessionDelegate")]
		DatadogURLSessionDelegate DdURLSessionDelegate { get; }

		// @property (readonly, nonatomic, strong) DatadogURLSessionDelegate * _Nonnull ddURLSessionDelegate;
		[NullAllowed, Export ("ddURLSessionDelegate", ArgumentSemantic.Strong)]
		NSObject WeakDdURLSessionDelegate { get; }
	}
}
