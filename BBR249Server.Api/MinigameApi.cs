using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BBR249Server.Core;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

public static class MinigameApi
{
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

	// Les niveaux officiels de la carte (identifiants Mongo de 24 caracteres hexa) n'existent plus sur ce serveur.
	// Sans reponse, le jeu reessaie a l'infini ("loading" sans fin). On sert donc un niveau public de remplacement,
	// choisi de facon stable a partir de l'identifiant demande.
	public static Row? GetOrStandIn(string id)
	{
		Row? row = GetMinigame(id);
		if (row != null || id.Length != 24 || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{24}$"))
		{
			return row;
		}
		List<Row> list = Db.Query("SELECT * FROM minigames WHERE template=0 AND state='public' AND gameMode='Race' AND levelData IS NOT NULL ORDER BY id");
		if (list.Count == 0)
		{
			return null;
		}
		int num = 0;
		foreach (char ch in id)
		{
			num = (num * 31 + ch) & 0x7FFFFFFF;
		}
		string unit = MiscApi.UnitOfOfficialLevel(id);
		if (unit == "Motorcycle" || unit == "OffroadCar")
		{
			List<Row> sameUnit = list.FindAll((Row r) => r.S("playerUnit") == unit);
			if (sameUnit.Count > 0)
			{
				list = sameUnit;
			}
		}
		Row row2 = list[num % list.Count];
		Console.WriteLine("[Minigame] official level " + id + " no longer exists, serving stand-in " + row2.S("id") + " (" + row2.S("name") + ")");
		return row2;
	}

	public static Row? GetMinigame(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			return Db.One("SELECT * FROM minigames WHERE id=$0", id);
		}
		return null;
	}

	public static string PlayerRating(string gameId, string playerId)
	{
		return Db.One("SELECT rating FROM ratings WHERE gameId=$0 AND playerId=$1", gameId, playerId)?.S("rating", "Unrated") ?? "Unrated";
	}

	public static JsonObject MetaJson(Row m, string playerId)
	{
		string text = m.S("id");
		JsonObject obj = new JsonObject
		{
			["id"] = text,
			["name"] = m.S("name"),
			["creatorId"] = m.S("creatorId", "??")
		};
		string text2 = PlayerApi.GetPlayer(m.S("creatorId"))?.S("name");
		obj["creatorName"] = ((text2 != null && text2.Length > 0) ? text2 : m.S("creatorName", "??"));
		obj["description"] = m.S("description");
		obj["timesPlayed"] = m.L("timesPlayed", 0L);
		obj["timesLiked"] = m.L("timesLiked", 0L);
		obj["timesRated"] = m.L("timesRated", 0L);
		obj["clientVersion"] = m.L("clientVersion", 0L);
		obj["levelRequirement"] = m.L("levelRequirement", 0L);
		obj["complexity"] = m.L("complexity", 0L);
		obj["playerUnit"] = m.S("playerUnit", "Any");
		obj["rating"] = PlayerRating(text, playerId);
		obj["gameQuality"] = m.D("gameQuality", 1.0);
		obj["itemsUsed"] = ParseOr(m.S("itemsUsed", "[]"), new JsonArray());
		obj["difficulty"] = m.S("difficulty", "New");
		obj["gameMode"] = m.S("gameMode", "Race");
		obj["totalWinners"] = m.L("totalWinners", 0L);
		obj["oneStarWinners"] = m.L("oneStarWinners", 0L);
		obj["twoStarWinners"] = m.L("twoStarWinners", 0L);
		obj["threeStarWinners"] = m.L("threeStarWinners", 0L);
		obj["participantCount"] = m.L("participantCount", 0L);
		obj["bestTime"] = m.L("bestTime", 0L);
		obj["editSessionCount"] = m.L("editSessionCount", 0L);
		obj["groundsModificationCount"] = m.L("groundsModificationCount", 0L);
		obj["itemsModificationCount"] = m.L("itemsModificationCount", 0L);
		obj["lastPlaySessionStartCount"] = m.L("lastPlaySessionStartCount", 0L);
		obj["timeSpentInEditMode"] = m.L("timeSpentInEditMode", 0L);
		obj["timeSpentEditing"] = m.L("timeSpentEditing", 0L);
		obj["state"] = m.S("state", "saved");
		JsonObject jsonObject = obj;
		if (m.S("creatorFacebookId") != "")
		{
			jsonObject["creatorFacebookId"] = m.S("creatorFacebookId");
		}
		if (m.S("creatorGameCenterId") != "")
		{
			jsonObject["creatorGameCenterId"] = m.S("creatorGameCenterId");
		}
		if (m.S("researchIdentifier") != "")
		{
			jsonObject["researchIdentifier"] = m.S("researchIdentifier");
		}
		if (m.L("medianTime", 0L) > 0)
		{
			jsonObject["median"] = m.L("medianTime", 0L);
		}
		if (m.S("publishTime") != "")
		{
			jsonObject["publishTime"] = m.S("publishTime");
		}
		JsonNode jsonNode = ParseOr(m.S("creatorUpgrades", "{}"), new JsonObject());
		if (jsonNode is JsonObject { Count: >0 } jsonObject2)
		{
			jsonObject["creatorUpgrades"] = jsonObject2;
		}
		if (m.S("creatorId") == playerId && playerId != "")
		{
			jsonObject["rewardCoins"] = m.L("rewardCoins", 0L);
			jsonObject["totalCoinsEarned"] = m.L("totalCoinsEarned", 0L);
			jsonObject["publishReward"] = false;
			jsonObject["playReward"] = false;
		}
		Row row = Db.One("SELECT time, stars FROM scores WHERE gameId=$0 AND playerId=$1", text, playerId);
		if (row != null)
		{
			jsonObject["played"] = true;
			jsonObject["t"] = row.L("time", 0L);
			jsonObject["s"] = row.L("stars", 0L);
		}
		return jsonObject;
	}

	private static JsonObject DataList(IEnumerable<Row> rows, string playerId)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (Row row in rows)
		{
			jsonArray.Add(MetaJson(row, playerId));
		}
		return new JsonObject { ["data"] = jsonArray };
	}

	private static string Limit(Ctx c, int def = 50)
	{
		return Math.Clamp(c.QInt("limit", def), 1, 200).ToString();
	}

	private static Task ListBy(Ctx c, string where, params object?[] args)
	{
		string sql = "SELECT * FROM minigames WHERE template=0 AND " + where + " ORDER BY createdAt DESC LIMIT " + Limit(c);
		return c.Json(DataList(Db.Query(sql, args), c.PlayerId));
	}

	private static Task Search(Ctx c)
	{
		List<string> list = new List<string> { "template=0", "state='public'" };
		List<object> list2 = new List<object>();
		// Niveau de la carte : le jeu demande UN niveau de course et force "Motorcycle" (PsGameLoopLevel). On l'ignore et on tire
		// au hasard parmi tous les niveaux de course publics (voiture et moto), sans tenir compte de la difficulte.
		bool mapPick = c.Q("limit") == "1" && c.Q("gameMode") == "Race";
		string text = c.Q("playerUnit");
		if (mapPick && text == "Motorcycle")
		{
			text = "";
		}
		if (text != "" && text != "Any")
		{
			list.Add($"playerUnit=${list2.Count}");
			list2.Add(text);
		}
		string text2 = c.Q("gameMode");
		if (text2 != "" && text2 != "Any")
		{
			list.Add($"gameMode=${list2.Count}");
			list2.Add(text2);
		}
		string text3 = c.Q("difficulty");
		if (mapPick)
		{
			text3 = "";
		}
		if (text3 != "" && text3 != "Any")
		{
			list.Add($"difficulty=${list2.Count}");
			list2.Add(text3);
		}
		string[] array = c.Q("items").Split(',', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text4 in array)
		{
			list.Add($"itemsUsed LIKE ${list2.Count}");
			list2.Add("%\"" + text4.Trim() + "\"%");
		}
		string sql = "SELECT * FROM minigames WHERE " + string.Join(" AND ", list) + " ORDER BY " + (mapPick ? "RANDOM()" : "publishTime DESC") + " LIMIT " + Limit(c, 10);
		return c.Json(DataList(Db.Query(sql, list2.ToArray()), c.PlayerId));
	}

	public static void CreditCreator(string gameId, string byPlayerId, int coins)
	{
		if (coins > 0 && !(gameId == ""))
		{
			Db.Exec("UPDATE minigames SET rewardCoins=rewardCoins+$0, totalCoinsEarned=totalCoinsEarned+$0 WHERE id=$1 AND state='public' AND creatorId<>$2 AND creatorId<>''", coins, gameId, byPlayerId);
		}
	}

	private static void RecomputeRatingStats(string gameId)
	{
		Db.Exec("UPDATE minigames SET\r\n            timesRated=(SELECT COUNT(*) FROM ratings WHERE gameId=$0 AND rating<>'Unrated'),\r\n            timesLiked=(SELECT COUNT(*) FROM ratings WHERE gameId=$0 AND rating='Positive')\r\n            WHERE id=$0", gameId);
	}

	private static async Task SaveVariant(Ctx c, string state, bool template = false)
	{
		List<byte[]> list = FilePacker.Split(c.Body, c.Header("FILE_SIZES"));
		JsonObject meta = new JsonObject();
		if (list.Count > 0)
		{
			try
			{
				meta = (JsonNode.Parse(list[0]) as JsonObject) ?? new JsonObject();
			}
			catch
			{
			}
		}
		string text = c.Q("id");
		if (text == "")
		{
			text = meta["id"]?.ToString() ?? "";
		}
		if (text == "")
		{
			text = PlayerApi.NewId();
		}
		Row player = PlayerApi.GetPlayer(c.PlayerId);
		Row minigame = GetMinigame(text);
		if (minigame == null)
		{
			Db.Exec("INSERT INTO minigames (id, creatorId, creatorName) VALUES ($0,$1,$2)", text, c.PlayerId, player?.S("name") ?? meta["creatorName"]?.ToString() ?? "??");
		}
		List<string> sets = new List<string>();
		List<object?> args = new List<object>();
		Text("name");
		Text("description");
		Text("gameMode");
		Text("playerUnit");
		Text("creatorFacebookId");
		Text("creatorGameCenterId");
		Num("clientVersion");
		Num("complexity");
		Num("levelRequirement");
		Num("editSessionCount");
		Num("groundsModificationCount");
		Num("itemsModificationCount");
		Num("lastPlaySessionStartCount");
		Num("timeSpentInEditMode");
		Num("timeSpentEditing");
		if (meta["bestTime"] != null && state == "public")
		{
			Num("bestTime");
		}
		if (meta["itemsUsed"] is JsonArray jsonArray)
		{
			Set("itemsUsed", jsonArray.ToJsonString());
		}
		if (meta["creatorUpgrades"] is JsonObject jsonObject)
		{
			Set("creatorUpgrades", jsonObject.ToJsonString());
		}
		Set("state", state);
		if (template)
		{
			Set("template", 1);
		}
		Set("publishTime", (state == "public") ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : "");
		Set("updatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
		if (list.Count > 1 && list[1].Length > 1)
		{
			Set("levelData", list[1]);
		}
		if (list.Count > 2 && list[2].Length > 1)
		{
			Set("screenshot", list[2]);
		}
		if (list.Count > 3 && list[3].Length > 1)
		{
			Set("creatorGhost", list[3]);
		}
		args.Add(text);
		Db.Exec($"UPDATE minigames SET {string.Join(",", sets)} WHERE id=${args.Count - 1}", args.ToArray());
		if (list.Count > 4 && list[4].Length > 1)
		{
			try
			{
				PlayerApi.MergeProgression(c.PlayerId, JsonNode.Parse(list[4]));
			}
			catch
			{
			}
		}
		Console.WriteLine($"[Minigame] {state}: {text} by {c.PlayerId} ({list.Count} parts) mode={meta["gameMode"]} unit={meta["playerUnit"]}");
		await c.Json(MetaJson(GetMinigame(text), c.PlayerId));
		void Num(string key)
		{
			if (meta[key] != null && double.TryParse(meta[key].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				Set(key, (long)result);
			}
		}
		void Set(string col, object? v)
		{
			sets.Add($"{col}=${args.Count}");
			args.Add(v);
		}
		void Text(string key)
		{
			if (meta[key] != null)
			{
				Set(key, meta[key].ToString());
			}
		}
	}

	public static void Register()
	{
		Router.Map("/v2/minigame/save", (Ctx c) => SaveVariant(c, "saved"));
		Router.Map("/v3/minigame/publish", (Ctx c) => SaveVariant(c, "public"));
		Router.Map("/v1/minigame/hide", (Ctx c) => SaveVariant(c, "hidden"));
		Router.Map("/v1/minigame/meta/find", async delegate(Ctx c)
		{
			Row minigame = GetOrStandIn(c.Q("id"));
			if (minigame == null)
			{
				await c.Error();
			}
			else
			{
				await c.Json(MetaJson(minigame, c.PlayerId));
			}
		});
		Router.Map("/v1/minigame/data/find", async delegate(Ctx c)
		{
			byte[] array = GetOrStandIn(c.Q("id"))?.B("levelData");
			if (array == null || array.Length == 0)
			{
				await c.Error();
			}
			else
			{
				await c.Bytes(array);
			}
		});
		Router.Map("/v1/minigame/data/save", async delegate(Ctx c)
		{
			if (c.Body.Length > 1)
			{
				Db.Exec("UPDATE minigames SET levelData=$0 WHERE id=$1", c.Body, c.Q("id"));
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/screenshot/find", async delegate(Ctx c)
		{
			byte[] array = GetOrStandIn(c.Q("gameId"))?.B("screenshot");
			if (array == null || array.Length == 0)
			{
				await c.Error();
			}
			else
			{
				await c.Bytes(array);
			}
		});
		Router.Map("/v1/minigame/screenshot/save", async delegate(Ctx c)
		{
			if (c.Body.Length > 1)
			{
				Db.Exec("UPDATE minigames SET screenshot=$0 WHERE id=$1", c.Body, c.Q("gameId"));
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/delete", async delegate(Ctx c)
		{
			string text = c.Q("id");
			Row minigame = GetMinigame(text);
			if (minigame != null && (minigame.S("creatorId") == c.PlayerId || c.PlayerId == ""))
			{
				Db.Exec("DELETE FROM minigames WHERE id=$0", text);
				Db.Exec("DELETE FROM scores WHERE gameId=$0", text);
				Db.Exec("DELETE FROM ratings WHERE gameId=$0", text);
				Db.Exec("DELETE FROM comments WHERE gameId=$0", text);
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/own", (Ctx c) => ListBy(c, "creatorId=$0", c.PlayerId));
		Router.Map("/v1/minigame/own/published", (Ctx c) => ListBy(c, "creatorId=$0 AND state='public'", c.PlayerId));
		Router.Map("/v1/minigame/own/saved", (Ctx c) => ListBy(c, "creatorId=$0 AND state='saved'", c.PlayerId));
		Router.Map("/v1/minigame/hidden", (Ctx c) => ListBy(c, "creatorId=$0 AND state='hidden'", c.PlayerId));
		Router.Map("/v1/minigame/meta/published", (Ctx c) => ListBy(c, "state='public'"));
		Router.Map("/v1/minigame/followee/published", (Ctx c) => ListBy(c, "creatorId=$0 AND state='public'", c.Q("playerId")));
		Router.Map("/v1/minigame/followee/find", (Ctx c) => ListBy(c, "state='public' AND creatorId IN (SELECT followeeId FROM follows WHERE followerId=$0)", c.PlayerId));
		Router.Map("/v1/minigame/history", (Ctx c) => ListBy(c, "id IN (SELECT gameId FROM scores WHERE playerId=$0)", c.Q("playerId", c.PlayerId)));
		Router.Map("/v2/minigame/meta/search", Search);
		Router.Map("/v1/minigame/meta/findItems", Search);
		Router.Map("/v1/minigame/subgenre/find", Search);
		Router.Map("/v1/minigame/trend/find", Search);
		Router.Map("/v1/minigame/popular/find", Search);
		Router.Map("/v1/minigame/unrated/find", Search);
		Router.Map("/v1/minigame/oneFresh", async delegate(Ctx c)
		{
			List<Row> list = Db.Query("SELECT * FROM minigames WHERE template=0 AND state='public' ORDER BY RANDOM() LIMIT 1");
			await c.Json((list.Count > 0) ? MetaJson(list[0], c.PlayerId) : new JsonObject());
		});
		Router.Map("/v1/rating/save", async delegate(Ctx c)
		{
			string text = c.Q("gameId");
			string text2 = c.Q("rating", "Unrated");
			if (text != "" && c.PlayerId != "")
			{
				bool flag = PlayerRating(text, c.PlayerId) == "Positive";
				if (text2 == "Positive" && !flag)
				{
					CreditCreator(text, c.PlayerId, 5);
				}
				Db.Exec("INSERT INTO ratings (gameId, playerId, rating) VALUES ($0,$1,$2) ON CONFLICT(gameId, playerId) DO UPDATE SET rating=$2", text, c.PlayerId, text2);
				RecomputeRatingStats(text);
			}
			PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/comment/save", async delegate(Ctx c)
		{
			Row player = PlayerApi.GetPlayer(c.PlayerId);
			string text = c.Q("comment");
			if (c.Q("gameId") != "" && text != "")
			{
				Db.Exec("INSERT INTO comments (gameId, playerId, name, comment, tag) VALUES ($0,$1,$2,$3,$4)", c.Q("gameId"), c.PlayerId, player?.S("name") ?? "", text, c.Q("tag"));
			}
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/comment/find", delegate(Ctx c)
		{
			JsonArray jsonArray = new JsonArray();
			List<Row> list = Db.Query("SELECT c.*, p.facebookId AS fb, p.gameCenterId AS gc FROM comments c\r\n                LEFT JOIN players p ON p.id=c.playerId WHERE c.gameId=$0 ORDER BY c.id DESC LIMIT 100", c.Q("gameId"));
			foreach (Row item in list)
			{
				JsonObject jsonObject = new JsonObject
				{
					["playerId"] = item.S("playerId"),
					["comment"] = item.S("comment"),
					["name"] = item.S("name")
				};
				if (item.S("tag") != "")
				{
					jsonObject["tag"] = item.S("tag");
				}
				if (item.S("fb") != "")
				{
					jsonObject["facebookId"] = item.S("fb");
				}
				if (item.S("gc") != "")
				{
					jsonObject["gameCenterId"] = item.S("gc");
				}
				jsonArray.Add(jsonObject);
			}
			return c.Json(new JsonObject { ["data"] = jsonArray });
		});
		Router.Map("/v1/minigame/claim", async delegate(Ctx c)
		{
			PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
			Db.Exec("UPDATE minigames SET rewardCoins=0 WHERE id=$0 AND creatorId=$1", c.Q("gameId"), c.PlayerId);
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/claimAll", async delegate(Ctx c)
		{
			PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
			Db.Exec("UPDATE minigames SET rewardCoins=0 WHERE creatorId=$0", c.PlayerId);
			await c.Json(new JsonObject());
		});
		Router.Map("/v1/minigame/getRewards", (Ctx c) => c.Json(new JsonObject { ["coins"] = Db.One("SELECT COALESCE(SUM(rewardCoins),0) AS n FROM minigames WHERE creatorId=$0", c.PlayerId)?.L("n", 0L) ?? 0 }));
		Router.Map("/v1/template/list", (Ctx c) => c.Json(DataList(Db.Query("SELECT * FROM minigames WHERE template=1 AND researchIdentifier NOT LIKE 'puzzle_%' ORDER BY createdAt DESC LIMIT " + Limit(c, 100)), c.PlayerId)));
		Router.Map("/v1/template/save", (Ctx c) => SaveVariant(c, "saved", template: true));
		Router.Map("/v1/template/duplicate", async delegate(Ctx c)
		{
			Row row = GetMinigame(c.Q("templateId"));
			if (row == null || c.PlayerId == "")
			{
				await c.Error();
				return;
			}
			string newId = PlayerApi.NewId();
			string creator = PlayerApi.GetPlayer(c.PlayerId)?.S("name") ?? "??";
			Db.Exec("INSERT INTO minigames (id, name, description, creatorId, creatorName, state, gameMode, playerUnit, difficulty, clientVersion, gameQuality, levelRequirement, complexity, researchIdentifier, itemsUsed, creatorUpgrades, template, levelData, screenshot) SELECT $0, name, description, $1, $2, 'saved', gameMode, playerUnit, difficulty, clientVersion, gameQuality, levelRequirement, complexity, researchIdentifier, itemsUsed, creatorUpgrades, 0, levelData, screenshot FROM minigames WHERE id=$3", newId, c.PlayerId, creator, row.S("id"));
			if (c.BodyJson() is JsonObject changes)
			{
				foreach (string col in new string[5] { "name", "description", "gameMode", "playerUnit", "researchIdentifier" })
				{
					if (changes[col] != null)
					{
						Db.Exec("UPDATE minigames SET " + col + "=$0 WHERE id=$1", changes[col].ToString(), newId);
					}
				}
				if (changes["itemsUsed"] is JsonArray items)
				{
					Db.Exec("UPDATE minigames SET itemsUsed=$0 WHERE id=$1", items.ToJsonString(), newId);
				}
			}
			Console.WriteLine($"[Template] {row.S("id")} duplicated to {newId} for {creator}");
			await c.Json(MetaJson(GetMinigame(newId), c.PlayerId));
		});
		Router.Map("/v1/template/find", async delegate(Ctx c)
		{
			Row row = GetMinigame(c.Q("id"));
			if (row == null || row.L("template", 0L) != 1)
			{
				await c.Error();
			}
			else
			{
				await c.Json(MetaJson(row, c.PlayerId));
			}
		});
	}
}
