using ObjCRuntime;

namespace DatadogWebViewTracking
{
	[Native]
	public enum DDPrivacyLevel : long
	{
		Allow = 0,
		Mask = 1,
		MaskUserInput = 2
	}
}
