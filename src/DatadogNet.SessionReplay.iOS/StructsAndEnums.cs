using ObjCRuntime;

namespace DatadogSessionReplay
{
	[Native]
	public enum DDSessionReplayConfigurationPrivacyLevel : long
	{
		Allow = 0,
		Mask = 1,
		MaskUserInput = 2
	}
}
