using System;
using DatadogSessionReplay;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace DatadogSessionReplay
{
	// NOTE ON RUNTIME NAMES
	//
	// Through 2.17.0 these classes were declared SWIFT_CLASS("_TtC20DatadogSessionReplay..."),
	// which sets objc_runtime_name to the mangled Swift symbol, so the binding had to spell that
	// mangled name out in [BaseType (Name = ...)].
	//
	// From 2.19.0 they are declared SWIFT_CLASS_NAMED("objc_SessionReplay"), which expands to
	// swift_name(...) only - a compile-time alias - and leaves the runtime name as the plain
	// @interface name. The binary confirms it: it exports _OBJC_CLASS_$_DDSessionReplay, not
	// _OBJC_CLASS_$__TtC20DatadogSessionReplay15DDSessionReplay.
	//
	// So Name= is deliberately absent here. Keeping the old mangled name would compile happily and
	// then fail to resolve the class at runtime.

	// @interface DDSessionReplay : NSObject
	[BaseType (typeof(NSObject))]
	[DisableDefaultCtor]
	interface DDSessionReplay
	{
		// +(void)enableWith:(DDSessionReplayConfiguration * _Nonnull)configuration;
		[Static]
		[Export ("enableWith:")]
		void EnableWith (DDSessionReplayConfiguration configuration);

		// +(void)startRecording;
		// Only needed when StartRecordingImmediately was set to false; recording otherwise begins
		// as soon as the feature is enabled.
		[Static]
		[Export ("startRecording")]
		void StartRecording ();

		// +(void)stopRecording;
		[Static]
		[Export ("stopRecording")]
		void StopRecording ();
	}

	// @interface DDSessionReplayConfiguration : NSObject
	[BaseType (typeof(NSObject))]
	[DisableDefaultCtor]
	interface DDSessionReplayConfiguration
	{
		// @property (nonatomic) float replaySampleRate;
		[Export ("replaySampleRate")]
		float ReplaySampleRate { get; set; }

		// @property (nonatomic) enum DDTextAndInputPrivacyLevel textAndInputPrivacyLevel;
		[Export ("textAndInputPrivacyLevel", ArgumentSemantic.Assign)]
		DDTextAndInputPrivacyLevel TextAndInputPrivacyLevel { get; set; }

		// @property (nonatomic) enum DDImagePrivacyLevel imagePrivacyLevel;
		[Export ("imagePrivacyLevel", ArgumentSemantic.Assign)]
		DDImagePrivacyLevel ImagePrivacyLevel { get; set; }

		// @property (nonatomic) enum DDTouchPrivacyLevel touchPrivacyLevel;
		[Export ("touchPrivacyLevel", ArgumentSemantic.Assign)]
		DDTouchPrivacyLevel TouchPrivacyLevel { get; set; }

		// @property (nonatomic) BOOL startRecordingImmediately;
		[Export ("startRecordingImmediately")]
		bool StartRecordingImmediately { get; set; }

		// @property (copy, nonatomic) NSURL * _Nullable customEndpoint;
		[NullAllowed, Export ("customEndpoint", ArgumentSemantic.Copy)]
		NSUrl CustomEndpoint { get; set; }

		// @property (copy, nonatomic) NSDictionary<NSString *,NSNumber *> * _Nonnull featureFlags;
		[Export ("featureFlags", ArgumentSemantic.Copy)]
		NSDictionary<NSString, NSNumber> FeatureFlags { get; set; }

		// @property (nonatomic) enum DDSessionReplayConfigurationPrivacyLevel defaultPrivacyLevel
		//     SWIFT_DEPRECATED_MSG("... Use the new privacy levels instead.");
		[Export ("defaultPrivacyLevel", ArgumentSemantic.Assign)]
		DDSessionReplayConfigurationPrivacyLevel DefaultPrivacyLevel { get; set; }

		// -(instancetype)initWithReplaySampleRate:(float)replaySampleRate
		//     textAndInputPrivacyLevel:(enum DDTextAndInputPrivacyLevel)textAndInputPrivacyLevel
		//     imagePrivacyLevel:(enum DDImagePrivacyLevel)imagePrivacyLevel
		//     touchPrivacyLevel:(enum DDTouchPrivacyLevel)touchPrivacyLevel;
		[Export ("initWithReplaySampleRate:textAndInputPrivacyLevel:imagePrivacyLevel:touchPrivacyLevel:")]
		[DesignatedInitializer]
		NativeHandle Constructor (float replaySampleRate, DDTextAndInputPrivacyLevel textAndInputPrivacyLevel, DDImagePrivacyLevel imagePrivacyLevel, DDTouchPrivacyLevel touchPrivacyLevel);

		// ... featureFlags:(NSDictionary<NSString *,NSNumber *> * _Nullable)featureFlags;
		[Export ("initWithReplaySampleRate:textAndInputPrivacyLevel:imagePrivacyLevel:touchPrivacyLevel:featureFlags:")]
		NativeHandle Constructor (float replaySampleRate, DDTextAndInputPrivacyLevel textAndInputPrivacyLevel, DDImagePrivacyLevel imagePrivacyLevel, DDTouchPrivacyLevel touchPrivacyLevel, [NullAllowed] NSDictionary<NSString, NSNumber> featureFlags);

		// -(instancetype)initWithReplaySampleRate:(float)replaySampleRate
		//     SWIFT_DEPRECATED_MSG("... Use init(replaySampleRate:textAndInputPrivacyLevel:...) instead.");
		//
		// Still bound so code written against 2.17.0 keeps compiling. It applies the SDK's own
		// defaults for the three fine-grained levels.
		[Export ("initWithReplaySampleRate:")]
		NativeHandle Constructor (float replaySampleRate);
	}

	// @interface DDSessionReplayPrivacyOverrides : NSObject
	//
	// Per-view privacy, overriding the session-wide levels. Reached through the UIView category
	// below rather than constructed directly in normal use.
	[BaseType (typeof(NSObject))]
	[DisableDefaultCtor]
	interface DDSessionReplayPrivacyOverrides
	{
		// -(instancetype _Nonnull)initWithView:(UIView * _Nonnull)view;
		[Export ("initWithView:")]
		[DesignatedInitializer]
		NativeHandle Constructor (UIView view);

		// @property (nonatomic) enum DDTextAndInputPrivacyLevelOverride textAndInputPrivacy;
		[Export ("textAndInputPrivacy", ArgumentSemantic.Assign)]
		DDTextAndInputPrivacyLevelOverride TextAndInputPrivacy { get; set; }

		// @property (nonatomic) enum DDImagePrivacyLevelOverride imagePrivacy;
		[Export ("imagePrivacy", ArgumentSemantic.Assign)]
		DDImagePrivacyLevelOverride ImagePrivacy { get; set; }

		// @property (nonatomic) enum DDTouchPrivacyLevelOverride touchPrivacy;
		[Export ("touchPrivacy", ArgumentSemantic.Assign)]
		DDTouchPrivacyLevelOverride TouchPrivacy { get; set; }

		// @property (strong, nonatomic) NSNumber * _Nullable hide;
		// NSNumber rather than bool: nil means "inherit", which a bool cannot express.
		[NullAllowed, Export ("hide", ArgumentSemantic.Strong)]
		NSNumber Hide { get; set; }
	}

	// @interface UIView (SWIFT_EXTENSION(DatadogSessionReplay))
	//
	// A category on UIView, so it is bound as a Category rather than as a type of its own. This is
	// how per-view privacy is set: view.DdSessionReplayPrivacyOverrides().ImagePrivacy = ...
	[Category]
	[BaseType (typeof(UIView))]
	interface UIView_DatadogSessionReplay
	{
		// @property (readonly, nonatomic, strong) DDSessionReplayPrivacyOverrides * _Nonnull ddSessionReplayPrivacyOverrides;
		[Export ("ddSessionReplayPrivacyOverrides")]
		DDSessionReplayPrivacyOverrides GetDdSessionReplayPrivacyOverrides ();
	}
}
