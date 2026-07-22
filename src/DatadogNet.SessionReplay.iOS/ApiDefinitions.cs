using DatadogSessionReplay;
using Foundation;
using ObjCRuntime;

namespace DatadogSessionReplay
{
	// @interface DDSessionReplay : NSObject
	[BaseType (typeof(NSObject), Name = "_TtC20DatadogSessionReplay15DDSessionReplay")]
	[DisableDefaultCtor]
	interface DDSessionReplay
	{
		// +(void)enableWith:(DDSessionReplayConfiguration * _Nonnull)configuration;
		[Static]
		[Export ("enableWith:")]
		void EnableWith (DDSessionReplayConfiguration configuration);
	}

	// @interface DDSessionReplayConfiguration : NSObject
	[BaseType (typeof(NSObject), Name = "_TtC20DatadogSessionReplay28DDSessionReplayConfiguration")]
	[DisableDefaultCtor]
	interface DDSessionReplayConfiguration
	{
		// @property (nonatomic) float replaySampleRate;
		[Export ("replaySampleRate")]
		float ReplaySampleRate { get; set; }

		// @property (nonatomic) enum DDSessionReplayConfigurationPrivacyLevel defaultPrivacyLevel;
		[Export ("defaultPrivacyLevel", ArgumentSemantic.Assign)]
		DDSessionReplayConfigurationPrivacyLevel DefaultPrivacyLevel { get; set; }

		// @property (copy, nonatomic) NSURL * _Nullable customEndpoint;
		[NullAllowed, Export ("customEndpoint", ArgumentSemantic.Copy)]
		NSUrl CustomEndpoint { get; set; }

		// -(instancetype _Nonnull)initWithReplaySampleRate:(float)replaySampleRate __attribute__((objc_designated_initializer));
		[Export ("initWithReplaySampleRate:")]
		[DesignatedInitializer]
		NativeHandle Constructor (float replaySampleRate);
	}
}
