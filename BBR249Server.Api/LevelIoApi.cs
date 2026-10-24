using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

// Export / import d'un niveau publie sous forme de fichier .zip (le client enregistre / lit le fichier sur le telephone).
// Contenu du zip : <id>/metadata (JSON), <id>/levelData (octets du niveau), <id>/screenshot, <id>/ghost (facultatifs).
public static class LevelIoApi
{
	private const int MaxZipBytes = 32 * 1024 * 1024;

	private static JsonNode ParseOr(string json, JsonNode fallback)
	{
		try
		{
			return JsonNode.Parse(json) ?? fallback;
		}
		catch
		{
			return fallback;
		}
	}

	private static void AddEntry(ZipArchive zip, string name, byte[]? data)
	{
		if (data == null || data.Length == 0)
		{
			return;
		}
		ZipArchiveEntry zipArchiveEntry = zip.CreateEntry(name, CompressionLevel.Optimal);
		using Stream stream = zipArchiveEntry.Open();
		stream.Write(data, 0, data.Length);
	}

	private static byte[]? ReadEntry(ZipArchive zip, string name)
	{
		foreach (ZipArchiveEntry entry in zip.Entries)
		{
			if (!string.Equals(entry.Name, name, StringComparison.Ordinal) || entry.Length > MaxZipBytes)
			{
				continue;
			}
			using Stream stream = entry.Open();
			using MemoryStream ms = new MemoryStream();
			stream.CopyTo(ms);
			return ms.ToArray();
		}
		return null;
	}

	public static void Register()
	{
		Router.Map("/v1/minigame/export", async delegate(Ctx c)
		{
			Row? row = MinigameApi.GetMinigame(c.Q("id"));
			byte[]? levelData = row?.B("levelData");
			if (row == null || levelData == null || levelData.Length == 0)
			{
				await c.Error();
				return;
			}
			string id = row.S("id");
			JsonObject meta = new JsonObject
			{
				["format"] = "bbr249-level",
				["formatVersion"] = 1,
				["id"] = id,
				["name"] = row.S("name"),
				["description"] = row.S("description"),
				["gameMode"] = row.S("gameMode", "Race"),
				["playerUnit"] = row.S("playerUnit", "Any"),
				["difficulty"] = row.S("difficulty", "New"),
				["clientVersion"] = row.L("clientVersion"),
				["complexity"] = row.L("complexity"),
				["levelRequirement"] = row.L("levelRequirement"),
				["bestTime"] = row.L("bestTime"),
				["editSessionCount"] = row.L("editSessionCount"),
				["creatorName"] = row.S("creatorName"),
				["publishTime"] = row.S("publishTime"),
				["itemsUsed"] = ParseOr(row.S("itemsUsed", "[]"), new JsonArray()),
				["creatorUpgrades"] = ParseOr(row.S("creatorUpgrades", "{}"), new JsonObject())
			};
			using MemoryStream ms = new MemoryStream();
			using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
			{
				AddEntry(zip, id + "/metadata", Encoding.UTF8.GetBytes(meta.ToJsonString()));
				AddEntry(zip, id + "/levelData", levelData);
				AddEntry(zip, id + "/screenshot", row.B("screenshot"));
				AddEntry(zip, id + "/ghost", row.B("creatorGhost"));
			}
			Console.WriteLine($"[Export] {row.S("name")} ({id}) by {c.PlayerId}: {ms.Length} bytes");
			await c.Bytes(ms.ToArray());
		});
		Router.Map("/v1/minigame/import", async delegate(Ctx c)
		{
			if (c.PlayerId == "" || c.Body.Length < 22 || c.Body.Length > MaxZipBytes)
			{
				await c.JsonText("{\"ok\":false,\"reason\":\"invalid\"}");
				return;
			}
			byte[]? metaBytes;
			byte[]? levelData;
			byte[]? screenshot;
			byte[]? ghost;
			try
			{
				using ZipArchive zip = new ZipArchive(new MemoryStream(c.Body), ZipArchiveMode.Read);
				metaBytes = ReadEntry(zip, "metadata");
				levelData = ReadEntry(zip, "levelData");
				screenshot = ReadEntry(zip, "screenshot");
				ghost = ReadEntry(zip, "ghost");
			}
			catch
			{
				await c.JsonText("{\"ok\":false,\"reason\":\"invalid\"}");
				return;
			}
			if (metaBytes == null || levelData == null || levelData.Length < 2)
			{
				await c.JsonText("{\"ok\":false,\"reason\":\"invalid\"}");
				return;
			}
			JsonObject meta;
			try
			{
				meta = (JsonNode.Parse(Encoding.UTF8.GetString(metaBytes)) as JsonObject) ?? new JsonObject();
			}
			catch
			{
				await c.JsonText("{\"ok\":false,\"reason\":\"invalid\"}");
				return;
			}
			string id = meta["id"]?.ToString() ?? "";
			if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{6,64}$"))
			{
				id = PlayerApi.NewId();
			}
			Row? existing = MinigameApi.GetMinigame(id);
			if (existing != null)
			{
				if (existing.S("creatorId") == c.PlayerId)
				{
					await c.JsonText("{\"ok\":false,\"reason\":\"already\"}");
					return;
				}
				id = PlayerApi.NewId();
			}
			Row? player = PlayerApi.GetPlayer(c.PlayerId);
			string creatorName = meta["creatorName"]?.ToString();
			if (string.IsNullOrWhiteSpace(creatorName))
			{
				creatorName = player?.S("name") ?? "??";
			}
			Db.Exec("INSERT INTO minigames (id, creatorId, creatorName) VALUES ($0,$1,$2)", id, c.PlayerId, creatorName);
			List<string> sets = new List<string>();
			List<object?> args = new List<object?>();
			void Set(string col, object? v)
			{
				sets.Add($"{col}=${args.Count}");
				args.Add(v);
			}
			void Text(string key, int max = 200)
			{
				string s = meta[key]?.ToString();
				if (s != null)
				{
					Set(key, s.Length > max ? s.Substring(0, max) : s);
				}
			}
			void Num(string key)
			{
				if (meta[key] != null && double.TryParse(meta[key].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
				{
					Set(key, (long)result);
				}
			}
			Text("name", 100);
			Text("description", 500);
			Text("gameMode", 40);
			Text("playerUnit", 40);
			Text("difficulty", 40);
			Num("clientVersion");
			Num("complexity");
			Num("levelRequirement");
			Num("bestTime");
			Num("editSessionCount");
			if (meta["itemsUsed"] is JsonArray itemsUsed)
			{
				Set("itemsUsed", itemsUsed.ToJsonString());
			}
			if (meta["creatorUpgrades"] is JsonObject creatorUpgrades)
			{
				Set("creatorUpgrades", creatorUpgrades.ToJsonString());
			}
			Set("state", "public");
			Set("publishTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
			Set("updatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
			Set("levelData", levelData);
			if (screenshot != null && screenshot.Length > 1)
			{
				Set("screenshot", screenshot);
			}
			if (ghost != null && ghost.Length > 1)
			{
				Set("creatorGhost", ghost);
			}
			args.Add(id);
			Db.Exec($"UPDATE minigames SET {string.Join(",", sets)} WHERE id=${args.Count - 1}", args.ToArray());
			Console.WriteLine($"[Import] {meta["name"]} -> {id} for {c.PlayerId}");
			await c.JsonText("{\"ok\":true,\"id\":\"" + id + "\"}");
		});
	}
}
