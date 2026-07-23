// Nullable annotations are enabled per file rather than for the project: the generated
// binding sources are not written against a nullable context, and switching the whole
// project over would bury real warnings here under hundreds of generated ones.
#nullable enable

using System;
using System.Collections.Generic;
using Foundation;

namespace DatadogObjc
{
	/// <summary>Which wire format a trace context is propagated in.</summary>
	/// <remarks>
	/// A C# mirror of <see cref="DDTracingHeaderType"/>, which is bound as a class of static
	/// instances rather than an enum because that is how it crosses from Swift into Objective-C.
	/// Comparing those instances by reference is not something the binding promises, and switching
	/// on them is not possible at all - so, as with <c>DDDatadog.SetTrackingConsent</c>, the
	/// ergonomic overloads take a real enum.
	/// </remarks>
	public enum TracingHeaderFormat
	{
		/// <summary>Datadog's own <c>x-datadog-*</c> headers.</summary>
		Datadog,

		/// <summary>B3 single-header format.</summary>
		B3,

		/// <summary>B3 multi-header format.</summary>
		B3Multi,

		/// <summary>W3C Trace Context (<c>traceparent</c>).</summary>
		TraceContext,
	}

	/// <summary>
	/// Ergonomic overloads over the OpenTracing API 2.x tracing is built on.
	/// </summary>
	/// <remarks>
	/// The generated members are all still there; these sit alongside them. Each one closes a gap
	/// that only appears from C#, and each has a counterpart on the Android side of a
	/// cross-platform app - so these are also what makes a shared tracing abstraction over both
	/// SDKs possible.
	/// </remarks>
	public static class TracingExtensions
	{
		/// <summary>
		/// The Datadog-format headers a trace context is propagated in.
		/// </summary>
		/// <remarks>
		/// Also the only route to a span's trace and span ids from Objective-C: <c>IOTSpanContext</c>
		/// declares nothing but <c>forEachBaggageItem</c>, and no bound type exposes a
		/// <c>traceID</c> or <c>spanID</c>. dd-sdk-android has both directly, on
		/// <c>SpanContext.toTraceId()</c> and <c>toSpanId()</c>, so this asymmetry is the iOS SDK's
		/// rather than the binding's.
		/// </remarks>
		const string TraceIdHeader = "x-datadog-trace-id";

		const string SpanIdHeader = "x-datadog-parent-id";

		/// <summary>
		/// Writes a span's trace context into HTTP headers, so the trace continues into the service
		/// being called.
		/// </summary>
		/// <param name="tracer">The tracer, usually <see cref="DDTracer.Shared"/>.</param>
		/// <param name="span">The span to propagate.</param>
		/// <param name="headerTypes">
		/// Which formats to write. Defaults to Datadog's own plus W3C Trace Context, which between
		/// them reach a Datadog-instrumented backend and a standards-compliant one without having
		/// to know which you have.
		/// </param>
		/// <returns>The headers to add to the request.</returns>
		/// <remarks>
		/// The raw call is <c>tracer.Inject (context, OT.FormatTextMap, carrier, out error)</c>,
		/// where the carrier is a writer object that is <i>also</i> where the result is read back
		/// from - and there is a different writer type per format, each with its own constructor
		/// arity. Getting that wrong produces no headers and no error.
		/// <para>
		/// The sampling strategy is head-based, because the keep-or-drop decision was made when the
		/// trace started; deciding it again per writer would propagate "sampled" in one format and
		/// "dropped" in another on the same request. Context injection is unconditional, so the
		/// headers still go out for a dropped trace - which is what lets the receiving service
		/// stitch the request together even when nothing is stored.
		/// </para>
		/// </remarks>
		public static IReadOnlyDictionary<string, string> InjectHeaders (
			this IOTTracer tracer,
			IOTSpan span,
			IReadOnlyList<TracingHeaderFormat>? headerTypes = null)
		{
			if (tracer is null)
				throw new ArgumentNullException (nameof (tracer));
			if (span is null)
				throw new ArgumentNullException (nameof (span));

			var headers = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);

			var formats = headerTypes ?? new[] { TracingHeaderFormat.Datadog, TracingHeaderFormat.TraceContext };

			foreach (var format in formats) {
				foreach (var field in Inject (tracer, span, format))
					headers[field.Key] = field.Value;
			}

			return headers;
		}

		/// <summary>The span's trace id, or an empty string if it has none.</summary>
		/// <remarks>
		/// Derived by injecting into a Datadog-format writer and reading the result, because
		/// <c>IOTSpanContext</c> exposes no id of its own. Worth caching if you need it more than
		/// once per span: each call constructs a writer and crosses into Swift.
		/// </remarks>
		public static string GetTraceId (this IOTSpan span) => GetId (span, TraceIdHeader);

		/// <summary>The span's own id, or an empty string if it has none.</summary>
		/// <inheritdoc cref="GetTraceId" path="/remarks"/>
		public static string GetSpanId (this IOTSpan span) => GetId (span, SpanIdHeader);

		/// <summary>Marks a span as failed and attaches an exception's type, message and stack.</summary>
		/// <remarks>
		/// The bound <c>SetError</c> takes an <see cref="NSError"/>, which a managed exception is
		/// not. <c>SetErrorWithKind</c> takes the three strings instead, so the managed type,
		/// message and stack all reach Datadog - and the kind is what APM groups errors by, so
		/// passing the exception type rather than its message is what keeps that grouping useful.
		/// </remarks>
		public static void SetError (this IOTSpan span, Exception exception)
		{
			if (span is null)
				throw new ArgumentNullException (nameof (span));
			if (exception is null)
				throw new ArgumentNullException (nameof (exception));

			span.SetErrorWithKind (
				exception.GetType ().FullName ?? exception.GetType ().Name,
				exception.Message,
				exception.StackTrace);
		}

		/// <summary>Attaches a structured log to a span, without hand-wrapping the values.</summary>
		public static void Log (this IOTSpan span, IReadOnlyDictionary<string, object?> fields)
		{
			if (span is null)
				throw new ArgumentNullException (nameof (span));
			if (fields is null)
				throw new ArgumentNullException (nameof (fields));

			span.Log (DatadogAttributes.From (fields));
		}

		static string GetId (IOTSpan span, string header)
		{
			if (span is null)
				throw new ArgumentNullException (nameof (span));

			foreach (var field in Inject (span.Tracer, span, TracingHeaderFormat.Datadog)) {
				if (string.Equals (field.Key, header, StringComparison.OrdinalIgnoreCase))
					return field.Value;
			}

			return string.Empty;
		}

		static IEnumerable<KeyValuePair<string, string>> Inject (
			IOTTracer tracer,
			IOTSpan span,
			TracingHeaderFormat format)
		{
			NSDictionary<NSString, NSString> fields;

			// One writer per format: dd-sdk-ios has no writer that emits several at once, unlike
			// dd-sdk-android where the header types are a property of the tracer and a single
			// inject call writes all of them.
			switch (format) {
			case TracingHeaderFormat.Datadog: {
				var writer = new DDHTTPHeadersWriter (DDTraceSamplingStrategy.HeadBased, DDTraceContextInjection.All);
				tracer.Inject (span.Context, OT.FormatTextMap, writer, out _);
				fields = writer.TraceHeaderFields;
				break;
			}

			case TracingHeaderFormat.TraceContext: {
				var writer = new DDW3CHTTPHeadersWriter (DDTraceSamplingStrategy.HeadBased, DDTraceContextInjection.All);
				tracer.Inject (span.Context, OT.FormatTextMap, writer, out _);
				fields = writer.TraceHeaderFields;
				break;
			}

			case TracingHeaderFormat.B3:
			case TracingHeaderFormat.B3Multi: {
				var encoding = format == TracingHeaderFormat.B3 ? DDInjectEncoding.Single : DDInjectEncoding.Multiple;
				var writer = new DDB3HTTPHeadersWriter (DDTraceSamplingStrategy.HeadBased, encoding, DDTraceContextInjection.All);
				tracer.Inject (span.Context, OT.FormatTextMap, writer, out _);
				fields = writer.TraceHeaderFields;
				break;
			}

			default:
				yield break;
			}

			foreach (var key in fields.Keys)
				yield return new KeyValuePair<string, string> (key.ToString (), fields[key].ToString ());
		}
	}
}
