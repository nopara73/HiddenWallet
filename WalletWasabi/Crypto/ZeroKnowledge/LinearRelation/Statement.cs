using NBitcoin.Secp256k1;
using System.Collections.Generic;
using System.Linq;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Helpers;
using WalletWasabi.Crypto.ZeroKnowledge;

namespace WalletWasabi.Crypto.ZeroKnowledge.LinearRelation
{
	public class Statement : IStatementDescription
	{
		public Statement(params Equation[] equations)
			: this(equations as IEnumerable<Equation>)
		{
		}

		public Statement(IEnumerable<Equation> equations)
		{
			// The equation matrix should not be jagged
			Guard.NotNullOrEmpty(nameof(equations), equations);
			var n = equations.First().Generators.Count();
			Guard.True(nameof(equations), equations.All(e => e.Generators.Count() == n));

			foreach (var generator in equations.SelectMany(equation => equation.Generators))
			{
				Guard.NotNull(nameof(generator), generator);
			}

			Equations = equations;
		}

		public IEnumerable<Equation> Equations { get; }

		public IEnumerable<GroupElement> PublicPoints
		{
			get { return Equations.Select(x => x.PublicPoint); }
		}

		public IEnumerable<GroupElement> Generators
		{
			get { return Equations.SelectMany(x => x.Generators); }
		}

		public bool CheckVerificationEquation(GroupElementVector publicNonces, Scalar challenge, IEnumerable<ScalarVector> allResponses)
		{
			// The responses matrix should match the generators in the equations and
			// there should be once nonce per equation.
			Guard.True(nameof(publicNonces), Equations.Count() == publicNonces.Count());
			Equations.CheckDimesions(allResponses);

			return Equations.Zip(publicNonces, allResponses, (equation, r, s) => equation.Verify(r, challenge, s)).All(x => x);
		}

		public GroupElementVector SimulatePublicNonces(Scalar challenge, IEnumerable<ScalarVector> allGivenResponses)
		{
			// The responses matrix should match the generators in the equations and
			Equations.CheckDimesions(allGivenResponses);

			return new GroupElementVector(Enumerable.Zip(Equations, allGivenResponses, (e, r) => e.Simulate(challenge, r)));
		}
	}
}
