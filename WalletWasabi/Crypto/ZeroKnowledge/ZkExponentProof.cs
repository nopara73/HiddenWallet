using NBitcoin.Secp256k1;
using System;
using System.Collections.Generic;
using System.Text;
using WalletWasabi.Helpers;

namespace WalletWasabi.Crypto.ZeroKnowledge
{
	public class ZkExponentProof
	{
		public ZkExponentProof(GE publicPoint, GE randomPoint, Scalar response)
		{
			Guard.True($"{nameof(publicPoint)}.{nameof(publicPoint.IsValidVariable)}", publicPoint.IsValidVariable);
			Guard.True($"{nameof(randomPoint)}.{nameof(randomPoint.IsValidVariable)}", randomPoint.IsValidVariable);

			PublicPoint = publicPoint;
			RandomPoint = randomPoint;
			Response = response;
		}

		public GE PublicPoint { get; }
		public GE RandomPoint { get; }
		public Scalar Response { get; }
	}
}
