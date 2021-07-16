using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Clients
{
	public class PreventSleepTests
	{
		[Fact]
		public async Task ProlongSystemAwakeCanBeExecutedAsync()
		{
			SynchronizationContext? synchronizationContext = SynchronizationContext.Current;

			await EnvironmentHelpers.ProlongSystemAwakeAsync(synchronizationContext);
		}
	}
}
