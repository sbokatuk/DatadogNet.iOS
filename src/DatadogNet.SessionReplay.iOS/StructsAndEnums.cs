using ObjCRuntime;


namespace DatadogSessionReplay
{
	/// <summary>Session-wide masking for images.</summary>
	[Native]
	public enum DDImagePrivacyLevel : long
	{
		MaskNonBundledOnly = 0,
		MaskAll = 1,
		MaskNone = 2
	}

	/// <summary>Per-view override of <see cref="DDImagePrivacyLevel"/>. <c>None</c> inherits the session-wide level.</summary>
	[Native]
	public enum DDImagePrivacyLevelOverride : long
	{
		None = 0,
		MaskNone = 1,
		MaskNonBundledOnly = 2,
		MaskAll = 3
	}

	/// <summary>The single session-wide privacy level used before 2.19.0. Deprecated upstream in favour of the three fine-grained levels; still accepted for now.</summary>
	[Native]
	public enum DDSessionReplayConfigurationPrivacyLevel : long
	{
		Allow = 0,
		Mask = 1,
		MaskUserInput = 2
	}

	/// <summary>Session-wide masking for text and user input.</summary>
	[Native]
	public enum DDTextAndInputPrivacyLevel : long
	{
		MaskSensitiveInputs = 0,
		MaskAllInputs = 1,
		MaskAll = 2
	}

	/// <summary>Per-view override of <see cref="DDTextAndInputPrivacyLevel"/>. <c>None</c> inherits the session-wide level.</summary>
	[Native]
	public enum DDTextAndInputPrivacyLevelOverride : long
	{
		None = 0,
		MaskSensitiveInputs = 1,
		MaskAllInputs = 2,
		MaskAll = 3
	}

	/// <summary>Session-wide masking for touch interactions.</summary>
	[Native]
	public enum DDTouchPrivacyLevel : long
	{
		Show = 0,
		Hide = 1
	}

	/// <summary>Per-view override of <see cref="DDTouchPrivacyLevel"/>. <c>None</c> inherits the session-wide level.</summary>
	[Native]
	public enum DDTouchPrivacyLevelOverride : long
	{
		None = 0,
		Show = 1,
		Hide = 2
	}

}
