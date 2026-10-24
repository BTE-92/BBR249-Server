using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using BBR249Server.Core;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

public static class PlayerApi
{
	public static string NewId()
	{
		return Guid.NewGuid().ToString("N");
	}

	public static string NowSync()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
	}

	public static Row? GetPlayer(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			return Db.One("SELECT * FROM players WHERE id=$0", id);
		}
		return null;
	}

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

	public static JsonObject ClientConfig()
	{
		return new JsonObject
		{
			["carRefreshMinutes"] = 6,
			["freshFreeInterval"] = 15,
			["fbConnectRewardAmount"] = 20,
			["dailyGemAmount"] = 10,
			["videoAdCount"] = 2,
			["videoAdCoolDown"] = 3600,
			["freshFreeCount"] = 3,
			["freshFreeCoolDown"] = 1800,
			["inRaceDiamondSpawnProbability"] = 25
		};
	}

	public static JsonObject PlayerJson(Row p)
	{
		JsonObject jsonObject = new JsonObject
		{
			["id"] = p.S("id"),
			["name"] = p.S("name"),
			["coins"] = p.L("coins", 0L),
			["diamonds"] = p.L("diamonds", 0L),
			["stars"] = p.L("stars", 0L),
			["bolts"] = p.L("bolts", 0L),
			["keys"] = p.L("keys", 0L),
			["level"] = p.L("level", 0L),
			["maxKeys"] = p.L("maxKeys", 0L),
			["itemLevel"] = (Settings.UnlockEverything ? Math.Max(p.L("itemLevel", 0L), 1000L) : p.L("itemLevel", 0L)),
			["cups"] = p.L("cups", 0L),
			["rank"] = p.L("rank", 0L),
			["acceptNotifications"] = true,
			["countryCode"] = p.S("countryCode", "US"),
			["itemDbVersion"] = 0,
			["upgrades"] = ParseOr(p.S("upgrades", "{}"), new JsonObject()),
			["boosters"] = ParseOr(p.S("boosters", "{}"), new JsonObject()),
			["data"] = ParseOr(p.S("data", "{}"), new JsonObject()),
			["claimedTutorials"] = ParseOr(p.S("claimedTutorials", "[]"), new JsonArray()),
			["clientConfig"] = ClientConfig(),
			["keyRefreshTimeLeft"] = p.L("keyRefresh", 0L),
			["OffroadCarVisual"] = new JsonArray(),
			["OffroadCarUpgrade"] = new JsonArray(),
			["MotorcycleVisual"] = new JsonArray(),
			["MotorcycleUpgrade"] = new JsonArray()
		};
		if (p.S("facebookId") != "")
		{
			jsonObject["facebookId"] = p.S("facebookId");
		}
		if (p.S("gameCenterId") != "")
		{
			jsonObject["gameCenterId"] = p.S("gameCenterId");
		}
		jsonObject["hash"] = Hasher.HashPlayer(p.S("id"), p.L("coins", 0L), p.L("diamonds", 0L), p.L("bolts", 0L), p.L("cups", 0L), p.L("keys", 0L), p.L("maxKeys", 0L), p.L("rank", 0L));
		return jsonObject;
	}

	public static JsonObject FriendJson(Row p)
	{
		JsonObject jsonObject = new JsonObject
		{
			["id"] = p.S("id"),
			["name"] = p.S("name"),
			["acceptNotifications"] = true,
			["countryCode"] = p.S("countryCode", "US"),
			["itemDbVersion"] = 0
		};
		if (p.S("facebookId") != "")
		{
			jsonObject["facebookId"] = p.S("facebookId");
		}
		if (p.S("gameCenterId") != "")
		{
			jsonObject["gameCenterId"] = p.S("gameCenterId");
		}
		return jsonObject;
	}

	// Tout nouveau joueur demarre avec la voiture et la moto ameliorees au maximum :
	// 20 niveaux par caracteristique (puissance / adherence / maniabilite) => palier 5 (palier = min/5 + 1).
	public const string NewPlayerBoostersJson = "{\"booster\":50}";

	public const string MaxUpgradesJson = "{\"OffroadCar\":{\"tier\":5,\"power\":20,\"grip\":20,\"handling\":20},\"Motorcycle\":{\"tier\":5,\"power\":20,\"grip\":20,\"handling\":20}}";

	private static long AsLong(JsonNode? n, long def)
	{
		if (!long.TryParse(n?.ToString(), out var result))
		{
			return def;
		}
		return result;
	}

	// Chemin principal "tout debloque" : structure synthetique (3 niveaux + coffre), mais avec la progression
	// que le client a lui-meme enregistree (noeud courant, etoiles, location). Sans cela, chaque reconnexion
	// (retour du centre de controle, etc.) remettait la carte au niveau 1.
	private static bool IsBlockContext(JsonObject data)
	{
		string ctx = data["context"]?.ToString() ?? "";
		return ctx == "Block" || ctx == "Signal";
	}

	private static JsonObject MainPathWithProgress(JsonArray stored)
	{
		JsonObject result = MainPathWithProgressCore(stored);
		UnlockBuilder.FixStoredSignalNodes(result["nodes"] as JsonArray);
		MapContent.RemoveDuplicateTv(result);
		MapContent.InjectPuzzle(result);
		return result;
	}

	private static JsonObject MainPathWithProgressCore(JsonArray stored)
	{
		JsonObject jsonObject = UnlockBuilder.SyntheticMainPath();
		JsonObject jsonObject2 = null;
		foreach (JsonNode item in stored)
		{
			if (item is JsonObject jsonObject3 && jsonObject3["name"]?.ToString() == "MainPath")
			{
				jsonObject2 = jsonObject3;
				break;
			}
		}
		if (jsonObject2 == null)
		{
			return jsonObject;
		}
		jsonObject["currentNode"] = Math.Max(1L, AsLong(jsonObject2["currentNode"], 1L));
		// Le client retrouve un noeud par sa position dans la liste (index = id - 1) : le chemin doit etre complet et trie.
		// On renvoie donc le chemin enregistre en entier (ses noeuds sont ceux que le client a lui-meme envoyes), trie par id.
		if (jsonObject2["nodes"] is JsonArray storedNodes && storedNodes.Count >= 4)
		{
			List<JsonObject> sorted = new List<JsonObject>();
			HashSet<long> seen = new HashSet<long>();
			foreach (JsonNode sn in storedNodes)
			{
				if (sn is JsonObject so && AsLong(so["id"], -1L) > 0 && seen.Add(AsLong(so["id"], -1L)))
				{
					sorted.Add((JsonObject)so.DeepClone());
				}
			}
			sorted.Sort((JsonObject a, JsonObject b) => AsLong(a["id"], 0L).CompareTo(AsLong(b["id"], 0L)));
			bool dense = sorted.Count > 0 && AsLong(sorted[sorted.Count - 1]["id"], 0L) == sorted.Count;
			if (dense)
			{
				JsonArray full = new JsonArray();
				long cur = AsLong(jsonObject["currentNode"], 1L);
				foreach (JsonObject so in sorted)
				{
					// un coffre / une television courants doivent rester "non debloques" : sinon le jeu saute l'animation d'ouverture
					// et les recompenses (il les croit deja recuperes)
					if (AsLong(so["id"], 0L) == cur && so["data"] is JsonObject curData && !IsBlockContext(curData))
					{
						curData["unlocked"] = true;
					}
					full.Add(so);
				}
				jsonObject["nodes"] = full;
				return jsonObject;
			}
		}
		JsonArray jsonArray = (JsonArray)jsonObject["nodes"];
		if (jsonObject2["nodes"] is JsonArray jsonArray2)
		{
			foreach (JsonNode item2 in jsonArray2)
			{
				if (!(item2 is JsonObject jsonObject4))
				{
					continue;
				}
				long num = AsLong(jsonObject4["id"], -1L);
				JsonObject jsonObject5 = null;
				foreach (JsonNode item3 in jsonArray)
				{
					if (item3 is JsonObject jsonObject6 && AsLong(jsonObject6["id"], -2L) == num)
					{
						jsonObject5 = jsonObject6;
						break;
					}
				}
				if (jsonObject5 != null)
				{
					jsonObject5["score"] = AsLong(jsonObject4["score"], 0L);
					jsonObject5["rented"] = jsonObject4["rented"]?.ToString() == "true" || jsonObject4["rented"]?.ToString() == "True";
				}
				else if (num > 0 && (AsLong(jsonObject4["score"], 0L) > 0 || jsonObject4["rented"]?.ToString()?.ToLowerInvariant() == "true" || num >= AsLong(jsonObject["currentNode"], 1L)))
				{
					// les noeuds deja joues, et le noeud courant + les suivants (generes par le client) : sans eux la carte reste vide
					JsonNode added = jsonObject4.DeepClone();
					if (added is JsonObject addedObj && num == AsLong(jsonObject["currentNode"], 1L) && addedObj["data"] is JsonObject addedData && !IsBlockContext(addedData))
					{
						addedData["unlocked"] = true;
					}
					jsonArray.Add(added);
				}
			}
		}
		return jsonObject;
	}

	// La bulle "Daily Gems" (defi de diamants) n'est creee par le client que pendant un dialogue de tutoriel
	// ("ACTION:SpawnDiamondNode"), absent des donnees recreees. Le client la reprend pourtant telle quelle quand elle
	// se trouve dans le chemin flottant enregistre : on y ajoute donc le defi du jour (et on retire ceux des jours passes).
	// Le client met lui-meme a jour le temps restant, et la remplace quand le defi est termine / expire.
	private static void EnsureDailyDiamondNode(JsonArray paths, string playerId)
	{
		JsonArray daily = TimedApi.DailyChallenges(playerId);
		if (daily.Count == 0 || !(daily[0] is JsonObject ch))
		{
			return;
		}
		string eventId = ch["timedEventId"]?.ToString() ?? "";
		string minigameId = ch["meta"]?["id"]?.ToString() ?? "";
		if (eventId == "" || minigameId == "")
		{
			return;
		}
		JsonObject? floating = null;
		for (int i = paths.Count - 1; i >= 0; i--)
		{
			if (paths[i] is JsonObject po && po["name"]?.ToString() == "FloatingPath")
			{
				if (floating == null)
				{
					floating = po;
				}
				else
				{
					paths.RemoveAt(i);
				}
			}
		}
		if (floating == null)
		{
			floating = new JsonObject { ["name"] = "FloatingPath", ["currentNode"] = 1, ["overwrite"] = true, ["lane"] = -2, ["nodes"] = new JsonArray() };
			paths.Add(floating);
		}
		if (!(floating["nodes"] is JsonArray nodes))
		{
			nodes = new JsonArray();
			floating["nodes"] = nodes;
		}
		for (int j = nodes.Count - 1; j >= 0; j--)
		{
			if (nodes[j]?["data"] is JsonObject nd && nd["context"]?.ToString() == "Challenge")
			{
				if (nd["timedEventId"]?.ToString() == eventId)
				{
					return;
				}
				nodes.RemoveAt(j);
			}
		}
		long id = 0;
		foreach (JsonNode? n in nodes)
		{
			id = Math.Max(id, AsLong(n?["id"], 0L));
		}
		id++;
		nodes.Add(new JsonObject
		{
			["id"] = id,
			["levelNumber"] = id,
			["score"] = 0,
			["rented"] = false,
			["data"] = new JsonObject
			{
				["context"] = "Challenge",
				["minigameId"] = minigameId,
				["unlocked"] = true,
				["timedEventId"] = eventId,
				["duration"] = AsLong(ch["duration"], 86400L),
				["timeleft"] = AsLong(ch["timeLeft"], 86400L),
				["endTime"] = DateTime.UtcNow.AddSeconds(AsLong(ch["timeLeft"], 86400L)).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
				["eventOver"] = false,
				["domeDestroyed"] = false
			}
		});
	}

	private static JsonArray StoredPaths(Row p)
	{
		JsonArray jsonArray = ((((ParseOr(p.S("progression", "{}"), new JsonObject()) is JsonObject jsonObject) ? jsonObject["paths"] : null) is JsonArray jsonArray2) ? ((JsonArray)jsonArray2.DeepClone()) : new JsonArray());
		if (Settings.UnlockEverything)
		{
			_ = MiscApi.PathVersion;
			JsonArray jsonArray3 = new JsonArray { MainPathWithProgress(jsonArray) };
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject2 && jsonObject2["name"]?.ToString() != "MainPath")
					{
						jsonArray3.Add(jsonObject2.DeepClone());
					}
				}
				MapContent.InjectInsaneBranch(jsonArray3);
				EnsureDailyDiamondNode(jsonArray3, p.S("id"));
				return jsonArray3;
			}
		}
		return jsonArray;
	}

	public static void MergeAchievements(string playerId, JsonNode? update)
	{
		Row player = GetPlayer(playerId);
		if (player == null || update == null)
		{
			return;
		}
		JsonObject jsonObject = (ParseOr(player.S("achievements", "{}"), new JsonObject()) as JsonObject) ?? new JsonObject();
		List<JsonObject> list = new List<JsonObject>();
		if (update is JsonArray jsonArray)
		{
			foreach (JsonNode item3 in jsonArray)
			{
				if (item3 is JsonObject item)
				{
					list.Add(item);
				}
			}
		}
		else if (update is JsonObject item2)
		{
			list.Add(item2);
		}
		bool flag = false;
		foreach (JsonObject item4 in list)
		{
			string text = item4["id"]?.ToString() ?? "";
			if (!(text == ""))
			{
				long result;
				long num = (long.TryParse(item4["value"]?.ToString(), out result) ? result : 0);
				long result2;
				long num2 = ((jsonObject[text] is JsonObject jsonObject2 && long.TryParse(jsonObject2["value"]?.ToString(), out result2)) ? result2 : (-1));
				if (num > num2)
				{
					JsonObject jsonObject3 = (JsonObject)item4.DeepClone();
					jsonObject3["value"] = num;
					jsonObject[text] = jsonObject3;
					flag = true;
				}
			}
		}
		if (flag)
		{
			Db.Exec("UPDATE players SET achievements=$0 WHERE id=$1", jsonObject.ToJsonString(), playerId);
		}
	}

	public static void MergeProgression(string playerId, JsonNode? update)
	{
		Row player = GetPlayer(playerId);
		if (player == null || !(update is JsonObject jsonObject) || !(jsonObject["paths"] is JsonArray jsonArray))
		{
			return;
		}
		JsonObject jsonObject2 = (ParseOr(player.S("progression", "{}"), new JsonObject()) as JsonObject) ?? new JsonObject();
		JsonArray jsonArray2 = (JsonArray)(jsonObject2["paths"] = (jsonObject2["paths"] as JsonArray) ?? new JsonArray());
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonObject jsonObject3))
			{
				continue;
			}
			string text = jsonObject3["name"]?.GetValue<string>() ?? "";
			if (text == "")
			{
				continue;
			}
			JsonObject jsonObject4 = null;
			foreach (JsonNode item2 in jsonArray2)
			{
				if (item2 is JsonObject jsonObject5 && jsonObject5["name"]?.GetValue<string>() == text)
				{
					jsonObject4 = jsonObject5;
					break;
				}
			}
			if (jsonObject4 == null)
			{
				jsonArray2.Add(jsonObject3.DeepClone());
				continue;
			}
			string[] array = new string[5] { "currentNode", "lane", "startNode", "parent", "overwrite" };
			foreach (string text2 in array)
			{
				if (jsonObject3[text2] != null)
				{
					if (text == "MainPath" && text2 == "currentNode" && jsonObject4[text2] != null)
					{
						long val = Convert.ToInt64(jsonObject4[text2].GetValue<object>().ToString());
						long val2 = Convert.ToInt64(jsonObject3[text2].GetValue<object>().ToString());
						jsonObject4[text2] = Math.Max(val, val2);
					}
					else
					{
						jsonObject4[text2] = jsonObject3[text2].DeepClone();
					}
				}
			}
			if (!(jsonObject3["nodes"] is JsonArray jsonArray3))
			{
				continue;
			}
			bool flag = jsonObject3["overwrite"]?.GetValue<bool>() ?? false;
			JsonArray jsonArray4 = (jsonObject4["nodes"] as JsonArray) ?? new JsonArray();
			if (flag)
			{
				jsonArray4 = new JsonArray();
			}
			jsonObject4["nodes"] = jsonArray4;
			foreach (JsonNode item3 in jsonArray3)
			{
				if (!(item3 is JsonObject jsonObject6))
				{
					continue;
				}
				string text3 = jsonObject6["id"]?.ToString() ?? "";
				int num = -1;
				for (int j = 0; j < jsonArray4.Count; j++)
				{
					if (jsonArray4[j] is JsonObject jsonObject7 && jsonObject7["id"]?.ToString() == text3)
					{
						num = j;
						break;
					}
				}
				if (num >= 0)
				{
					jsonArray4[num] = jsonObject6.DeepClone();
				}
				else
				{
					jsonArray4.Add(jsonObject6.DeepClone());
				}
			}
		}
		Db.Exec("UPDATE players SET progression=$0 WHERE id=$1", jsonObject2.ToJsonString(), playerId);
	}

	public static void ApplyResources(string playerId, JsonNode? body)
	{
		if (string.IsNullOrEmpty(playerId) || !(body is JsonObject jsonObject))
		{
			return;
		}
		JsonObject res = jsonObject["Resources"] as JsonObject;
		if (res == null && jsonObject["update"] is JsonObject jsonObject2)
		{
			res = jsonObject2["Resources"] as JsonObject;
		}
		List<string> sets;
		List<object?> args;
		if (res != null)
		{
			sets = new List<string>();
			args = new List<object>();
			AddInt("coins", "coins");
			AddInt("diamonds", "diamonds");
			AddInt("stars", "stars");
			AddInt("bolts", "bolts");
			AddInt("level", "level");
			AddInt("keys", "keys");
			AddInt("maxKeys", "maxKeys");
			AddInt("cups", "cups");
			AddInt("rank", "rank");
			AddInt("itemLevel", "itemLevel");
			AddInt("keyRefresh", "keyRefresh");
			if (res["upgrades"] is JsonObject jsonObject3)
			{
				sets.Add($"upgrades=${args.Count}");
				args.Add(jsonObject3.ToJsonString());
			}
			if (res["boosters"] is JsonObject jsonObject4)
			{
				sets.Add($"boosters=${args.Count}");
				args.Add(jsonObject4.ToJsonString());
			}
			if (sets.Count != 0)
			{
				args.Add(playerId);
				Db.Exec($"UPDATE players SET {string.Join(",", sets)} WHERE id=${args.Count - 1}", args.ToArray());
			}
		}
		void AddInt(string key, string col)
		{
			if (res[key] == null)
			{
				return;
			}
			try
			{
				long num = Convert.ToInt64(res[key].GetValue<object>().ToString());
				sets.Add($"{col}=${args.Count}");
				args.Add(num);
			}
			catch
			{
			}
		}
	}

	// Reinstallation du jeu : si "adopt.txt" (un identifiant de joueur) existe a cote du serveur,
	// la prochaine connexion SANS identifiant reprend ce compte au lieu d'en creer un nouveau.
	// Le fichier est supprime des qu'il est utilise.
	private static string AdoptPending()
	{
		try
		{
			string path = System.IO.Path.Combine(Settings.BaseDir, "adopt.txt");
			if (!System.IO.File.Exists(path))
			{
				return "";
			}
			string text = System.IO.File.ReadAllText(path).Trim();
			System.IO.File.Delete(path);
			if (GetPlayer(text) == null)
			{
				Console.WriteLine("[Login] adopt.txt: unknown player id, ignored");
				return "";
			}
			Console.WriteLine("[Login] adopt.txt: new install adopts existing player " + text);
			return text;
		}
		catch
		{
			return "";
		}
	}

	public static void Register()
	{
		Router.Map("/v4/player/login", async delegate(Ctx c)
		{
			JsonObject jsonObject = c.BodyJson() as JsonObject;
			string text = jsonObject?["id"]?.ToString() ?? c.PlayerId;
			if (string.IsNullOrEmpty(text))
			{
				text = AdoptPending();
			}
			string text2 = jsonObject?["name"]?.ToString() ?? "";
			string text3 = jsonObject?["udid"]?.ToString() ?? "";
			Row player = GetPlayer(text);
			if (player == null)
			{
				if (string.IsNullOrEmpty(text))
				{
					text = NewId();
				}
				if (string.IsNullOrWhiteSpace(text2))
				{
					text2 = "Player" + text.Substring(0, 4);
				}
				Db.Exec("INSERT INTO players (id, name, udid, layoutVersion, upgrades, boosters) VALUES ($0,$1,$2,$3,$4,$5)", text, text2, text3, 3, MaxUpgradesJson, NewPlayerBoostersJson);
				// Le jeu ne permet pas de "suivre" un joueur de zero (seulement via Facebook, absent ici) : sur ce serveur local,
				// chaque nouveau joueur devient ami (abonnement dans les deux sens) avec tous les joueurs existants.
				Db.Exec("INSERT OR IGNORE INTO follows (followerId, followeeId) SELECT $0, id FROM players WHERE id<>$0", text);
				Db.Exec("INSERT OR IGNORE INTO follows (followerId, followeeId) SELECT id, $0 FROM players WHERE id<>$0", text);
				Console.WriteLine($"[Login] New player created: {text2} ({text}), friends with every other player");
				player = GetPlayer(text);
			}
			else
			{
				if (player.S("name") == "" && text2 != "")
				{
					Db.Exec("UPDATE players SET name=$0 WHERE id=$1", text2, text);
				}
				Db.Exec("UPDATE players SET lastLogin=CURRENT_TIMESTAMP WHERE id=$0", text);
				player = GetPlayer(text);
				Console.WriteLine($"[Login] {player.S("name")} ({text})");
			}
			if (Settings.RichMode && (player.L("coins", 0L) < 2000000 || player.L("diamonds", 0L) < 2000000))
			{
				Db.Exec("UPDATE players SET coins=MAX(coins,$0), diamonds=MAX(diamonds,$0) WHERE id=$1", 2000000, text);
				player = GetPlayer(text);
			}
			string text4 = NowSync();
			Db.Exec("UPDATE players SET lastPathSync=$0 WHERE id=$1", text4, text);
			bool flag = Settings.UnlockEverything && player.L("layoutVersion", 0L) != 3;
			if (flag)
			{
				Db.Exec("UPDATE players SET layoutVersion=$0 WHERE id=$1", 3, text);
				Console.WriteLine("[Login] " + player.S("name") + ": map layout changed, asking the device to reset its cached map once.");
			}
			JsonObject jsonObject2 = PlayerJson(player);
			if (flag)
			{
				jsonObject2["resetLocal"] = "yes";
			}
			jsonObject2["sessionId"] = NewId();
			jsonObject2["clientVersion"] = 0;
			jsonObject2["versionInfo"] = "";
			jsonObject2["skipTimerLeft"] = 0;
			jsonObject2["majorPath"] = 2;
			jsonObject2["pathVersion"] = MiscApi.PathVersion;
			jsonObject2["paths"] = StoredPaths(player);
			jsonObject2["timedEvents"] = TimedApi.TimedEventsJson(text);
			jsonObject2["lastPathSync"] = text4;
			JsonArray jsonArray = new JsonArray();
			if (ParseOr(player.S("achievements", "{}"), new JsonObject()) is JsonObject jsonObject3)
			{
				foreach (KeyValuePair<string, JsonNode> item in jsonObject3)
				{
					if (item.Value != null)
					{
						jsonArray.Add(item.Value.DeepClone());
					}
				}
			}
			jsonObject2["achievements"] = jsonArray;
			await c.Json(jsonObject2);
		});
		Router.Map("/v1/player/info", async delegate(Ctx c)
		{
			Row player = GetPlayer(c.PlayerId);
			if (player == null)
			{
				await c.Error();
			}
			else
			{
				await c.Json(PlayerJson(player));
			}
		});
		Router.Map("/v1/player/stats/find", (Ctx c) => c.Json(ChallengeApi.VersusStats(c.Q("playerId", c.PlayerId))));
		Router.Map("/v1/player/remove", async delegate(Ctx c)
		{
			if (!string.IsNullOrEmpty(c.PlayerId))
			{
				Db.Exec("DELETE FROM follows WHERE followerId=$0 OR followeeId=$0", c.PlayerId);
				Db.Exec("DELETE FROM players WHERE id=$0", c.PlayerId);
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/player/resetLevel", async delegate(Ctx c)
		{
			Db.Exec("UPDATE players SET progression='{}', upgrades='{}', boosters='{}', data='{}', claimedTutorials='[]' WHERE id=$0", c.PlayerId);
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/player/data/remove", async delegate(Ctx c)
		{
			Row player = GetPlayer(c.PlayerId);
			if (player != null)
			{
				JsonObject jsonObject = (ParseOr(player.S("data", "{}"), new JsonObject()) as JsonObject) ?? new JsonObject();
				string[] array = c.Q("fields").Split(',', StringSplitOptions.RemoveEmptyEntries);
				foreach (string propertyName in array)
				{
					jsonObject.Remove(propertyName);
				}
				Db.Exec("UPDATE players SET data=$0 WHERE id=$1", jsonObject.ToJsonString(), c.PlayerId);
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/player/skip", async delegate(Ctx c)
		{
			ApplyResources(c.PlayerId, c.BodyJson());
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/tutorial/claim", async delegate(Ctx c)
		{
			ApplyResources(c.PlayerId, c.BodyJson());
			Row player = GetPlayer(c.PlayerId);
			string tutorial = c.Q("tutorial");
			if (player != null && tutorial != "")
			{
				JsonArray jsonArray = (ParseOr(player.S("claimedTutorials", "[]"), new JsonArray()) as JsonArray) ?? new JsonArray();
				if (!jsonArray.Any((JsonNode x) => x?.ToString() == tutorial))
				{
					jsonArray.Add(tutorial);
				}
				Db.Exec("UPDATE players SET claimedTutorials=$0 WHERE id=$1", jsonArray.ToJsonString(), c.PlayerId);
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v2/player/data/change", async delegate(Ctx c)
		{
			JsonNode jsonNode = c.BodyJson();
			ApplyResources(c.PlayerId, jsonNode);
			if (jsonNode is JsonObject jsonObject)
			{
				MergeProgression(c.PlayerId, jsonObject["progression"]);
				MergeAchievements(c.PlayerId, jsonObject["achievements"]);
			}
			string text = NowSync();
			Db.Exec("UPDATE players SET lastPathSync=$0 WHERE id=$1", text, c.PlayerId);
			await c.Json(new JsonObject { ["lastPathSync"] = text });
		});
		Router.Map("/v1/player/changeName", async delegate(Ctx c)
		{
			string text = (c.BodyJson() as JsonObject)?["name"]?.ToString() ?? "";
			if (text != "")
			{
				Db.Exec("UPDATE players SET name=$0 WHERE id=$1", text, c.PlayerId);
				// le nom est aussi recopie sur les niveaux et les scores du joueur : on les met a jour pour que tout le monde voie le nouveau nom
				Db.Exec("UPDATE minigames SET creatorName=$0 WHERE creatorId=$1", text, c.PlayerId);
				Db.Exec("UPDATE scores SET name=$0 WHERE playerId=$1", text, c.PlayerId);
				Db.Exec("UPDATE comments SET name=$0 WHERE playerId=$1", text, c.PlayerId);
				Console.WriteLine($"[Player] renamed to {text} ({c.PlayerId})");
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/achievement/upsert", async delegate(Ctx c)
		{
			MergeAchievements(c.PlayerId, c.BodyJson());
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/player/skipTimeLeft", (Ctx c) => c.Json(new JsonObject { ["skipTimerLeft"] = 0 }));
		Router.Map("/v1/player/resetSkipTimer", (Ctx c) => c.Json(new JsonObject()));
		Router.Map("/v1/player/follow/add", async delegate(Ctx c)
		{
			string text = c.Q("followeeId");
			if (text != "" && text != c.PlayerId)
			{
				Db.Exec("INSERT OR IGNORE INTO follows (followerId, followeeId) VALUES ($0,$1)", c.PlayerId, text);
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/player/follow/remove", async delegate(Ctx c)
		{
			string text = c.Q("followerId", c.PlayerId);
			Db.Exec("DELETE FROM follows WHERE followerId=$0 AND followeeId=$1", text, c.Q("followeeId"));
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/player/follow/followees", (Ctx c) => c.Json(new JsonObject { ["data"] = PlayerList("SELECT p.* FROM follows f JOIN players p ON p.id=f.followeeId WHERE f.followerId=$0", c.Q("followerId", c.PlayerId)) }));
		Router.Map("/v1/player/follow/followers", (Ctx c) => c.Json(new JsonObject { ["data"] = PlayerList("SELECT p.* FROM follows f JOIN players p ON p.id=f.followerId WHERE f.followeeId=$0", c.Q("followeeId", c.PlayerId)) }));
		Router.Map("/v1/player/friends", (Ctx c) => c.Json(new JsonObject
		{
			["followees"] = PlayerList("SELECT p.* FROM follows f JOIN players p ON p.id=f.followeeId WHERE f.followerId=$0", c.PlayerId),
			["followers"] = PlayerList("SELECT p.* FROM follows f JOIN players p ON p.id=f.followerId WHERE f.followeeId=$0", c.PlayerId)
		}));
	}

	private static JsonArray PlayerList(string sql, string arg)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (Row item in Db.Query(sql, arg))
		{
			jsonArray.Add(FriendJson(item));
		}
		return jsonArray;
	}
}
