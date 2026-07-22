# DatadogNet.iOS

.NET for iOS and .NET MAUI bindings for the native [Datadog iOS SDK (`dd-sdk-ios`)](https://github.com/DataDog/dd-sdk-ios).

Enable Datadog Real User Monitoring, Logs, Trace, Session Replay, WebView tracking and crash
reporting from C# in a `net8.0-ios`, `net9.0-ios` or `net10.0-ios` app.

```bash
dotnet add package DatadogNet.Objc.iOS
```

```csharp
using DatadogObjc;

var configuration = new DDConfiguration(clientToken: "<CLIENT_TOKEN>", env: "production");
configuration.Site = DDSite.Us1;

DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted);
DDRUM.EnableWith(new DDRUMConfiguration(applicationID: "<RUM_APPLICATION_ID>"));
```

Full documentation is in progress.

## Licence

The binding code in this repository is MIT. The native binaries the packages ship are built by
Datadog and are Apache-2.0. Each package carries both texts under `licenses/`.
