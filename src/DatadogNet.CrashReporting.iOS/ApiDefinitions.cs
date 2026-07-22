using Foundation;

namespace DatadogCrashReporting
{
	[Static]
	partial interface Constants
	{
		// extern double DatadogCrashReportingVersionNumber;
		[Field ("DatadogCrashReportingVersionNumber", "__Internal")]
		double DatadogCrashReportingVersionNumber { get; }

		// extern const unsigned char[] DatadogCrashReportingVersionString;
		[Field ("DatadogCrashReportingVersionString", "__Internal")]
		NSString DatadogCrashReportingVersionString { get; }
	}

	// @interface DDCrashReporter : NSObject
	[BaseType (typeof(NSObject))]
	interface DDCrashReporter
	{
		// +(void)enable;
		[Static]
		[Export ("enable")]
		void Enable ();
	}
}
