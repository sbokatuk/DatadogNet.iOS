// Nullable annotations are enabled per file rather than for the project: the generated
// binding sources are not written against a nullable context, and switching the whole
// project over would bury real warnings here under hundreds of generated ones.
#nullable enable

using System;
using Foundation;
using ObjCRuntime;

namespace DatadogObjc
{
	public partial class DDURLSessionInstrumentationConfiguration
	{
		/// <summary>
		/// Creates a configuration that instruments the given <c>NSUrlSessionDataDelegate</c> type.
		/// </summary>
		/// <param name="delegateType">
		/// The delegate class to instrument. Must be an <see cref="NSObject"/> subclass that
		/// implements <see cref="INSUrlSessionDataDelegate"/> and is registered with the
		/// Objective-C runtime.
		/// </param>
		/// <remarks>
		/// The native initializer takes an Objective-C <c>Class</c>, which reaches C# as a bare
		/// <see cref="IntPtr"/>. Passing the right value means knowing to call
		/// <c>Class.GetHandle (typeof (T))</c> - which nothing in the generated signature hints at,
		/// and which silently yields <see cref="IntPtr.Zero"/> for a type the runtime does not know,
		/// leaving instrumentation quietly disabled rather than failing.
		/// </remarks>
		/// <exception cref="ArgumentException">
		/// <paramref name="delegateType"/> is not a registered Objective-C class.
		/// </exception>
		public static DDURLSessionInstrumentationConfiguration Create (Type delegateType) =>
			new DDURLSessionInstrumentationConfiguration (HandleFor (delegateType, nameof (delegateType)));

		/// <inheritdoc cref="Create(Type)"/>
		/// <typeparam name="TDelegate">The delegate class to instrument.</typeparam>
		public static DDURLSessionInstrumentationConfiguration Create<TDelegate> ()
			where TDelegate : NSObject, INSUrlSessionDataDelegate =>
			Create (typeof (TDelegate));

		/// <summary>The delegate class being instrumented.</summary>
		/// <remarks>
		/// The typed view of <see cref="DelegateClass"/>, which is an <see cref="IntPtr"/> holding
		/// an Objective-C <c>Class</c>. Returns <see langword="null"/> if the class does not map
		/// back to a managed type.
		/// </remarks>
		public Type? DelegateType {
			get => DelegateClass == IntPtr.Zero ? null : Class.Lookup (new Class (DelegateClass));
			set => DelegateClass = value is null ? IntPtr.Zero : HandleFor (value, nameof (value));
		}

		internal static IntPtr HandleFor (Type delegateType, string parameterName)
		{
			if (delegateType is null)
				throw new ArgumentNullException (parameterName);

			var handle = Class.GetHandle (delegateType);
			if (handle == IntPtr.Zero) {
				throw new ArgumentException (
					$"'{delegateType.FullName}' is not a registered Objective-C class. It must derive from " +
					"NSObject, implement INSUrlSessionDataDelegate, and carry a [Register] attribute.",
					parameterName);
			}

			return handle;
		}
	}

	public partial class DDURLSessionInstrumentation
	{
		/// <summary>Starts instrumenting the given delegate type for RUM resource and trace collection.</summary>
		/// <remarks>
		/// Shorthand for
		/// <c>EnableWithConfiguration (DDURLSessionInstrumentationConfiguration.Create (delegateType))</c>.
		/// Enable this once, before creating the <c>NSUrlSession</c> that uses the delegate.
		/// </remarks>
		public static void Enable (Type delegateType) =>
			EnableWithConfiguration (DDURLSessionInstrumentationConfiguration.Create (delegateType));

		/// <inheritdoc cref="Enable(Type)"/>
		/// <typeparam name="TDelegate">The delegate class to instrument.</typeparam>
		public static void Enable<TDelegate> ()
			where TDelegate : NSObject, INSUrlSessionDataDelegate =>
			Enable (typeof (TDelegate));

		/// <summary>Stops instrumenting the given delegate type.</summary>
		public static void Disable (Type delegateType) =>
			DisableWithDelegateClass (
				DDURLSessionInstrumentationConfiguration.HandleFor (delegateType, nameof (delegateType)));

		/// <inheritdoc cref="Disable(Type)"/>
		/// <typeparam name="TDelegate">The delegate class to stop instrumenting.</typeparam>
		public static void Disable<TDelegate> ()
			where TDelegate : NSObject, INSUrlSessionDataDelegate =>
			Disable (typeof (TDelegate));
	}
}
