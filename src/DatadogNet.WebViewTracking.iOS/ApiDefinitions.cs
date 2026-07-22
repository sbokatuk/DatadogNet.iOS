using Foundation;
using WebKit;

namespace DatadogWebViewTracking
{
	// @interface DDWebViewTracking : NSObject
	[BaseType (typeof(NSObject))]
	[DisableDefaultCtor]
	interface DDWebViewTracking
	{
		// +(void)enableWithWebView:(WKWebView * _Nonnull)webView hosts:(NSSet<NSString *> * _Nonnull)hosts logsSampleRate:(float)logsSampleRate;
		[Static]
		[Export ("enableWithWebView:hosts:logsSampleRate:")]
		void EnableWithWebView (WKWebView webView, NSSet<NSString> hosts, float logsSampleRate);

		// +(void)disableWithWebView:(WKWebView * _Nonnull)webView;
		[Static]
		[Export ("disableWithWebView:")]
		void DisableWithWebView (WKWebView webView);
	}
}
