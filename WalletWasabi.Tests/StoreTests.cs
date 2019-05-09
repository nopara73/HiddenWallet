using NBitcoin;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Stores;
using Xunit;

namespace WalletWasabi.Tests
{
	public class StoreTests
	{
		[Fact]
		public async Task IndexStoreTestsAsync()
		{
			await Task.Delay(1);
			var indexStore = new IndexStore();

			var dir = Path.Combine(Global.DataDir, nameof(IndexStoreTestsAsync));
			var network = Network.Main;
			await indexStore.InitializeAsync(dir, network);
		}

		[Fact]
		public async Task AsyncMutexTestsAsync()
		{
			AsyncMutex asyncMutex = new AsyncMutex("mutex1");

			// Cannot be IDisposable because the pattern is like Nito's AsyncLock.
			Assert.False(asyncMutex is IDisposable);

			// Use the mutex two times after each other.
			using (await asyncMutex.LockAsync())
			{
				await Task.Delay(1);
			}

			using (await asyncMutex.LockAsync())
			{
				await Task.Delay(1);
			}

			// Release the Mutex from another thread.

			var disposable = await asyncMutex.LockAsync();

			var myThread = new Thread(new ThreadStart(() =>
			 {
				 disposable.Dispose();
			 }));
			myThread.Start();
			myThread.Join();

			using (await asyncMutex.LockAsync())
			{
				await Task.Delay(1);
			}

			// Acquire the Mutex with a background thread.

			var myTask = Task.Run(async () =>
			{
				using (await asyncMutex.LockAsync())
				{
					await Task.Delay(3000);
				}
			});

			// Wait for the Task.Run to Acquire the Mutex.
			await Task.Delay(100);

			// Try to get the Mutex and save the time.
			DateTime timeOfstart = DateTime.Now;
			DateTime timeOfAcquired = default;

			using (await asyncMutex.LockAsync())
			{
				timeOfAcquired = DateTime.Now;
			};

			Assert.True(myTask.IsCompletedSuccessfully);

			var elapsed = timeOfAcquired - timeOfstart;
			Assert.InRange(elapsed, TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(4000));

			// Standard Mutex test.
			int cnt = 0;
			List<int> numbers = new List<int>();
			var rand = new Random();
			async Task TestLockAsync()
			{
				using (await asyncMutex.LockAsync())
				{
					cnt++;

					await Task.Delay(rand.Next(5));
					numbers.Add(cnt);
				}
			}

			var tasks = new List<Task>();

			for (int i = 0; i < 100; i++)
			{
				var task = TestLockAsync();

				tasks.Add(task);
			}

			await Task.WhenAll(tasks);

			Assert.Equal(100, numbers.Count);
			for (int i = 1; i < 100; i++)
			{
				var prevnum = numbers[i - 1];
				var num = numbers[i];
				Assert.Equal(prevnum + 1, num);
			}
		}

		[Fact]
		public async Task AsyncMutexSameNameTestsAsync()
		{
			// Different AsyncMutex object but same name.
			AsyncMutex asyncMutex = new AsyncMutex("mutex1");
			AsyncMutex asyncMutex2 = new AsyncMutex("mutex1");

			// Acquire the first mutex with a background thread and hold it for a while.
			var myTask2 = Task.Run(async () =>
			{
				using (await asyncMutex.LockAsync())
				{
					await Task.Delay(3000);
				}
			});

			// Make sure the task started.
			await Task.Delay(100);

			// Now try to acquire another AsyncMutex object but with the same name! Should throw an exception.
			await Assert.ThrowsAsync<IOException>(async () =>
			{
				using (await asyncMutex2.LockAsync())
				{
				}
			});

			await myTask2;
			Assert.True(myTask2.IsCompletedSuccessfully);
		}

		[Fact]
		public async Task IoManagerTestsAsync()
		{
			var file1 = Path.Combine(Global.DataDir, nameof(IoManagerTestsAsync), $"file1.dat");
			var file2 = Path.Combine(Global.DataDir, nameof(IoManagerTestsAsync), $"file2.dat");

			Random random = new Random();
			List<string> lines = new List<string>();
			for (int i = 0; i < 1000; i++)
			{
				const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

				string line = new string(Enumerable.Repeat(chars, 100)
				  .Select(s => s[random.Next(s.Length)]).ToArray());

				lines.Add(line);
			}

			// Single thread file operations.

			IoManager ioman1 = new IoManager(file1);

			// Delete the file if Exist.

			ioman1.DeleteMe();
			Assert.False(ioman1.Exists());

			Assert.False(File.Exists(ioman1.DigestFilePath));

			// Write the data to the file.

			await ioman1.WriteAllLinesAsync(lines);
			Assert.True(ioman1.Exists());

			// Check if the digest file is created.

			Assert.True(File.Exists(ioman1.DigestFilePath));

			// Read back the content and check.

			bool IsStringArraysEqual(string[] lines1, string[] lines2)
			{
				if (lines1.Length != lines2.Length) return false;

				for (int i = 0; i < lines1.Length; i++)
				{
					string line = lines2[i];
					var readline = lines1[i];

					if (!line.Equals(readline)) return false;
				}
				return true;
			}

			var readLines = await ioman1.ReadAllLinesAsync();

			Assert.True(IsStringArraysEqual(readLines, lines.ToArray()));

			// Check digest file, and write only differ logic.

			// Write the same content, file should not be written.
			var currentDate = File.GetLastWriteTimeUtc(ioman1.OriginalFilePath);
			await Task.Delay(500);
			await ioman1.WriteAllLinesAsync(lines);
			var noChangeDate = File.GetLastWriteTimeUtc(ioman1.OriginalFilePath);
			Assert.Equal(currentDate, noChangeDate);

			// Write different content, file should be written.
			currentDate = File.GetLastWriteTimeUtc(ioman1.OriginalFilePath);
			await Task.Delay(500);
			lines.Add("Lorem ipsum dolor sit amet, consectetur adipiscing elit.");
			await ioman1.WriteAllLinesAsync(lines);
			var newContentDate = File.GetLastWriteTimeUtc(ioman1.OriginalFilePath);
			Assert.NotEqual(currentDate, newContentDate);

			/* The next test is commented out because on mac and on linux File.Open does not lock the file
			 * it can be still written by the ioman1.WriteAllLinesAsync(). Tried with FileShare.None FileShare.Delete
			 * FileStream.Lock none of them are working or caused not supported on this platform exception.
			 * So there is no OP system way to garantee that the file won't be written during another write operation.
			 * For example git is using lock files to solve this problem. We are using system wide mutexes.
			 * For now there is no other way to do this. Some useful links :
			 * https://stackoverflow.com/questions/2751734/how-do-filesystems-handle-concurrent-read-write
			 * https://github.com/dotnet/corefx/issues/5964
			 */

			//using (File.OpenWrite(ioman1.OriginalFilePath))
			//{
			//	// Should be OK because the same data is written.
			//	await ioman1.WriteAllLinesAsync(lines);
			//}
			//using (File.OpenWrite(ioman1.OriginalFilePath))
			//{
			//	// Should fail because different data is written.
			//	await Assert.ThrowsAsync<IOException>(async () => await ioman1.WriteAllLinesAsync(lines));
			//}

			await ioman1.WriteAllLinesAsync(lines);

			// Mutex tests.

			// Acquire the Mutex with a background thread.

			var myTask = Task.Run(async () =>
			{
				using (await ioman1.Mutex.LockAsync())
				{
					await Task.Delay(3000);
				}
			});

			// Wait for the Task.Run to Acquire the Mutex.
			await Task.Delay(100);

			// Try to get the Mutex and save the time.
			DateTime timeOfstart = DateTime.Now;
			DateTime timeOfAcquired = default;

			using (await ioman1.Mutex.LockAsync())
			{
				timeOfAcquired = DateTime.Now;
			}

			Assert.True(myTask.IsCompletedSuccessfully);

			var elapsed = timeOfAcquired - timeOfstart;
			Assert.InRange(elapsed, TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(4000));

			// Simulate file write error and recovery logic.

			// We have only *.new and *.old files.
			File.Copy(ioman1.OriginalFilePath, ioman1.OldFilePath);
			File.Move(ioman1.OriginalFilePath, ioman1.NewFilePath);

			// At this point there is now OriginalFile.

			var newFile = await ioman1.ReadAllLinesAsync();

			Assert.True(IsStringArraysEqual(newFile, lines.ToArray()));

			// Add one more line to have different data.
			lines.Add("Lorem ipsum dolor sit amet, consectetur adipiscing elit.");

			await ioman1.WriteAllLinesAsync(lines);

			// Check recovery mechanism.

			Assert.True(
				File.Exists(ioman1.OriginalFilePath) &&
				!File.Exists(ioman1.OldFilePath) &&
				!File.Exists(ioman1.NewFilePath));

			ioman1.DeleteMe();

			Assert.False(ioman1.Exists());

			// Check if directory is empty.

			var fileCount = Directory.EnumerateFiles(Path.GetDirectoryName(ioman1.OriginalFilePath)).Count();
			Assert.Equal(0, fileCount);

			// Check Mutex usage on simultaneous file writes.

			IoManager ioman2 = new IoManager(file2);

			await Task.Run(async () =>
			{
				using (await ioman1.Mutex.LockAsync())
				{
					// Should not be a problem because they using different Mutexes.
					using (await ioman2.Mutex.LockAsync())
					{
						await ioman1.WriteAllLinesAsync(lines);
						await ioman2.WriteAllLinesAsync(lines);
						ioman1.DeleteMe();
						ioman2.DeleteMe();
					}
				}
			});

			// TryReplace test.
			var dummyFilePath = $"{ioman1.OriginalFilePath}dummy";
			var dummyContent = new string[]
			{
					"banana",
					"peach"
			};
			await File.WriteAllLinesAsync(dummyFilePath, dummyContent);

			await ioman1.WriteAllLinesAsync(lines);

			ioman1.TryReplaceMeWith(dummyFilePath);

			var fruits = await ioman1.ReadAllLinesAsync();

			Assert.True(IsStringArraysEqual(dummyContent, fruits));

			Assert.False(File.Exists(dummyFilePath));

			ioman1.DeleteMe();
		}

		[Fact]
		public async Task IoTestsAsync()
		{
			var file1 = Path.Combine(Global.DataDir, nameof(IoTestsAsync), $"file.dat");

			IoManager ioman1 = new IoManager(file1);
			ioman1.DeleteMe();

			Assert.False(ioman1.Exists());
			using (await ioman1.Mutex.LockAsync())
			{
				await ioman1.WriteAllLinesAsync(new string[0]);
			}

			string RandomString()
			{
				StringBuilder builder = new StringBuilder();
				var rnd = new Random();
				char ch;
				for (int i = 0; i < rnd.Next(10, 100); i++)
				{
					ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26 * rnd.NextDouble() + 65)));
					builder.Append(ch);
				}
				return builder.ToString();
			};

			var list = new List<string>();
			async Task WriteNextLineAsync()
			{
				var next = RandomString();
				lock (list)
				{
					list.Add(next);
				}
				using (await ioman1.Mutex.LockAsync())
				{
					var lines = (await ioman1.ReadAllLinesAsync()).ToList();
					lines.Add(next);
					await ioman1.WriteAllLinesAsync(lines);
				}
			};

			var t1 = new Thread(() =>
			{
				for (var i = 0; i < 500; i++)
				{
					/* We have to block the Thread.
					 * If we use async/await pattern then Join() function at the end will indicate that the Thread is finished -
					 * which is not true bacause the WriteNextLineAsync() is not yet finished. The reason is that await will return execution
					 * the to the calling thread it is detected as the thread is done. t1 and t2 and t3 will still run in parallel!
					 */
					WriteNextLineAsync().Wait();
				}
			});
			var t2 = new Thread(() =>
			  {
				  for (var i = 0; i < 500; i++)
				  {
					  WriteNextLineAsync().Wait();
				  }
			  });
			var t3 = new Thread(() =>
			  {
				  for (var i = 0; i < 500; i++)
				  {
					  WriteNextLineAsync().Wait();
				  }
			  });

			t1.Start();
			// t2.Start();
			// t3.Start();
			await Task.Delay(100);
			t1.Join();
			// t2.Join();
			// t3.Join();
			Assert.False(t1.IsAlive);
			Assert.False(t2.IsAlive);
			Assert.False(t3.IsAlive);

			var alllines = File.ReadAllLines(file1);
			Assert.NotEmpty(alllines);

			/* Lines were added to the list and to the file parallel so the two data should be equal.
			 * If we "substract" them from each other we should get empty array.
			 */

			var diff = alllines.Except(list);
			Assert.Empty(diff);
		}
	}
}
