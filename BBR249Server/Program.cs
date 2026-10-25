using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BBR249Server.Api;
using BBR249Server.Core;
using BBR249Server.Data;
using BBR249Server.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BBR249Server;

public static class Program
{
	private static string? Arg(string[] args, string name)
	{
		int num = Array.IndexOf(args, name);
		if (num < 0 || num + 1 >= args.Length)
		{
			return null;
		}
		return args[num + 1];
	}

	// Anciennes versions : tout etait melange avec le programme. On range les fichiers deja presents dans "Data".
	private static void MigrateLegacyLayout()
	{
		try
		{
			Directory.CreateDirectory(Settings.DataDir);
			foreach (string name in new string[] { "game249.db", "game249.db-wal", "game249.db-shm", "server249.log", "BigBangRacing_RootCA.pem", "BigBangRacing_RootCA.key", "server249.pfx", "server249.pfx.hosts" })
			{
				string from = Path.Combine(Settings.BaseDir, name);
				string to = Path.Combine(Settings.DataDir, name);
				if (File.Exists(from) && !File.Exists(to))
				{
					File.Move(from, to);
				}
			}
			string oldServerData = Path.Combine(Settings.BaseDir, "ServerData");
			if (Directory.Exists(oldServerData) && !Directory.Exists(Settings.ServerDataDir))
			{
				Directory.Move(oldServerData, Settings.ServerDataDir);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[Setup] Could not tidy the old file layout: " + ex.Message);
		}
	}

	// La carte de l'aventure est integree au programme : on la recopie dans Data\ServerData au premier lancement.
	private static void EnsureServerData()
	{
		if (File.Exists(Settings.InitialDataFile))
		{
			return;
		}
		try
		{
			using Stream? res = typeof(Program).Assembly.GetManifestResourceStream("LocalInitialData.txt");
			if (res == null)
			{
				return;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(Settings.InitialDataFile));
			using FileStream fs = File.Create(Settings.InitialDataFile);
			res.CopyTo(fs);
			Console.WriteLine("[Setup] Wrote the game's path data to " + Settings.InitialDataFile);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[Setup] Could not write the path data (" + ex.Message + ")");
		}
	}

	private static IPAddress LocalIp()
	{
		try
		{
			using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP);
			socket.Connect("8.8.8.8", 53);
			return ((IPEndPoint)socket.LocalEndPoint).Address;
		}
		catch
		{
			return IPAddress.Loopback;
		}
	}

	[DllImport("kernel32.dll")]
	private static extern bool AttachConsole(int processId);

	private static bool _gui;

	public static bool IsAdministrator()
	{
		try
		{
			using WindowsIdentity id = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return true;
		}
	}

	// Par defaut : une fenetre (pas de console). "--console" lance le serveur dans la console, sans fenetre.
	[STAThread]
	public static int Main(string[] args)
	{
		string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
		if (!string.IsNullOrEmpty(exeDir))
		{
			Settings.BaseDir = exeDir;
		}
		if (args.Contains("--selftest-path"))
		{
			AttachConsole(-1);
			return MapContent.SelfTest();
		}
		if (args.Contains("--console"))
		{
			AttachConsole(-1);
			return RunServerAsync(args).GetAwaiter().GetResult();
		}
		using Mutex mutex = new Mutex(true, "BBR249Server.SingleInstance", out bool first);
		if (!first)
		{
			MessageBox.Show("The server is already running.", "Big Bang Racing 2.4.9 - Offline Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return 0;
		}
		_gui = true;
		ApplicationConfiguration.Initialize();
		Task.Run(async delegate
		{
			try
			{
				if (await RunServerAsync(args) != 0)
				{
					ServerInfo.State = "Error";
				}
			}
			catch (Exception ex)
			{
				ServerInfo.State = "Error";
				ServerInfo.Error = ex.Message;
				Console.WriteLine("[Server] " + ex);
			}
		});
		Application.Run(new ServerWindow());
		Environment.Exit(0);
		return 0;
	}

	private static async Task<int> RunServerAsync(string[] args)
	{
		string text = Arg(args, "--http-port");
		if (text != null)
		{
			Settings.HttpPort = int.Parse(text);
		}
		string text2 = Arg(args, "--https-port");
		if (text2 != null)
		{
			Settings.HttpsPort = int.Parse(text2);
		}
		string text3 = Arg(args, "--dns-port");
		if (text3 != null)
		{
			Settings.DnsPort = int.Parse(text3);
		}
		string text4 = Arg(args, "--dir");
		if (text4 != null)
		{
			Settings.BaseDir = Path.GetFullPath(text4);
		}
		if (args.Contains("--keep-locks"))
		{
			Settings.UnlockEverything = false;
		}
		if (args.Contains("--verbose"))
		{
			Settings.Verbose = true;
		}
		if (args.Contains("--no-builder-mission"))
		{
			Settings.BuilderMission = false;
		}
		foreach (string arg in args)
		{
			if (arg.StartsWith("--puzzle-section="))
			{
				Settings.PuzzleSection = arg.Substring("--puzzle-section=".Length);
			}
		}
		if (args.Contains("--no-insane"))
		{
			Settings.InsaneBranch = false;
		}
		if (args.Contains("--no-puzzle"))
		{
			Settings.PuzzleMission = false;
		}
		if (args.Contains("--normal-economy"))
		{
			Settings.RichMode = false;
		}
		Directory.SetCurrentDirectory(Settings.BaseDir);
		MigrateLegacyLayout();
		Directory.CreateDirectory(Settings.DataDir);
		try
		{
			StreamWriter b = new StreamWriter(new FileStream(Path.Combine(Settings.DataDir, "server249.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
			{
				AutoFlush = true
			};
			Console.SetOut(new TeeWriter(_gui ? new GuiLogWriter() : Console.Out, b));
		}
		catch
		{
		}
		Console.WriteLine($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
		Console.WriteLine("Big Bang Racing 2.4.9 offline server");
		EnsureServerData();
		Directory.CreateDirectory(Settings.MusicDir);
		Directory.CreateDirectory(Path.GetDirectoryName(Settings.InitialDataFile));
		Db.Init();
		MapContent.EnsureSeedLevels();
		MapContent.EnsureBuilderTemplate();
		MapContent.EnsurePuzzleLevel();
		X509Certificate2 cert;
		try
		{
			cert = CertManager.EnsureServerCertificate();
		}
		catch (Exception ex)
		{
			Console.WriteLine("[Setup] Could not create the HTTPS certificate: " + ex.Message);
			ServerInfo.Error = "Could not create the HTTPS certificate: " + ex.Message;
			return 1;
		}
		PlayerApi.Register();
		MinigameApi.Register();
		LevelIoApi.Register();
		LeftoverApi.Register();
		ScoreApi.Register();
		TimedApi.Register();
		ChallengeApi.Register();
		MiscApi.Register();
		IPAddress ip = LocalIp();
		ServerInfo.Ip = ip.ToString();
		Console.WriteLine($"PC IP: {ip}");
		if (!File.Exists(Settings.InitialDataFile))
		{
			Console.WriteLine("[Warning] " + Settings.InitialDataFile + " is missing - the game cannot load its progression data.");
		}
		DnsServer dns = null;
		if (!args.Contains("--no-dns"))
		{
			try
			{
				dns = new DnsServer(ip, Settings.DnsPort);
				dns.Start();
				Console.WriteLine($"[DNS] Listening on port {Settings.DnsPort}");
			}
			catch (Exception ex2)
			{
				Console.WriteLine($"[DNS] Port {Settings.DnsPort} error: {ex2.Message}");
				ServerInfo.Error = $"DNS port {Settings.DnsPort} is not available: {ex2.Message}";
			}
		}
		DiscoveryServer discovery = null;
		try
		{
			discovery = new DiscoveryServer();
			discovery.Start();
			Console.WriteLine($"[Discovery] Listening on UDP port {DiscoveryServer.Port}");
		}
		catch (Exception ex4)
		{
			Console.WriteLine($"[Discovery] Port {DiscoveryServer.Port} error: {ex4.Message}");
		}
		WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(new string[0]);
		webApplicationBuilder.Logging.ClearProviders();
		webApplicationBuilder.WebHost.ConfigureKestrel(delegate(KestrelServerOptions o)
		{
			o.Limits.MaxRequestBodySize = 209715200L;
			o.AddServerHeader = false;
			o.Listen(IPAddress.Any, Settings.HttpPort);
			o.Listen(IPAddress.Any, Settings.HttpsPort, delegate(ListenOptions lo)
			{
				lo.Protocols = HttpProtocols.Http1;
				lo.UseHttps(cert);
			});
		});
		WebApplication app = webApplicationBuilder.Build();
		app.Use((HttpContext http, Func<Task> next) => Cors.Handle(http, next));
		app.Run(Router.Handle);
		try
		{
			await app.StartAsync();
			Console.WriteLine($"[HTTP] Listening on port {Settings.HttpPort}");
			Console.WriteLine($"[HTTPS] Listening on port {Settings.HttpsPort}");
			ServerInfo.State = "Running";
			Console.WriteLine($"Routes: {Router.Count}. Patched Android game: nothing to set up. iOS / original game: install {Settings.RootCaCertDer} and point the DNS at {ip}.");
		}
		catch (Exception ex3)
		{
			Console.WriteLine($"[Server] Could not start (are ports {Settings.HttpPort}/{Settings.HttpsPort}/{Settings.DnsPort} free, and are you running as administrator?): {ex3.Message}");
			ServerInfo.Error = $"Could not start: ports {Settings.HttpPort}/{Settings.HttpsPort} must be free ({ex3.Message})";
			dns?.Stop();
			return 1;
		}
		await app.WaitForShutdownAsync();
		discovery?.Stop();
		dns?.Stop();
		return 0;
	}
}
