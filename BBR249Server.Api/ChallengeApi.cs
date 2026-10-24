using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BBR249Server.Data;
using BBR249Server.Net;

namespace BBR249Server.Api;

public static class ChallengeApi
{
	private const int TimeLeftSeconds = 604800;

	private static Row? Get(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			return Db.One("SELECT * FROM challenges WHERE id=$0", id);
		}
		return null;
	}

	private static int SideOf(Row ch, string playerId)
	{
		if (playerId == "")
		{
			return 0;
		}
		if (ch.S("p1") == playerId)
		{
			return 1;
		}
		if (ch.S("p2") == playerId)
		{
			return 2;
		}
		return 0;
	}

	private static string Col(string name, int side)
	{
		return name + side;
	}

	private static bool Joined(Row ch)
	{
		if (ch.S("p2") != "")
		{
			if (!(ch.S("status") == "active"))
			{
				return ch.S("status") == "finished";
			}
			return true;
		}
		return false;
	}

	private static JsonObject Participant(Row ch, int side)
	{
		string text = ch.S(Col("p", side));
		Row player = PlayerApi.GetPlayer(text);
		JsonObject jsonObject = new JsonObject
		{
			["n"] = player?.S("name") ?? "?",
			["t"] = ch.L(Col("t", side), 0L),
			["s"] = ch.L(Col("stars", side), 0L),
			["p"] = text
		};
		if (player != null && player.S("facebookId") != "")
		{
			jsonObject["fb"] = player.S("facebookId");
		}
		if (player != null && player.S("gameCenterId") != "")
		{
			jsonObject["gc"] = player.S("gameCenterId");
		}
		return new JsonObject
		{
			["score"] = jsonObject,
			["rank"] = player?.L("rank", 0L) ?? 0,
			["stars"] = ch.L(Col("stars", side), 0L),
			["tries"] = ch.L(Col("tries", side), 0L)
		};
	}

	public static JsonObject ToMeta(Row ch, string viewer)
	{
		JsonArray jsonArray = new JsonArray { Participant(ch, 1) };
		if (Joined(ch))
		{
			jsonArray.Add(Participant(ch, 2));
		}
		JsonObject jsonObject = new JsonObject
		{
			["timedEventId"] = ch.S("id"),
			["gameId"] = ch.S("gameId"),
			["timeLeft"] = 604800,
			["round"] = 1,
			["status"] = ch.S("status"),
			["participants"] = jsonArray
		};
		string text = ch.S("currentPlayer");
		if (text != "")
		{
			jsonObject["currentPlayer"] = text;
		}
		if (ch.S("winner") != "")
		{
			jsonObject["winner"] = ch.S("winner");
		}
		if (ch.S("challenged") != "")
		{
			jsonObject["challengedPlayer"] = ch.S("challenged");
		}
		int num = SideOf(ch, viewer);
		long num2 = ((num == 0) ? 0 : ch.L(Col("t", (num != 1) ? 1 : 2), 0L));
		jsonObject["matchBall"] = num != 0 && viewer == text && num2 > 0 && Joined(ch);
		Row minigame = MinigameApi.GetMinigame(ch.S("gameId"));
		if (minigame != null)
		{
			jsonObject["meta"] = MinigameApi.MetaJson(minigame, viewer);
		}
		return jsonObject;
	}

	public static JsonArray ListFor(string playerId, string kind)
	{
		JsonArray jsonArray = new JsonArray();
		if (playerId == "")
		{
			return jsonArray;
		}
		List<Row> list = Db.Query("SELECT * FROM challenges WHERE kind=$0 AND (\n                (p1=$1 AND claimed1=0)\n             OR (p2=$1 AND claimed2=0)\n             OR (challenged=$1 AND p2='' AND claimed2=0 AND (t1>0 OR status='declined'))\n            ) ORDER BY createdAt DESC LIMIT 50", kind, playerId);
		foreach (Row item in list)
		{
			jsonArray.Add(ToMeta(item, playerId));
		}
		return jsonArray;
	}

	public static JsonObject VersusStats(string playerId)
	{
		Row row = Db.One("SELECT COUNT(*) AS plays, COALESCE(SUM(winner=$0),0) AS wins FROM challenges\n                          WHERE kind='versus' AND status='finished' AND winner<>'' AND (p1=$0 OR p2=$0)", playerId);
		long num = row?.L("plays", 0L) ?? 0;
		long num2 = row?.L("wins", 0L) ?? 0;
		return new JsonObject
		{
			["versusPlays"] = num,
			["versusWins"] = num2,
			["versusLosses"] = num - num2
		};
	}

	private static void Finish(Row ch, string winner)
	{
		Db.Exec("UPDATE challenges SET status='finished', winner=$0, currentPlayer='', updatedAt=CURRENT_TIMESTAMP WHERE id=$1", winner, ch.S("id"));
	}

	private static void AfterTarget(Row ch)
	{
		if (Joined(ch))
		{
			Db.Exec("UPDATE challenges SET currentPlayer=p2, tries2=0, updatedAt=CURRENT_TIMESTAMP WHERE id=$0", ch.S("id"));
		}
		else
		{
			Db.Exec("UPDATE challenges SET currentPlayer='', updatedAt=CURRENT_TIMESTAMP WHERE id=$0", ch.S("id"));
		}
	}

	private static void ApplyOutcome(Row ch, string me, bool win, bool lose)
	{
		int num = SideOf(ch, me);
		if (num == 0 || ch.S("status") == "finished")
		{
			return;
		}
		string text = ch.S(Col("p", (num != 1) ? 1 : 2));
		long num2 = ch.L(Col("t", (num != 1) ? 1 : 2), 0L);
		if (lose)
		{
			if (text != "" && Joined(ch))
			{
				Finish(ch, text);
			}
		}
		else if (win)
		{
			if (num2 > 0 && Joined(ch))
			{
				Finish(ch, me);
			}
			else if (num == 1)
			{
				AfterTarget(ch);
			}
		}
	}

	private static async Task Overwrite(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		int num = ((row != null) ? SideOf(row, c.PlayerId) : 0);
		if (row == null || num == 0)
		{
			await c.Error();
			return;
		}
		long num2 = c.QInt("time");
		long num3 = row.L(Col("t", num), 0L);
		byte[] array = ((c.Body.Length > 1) ? c.Body : null);
		if (num2 > 0 && (num3 == 0L || num2 < num3))
		{
			Db.Exec($"UPDATE challenges SET t{num}=$0, ghost{num}=COALESCE($1, ghost{num}), updatedAt=CURRENT_TIMESTAMP WHERE id=$2", num2, array, row.S("id"));
		}
		ApplyOutcome(Get(row.S("id")), c.PlayerId, c.QBool("winRound"), c.QBool("loseRound"));
		await c.Json(new JsonObject());
	}

	private static async Task Quit(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		if (row == null || SideOf(row, c.PlayerId) == 0)
		{
			await c.Json(new JsonObject());
			return;
		}
		ApplyOutcome(row, c.PlayerId, c.QBool("winRound"), c.QBool("loseRound"));
		await c.Json(new JsonObject());
	}

	private static async Task Win(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
		if (row != null && SideOf(row, c.PlayerId) != 0 && row.S("status") != "finished" && Joined(row))
		{
			Finish(row, c.PlayerId);
		}
		await c.Json(new JsonObject());
	}

	private static async Task Forfeit(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
		if (row != null)
		{
			int num = SideOf(row, c.PlayerId);
			if (num != 0 && row.S("status") != "finished")
			{
				string text = row.S(Col("p", (num != 1) ? 1 : 2));
				if (text != "" && Joined(row))
				{
					Finish(row, text);
				}
				else
				{
					Db.Exec($"UPDATE challenges SET status='finished', claimed{num}=1, claimed{((num != 1) ? 1 : 2)}=1 WHERE id=$0", row.S("id"));
				}
			}
		}
		await c.Json(new JsonObject());
	}

	private static async Task SetTries(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		int num = ((row != null) ? SideOf(row, c.PlayerId) : 0);
		if (row != null && num != 0)
		{
			Db.Exec($"UPDATE challenges SET tries{num}=$0 WHERE id=$1", c.QInt("tries"), row.S("id"));
		}
		await c.Json(new JsonObject());
	}

	private static async Task Claim(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
		if (row != null)
		{
			int num = SideOf(row, c.PlayerId);
			if (num != 0)
			{
				Db.Exec($"UPDATE challenges SET claimed{num}=1 WHERE id=$0", row.S("id"));
				if (!Joined(row))
				{
					Db.Exec("UPDATE challenges SET status='finished', claimed1=1, claimed2=1 WHERE id=$0", row.S("id"));
				}
			}
		}
		await c.Json(new JsonObject());
	}

	private static async Task ClaimAndSave(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		JsonNode jsonNode = c.BodyJson();
		PlayerApi.ApplyResources(c.PlayerId, jsonNode);
		if (jsonNode is JsonObject jsonObject)
		{
			PlayerApi.MergeProgression(c.PlayerId, jsonObject["progression"]);
		}
		if (row != null)
		{
			int num = SideOf(row, c.PlayerId);
			if (num == 0 && row.S("challenged") == c.PlayerId)
			{
				num = 2;
			}
			if (num != 0)
			{
				Db.Exec($"UPDATE challenges SET claimed{num}=1 WHERE id=$0", row.S("id"));
			}
		}
		await c.Json(new JsonObject { ["lastPathSync"] = PlayerApi.NowSync() });
	}

	// Utilise par la fenetre du serveur : cree un defi "ami" de `challengerId` vers `challengedId` sur un niveau de course.
	// Le defieur n'a pas a jouer : son temps a battre est son meilleur temps sur ce niveau, sinon le meilleur temps enregistre
	// sur le niveau, sinon le temps du createur. Le defi apparait chez l'autre joueur comme une bulle sur la carte.
	// Retourne null si tout va bien, sinon le message d'erreur.
	public static string? CreateFriendlyAdmin(string challengerId, string challengedId, string gameId)
	{
		if (challengerId == challengedId)
		{
			return "Choose two different players.";
		}
		Row? level = MinigameApi.GetMinigame(gameId);
		if (level == null || PlayerApi.GetPlayer(challengerId) == null || PlayerApi.GetPlayer(challengedId) == null)
		{
			return "Unknown player or level.";
		}
		Row? score = Db.One("SELECT time, stars, ghost FROM scores WHERE gameId=$0 AND playerId=$1 AND time>0", gameId, challengerId);
		if (score == null)
		{
			score = Db.One("SELECT time, stars, ghost FROM scores WHERE gameId=$0 AND time>0 ORDER BY time LIMIT 1", gameId);
		}
		byte[]? ghost = score?.B("ghost");
		if (ghost == null || ghost.Length < 2)
		{
			ghost = level.B("creatorGhost");
		}
		long time = score != null && score.L("time", 0L) > 0 ? score.L("time", 0L) : (level.L("bestTime", 0L) > 0 ? level.L("bestTime", 0L) : 60000L);
		long stars = score != null ? Math.Max(1L, score.L("stars", 1L)) : 3L;
		string id = PlayerApi.NewId();
		Db.Exec("INSERT INTO challenges (id, kind, gameId, p1, challenged, status, currentPlayer, t1, stars1, tries1, ghost1) VALUES ($0,'friendly',$1,$2,$3,'pending','',$4,$5,1,$6)", id, gameId, challengerId, challengedId, time, stars, ghost);
		Console.WriteLine($"[Challenge] friendly {id}: {PlayerApi.GetPlayer(challengerId)?.S("name")} -> {PlayerApi.GetPlayer(challengedId)?.S("name")} on {gameId} (time to beat {time} ms) - created from the server window");
		return null;
	}

	private static async Task GhostOf(Ctx c)
	{
		Row row = Get(c.Q("challengeId"));
		if (row == null)
		{
			await c.Error();
			return;
		}
		int num = SideOf(row, c.Q("playerId"));
		byte[] array = ((num == 0) ? null : row.B(Col("ghost", num)));
		if (array == null || array.Length < 2)
		{
			await c.Error();
			return;
		}
		Row player = PlayerApi.GetPlayer(c.Q("playerId"));
		await c.Bytes(array, null, new Dictionary<string, string>
		{
			["GHOST_TIME"] = row.L(Col("t", num), 0L).ToString(),
			["GHOST_NAME"] = Uri.EscapeDataString(player?.S("name") ?? "")
		});
	}

	private static void MapFamily(string prefix, string overwritePath)
	{
		Router.Map(overwritePath, Overwrite);
		Router.Map("/v1/" + prefix + "/score/quit", Quit);
		Router.Map("/v1/" + prefix + "/win", Win);
		Router.Map("/v1/" + prefix + "/forfeit", Forfeit);
		Router.Map("/v1/" + prefix + "/tries/set", SetTries);
		Router.Map("/v1/" + prefix + "/claim", Claim);
		Router.Map("/v1/" + prefix + "/claimAndSave", ClaimAndSave);
	}

	public static void Register()
	{
		MapFamily("friendly", "/v1/friendly/score/overwrite");
		MapFamily("versus", "/v2/versus/score/overwrite");
		Router.Map("/v1/ghost/versus/get", GhostOf);
		Router.Map("/v1/ghost/friendly/get", GhostOf);
		Router.Map("/v1/friendly/create", async delegate(Ctx c)
		{
			string text = c.Q("gameId");
			string text2 = c.Q("challengedId");
			if (MinigameApi.GetMinigame(text) == null || PlayerApi.GetPlayer(text2) == null || text2 == c.PlayerId)
			{
				await c.Error();
			}
			else
			{
				PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
				string text3 = PlayerApi.NewId();
				Db.Exec("INSERT INTO challenges (id, kind, gameId, p1, challenged, status, currentPlayer, comment)\n                      VALUES ($0,'friendly',$1,$2,$3,'pending',$2,$4)", text3, text, c.PlayerId, text2, c.Q("comment"));
				Console.WriteLine($"[Challenge] friendly {text3}: {PlayerApi.GetPlayer(c.PlayerId)?.S("name")} -> {PlayerApi.GetPlayer(text2)?.S("name")} on {text}");
				await c.Json(ToMeta(Get(text3), c.PlayerId));
			}
		});
		Router.Map("/v1/friendly/join", async delegate(Ctx c)
		{
			Row row = Get(c.Q("challengeId"));
			if (row == null || row.S("kind") != "friendly" || row.S("status") != "pending" || row.S("challenged") != c.PlayerId)
			{
				await c.Error();
			}
			else
			{
				PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
				Db.Exec("UPDATE challenges SET p2=$0, status='active', currentPlayer=$0, tries2=0, updatedAt=CURRENT_TIMESTAMP WHERE id=$1", c.PlayerId, row.S("id"));
				await c.Json(ToMeta(Get(row.S("id")), c.PlayerId));
			}
		});
		Router.Map("/v1/friendly/decline", async delegate(Ctx c)
		{
			Row row = Get(c.Q("challengeId"));
			if (row == null || row.S("challenged") != c.PlayerId || row.S("status") != "pending")
			{
				await c.Error();
			}
			else
			{
				PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
				Db.Exec("UPDATE challenges SET status='declined', currentPlayer='', claimed2=1, updatedAt=CURRENT_TIMESTAMP WHERE id=$0", row.S("id"));
				await c.Json(ToMeta(Get(row.S("id")), c.PlayerId));
			}
		});
		Router.Map("/v4/versus/create", async delegate(Ctx c)
		{
			PlayerApi.ApplyResources(c.PlayerId, c.BodyJson());
			string playerId = c.PlayerId;
			Row row = Db.One("SELECT * FROM challenges WHERE kind='versus' AND status='open' AND p2='' AND p1<>$0 AND t1>0\n                                ORDER BY createdAt LIMIT 1", playerId);
			if (row != null)
			{
				Db.Exec("DELETE FROM challenges WHERE kind='versus' AND status='open' AND p1=$0 AND p2='' AND t1=0", playerId);
				Db.Exec("UPDATE challenges SET p2=$0, status='active', currentPlayer=$0, tries2=0, updatedAt=CURRENT_TIMESTAMP WHERE id=$1", playerId, row.S("id"));
				Console.WriteLine($"[Challenge] versus {row.S("id")}: matched {PlayerApi.GetPlayer(playerId)?.S("name")} with {PlayerApi.GetPlayer(row.S("p1"))?.S("name")}");
				await c.Json(ToMeta(Get(row.S("id")), playerId));
				return;
			}
			Row row3 = Db.One("SELECT * FROM challenges WHERE kind='versus' AND status='open' AND p1=$0 AND p2='' AND t1=0 ORDER BY createdAt LIMIT 1", playerId);
			if (row3 != null)
			{
				await c.Json(ToMeta(row3, playerId));
			}
			else
			{
				Row row2 = MinigameApi.GetMinigame(c.Q("gameId"));
				if (row2 == null)
				{
					row2 = Db.One("SELECT * FROM minigames WHERE state='public' AND template=0 AND gameMode='Race' ORDER BY RANDOM() LIMIT 1");
				}
				if (row2 == null)
				{
					await c.Error();
				}
				else
				{
					string text = PlayerApi.NewId();
					Db.Exec("INSERT INTO challenges (id, kind, gameId, p1, status, currentPlayer) VALUES ($0,'versus',$1,$2,'open',$2)", text, row2.S("id"), playerId);
					Console.WriteLine($"[Challenge] versus {text}: {PlayerApi.GetPlayer(playerId)?.S("name")} is searching an opponent on {row2.S("id")}");
					await c.Json(ToMeta(Get(text), playerId));
				}
			}
		});
	}
}
