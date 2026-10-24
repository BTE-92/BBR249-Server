using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BBR249Server.Core;

namespace BBR249Server.Net;

public sealed class DnsServer
{
	private readonly IPAddress _fakeIp;

	private readonly UdpClient _udp;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly ConcurrentDictionary<string, byte> _loggedBlocked = new ConcurrentDictionary<string, byte>();

	public DnsServer(IPAddress fakeIp, int port)
	{
		_fakeIp = fakeIp;
		_udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
		try
		{
			_udp.Client.IOControl((IOControlCode)(-1744830452L), new byte[1], null);
		}
		catch
		{
		}
	}

	public void Start()
	{
		Task.Run((Func<Task?>)Loop);
	}

	public void Stop()
	{
		_cts.Cancel();
		_udp.Close();
	}

	private async Task Loop()
	{
		while (!_cts.IsCancellationRequested)
		{
			try
			{
				UdpReceiveResult result = await _udp.ReceiveAsync(_cts.Token);
				Task.Run(() => Handle(result.Buffer, result.RemoteEndPoint));
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch
			{
			}
		}
	}

	private static (string Name, int End)? ParseQuestion(byte[] data)
	{
		if (data.Length < 17)
		{
			return null;
		}
		int i = 12;
		List<string> list = new List<string>();
		int num;
		for (; i < data.Length; i += 1 + num)
		{
			num = data[i];
			if (num == 0)
			{
				i++;
				break;
			}
			if ((num & 0xC0) != 0 || i + 1 + num > data.Length)
			{
				return null;
			}
			list.Add(Encoding.ASCII.GetString(data, i + 1, num));
		}
		if (i + 4 > data.Length)
		{
			return null;
		}
		return (string.Join(".", list).ToLowerInvariant(), i + 4);
	}

	private static byte[] Header(byte[] req, ushort flags, ushort answers)
	{
		return new byte[12]
		{
			req[0],
			req[1],
			(byte)(flags >> 8),
			(byte)flags,
			0,
			1,
			(byte)(answers >> 8),
			(byte)answers,
			0,
			0,
			0,
			0
		};
	}

	private static byte[] Slice(byte[] a, int start, int end)
	{
		byte[] array = new byte[end - start];
		Buffer.BlockCopy(a, start, array, 0, array.Length);
		return array;
	}

	private byte[] FakeAnswer(byte[] req, int qend)
	{
		byte[] first = Header(req, 33152, 1);
		byte[] second = Slice(req, 12, qend);
		List<byte> list = new List<byte>
		{
			192, 12, 0, 1, 0, 1, 0, 0, 0, 60,
			0, 4
		};
		list.AddRange(_fakeIp.GetAddressBytes());
		return first.Concat(second).Concat(list).ToArray();
	}

	private static byte[] EmptyAnswer(byte[] req, int qend, ushort flags)
	{
		return Header(req, flags, 0).Concat(Slice(req, 12, qend)).ToArray();
	}

	private static bool IsTarget(string name)
	{
		return Settings.TargetHosts.Contains<string>(name, StringComparer.OrdinalIgnoreCase);
	}

	private static bool IsBlocked(string name)
	{
		return Settings.BlockedPatterns.Any((string p) => name.Contains(p, StringComparison.Ordinal));
	}

	private async Task Handle(byte[] data, IPEndPoint from)
	{
		_ = 5;
		try
		{
			(string, int)? tuple = ParseQuestion(data);
			if (!tuple.HasValue)
			{
				return;
			}
			(string, int) value = tuple.Value;
			string item = value.Item1;
			int item2 = value.Item2;
			int num = (data[item2 - 4] << 8) | data[item2 - 3];
			if (IsTarget(item) && num == 1)
			{
				await _udp.SendAsync(FakeAnswer(data, item2), from);
				return;
			}
			if (IsTarget(item) && (num == 28 || num == 65))
			{
				await _udp.SendAsync(EmptyAnswer(data, item2, 33152), from);
				return;
			}
			if (IsBlocked(item))
			{
				if (Settings.Verbose || _loggedBlocked.TryAdd(item, 0))
				{
					Console.WriteLine("[DNS] blocked " + item);
				}
				await _udp.SendAsync(EmptyAnswer(data, item2, 33155), from);
				return;
			}
			using UdpClient up = new UdpClient();
			up.Client.ReceiveTimeout = 3000;
			await up.SendAsync(data, Settings.UpstreamDns);
			Task<UdpReceiveResult> recv = up.ReceiveAsync();
			if (await Task.WhenAny(recv, Task.Delay(3000)) == recv)
			{
				await _udp.SendAsync(recv.Result.Buffer, from);
			}
		}
		catch
		{
		}
	}
}
