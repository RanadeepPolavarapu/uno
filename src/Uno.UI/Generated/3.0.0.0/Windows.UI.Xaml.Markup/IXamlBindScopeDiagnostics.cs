#pragma warning disable 108 // new keyword hiding
#pragma warning disable 114 // new keyword hiding
namespace Windows.UI.Xaml.Markup
{
	#if __ANDROID__ || __IOS__ || NET46 || __WASM__ || __MACOS__
	[global::Uno.NotImplemented]
	#endif
	public  partial interface IXamlBindScopeDiagnostics 
	{
		#if __ANDROID__ || __IOS__ || NET46 || __WASM__ || __MACOS__
		void Disable( int lineNumber,  int columnNumber);
		#endif
	}
}