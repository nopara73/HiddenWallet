using System.Collections.Generic;
using WalletWasabi.Helpers;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Crypto.ZeroKnowledge.LinearRelation;
using WalletWasabi.Crypto;
using NBitcoin.Secp256k1;
using NBitcoin;

namespace System.Linq
{
	public static class Extensions
	{
		public static Scalar Sum(this IEnumerable<Scalar> scalars) =>
			scalars.Aggregate(Scalar.Zero, (s, acc) => s + acc);

		public static GroupElement Sum(this IEnumerable<GroupElement> groupElements) =>
			groupElements.Aggregate(GroupElement.Infinity, (ge, acc) => ge + acc);

		public static Money ToMoney(this Scalar scalar) =>
			Money.Satoshis(scalar.ToUlong());

		public static ulong ToUlong(this Scalar scalar) =>
			((ulong)scalar.d1 << 32) | scalar.d0;

		public static IEnumerable<TResult> Zip<TFirst, TSecond, TThird, TResult>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second, IEnumerable<TThird> third, Func<TFirst, TSecond, TThird, TResult> resultSelector)
		{
			_ = Guard.NotNull(nameof(first), first);
			_ = Guard.NotNull(nameof(second), second);
			_ = Guard.NotNull(nameof(third), third);
			_ = Guard.NotNull(nameof(resultSelector), resultSelector);
			using var e1 = first.GetEnumerator();
			using var e2 = second.GetEnumerator();
			using var e3 = third.GetEnumerator();
			while (e1.MoveNext() && e2.MoveNext() && e3.MoveNext())
			{
				yield return resultSelector(e1.Current, e2.Current, e3.Current);
			}
		}
	}
}
