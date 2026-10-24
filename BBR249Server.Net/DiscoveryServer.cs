using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BBR249Server.Net;

// Repond aux appels du jeu ("BBR249?" en broadcast UDP) pour qu'il trouve le serveur sur le reseau local,
// sans passer par le DNS (certains routeurs imposent leur propre serveur DNS IPv6, que le telephone interroge en premier).
public sealed class DiscoveryServer
{
	public const int Port = 49249;

	private readonly UdpClient _udp;

	public DiscoveryServer()
	{
		_udp = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
		_udp.EnableBroadcast = true;
	}

	public void Start()
	{
		Task.Run(async delegate
		{
			while (true)
			{
				try
				{
					UdpReceiveResult r = await _udp.ReceiveAsync();
					if (r.Buffer.Length >= 6 && Encoding.ASCII.GetString(r.Buffer, 0, 6) == "BBR249")
					{
						byte[] reply = Encoding.ASCII.GetBytes("BBR249");
						await _udp.SendAsync(reply, reply.Length, r.RemoteEndPoint);
					}
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch
				{
				}
			}
		});
	}

	public void Stop()
	{
		_udp.Close();
	}
}
