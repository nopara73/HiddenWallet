using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WalletWasabi.Fluent.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests
{
	public class StartWasabiOnSystemStartupTests
	{
		[Fact]
		public async Task ModifyStartupOnDifferentSystemsTestAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await StartupHelper.ModifyStartupSettingAsync(true);
				await StartupHelper.ModifyStartupSettingAsync(false);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await Assert.ThrowsAsync<NotImplementedException>(async () => await StartupHelper.ModifyStartupSettingAsync(true));
				await Assert.ThrowsAsync<NotImplementedException>(async () => await StartupHelper.ModifyStartupSettingAsync(false));
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				await StartupHelper.ModifyStartupSettingAsync(true);
				await StartupHelper.ModifyStartupSettingAsync(false);
			}
		}
	}
}
