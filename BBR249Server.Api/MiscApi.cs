using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BBR249Server.Core;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

public static class MiscApi
{
	private static byte[]? _pathBlob;

	private static int _pathVersion = 1;

	public static int PathVersion
	{
		get
		{
			LoadPathBlob();
			return _pathVersion;
		}
	}

	private static byte[]? LoadPathBlob()
	{
		if (_pathBlob != null)
		{
			return _pathBlob;
		}
		if (!File.Exists(Settings.InitialDataFile))
		{
			return null;
		}
		string text = File.ReadAllText(Settings.InitialDataFile, Encoding.UTF8).TrimStart('\ufeff', ' ', '\r', '\n', '\t');
		if (!text.StartsWith("{"))
		{
			return null;
		}
		try
		{
			if (JsonNode.Parse(text) is JsonObject jsonObject2)
			{
				BuildOfficialUnits(jsonObject2);
				int num3 = UnlockBuilder.FixTextKeys(jsonObject2);
				if (num3 > 0)
				{
					text = jsonObject2.ToJsonString();
					Console.WriteLine($"[Path] Replaced {num3} untranslated text key(s) (vehicle names).");
				}
				int num4 = UnlockBuilder.FixDevItemData(jsonObject2);
				if (num4 > 0)
				{
					text = jsonObject2.ToJsonString();
					Console.WriteLine($"[Path] Dev Items category renamed, {num4} item name/icon fix(es).");
				}
				int num7 = MapContent.AddVariety(jsonObject2);
				if (num7 > 0)
				{
					text = jsonObject2.ToJsonString();
					Console.WriteLine($"[Path] Map variety: {num7} change(s) (Construction checkpoints{(Settings.BuilderMission ? ", builder mission" : "")}{(Settings.PuzzleMission ? ", puzzle" : "")}).");
				}
				int num6 = UnlockBuilder.RenameTvSections(jsonObject2);
				int num5 = UnlockBuilder.FixSignalNodes(jsonObject2) + num6 + UnlockBuilder.RemoveTestDialogues(jsonObject2);
				if (num5 > 0)
				{
					text = jsonObject2.ToJsonString();
					Console.WriteLine($"[Path] Signal (TV) nodes made playable on the unmodified client: {num5} fix(es).");
				}
				int num = UnlockBuilder.RemoveUnsupportedGameModes(jsonObject2);
				if (num > 0)
				{
					text = jsonObject2.ToJsonString();
					Console.WriteLine($"[Path] Removed {num} game mode(s) the client cannot create levels with (Signal).");
				}
			}
		}
		catch (Exception ex2)
		{
			Console.WriteLine("[Path] Could not filter game modes: " + ex2.Message);
		}
		if (Settings.UnlockEverything)
		{
			try
			{
				if (JsonNode.Parse(text) is JsonObject jsonObject)
				{
					int value = UnlockBuilder.DisableTutorialStart(jsonObject);
					int value2 = UnlockBuilder.LinkAllItemsToFirstUnlock(jsonObject);
					text = jsonObject.ToJsonString();
					Console.WriteLine($"[Path] Unlock-everything: linked {value2} editor items to '{UnlockBuilder.FirstUnlockName}', disabled the first-launch tutorial ({value} references).");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("[Path] Could not patch the graph: " + ex.Message);
			}
		}
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		byte[] value3 = SHA256.HashData(bytes);
		_pathVersion = (int)(2 + BitConverter.ToUInt32(value3, 0) % 1000000);
		_pathBlob = FilePacker.ZipBytes(bytes);
		return _pathBlob;
	}

	// identifiant de niveau officiel -> vehicule prevu par la carte ("Motorcycle", "OffroadCar" ou "Any")
	private static readonly System.Collections.Generic.Dictionary<string, string> OfficialUnits = new System.Collections.Generic.Dictionary<string, string>();

	public static string UnitOfOfficialLevel(string levelId)
	{
		LoadPathBlob();
		return OfficialUnits.TryGetValue(levelId, out string value) ? value : "Any";
	}

	private static void BuildOfficialUnits(JsonObject graph)
	{
		OfficialUnits.Clear();
		if (!(graph["nodes"] is JsonArray jsonArray))
		{
			return;
		}
		foreach (JsonNode item in jsonArray)
		{
			string text = item?["data"]?["levelMinigameId"]?.ToString() ?? "";
			string text2 = item?["data"]?["levelPlayerUnit"]?.ToString() ?? "Any";
			if (text != "")
			{
				OfficialUnits[text] = text2;
			}
		}
	}

	public static void Register()
	{
		Router.Map("/v1/path/reset", async delegate(Ctx c)
		{
			Db.Exec("UPDATE players SET progression='{}', claimedTutorials='[]', lastPathSync=$0 WHERE id=$1", PlayerApi.NowSync(), c.PlayerId);
			await c.Json(new JsonObject { ["lastPathSync"] = PlayerApi.NowSync() });
		});
		Router.Map("/v1/ads/config", (Ctx c) => c.Json(new JsonObject()));
		Router.Map("/v1/path/db/find", async delegate(Ctx c)
		{
			byte[] array = LoadPathBlob();
			if (array == null)
			{
				Console.WriteLine("[Path] Missing or invalid " + Settings.InitialDataFile);
				await c.Error();
			}
			else
			{
				await c.Bytes(array);
			}
		});
		Router.Map("/v1/preload/checkFile", async delegate(Ctx c)
		{
			string text = c.Q("name");
			bool flag = text != "" && !text.Contains("..") && !text.Contains('/') && !text.Contains('\\');
			string text2 = (flag ? text : "Placeholder");
			if (!File.Exists(Path.Combine(Settings.MusicDir, text2 + ".bank")))
			{
				Directory.CreateDirectory(Settings.MusicDir);
				File.WriteAllBytes(Path.Combine(Settings.MusicDir, text2 + ".bank"), new byte[16]);
			}
			string value = (c.Http.Request.Host.HasValue ? c.Http.Request.Host.Value : "woeprod.traplightgames.com");
			await c.Json(new JsonObject
			{
				["name"] = (flag ? text : "Placeholder") + "_offline",
				["type"] = "audio",
				["path"] = $"{c.Http.Request.Scheme}://{value}/ServerData/Music/{Uri.EscapeDataString(text2)}.bank",
				["version"] = 2
			});
		});
	}
}
