using NBitcoin.Secp256k1;
using System;
using System.Collections.Generic;
using System.Text;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Crypto.ZeroKnowledge;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Crypto.ZeroKnowledge
{
	public class ProverTests
	{
		[Fact]
		public void Throws()
		{
			var two = new Scalar(2);

			// Demonstrate when it shouldn't throw.
			Prover.CreateProof(two, new Statement(two * Generators.G, Generators.G));

			// Zero cannot pass through.
			Assert.ThrowsAny<ArgumentException>(() => Prover.CreateProof(Scalar.Zero, new Statement(Generators.G, Generators.G)));

			// Public point must be generator * secret.
			Assert.ThrowsAny<InvalidOperationException>(() => Prover.CreateProof(two, new Statement(Generators.G, Generators.G)));
			Assert.ThrowsAny<InvalidOperationException>(() => Prover.CreateProof(two, new Statement(new Scalar(3) * Generators.G, Generators.G)));
			Assert.ThrowsAny<InvalidOperationException>(() => Prover.CreateProof(two, new Statement(Scalar.One * Generators.G, Generators.G)));

			// Secret cannot overflow.
			Assert.ThrowsAny<ArgumentException>(() => Prover.CreateProof(EC.N, new Statement(EC.N * Generators.G, Generators.G)));
			Assert.ThrowsAny<ArgumentException>(() => Prover.CreateProof(CryptoHelpers.ScalarLargestOverflow, new Statement(CryptoHelpers.ScalarLargestOverflow * Generators.G, Generators.G)));
		}

		[Fact]
		public void RandomOverflow()
		{
			var mockRandom = new MockRandom();
			foreach (var scalar in CryptoHelpers.GetScalars(x => x.IsOverflow))
			{
				mockRandom.GetScalarResults.Add(scalar);

				Assert.ThrowsAny<InvalidOperationException>(() => Prover.CreateProof(Scalar.One, new Statement(Scalar.One * Generators.G, Generators.G), mockRandom));
			}
		}

		[Fact]
		public void RandomZero()
		{
			var mockRandom = new MockRandom();
			mockRandom.GetScalarResults.Add(Scalar.Zero);

			// Don't tolerate if the second zero scalar random is received.
			mockRandom.GetScalarResults.Add(Scalar.Zero);

			Assert.ThrowsAny<InvalidOperationException>(() => Prover.CreateProof(Scalar.One, new Statement(Scalar.One * Generators.G, Generators.G), mockRandom));
		}
	}
}
