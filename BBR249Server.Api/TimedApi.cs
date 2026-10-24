using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

public static class TimedApi
{
	private const int DailyDuration = 86400;

	// "Daily Gems" : un niveau de collecte d'etoiles public, different chaque jour (UTC).
	// Le client calcule lui-meme les diamants gagnes ; le serveur fournit juste le niveau et le temps restant.
	public static JsonArray DailyChallenges(string playerId)
	{
		JsonArray jsonArray = new JsonArray();
		List<Row> list = Db.Query("SELECT * FROM minigames WHERE template=0 AND state='public' AND gameMode='StarCollect' ORDER BY id");
		if (list.Count == 0)
		{
			return jsonArray;
		}
		DateTime utcNow = DateTime.UtcNow;
		int num = (int)(utcNow.Date - new DateTime(2020, 1, 1)).TotalDays;
		Row m = list[num % list.Count];
		jsonArray.Add(new JsonObject
		{
			["timedEventId"] = "daily" + utcNow.ToString("yyyyMMdd"),
			["timeLeft"] = (int)(utcNow.Date.AddDays(1.0) - utcNow).TotalSeconds,
			["duration"] = DailyDuration,
			["meta"] = MinigameApi.MetaJson(m, playerId)
		});
		return jsonArray;
	}

	// Niveau "Fresh and Free" (bulle flottante) : un niveau public, de preference d'un autre createur et pas encore joue par le joueur.
	// Le choix est stable pendant une heure pour un joueur donne (la bulle ne change pas a chaque rafraichissement de la carte) ;
	// il change aussi des que le joueur a joue le niveau propose. La bulle est presente des qu'un niveau public existe.
	// Le client ne le demande pas lui-meme : il le lit dans la cle "freshAndFree" des evenements chronometres.
	public static JsonObject? FreshAndFree(string playerId)
	{
		string[] queries = new string[]
		{
			"SELECT id FROM minigames WHERE template=0 AND state='public' AND levelData IS NOT NULL AND creatorId<>$0 AND id NOT IN (SELECT gameId FROM scores WHERE playerId=$0) ORDER BY id",
			"SELECT id FROM minigames WHERE template=0 AND state='public' AND levelData IS NOT NULL AND id NOT IN (SELECT gameId FROM scores WHERE playerId=$0) ORDER BY id",
			"SELECT id FROM minigames WHERE template=0 AND state='public' AND levelData IS NOT NULL ORDER BY id"
		};
		int seed = 17;
		foreach (char ch in playerId + DateTime.UtcNow.ToString("yyyyMMddHH"))
		{
			seed = (seed * 31 + ch) & 0x7FFFFFFF;
		}
		foreach (string sql in queries)
		{
			System.Collections.Generic.List<Row> rows = Db.Query(sql, playerId);
			if (rows.Count > 0)
			{
				Row? picked = MinigameApi.GetMinigame(rows[seed % rows.Count].S("id"));
				if (picked != null)
				{
					return MinigameApi.MetaJson(picked, playerId);
				}
			}
		}
		return null;
	}

	public static JsonObject TimedEventsJson(string playerId)
	{
		JsonObject result = TimedEventsBase(playerId);
		JsonObject? fresh = FreshAndFree(playerId);
		if (fresh != null)
		{
			result["freshAndFree"] = fresh;
		}
		return result;
	}

	private static JsonObject TimedEventsBase(string playerId)
	{
		return new JsonObject
		{
			["dailyChallenges"] = DailyChallenges(playerId),
			["versusChallenges"] = ChallengeApi.ListFor(playerId, "versus"),
			["friendlyChallenges"] = ChallengeApi.ListFor(playerId, "friendly"),
			["daily"] = new JsonArray(),
			["active"] = new JsonArray(),
			["claimable"] = new JsonArray(),
			["claimed"] = new JsonArray()
		};
	}

	public static void Register()
	{
		Router.Map("/v1/metagame/getTimed", (Ctx c) => c.Json(TimedEventsJson(c.PlayerId)));
		Router.Map("/v1/challenge/daily/find", (Ctx c) => c.Json(new JsonObject { ["data"] = DailyChallenges(c.PlayerId) }));
		Router.Map("/v1/tournament/find", (Ctx c) => c.Json(new JsonObject
		{
			["daily"] = new JsonArray(),
			["active"] = new JsonArray(),
			["claimable"] = new JsonArray(),
			["claimed"] = new JsonArray()
		}));
		Router.Map("/v1/tournament/get", (Ctx c) => c.Error());
		Router.Map("/v1/reward/claim", (Ctx c) => c.Json(new JsonObject { ["reward"] = new JsonObject
		{
			["rewardCoins"] = 0,
			["rewardDiamonds"] = 0
		} }));
	}
}
