//
// Authors:
//   Marek Habersack <mhabersack@novell.com>
//
// (C) 2010 Novell, Inc (http://novell.com/)
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Configuration.Provider;

namespace System.Web.Caching
{
	public sealed class OutputCacheProviderCollection : ProviderCollection
	{
		public OutputCacheProvider this [string name] {
			get { return base [name] as OutputCacheProvider; }
		}
		
		public OutputCacheProviderCollection ()
		{}

		public override void Add (ProviderBase provider)
		{
			if (provider == null)
				throw new ArgumentNullException ("provider");

			if (!(provider is OutputCacheProvider))
				throw new ArgumentException ("is not of type OutputCacheProvider.", "provider");

			base.Add (provider);
		}

		public void CopyTo (OutputCacheProvider[] array, int index)
		{
			base.CopyTo (array, index);
		}
	}
}