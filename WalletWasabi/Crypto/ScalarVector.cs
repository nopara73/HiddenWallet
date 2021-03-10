using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Helpers;

namespace WalletWasabi.Crypto
{
	public class ScalarVector : IEnumerable<Scalar>
	{
		[JsonConstructor]
		internal ScalarVector(IEnumerable<Scalar> scalars)
		{
			_ = Guard.NotNullOrEmpty(nameof(scalars), scalars);
			Scalars = scalars.ToArray();
		}

		internal ScalarVector(params Scalar[] scalars)
			: this(scalars as IEnumerable<Scalar>)
		{
		}

		private IEnumerable<Scalar> Scalars { get; }

		public IEnumerator<Scalar> GetEnumerator() =>
			Scalars.GetEnumerator();

		public int Count => Scalars.Count();

		public static GroupElement operator *(ScalarVector scalars, GroupElementVector groupElements)
		{
			_ = Guard.True(nameof(groupElements.Count), groupElements.Count == scalars.Count);

			var gej = ECMultContext.Instance.MultBatch(scalars.ToArray(), groupElements.Select(x => x.Ge).ToArray());
			return new GroupElement( gej );
		}

		public static ScalarVector operator *(Scalar scalar, ScalarVector scalars)
		{
			_ = Guard.NotNull(nameof(scalars), scalars);

			return new ScalarVector(scalars.Select(si => scalar * si));
		}

		public static ScalarVector operator +(ScalarVector scalars1, ScalarVector scalars2)
		{
			_ = Guard.NotNull(nameof(scalars1), scalars1);
			_ = Guard.NotNull(nameof(scalars2), scalars2);
			_ = Guard.True(nameof(scalars1.Count), scalars1.Count == scalars2.Count);

			return new ScalarVector(Enumerable.Zip(scalars1, scalars2, (s1, s2) => s1 + s2));
		}

		IEnumerator IEnumerable.GetEnumerator() =>
			GetEnumerator();
	}
}
