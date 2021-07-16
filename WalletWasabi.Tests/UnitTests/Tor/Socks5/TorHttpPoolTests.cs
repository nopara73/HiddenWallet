using Moq;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Tor.Socks5;
using WalletWasabi.Tor.Socks5.Pool;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Tor.Socks5
{
	/// <seealso cref="XunitConfiguration.SerialCollectionDefinition"/>
	[Collection("Serial unit tests collection")]
	public class TorHttpPoolTests
	{
		/// <summary>
		/// Tests that <see cref="TorHttpPool.SendAsync(HttpRequestMessage, ICircuit, CancellationToken)"/> method respects provided Tor circuit.
		/// </summary>
		[Fact]
		public async Task UseCorrectIdentitiesAsync()
		{
			using CancellationTokenSource timeoutCts = new(TimeSpan.FromMinutes(1));

			ICircuit defaultIdentity = DefaultCircuit.Instance;
			ICircuit aliceIdentity = new PersonCircuit();
			ICircuit bobIdentity = new PersonCircuit();

			using TorTcpConnection aliceConnection = new(null!, new MemoryStream(), aliceIdentity, true);
			using TorTcpConnection bobConnection = new(null!, new MemoryStream(), bobIdentity, true);
			using TorTcpConnection defaultConnection = new(null!, new MemoryStream(), defaultIdentity, true);

			Mock<TorTcpConnectionFactory> mockTcpConnectionFactory = new(MockBehavior.Strict, new IPEndPoint(IPAddress.Loopback, 7777));
			_ = mockTcpConnectionFactory.Setup(c => c.ConnectAsync(It.IsAny<Uri>(), aliceIdentity, It.IsAny<CancellationToken>())).ReturnsAsync(aliceConnection);
			_ = mockTcpConnectionFactory.Setup(c => c.ConnectAsync(It.IsAny<Uri>(), bobIdentity, It.IsAny<CancellationToken>())).ReturnsAsync(bobConnection);
			_ = mockTcpConnectionFactory.Setup(c => c.ConnectAsync(It.IsAny<Uri>(), defaultIdentity, It.IsAny<CancellationToken>())).ReturnsAsync(defaultConnection);

			TorTcpConnectionFactory tcpConnectionFactory = mockTcpConnectionFactory.Object;

			// Use implementation of TorHttpPool and only replace SendCoreAsync behavior.
			Mock<TorHttpPool> mockTorHttpPool = new(MockBehavior.Loose, tcpConnectionFactory) { CallBase = true };
			mockTorHttpPool.Setup(x => x.SendCoreAsync(It.IsAny<TorTcpConnection>(), It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
				.Returns((TorTcpConnection tcpConnection, HttpRequestMessage request, CancellationToken cancellationToken) =>
				{
					HttpResponseMessage httpResponse = new(HttpStatusCode.OK);

					if (tcpConnection == aliceConnection)
					{
						httpResponse.Content = new StringContent("Alice circuit!");
					}
					else if (tcpConnection == bobConnection)
					{
						httpResponse.Content = new StringContent("Bob circuit!");
					}
					else if (tcpConnection == defaultConnection)
					{
						httpResponse.Content = new StringContent("Default circuit!");
					}
					else
					{
						throw new NotSupportedException();
					}

					return Task.FromResult(httpResponse);
				});

			using TorHttpPool pool = mockTorHttpPool.Object;

			using HttpRequestMessage request = new(HttpMethod.Get, "http://wasabi.backend");

			using HttpResponseMessage aliceResponse = await pool.SendAsync(request, aliceIdentity);
			Assert.Equal("Alice circuit!", await aliceResponse.Content.ReadAsStringAsync(timeoutCts.Token));

			using HttpResponseMessage bobResponse = await pool.SendAsync(request, bobIdentity);
			Assert.Equal("Bob circuit!", await bobResponse.Content.ReadAsStringAsync(timeoutCts.Token));

			using HttpResponseMessage defaultResponse = await pool.SendAsync(request, defaultIdentity);
			Assert.Equal("Default circuit!", await defaultResponse.Content.ReadAsStringAsync(timeoutCts.Token));

			mockTcpConnectionFactory.VerifyAll();
		}

		/// <summary>
		/// Tests that <see cref="TorHttpPool.SendAsync(HttpRequestMessage, ICircuit, CancellationToken)"/> method sends data as expected and
		/// when sends an HTTP reply, it is correctly processed by <see cref="TorHttpPool"/>.
		/// </summary>
		[Fact]
		public async Task RequestAndReplyAsync()
		{
			using CancellationTokenSource timeoutCts = new(TimeSpan.FromMinutes(1));

			ICircuit circuit = DefaultCircuit.Instance;

			// Set up FAKE transport stream, so Tor is not in play.
			await using TransportStream transportStream = new(nameof(RequestAndReplyAsync));
			await transportStream.ConnectAsync(timeoutCts.Token);

			using TorTcpConnection connection = new(tcpClient: null!, transportStream.Client, circuit, allowRecycling: true);

			Mock<TorTcpConnectionFactory> mockFactory = new(MockBehavior.Strict, new IPEndPoint(IPAddress.Loopback, 7777));
			mockFactory.Setup(c => c.ConnectAsync(It.IsAny<Uri>(), It.IsAny<ICircuit>(), It.IsAny<CancellationToken>())).ReturnsAsync(connection);

			using StreamReader serverReader = new(transportStream.Server);
			using StreamWriter serverWriter = new(transportStream.Server);

			using TorHttpPool pool = new(mockFactory.Object);
			using HttpRequestMessage request = new(HttpMethod.Get, "http://somesite.com");

			Task sendTask = Task.Run(async () =>
			{
				Debug.WriteLine("[client] About send HTTP request.");
				using HttpResponseMessage httpResponseMessage = await pool.SendAsync(request, circuit).ConfigureAwait(false);
				Assert.Equal(HttpStatusCode.OK, httpResponseMessage.StatusCode);
				Debug.WriteLine("[client] Done sending HTTP request.");
			});

			// Server part follows.
			Debug.WriteLine("[server] About to read data.");

			// We expect to get this plaintext HTTP request headers from the client.
			string[] expectedResponse = new[]
			{
				"GET / HTTP/1.1",
				"Accept-Encoding:gzip",
				"User-Agent:" + TorHttpPool.UserAgent,
				"Host:somesite.com",
				""
			};

			// Assert replies line by line.
			foreach (string expectedLine in expectedResponse)
			{
				Assert.Equal(expectedLine, await serverReader.ReadLineAsync().WithAwaitCancellationAsync(timeoutCts.Token));
			}

			// We respond to the client with the following content.
			Debug.WriteLine("[server] Send response for the request.");
			await serverWriter.WriteAsync(
				string.Join(
				"\r\n",
				"HTTP/1.1 200 OK",
				"Date: Wed, 02 Dec 2020 18:20:54 GMT",
				"Content-Type: application/json; charset=utf-8",
				"Content-Length: 389",
				"Connection: keep-alive",
				"ETag: W/\"185-ck4yLFUDHZl9lYSDUF6oMrTCEss\"",
				"Vary: Accept-Encoding",
				"set-cookie: sails.sid=s%3AMPaQCDY1u1swPgAI5RhbPg2extVNNhjI.oby40NpOE2CpyzIdRlGhD7Uja%2BGX1WbBaFV13T0f4eA; Path=/; HttpOnly",
				"",
				"{\"args\":{},\"data\":\"This is expected to be sent back as part of response body.\",\"files\":{},\"form\":{},\"headers\":{\"x-forwarded-proto\":\"http\",\"x-forwarded-port\":\"80\",\"host\":\"postman-echo.com\",\"x-amzn-trace-id\":\"Root=1-5fc7db06-24adc2a91c86c14f2d63ea61\",\"content-length\":\"58\",\"accept-encoding\":\"gzip\",\"content-type\":\"text/plain; charset=utf-8\"},\"json\":null,\"url\":\"http://postman-echo.com/post\"}").AsMemory(),
				timeoutCts.Token);
			await serverWriter.FlushAsync().WithAwaitCancellationAsync(timeoutCts.Token);

			Debug.WriteLine("[server] Wait for the sendTask to finish.");
			await sendTask;
			Debug.WriteLine("[server] Send task finished.");
		}

		/// <summary>
		/// Transport stream implementation that behaves similarly to <see cref="NetworkStream"/>.
		/// <para>Writer can write to the stream multiple times, reader can read the written data.</para>
		/// </summary>
		/// <remarks>
		/// <see cref="MemoryStream"/> is not easy to use as a replacement for <see cref="NetworkStream"/> as we would need to use seek operation.
		/// </remarks>
		internal class TransportStream : IAsyncDisposable
		{
			public TransportStream(string testName)
			{
				// Construct unique pipe name.
				int n = new Random().Next(0, 1_000_000);
				string pipeName = $"{testName}.Pipe.{n}";

				Server = new(pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
				Client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			}

			public NamedPipeServerStream Server { get; }
			public NamedPipeClientStream Client { get; }

			public async Task ConnectAsync(CancellationToken cancellationToken)
			{
				Task connectClientTask = Server.WaitForConnectionAsync(cancellationToken);
				await Client.ConnectAsync(cancellationToken).ConfigureAwait(false);
				await connectClientTask.ConfigureAwait(false);
			}

			public async ValueTask DisposeAsync()
			{
				await Server.DisposeAsync();
				await Client.DisposeAsync();
			}
		}
	}
}
