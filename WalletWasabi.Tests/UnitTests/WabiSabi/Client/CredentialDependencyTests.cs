using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Client.CredentialDependencies;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class CredentialDependencyTests
	{
		[Fact]
		public async void AsyncDependencyGraphTraversalAsync()
		{
			var g = DependencyGraph.ResolveCredentialDependencies(
				inputValues: new[] { new ulong[] { 10000, 1930 }, new ulong[] { 1000, 1930 } },
				outputValues: new[] { new ulong[] { 5000, 31 }, new ulong[] { 3500, 31 }, new ulong[] { 2500, 31 } });

			await SimulateAsyncRequestsAsync(g);
		}

		// Demonstrate how to use the dependency grap. Also checks it can be
		// executed with no deadlocks.
		private async Task SimulateAsyncRequestsAsync(DependencyGraph g)
		{
			IEnumerable<CredentialDependency> allEdges = g.EdgeSets.Values.SelectMany(edgeSet => edgeSet.Predecessors.Values.SelectMany(edges => edges));

			// Create a task completion source for each credential that needs to be issued.
			var taskCompletionSources = allEdges.ToImmutableDictionary(e => e, _ => new TaskCompletionSource<ulong>());

			// Track all "sent" requests for testing purposes
			var sent = new ConcurrentDictionary<RequestNode, bool>();

			var rng = new Random();

			// Simulate sending a request. Instead of actual Credential objects,
			// credentials are just represented as ulongs.
			async Task SimulateRequest(RequestNode node)
			{
				var inEdges = Enumerable.Concat(g.InEdges(node, CredentialType.Amount), g.InEdges(node, CredentialType.Vsize));

				var credentialTasks = inEdges.Select(edge =>taskCompletionSources[edge].Task);
				await Task.WhenAll(credentialTasks);

				var credentialsToPresent = credentialTasks.Select(t => t.Result);
				Assert.Equal<ulong>(inEdges.Select(e => e.Value).OrderBy(x => x), credentialsToPresent.OrderBy(x => x));

				Assert.False(sent.ContainsKey(node));
				sent[node] = true;

				await Task.Delay(1 + rng.Next(10));

				foreach (var edge in Enumerable.Concat(g.OutEdges(node, CredentialType.Amount), g.OutEdges(node, CredentialType.Vsize)))
				{
					taskCompletionSources[edge].SetResult(edge.Value);
				}
			}

			var tasks = g.Vertices.Select(SimulateRequest).ToArray();

			Task.WaitAll(tasks, TimeSpan.FromMinutes(2));

			Assert.Empty(tasks.Where(t => t.Status != TaskStatus.RanToCompletion));
			Assert.True(g.Vertices.All(v => sent.ContainsKey(v)));
		}

		[Fact]
		public void ResolveCredentialDependenciesBasic()
		{
			// Whitebox test of simple case, ensuring that edge values are
			// correct
			var inputValues = new ulong[][] { new[] { 3UL, 3UL } };
			var outputValues = new ulong[][] { new[] { 1UL, 1UL }, new[] { 1UL, 1UL }, new[] { 1UL, 1UL } };
			var g = DependencyGraph.ResolveCredentialDependencies(inputValues, outputValues);

			Assert.Equal(5, g.Vertices.Count);

			var edges = g.OutEdges(g.Vertices[0], CredentialType.Amount);

			Assert.Equal(4, edges.Count());
			Assert.Equal(2, edges.Where(x => x.Value > 0).Count());

			var small = edges.OrderByDescending(e => e.Value).Skip(1).First();
			Assert.Equal(1UL, small.Value);
			Assert.Equal(g.Vertices[3], small.To);
			Assert.Empty(g.OutEdges(small.To, CredentialType.Amount));
			Assert.Equal(2, g.InEdges(small.To, CredentialType.Amount).Count());
			Assert.Equal(1, g.InEdges(small.To, CredentialType.Amount).Select(e => e.From).Distinct().Count());
			Assert.Empty(g.OutEdges(small.To, CredentialType.Vsize));
			Assert.Equal(2, g.InEdges(small.To, CredentialType.Vsize).Count());
			Assert.Equal(g.Vertices[0], g.InEdges(small.To, CredentialType.Vsize).Select(e => e.From).Distinct().Single());

			var large = edges.OrderByDescending(e => e.Value).First();
			Assert.Equal(2UL, large.Value);
			Assert.Equal(2, g.InEdges(large.To, CredentialType.Amount).Count());
			Assert.Equal(1, g.InEdges(large.To, CredentialType.Amount).Select(e => e.From).Distinct().Count());

			Assert.Equal(g.OutEdges(large.To, CredentialType.Amount).Select(e => e.To).ToHashSet(), g.Vertices.Skip(1).Take(2).ToHashSet());
			Assert.Equal(g.OutEdges(large.To, CredentialType.Amount).Where(e => e.Value == 0).Select(e => e.To).ToHashSet(), g.Vertices.Skip(1).Take(2).ToHashSet());
			Assert.Equal(g.OutEdges(large.To, CredentialType.Amount).Where(e => e.Value == 1).Select(e => e.To).ToHashSet(), g.Vertices.Skip(1).Take(2).ToHashSet());

			AssertResolvedGraphInvariants(g, inputValues, outputValues);
		}

		[Fact]
		public void ResolveCredentialDependenciesNoVsize()
		{
			var inputValues = new ulong[][] { new[] { 1UL, 0UL } };
			var outputValues = new ulong[][] { new[] { 1UL, 0UL } };
			var g = DependencyGraph.ResolveCredentialDependencies(inputValues, outputValues);

			Assert.Equal(2, g.Vertices.Count);

			var edges = g.OutEdges(g.Vertices[0], CredentialType.Amount);

			Assert.Equal(2, edges.Count());
			Assert.Equal(1, edges.Where(x => x.Value > 0).Count());

			var nonZeroEdge = edges.OrderByDescending(e => e.Value).First();
			Assert.Equal(1UL, nonZeroEdge.Value);
			Assert.Equal(g.Vertices[1], nonZeroEdge.To);
			Assert.Empty(g.OutEdges(nonZeroEdge.To, CredentialType.Amount));
			Assert.Equal(2, g.InEdges(nonZeroEdge.To, CredentialType.Amount).Count());
			Assert.Single(g.InEdges(nonZeroEdge.To, CredentialType.Amount).Select(e => e.From).Distinct());
			Assert.Empty(g.OutEdges(nonZeroEdge.To, CredentialType.Vsize));
			Assert.Equal(2, g.InEdges(nonZeroEdge.To, CredentialType.Vsize).Count());
			Assert.Equal(2, g.InEdges(nonZeroEdge.To, CredentialType.Vsize).Select(e => e.Value == 0).Count());
			Assert.Equal(g.Vertices[0], g.InEdges(nonZeroEdge.To, CredentialType.Vsize).Select(e => e.From).Distinct().Single());

			AssertResolvedGraphInvariants(g, inputValues, outputValues);
		}

		[Theory]
		[InlineData("1,1", "1,1", 2)]
		[InlineData("1,0", "1,0", 2)]
		[InlineData("2,0", "1,0", 2)]
		[InlineData("2,2", "1,1", 2)]
		[InlineData("2,2", "1,2", 2)]
		[InlineData("2,2", "2,2", 2)]
		[InlineData("2,2", "1,1 1,1", 3)]
		[InlineData("1,1 1,1", "2,2", 3)]
		[InlineData("1,1 1,1 1,1", "3,3", 5)]
		[InlineData("3,3", "1,1 1,1 1,1", 5)]
		[InlineData("1,0 1,0 1,0", "3,0", 5)]
		[InlineData("3,0", "1,0 1,0 1,0", 5)]
		[InlineData("1,5 1,5 1,5", "3,1", 5)]
		[InlineData("3,5", "1,1 1,1 1,1", 6)] // Can be improved to 5
		[InlineData("3,5", "1,1 1,1", 4)]
		[InlineData("4,0", "1,0 1,0 1,0 1,0", 7)]
		[InlineData("10,0", "1,0 1,0", 4)]
		[InlineData("10,0", "1,0 1,0 1,0", 6)]
		[InlineData("10,0", "1,0 1,0 1,0 1,0", 8)]
		[InlineData("10,0", "1,0 1,0 1,0 1,0 1,0", 10)]
		[InlineData("10,0", "1,0 1,0 1,0 1,0 1,0 1,0", 12)]
		[InlineData("10,0", "1,0 1,0 1,0 1,0 1,0 1,0 1,0 1,0 1,0 1,0", 19)]
		[InlineData("3,0 3,0", "2,0 2,0 2,0", 5)]
		[InlineData("5,0 1,0", "2,0 2,0 2,0", 6)]
		[InlineData("2,0 2,0", "3,0", 3)]
		[InlineData("3,0 3,0", "2,0 2,0 1,0", 6)]
		[InlineData("8,0 1,0", "3,0 3,0 3,0", 6)]
		[InlineData("8,0 2,0", "3,0 3,0 3,0", 6)]
		[InlineData("8,3 1,0", "3,1 3,1 3,1", 6)]
		[InlineData("8,3 2,0", "3,1 3,1 3,1", 6)]
		[InlineData("8,2 2,2", "3,1 3,1 3,1", 6)]
		[InlineData("3,0 1,0 1,0 1,0", "2,0 2,0 2,0", 7)]
		[InlineData("3,0 1,1 1,1 1,1", "2,1 2,1 2,1", 7)]
		[InlineData("3,3 1,0 1,0 1,0", "2,1 2,1 2,1", 8)]
		[InlineData("3,1 1,1 1,1 1,1", "2,1 2,1 2,1", 7)]
		[InlineData("3,3 1,1 1,1 1,1", "2,1 2,1 2,1", 7)]
		[InlineData("3,3 1,3 1,3 1,3", "2,1 2,1 2,1", 7)]
		[InlineData("3,2 1,0", "2,1 2,1", 4)]
		[InlineData("3,6 1,0", "2,1 2,1", 5)]
		[InlineData("3,6 1,6", "2,1 2,1", 4)]
		[InlineData("4,3", "1,1 1,1 1,1", 6)]
		[InlineData("3,6 1,0 1,0 1,0", "2,1 2,1 2,1", 9)]
		[InlineData("2,6 2,6", "1,3 1,3 1,3 1,3", 6)]
		[InlineData("2,6 2,6", "1,1 1,1 1,1 1,1", 8)]
		[InlineData("3,6 1,6", "4,1", 3)]
		[InlineData("3,6 1,6", "2,1 1,1 1,1", 6)] // Can be improved to 5
		[InlineData("3,6 1,6 1,6 1,6", "2,1 2,1 1,1", 7)]
		[InlineData("3,6 1,6 1,6 1,6", "2,1 2,1 1,1 1,1", 8)]
		[InlineData("3,6 1,6 1,6 1,6", "2,1 2,1 2,1", 7)]
		[InlineData("5,6 1,6 1,6 1,6 1,6 1,6", "2,1 2,1 2,1 2,1 2,1", 12)]
		[InlineData("5,6 5,6 2,6 1,6 1,6 1,6", "8,1 3,1 2,1 2,1", 10)]
		[InlineData("6,6 6,6 3,6", "8,1 3,1 2,1 1,1 1,1", 10)]
		[InlineData("20,1 5,6 5,6 2,6 2,6 1,6 1,6 1,6", "11,1 11,1 8,1 3,1 2,1 2,1", 15)]
		[InlineData("1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "21,21", 41)]
		[InlineData("21,21", "1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", 41)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,1 10,1 10,1 10,1", 41)]
		[InlineData("21,10 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,5", 41)]
		[InlineData("21,6 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,5", 41)]
		[InlineData("21,5 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,5", 41)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,5", 41)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 11,6", 42)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 11,5", 41)]
		[InlineData("20,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,6", 42)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "11,5 10,5 10,5 10,6", 42)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,6", 43)]
		[InlineData("21,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1 1,1", "10,5 10,5 10,5 10,5", 42)]
		[InlineData("1,255 1,255 1,255 1,255", "4,1", 7)]
		[InlineData("13,255 1,255 1,255 1,255 1,255 1,255", "3,255 3,255 3,255 3,255 3,255 3,255", 17)]
		public async void ResolveCredentialDependenciesAsync(string inputs, string outputs, int finalVertexCount)
		{
			// blackbox tests (apart from finalVertexCount, which leaks
			// information about implementation) covering valid range
			// of inputs with various corner cases that must be handled.

			// Parse values out of strings because InputData can't contain arrays
			var inputValues = inputs.Split(" ").Select(x => x.Split(",").Select(y => ulong.Parse(y)));
			var outputValues = outputs.Split(" ").Select(x => x.Split(",").Select(y => ulong.Parse(y)));

			var g = DependencyGraph.ResolveCredentialDependencies(inputValues, outputValues);

			// Useful for debugging:
			// File.WriteAllText("/tmp/graphs/" + inputs + " -- " + outputs + ".dot", g.Graphviz());
			// the resulting graph can be rendered with graphviz (dot -Tpng -O *.dot)
			AssertResolvedGraphInvariants(g, inputValues, outputValues);
			Assert.Equal(finalVertexCount, g.Vertices.Count);

			// TODO when sum(in) == sum(out) for all credential types, also
			// verify the reverse graph can be resolved.

			await SimulateAsyncRequestsAsync(g);
		}

		private void AssertResolvedGraphInvariants(DependencyGraph graph, IEnumerable<IEnumerable<ulong>> inputValues, IEnumerable<IEnumerable<ulong>> outputValues)
		{
			foreach (var credentialType in DependencyGraph.CredentialTypes)
			{
				// Input nodes
				foreach (var node in graph.Inputs)
				{
					var balance = graph.Balance(node, credentialType);

					Assert.True(balance >= 0);
					if (credentialType == CredentialType.Vsize)
					{
						Assert.InRange(balance, 0, ProtocolConstants.MaxVsizeCredentialValue);
					}
					Assert.Equal(0, node.MaxInDegree);
					Assert.Equal(node.MaxInDegree, graph.InDegree(node, credentialType));

					var outDegree = graph.OutDegree(node, credentialType);
					Assert.InRange(outDegree, 0, DependencyGraph.K - (balance == 0 ? 0 : 1)); // for amount creds, 1..K?
				}

				// Output nodes
				foreach (var node in graph.Outputs)
				{
					var balance = graph.Balance(node, credentialType);

					Assert.Equal(0, balance);

					Assert.Equal(DependencyGraph.K, node.MaxInDegree);
					Assert.Equal(node.MaxInDegree, graph.InDegree(node, credentialType));

					Assert.Equal(0, node.MaxOutDegree);
					Assert.Equal(0, node.MaxZeroOnlyOutDegree);
					Assert.Equal(0, graph.OutDegree(node, credentialType));
				}

				// Reissuance nodes
				foreach (var node in graph.Reissuances)
				{
					var balance = graph.Balance(node, credentialType);

					Assert.True(balance >= 0);
					if (credentialType == CredentialType.Vsize)
					{
						Assert.InRange(balance, 0, ProtocolConstants.MaxVsizeCredentialValue);
					}

					Assert.Equal(DependencyGraph.K, node.MaxInDegree);
					Assert.Equal(node.MaxInDegree, graph.InDegree(node, credentialType));

					var outDegree = graph.OutDegree(node, credentialType);
					Assert.InRange(outDegree, 0, DependencyGraph.K - (balance == 0 ? 0 : 1));
				}
			}

			// Ensure that vsize credentials do not exceed the range proof width
			foreach (var edge in graph.EdgeSets[CredentialType.Vsize].Successors.Values.SelectMany(x => x))
			{
				Assert.InRange<ulong>(edge.Value, 0, ProtocolConstants.MaxVsizeCredentialValue);
			}

			// TODO add InlineData param for max depth?

			// TODO assert max depth < ceil(log count)?
		}

		[Fact]
		public void ResolveCredentialDependenciesThrows()
		{
			foreach ((var inputValues, var outputAmounts) in new (ulong[][], ulong[][])[]
			{
				(new[] { new[] { 1UL, 0UL } }, new[] { new[] { 2UL, 0UL } }),
				(new[] { Array.Empty<ulong>() }, new[] { new[] { 1UL, 0UL } }),
				(new[] { new[] { 1UL } }, new[] { new[] { 1UL, 0UL } }),
				(new[] { new[] { 1UL, 1UL, 1UL, } }, new[] { new[] { 1UL, 0UL } }),
				(new[] { new[] { 1UL, 0UL } }, new[] { new[] { 1UL } }),
				(new[] { new[] { 1UL, 0UL } }, new[] { new[] { 1UL, 0UL, 0UL } }),
			})
			{
				Assert.Throws<ArgumentException>(() => DependencyGraph.ResolveCredentialDependencies(inputValues, outputAmounts));
			}
		}

		[Fact]
		public void EdgeConstraints()
		{
			var g = DependencyGraph.FromValues(new[] { new ulong[] { 11, 0 }, new ulong[] { 8, 0 } }, new[] { new ulong[] { 7, 0 }, new ulong[] { 11, 0 } });

			var i = g.Inputs[0];
			var o = g.Outputs[0];

			var edgeSet = g.EdgeSets[0];

			Assert.Equal(2, DependencyGraph.K);

			// insufficient out degree or in degree
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(o, i, 1));
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(o, i, 0));

			// insufficient out degree
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(o, o, 1));
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(o, o, 0));

			// insufficient in degree
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, i, 1));
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, i, 0));

			// excessive inwards/outwards value
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 12));

			// excessive inwards value
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 11));

			// excessive inwards value
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 7).AddEdge(i, o, 5));

			// max indegree exceeded
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 7).AddEdge(i, o, 0).AddEdge(i, o, 0));

			var o2 = g.Outputs[1];
			Assert.Equal(0, edgeSet.AddEdge(i, o2, 7).AddEdge(i, o2, 4).Balance(i));
			Assert.Equal(0, edgeSet.AddEdge(i, o2, 7).AddEdge(i, o2, 4).Balance(o2));
			Assert.Equal(0, edgeSet.AddEdge(i, o, 7).AddEdge(i, o, 0).AddEdge(i, o2, 4).Balance(i));
			Assert.Equal(1, edgeSet.AddEdge(i, o, 7).AddEdge(i, o, 0).AddEdge(i, o2, 0).RemainingOutDegree(i));
			Assert.Equal(1, edgeSet.AddEdge(i, o2, 11).AddEdge(i, o2, 0).AddEdge(i, o, 0).RemainingOutDegree(i));

			// insufficient outwards balance
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 1).AddEdge(i, o2, 11));

			// final in edge must discharge remaining negative balance
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 7).AddEdge(i, o, 0).AddEdge(i, o2, 4).AddEdge(i, o2, 0));

			// final out edge must discharge remaining balance
			Assert.Throws<InvalidOperationException>(() => edgeSet.AddEdge(i, o, 7).AddEdge(i, o2, 3));
		}
	}
}
