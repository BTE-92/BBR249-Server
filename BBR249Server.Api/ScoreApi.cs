using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BBR249Server.Core;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

public static class ScoreApi
{
	private const string ScoreSelect = "SELECT s.*, COALESCE(NULLIF(p.name,''), s.name) AS name, p.facebookId AS fb, p.gameCenterId AS gc FROM scores s LEFT JOIN players p ON p.id=s.playerId";

	private static bool IsRace(string gameId, string fallbackMode)
	{
		string a = MinigameApi.GetMinigame(gameId)?.S("gameMode") ?? fallbackMode;
		return !string.Equals(a, "StarCollect", StringComparison.OrdinalIgnoreCase);
	}

	private static string OrderBy(bool race)
	{
		if (!race)
		{
			return "s.stars DESC, (s.time<=0), s.time ASC";
		}
		return "(s.time<=0), s.time ASC, s.stars DESC";
	}

	private static JsonObject EntryJson(Row r)
	{
		JsonObject jsonObject = new JsonObject
		{
			["n"] = r.S("name"),
			["t"] = r.L("time", 0L),
			["s"] = r.L("stars", 0L),
			["p"] = r.S("playerId")
		};
		if (r.S("fb") != "")
		{
			jsonObject["fb"] = r.S("fb");
		}
		if (r.S("gc") != "")
		{
			jsonObject["gc"] = r.S("gc");
		}
		return jsonObject;
	}

	private static JsonObject Table(string gameId, string playerId, bool race, int limit)
	{
		JsonArray jsonArray = new JsonArray();
		foreach (Row item in Db.Query($"{"SELECT s.*, COALESCE(NULLIF(p.name,''), s.name) AS name, p.facebookId AS fb, p.gameCenterId AS gc FROM scores s LEFT JOIN players p ON p.id=s.playerId"} WHERE s.gameId=$0 ORDER BY {OrderBy(race)} LIMIT {limit}", gameId))
		{
			jsonArray.Add(EntryJson(item));
		}
		JsonArray jsonArray2 = new JsonArray();
		foreach (Row item2 in Db.Query("SELECT s.*, COALESCE(NULLIF(p.name,''), s.name) AS name, p.facebookId AS fb, p.gameCenterId AS gc FROM scores s LEFT JOIN players p ON p.id=s.playerId WHERE s.gameId=$0 AND s.playerId IN (SELECT followeeId FROM follows WHERE followerId=$1) ORDER BY " + OrderBy(race), gameId, playerId))
		{
			jsonArray2.Add(EntryJson(item2));
		}
		return new JsonObject
		{
			["topScores"] = jsonArray,
			["followeeScores"] = jsonArray2
		};
	}

	private static void RecomputeLevelStats(string gameId)
	{
		Db.Exec("UPDATE minigames SET\r\n            participantCount=(SELECT COUNT(*) FROM scores WHERE gameId=$0),\r\n            totalWinners=(SELECT COUNT(*) FROM scores WHERE gameId=$0 AND (time>0 OR stars>0)),\r\n            oneStarWinners=(SELECT COUNT(*) FROM scores WHERE gameId=$0 AND stars>=1),\r\n            twoStarWinners=(SELECT COUNT(*) FROM scores WHERE gameId=$0 AND stars>=2),\r\n            threeStarWinners=(SELECT COUNT(*) FROM scores WHERE gameId=$0 AND stars>=3),\r\n            bestTime=COALESCE((SELECT MIN(time) FROM scores WHERE gameId=$0 AND time>0),0)\r\n            WHERE id=$0", gameId);
	}

	private static string Escape(string s)
	{
		return Uri.EscapeDataString(s);
	}

	private static Task SingleGhost(Ctx c, byte[]? ghost, string name, long time)
	{
		if (ghost == null || ghost.Length < 2)
		{
			return c.Error();
		}
		return c.Bytes(ghost, null, new Dictionary<string, string>
		{
			["GHOST_TIME"] = time.ToString(),
			["GHOST_NAME"] = Escape(name)
		});
	}

	public static void Register()
	{
		Router.Map("/v5/highscore/send", async delegate(Ctx c)
		{
			string text = c.Q("gameId");
			long num = c.QInt("time");
			long num2 = c.QInt("stars");
			bool flag = IsRace(text, c.Q("gameMode", "Race"));
			string text2 = PlayerApi.GetPlayer(c.PlayerId)?.S("name");
			string text3 = ((text2 != null && text2.Length > 0) ? text2 : c.Q("name"));
			byte[] array = ((c.Body.Length > 1) ? c.Body : null);
			Row row = Db.One("SELECT * FROM scores WHERE gameId=$0 AND playerId=$1", text, c.PlayerId);
			bool flag2 = row == null || ((!flag) ? (num2 > row.L("stars", 0L) || (num2 == row.L("stars", 0L) && num > 0 && (row.L("time", 0L) <= 0 || num < row.L("time", 0L)))) : (num > 0 && (row.L("time", 0L) <= 0 || num < row.L("time", 0L))));
			if (row == null)
			{
				Db.Exec("INSERT INTO scores (gameId, playerId, name, time, stars, ghost) VALUES ($0,$1,$2,$3,$4,$5)", text, c.PlayerId, text3, num, num2, array);
			}
			else if (flag2)
			{
				// l'ancien record devient un ghost de l'historique (3 ghosts au total par joueur et par niveau : le courant + les 2 meilleurs anciens)
				byte[]? oldGhost = row.B("ghost");
				if (oldGhost != null && oldGhost.Length > 1 && row.L("time", 0L) > 0)
				{
					Db.Exec("INSERT INTO ghost_history (gameId, playerId, time, stars, ghost) VALUES ($0,$1,$2,$3,$4)", text, c.PlayerId, row.L("time", 0L), row.L("stars", 0L), oldGhost);
					Db.Exec("DELETE FROM ghost_history WHERE gameId=$0 AND playerId=$1 AND rowid NOT IN (SELECT rowid FROM ghost_history WHERE gameId=$0 AND playerId=$1 ORDER BY time ASC LIMIT 2)", text, c.PlayerId);
				}
				Db.Exec("UPDATE scores SET name=$0, time=$1, stars=$2, ghost=COALESCE($3, ghost), createdAt=CURRENT_TIMESTAMP WHERE gameId=$4 AND playerId=$5", text3, num, num2, array, text, c.PlayerId);
			}
			Db.Exec("UPDATE minigames SET timesPlayed=timesPlayed+1 WHERE id=$0", text);
			if (row == null && (num > 0 || num2 > 0))
			{
				MinigameApi.CreditCreator(text, c.PlayerId, 2);
			}
			RecomputeLevelStats(text);
			Console.WriteLine($"[Score] {text3} on {text}: time={num} stars={num2} ({(flag2 ? "new best" : "not better")})");
			await c.Json(Table(text, c.PlayerId, flag, 100));
		});
		Router.Map("/v2/highscore/quit", async delegate(Ctx c)
		{
			Db.Exec("UPDATE minigames SET timesPlayed=timesPlayed+1 WHERE id=$0", c.Q("gameId"));
			await c.Json(new JsonObject());
		});
		Router.Map("/v2/highscore/find", delegate(Ctx c)
		{
			string gameId = c.Q("gameId");
			return c.Json(Table(gameId, c.PlayerId, IsRace(gameId, c.Q("gameMode", "Race")), Math.Clamp(c.QInt("limit", 100), 1, 200)));
		});
		Router.Map("/v1/highscore/next", async delegate(Ctx c)
		{
			string text = c.Q("gameId");
			long num = c.QInt("time");
			Row row = (IsRace(text, c.Q("gameMode", "Race")) ? Db.One("SELECT s.*, COALESCE(NULLIF(p.name,''), s.name) AS name, p.facebookId AS fb, p.gameCenterId AS gc FROM scores s LEFT JOIN players p ON p.id=s.playerId WHERE s.gameId=$0 AND s.time>0 AND s.time<$1 AND s.playerId<>$2 ORDER BY s.time DESC LIMIT 1", text, (num <= 0) ? long.MaxValue : num, c.PlayerId) : null);
			await c.Json((row != null) ? EntryJson(row) : new JsonObject());
		});
		Router.Map("/v2/ghost/next", async delegate(Ctx c)
		{
			string text = c.Q("gameId");
			long result;
			List<long> list = (from s in c.Q("times").Split(',', StringSplitOptions.RemoveEmptyEntries)
				select (!long.TryParse(s, out result)) ? long.MaxValue : result).ToList();
			bool race = IsRace(text, "Race");
			List<Row> source = Db.Query("SELECT s.*, COALESCE(NULLIF(p.name,''), s.name) AS name, p.facebookId AS fb, p.gameCenterId AS gc FROM (SELECT gameId, playerId, name, time, stars, ghost FROM scores WHERE gameId=$0 AND ghost IS NOT NULL AND length(ghost)>1 UNION ALL SELECT gameId, playerId, '' AS name, time, stars, ghost FROM ghost_history WHERE gameId=$0 AND ghost IS NOT NULL AND length(ghost)>1) s LEFT JOIN players p ON p.id=s.playerId ORDER BY " + OrderBy(race), text);
			List<Row> list2 = new List<Row>();
			foreach (long t in list)
			{
				List<Row> free = source.Where((Row r) => !list2.Any((Row x) => x.S("playerId") == r.S("playerId") && x.L("time", 0L) == r.L("time", 0L))).ToList();
				Row pick = null;
				if (t >= int.MaxValue)
				{
					pick = free.FirstOrDefault();
				}
				else
				{
					pick = (from r in free
						where r.L("time", 0L) > 0 && r.L("time", 0L) <= t
						orderby r.L("time", 0L) descending
						select r).FirstOrDefault() ?? free.FirstOrDefault();
				}
				if (pick != null)
				{
					list2.Add(pick);
				}
			}
			if (list2.Count == 0)
			{
				await c.Error();
			}
			else
			{
				List<byte[]> list3 = new List<byte[]>();
				foreach (Row item in list2)
				{
					JsonObject jsonObject = new JsonObject
					{
						["name"] = item.S("name"),
						["time"] = item.L("time", 0L),
						["playerId"] = item.S("playerId")
					};
					list3.Add(FilePacker.Utf8(jsonObject.ToJsonString()));
					list3.Add(item.B("ghost"));
				}
				var (data, fileSizes) = FilePacker.Combine(list3.ToArray());
				await c.Bytes(data, fileSizes);
			}
		});
		Router.Map("/v1/ghost/creator/get", async delegate(Ctx c)
		{
			Row minigame = MinigameApi.GetMinigame(c.Q("gameId"));
			await SingleGhost(c, minigame?.B("creatorGhost"), minigame?.S("creatorName") ?? "", minigame?.L("bestTime", 0L) ?? 0);
		});
		Router.Map("/v1/ghost/next", async delegate(Ctx c)
		{
			string text = c.Q("gameId");
			long num = c.QInt("time");
			Row row = Db.One("SELECT s.*, COALESCE(NULLIF(p.name,''), s.name) AS name, p.facebookId AS fb, p.gameCenterId AS gc FROM scores s LEFT JOIN players p ON p.id=s.playerId WHERE s.gameId=$0 AND s.ghost IS NOT NULL AND length(s.ghost)>1 AND s.time>0 AND s.time<$1 AND s.playerId<>$2 ORDER BY s.time DESC LIMIT 1", text, (num <= 0) ? long.MaxValue : num, c.PlayerId);
			await SingleGhost(c, row?.B("ghost"), row?.S("name") ?? "", row?.L("time", 0L) ?? 0);
		});
		Router.Map("/v1/ghost/versus/get", (Ctx c) => c.Error());
		Router.Map("/v1/ghost/friendly/get", (Ctx c) => c.Error());
	}
}
