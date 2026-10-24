using System.IO;
using System.Net;

namespace BBR249Server.Core;

public static class Settings
{
	public static string BaseDir = Directory.GetCurrentDirectory();

	public static bool UnlockEverything = true;

	public static bool RichMode = true;

	// mission de construction sur la carte (--no-builder-mission pour la retirer)
	public static bool BuilderMission = true;

	// branche de niveaux "Impossible" (--no-insane pour la retirer)
	public static bool InsaneBranch = true;

	// niveau puzzle sur la carte (--no-puzzle pour le retirer)
	public static bool PuzzleMission = true;

	// section de la carte qui recoit le puzzle ("" = automatique : voir MapContent ; --puzzle-section=NOM pour en imposer une)
	public static string PuzzleSection = "";

	public const int RichAmount = 2000000;

	public const int PathLayoutVersion = 3;

	public const int CreatorRewardPerCompletion = 2;

	public const int CreatorRewardPerLike = 5;

	public static bool Verbose = false;

	public static int HttpPort = 80;

	public static int HttpsPort = 443;

	public static int DnsPort = 53;

	public static readonly string[] TargetHosts = new string[3] { "woeprod.traplightgames.com", "woeprod-1324136205.us-west-1.elb.amazonaws.com", "graph.facebook.com" };

	public static readonly string[] BlockedPatterns = new string[12]
	{
		"facebook.com", "fbcdn.net", "app-measurement.com", "firebaseinstallations.googleapis.com", "firebaseremoteconfig.googleapis.com", "fcm.googleapis.com", "firebase.googleapis.com", "crashlytics.com", "omniata.com", "chartboost.com",
		"everyplay.com", "helpshift.com"
	};

	public static readonly IPEndPoint UpstreamDns = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);

	// Tout ce que le serveur genere (base de donnees, certificats, journal, donnees du jeu) vit dans le sous-dossier "Data".
	public static string DataDir => Path.Combine(BaseDir, "Data");

	public static string ServerDataDir => Path.Combine(DataDir, "ServerData");

		public static string InitialDataFile => Path.Combine(ServerDataDir, "InitialData", "LocalInitialData.txt");

	public static string MusicDir => Path.Combine(ServerDataDir, "Music");

	public static string DbFile => Path.Combine(DataDir, "game249.db");

	public static string RootCaCert => Path.Combine(DataDir, "BigBangRacing_RootCA.pem");

	public static string RootCaCertDer => Path.Combine(DataDir, "BigBangRacing_RootCA.crt");

	public static string RootCaKey => Path.Combine(DataDir, "BigBangRacing_RootCA.key");

	public static string LeafPfx => Path.Combine(DataDir, "server249.pfx");
}
