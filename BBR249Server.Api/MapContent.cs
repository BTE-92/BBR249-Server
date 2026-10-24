using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using BBR249Server.Core;
using BBR249Server.Data;

namespace BBR249Server.Api;

// Contenu ajoute a la carte de l'aventure (apres les noeuds television) avec des types de noeuds que le jeu sait deja gerer
// mais que la carte recreee n'utilisait pas :
//  - coffres "Construction" (le point de controle avec la grue, banniere "builder") en alternance avec les coffres ;
//  - une mission de construction : un noeud "LevelTemplate" qui ouvre l'editeur sur une copie d'un niveau modele ; le joueur
//    construit puis PUBLIE son niveau pour avancer (c'est le fonctionnement prevu par le jeu). Desactivable : --no-builder-mission.
public static class MapContent
{
	// identifiant (32 hexa, comme les autres niveaux) du niveau modele fourni avec le serveur
	public const string TemplateId = "7e47a1e0b3d14e7a9c11d0b17e5a0001";

	private const string TemplateIdentifier = "builder_mission_1";

	// sections dont le coffre final devient un coffre "Construction"
	private static readonly string[] ConstructionSections =
	{
		"unlock_platforms", "unlock_big_dome", "unlock_doubleups", "unlock_bouncer", "unlock_crusher", "unlock_fan", "unlock_huuuge_dome"
	};

	// section qui recoit la mission de construction (apres son premier niveau)
	private const string MissionSection = "unlock_explosives";

	// niveau puzzle "The Enigma" (fourni avec le serveur, sans les objets de la solution) ; les seuls objets permis au joueur :
	public const string PuzzleId = "7e47a1e0b3d14e7a9c11d0b17e5a0002";
	private static readonly string[] PuzzleItems = { "MaterialSand", "MaterialDanger", "MaterialMetal", "MaterialMud", "MaterialIce", "MaterialLava" };

	// Cree (une fois) le niveau modele dans la base : une ligne "template" (jamais proposee comme niveau public),
	// copiee pour le joueur par /v1/template/duplicate.
	public static void EnsureBuilderTemplate()
	{
		try
		{
			if (Db.One("SELECT id FROM minigames WHERE id=$0", TemplateId) != null)
			{
				return;
			}
			using Stream? res = typeof(MapContent).Assembly.GetManifestResourceStream("builder_template.bin");
			if (res == null)
			{
				return;
			}
			using MemoryStream ms = new MemoryStream();
			res.CopyTo(ms);
			Db.Exec("INSERT INTO minigames (id, name, description, creatorId, creatorName, state, gameMode, playerUnit, difficulty, clientVersion, gameQuality, levelRequirement, complexity, researchIdentifier, itemsUsed, creatorUpgrades, template, levelData) VALUES ($0,$1,$2,'','Big Bang Racing','public','Race','OffroadCar','New',249,1.0,0,0,$3,$4,$5,1,$6)",
				TemplateId, "Builder level", "A blank level to build on.", TemplateIdentifier, "[\"OffroadCar\",\"MaterialSand\"]",
				"{\"handlingAngle\":35,\"tireForce\":1.3,\"suspensionForce\":-0.7,\"COG\":6,\"handlingForce\":1,\"grip\":1.85,\"tireRate\":70,\"suspensionDamp\":230}", ms.ToArray());
			Console.WriteLine("[Setup] Created the builder template level " + TemplateId);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[Setup] Could not create the builder template level: " + ex.Message);
		}
	}

	// Starter levels: the official levels of the adventure map no longer exist, so the map serves public Race levels of the
	// database instead (see MinigameApi.GetOrStandIn). A brand new offline install has none, so a few are bundled.
	public static void EnsureSeedLevels()
	{
		try
		{
			if (Db.One("SELECT id FROM minigames WHERE template=0 AND state='public' AND gameMode='Race' AND levelData IS NOT NULL") != null)
			{
				return;
			}
			using Stream? res = typeof(MapContent).Assembly.GetManifestResourceStream("seed_levels.json");
			if (res == null)
			{
				return;
			}
			string json;
			using (StreamReader reader = new StreamReader(res, System.Text.Encoding.UTF8))
			{
				json = reader.ReadToEnd();
			}
			JsonArray rows = (JsonArray)JsonNode.Parse(json);
			int added = 0;
			foreach (JsonNode? n in rows)
			{
				JsonObject o = (JsonObject)n;
				Func<string, string> str = (k) => o[k]?.ToString() ?? "";
				Func<string, byte[]?> blob = (k) => o[k] == null ? null : Convert.FromBase64String(o[k].ToString());
				Db.Exec("INSERT OR IGNORE INTO minigames (id, name, description, creatorId, creatorName, state, gameMode, playerUnit, difficulty, clientVersion, gameQuality, levelRequirement, complexity, researchIdentifier, itemsUsed, creatorUpgrades, publishTime, template, levelData, screenshot, creatorGhost) VALUES ($0,$1,$2,'',$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,0,$16,$17,$18)",
					str("id"), str("name"), str("description"), str("creatorName"), str("state"), str("gameMode"), str("playerUnit"), str("difficulty"),
					o["clientVersion"]?.GetValue<long>() ?? 249L, o["gameQuality"]?.GetValue<double>() ?? 1.0, o["levelRequirement"]?.GetValue<long>() ?? 0L, o["complexity"]?.GetValue<long>() ?? 0L,
					str("researchIdentifier"), str("itemsUsed"), str("creatorUpgrades"), str("publishTime"), blob("levelData"), blob("screenshot"), blob("creatorGhost"));
				added++;
			}
			Console.WriteLine("[Setup] Added " + added + " starter levels");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[Setup] Could not add the starter levels: " + ex.Message);
		}
	}

	// Cree (une fois) le niveau du puzzle : ligne "template" cachee de la liste des modeles (researchIdentifier "puzzle_..."),
	// lue directement par le noeud puzzle de la carte.
	public static void EnsurePuzzleLevel()
	{
		try
		{
			using Stream? res = typeof(MapContent).Assembly.GetManifestResourceStream("puzzle_level.bin");
			if (res == null)
			{
				return;
			}
			using MemoryStream ms = new MemoryStream();
			res.CopyTo(ms);
			byte[]? shot = null;
			using (Stream? img = typeof(MapContent).Assembly.GetManifestResourceStream("puzzle_screenshot.jpg"))
			{
				if (img != null)
				{
					using MemoryStream ims = new MemoryStream();
					img.CopyTo(ims);
					shot = ims.ToArray();
				}
			}
			const string items = "[\"OffroadCar\",\"CollectibleStar\",\"MaterialMud\",\"MaterialLava\",\"DecorationalSign\",\"MaterialDanger\",\"MaterialMetal\",\"PsRollingBoulder\",\"ArrowSign\",\"Goal\",\"MaterialSand\",\"DangerPlatform\",\"TimeBomb\"]";
			if (Db.One("SELECT id FROM minigames WHERE id=$0", PuzzleId) != null)
			{
				Db.Exec("UPDATE minigames SET levelData=$0, screenshot=$1, itemsUsed=$2, gameMode='StarCollect', creatorName='BTE', publishTime='2026-10-08T00:00:00.000Z' WHERE id=$3", ms.ToArray(), shot, items, PuzzleId);
				return;
			}
			Db.Exec("INSERT INTO minigames (id, name, description, creatorId, creatorName, state, gameMode, playerUnit, difficulty, clientVersion, gameQuality, levelRequirement, complexity, researchIdentifier, itemsUsed, creatorUpgrades, template, levelData, screenshot, publishTime) VALUES ($0,$1,$2,'','BTE','public','StarCollect','OffroadCar','New',249,1.0,0,0,'puzzle_enigma',$3,$4,1,$5,$6,$7)",
				PuzzleId, "The Enigma", "Good Luck :D", items,
				"{\"handlingAngle\":35,\"tireForce\":1.3,\"suspensionForce\":-0.7,\"COG\":6,\"handlingForce\":1,\"grip\":1.85,\"tireRate\":70,\"suspensionDamp\":230}", ms.ToArray(), shot, "2026-10-08T00:00:00.000Z");
			Console.WriteLine("[Setup] Created the puzzle level " + PuzzleId);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[Setup] Could not create the puzzle level: " + ex.Message);
		}
	}

	// Noeuds de la section (dans l'ordre) : on part du bloc Unlock et on suit le premier lien vers un noeud de chemin.
	private static List<JsonNode> SectionChain(JsonArray nodes, JsonArray edges, string unlockName)
	{
		Dictionary<int, JsonNode> byId = new Dictionary<int, JsonNode>();
		foreach (JsonNode n in nodes)
		{
			byId[n["id"].GetValue<int>()] = n;
		}
		List<JsonNode> chain = new List<JsonNode>();
		JsonNode? unlock = nodes.FirstOrDefault((JsonNode n) => (n?["data"]?["nodeType"]?.ToString() ?? "") == "Unlock" && (n["name"]?.ToString() ?? "") == unlockName);
		if (unlock == null)
		{
			return chain;
		}
		string[] pathTypes = { "Level", "Block", "Signal", "EditorPuzzle", "LevelTemplate", "VersusRace" };
		int current = unlock["id"].GetValue<int>();
		for (int guard = 0; guard < 200; guard++)
		{
			int next = -1;
			foreach (JsonNode e in edges)
			{
				if (e["source"].GetValue<int>() == current && byId.TryGetValue(e["target"].GetValue<int>(), out JsonNode? t) && pathTypes.Contains(t["data"]?["nodeType"]?.ToString() ?? ""))
				{
					next = e["target"].GetValue<int>();
					break;
				}
			}
			if (next < 0)
			{
				break;
			}
			chain.Add(byId[next]);
			current = next;
		}
		return chain;
	}

	// Le jeu cree lui-meme les nouvelles sections de la carte et n'y met pas le noeud puzzle : on l'ajoute donc dans le chemin
	// ENREGISTRE du joueur, apres le premier niveau de la section (tant que le joueur n'a pas depasse cet endroit). Les noeuds suivants
	// sont renumerotes (le client retrouve un noeud par son rang dans la liste).
	// Sections qui se terminent par un coffre/garage "Block" : le puzzle va dans la premiere ou le joueur n'est pas encore passe.
	// Un nouveau joueur l'a donc des les premiers niveaux ; un joueur avance le trouve dans la prochaine section deja enregistree.
	private static readonly string[] PuzzleCandidates =
	{
		"unlock_car", "unlock_garage", "unlock_treadmill", "unlock_dynamics", "unlock_bike", "unlock_powerups", "unlock_platforms",
		"unlock_explosives", "unlock_big_dome", "unlock_mudandice", "unlock_doubleups", "unlock_gears"
	};

	public static bool InjectPuzzle(JsonObject mainPath)
	{
		if (!Settings.PuzzleMission)
		{
			return false;
		}
		if (Settings.PuzzleSection != "")
		{
			return InjectPuzzleAt(mainPath, Settings.PuzzleSection);
		}
		foreach (string section in PuzzleCandidates)
		{
			if (InjectPuzzleAt(mainPath, section))
			{
				return true;
			}
		}
		return false;
	}

	private static bool InjectPuzzleAt(JsonObject mainPath, string section)
	{
		if (!Settings.PuzzleMission || !(mainPath["nodes"] is JsonArray nodes) || nodes.Count < 4)
		{
			return false;
		}
		long Num(JsonNode? n, long def)
		{
			return long.TryParse(n?.ToString(), out long v) ? v : def;
		}
		string Ctx(JsonNode? n)
		{
			return n?["data"]?["context"]?.ToString() ?? "";
		}
		for (int i = 0; i < nodes.Count; i++)
		{
			if (Ctx(nodes[i]) == "EditorPuzzle" && (nodes[i]["data"]?["minigameId"]?.ToString() ?? "") == PuzzleId)
			{
				// noeud deja enregistre : on lui remet la liste de materiaux actuelle
				if (nodes[i]["data"] is JsonObject pd)
				{
					pd["puzzleLimitedItems"] = new JsonArray(PuzzleItems.Select((string it) => (JsonNode)JsonValue.Create(it)).ToArray());
				}
				return false;
			}
		}
		int block = -1;
		for (int i = 0; i < nodes.Count; i++)
		{
			if (Ctx(nodes[i]) == "Block" && (nodes[i]["data"]?["unlockedProgression"]?.ToString() ?? "") == section)
			{
				block = i;
				break;
			}
		}
		if (block < 0)
		{
			return false;
		}
		int start = block;
		while (start - 1 >= 0 && Ctx(nodes[start - 1]) == "Level")
		{
			start--;
		}
		if (block - start < 2)
		{
			return false;
		}
		long firstId = Num(nodes[start]["id"], -1L);
		long lastId = Num(nodes[block - 1]["id"], -1L);
		long current = Num(mainPath["currentNode"], 1L);
		if (firstId < 1 || current > lastId)
		{
			return false;
		}
		// le noeud puzzle suit le premier niveau de la section, ou le niveau en cours si le joueur est deja dans la section
		long after = Math.Max(firstId, current);
		int pos = start;
		while (pos < block && Num(nodes[pos]["id"], -1L) < after)
		{
			pos++;
		}
		start = pos;
		firstId = after;
		JsonObject puzzle = new JsonObject
		{
			["id"] = firstId + 1,
			["levelNumber"] = Math.Max(Num(nodes[start]["levelNumber"], 0L), 0L) + 1,
			["score"] = 0,
			["rented"] = false,
			["data"] = new JsonObject
			{
				["context"] = "EditorPuzzle",
				["minigameId"] = PuzzleId,
				["unlocked"] = true,
				["puzzleLimitedItems"] = new JsonArray(PuzzleItems.Select((string it) => (JsonNode)JsonValue.Create(it)).ToArray()),
				["dialogueIdentifiers"] = new JsonArray("puzzle_enigma_intro"),
				["dialogueTriggers"] = new JsonArray("LoopStart")
			}
		};
		for (int i = start + 1; i < nodes.Count; i++)
		{
			nodes[i]["id"] = Num(nodes[i]["id"], 0L) + 1;
			// le noeud puzzle prend un numero de niveau : les niveaux suivants decalent d'un cran (1, 2, 3... sans doublon)
			if (Num(nodes[i]["levelNumber"], -1L) > 0)
			{
				nodes[i]["levelNumber"] = Num(nodes[i]["levelNumber"], 0L) + 1;
			}
		}
		nodes.Insert(start + 1, puzzle);
		Console.WriteLine("[Path] Puzzle node added to the player's path after node " + firstId + ".");
		return true;
	}

	// Branche de 3 niveaux "Impossible" (bouton rouge "INSANE") : le jeu sait gerer les chemins secondaires mais la carte recreee
	// n'en contient aucun. Ajoutee au chemin ENREGISTRE, sur le premier niveau pas encore termine (a partir du noeud 4).
	public static bool InjectInsaneBranch(JsonArray paths)
	{
		if (!Settings.InsaneBranch)
		{
			return false;
		}
		JsonObject? main = null;
		foreach (JsonNode? p in paths)
		{
			if (p is JsonObject po && po["name"]?.ToString() == "MainPath")
			{
				main = po;
			}
			else if (p is JsonObject so && (so["name"]?.ToString() ?? "").EndsWith("_SidePath"))
			{
				return false;
			}
		}
		if (main == null || !(main["nodes"] is JsonArray nodes))
		{
			return false;
		}
		long current = long.TryParse(main["currentNode"]?.ToString(), out long cv) ? cv : 1L;
		JsonNode? attach = null;
		foreach (JsonNode? n in nodes)
		{
			long id = long.TryParse(n?["id"]?.ToString(), out long iv) ? iv : -1L;
			if (id >= Math.Max(current, 4L) && (n?["data"]?["context"]?.ToString() ?? "") == "Level")
			{
				attach = n;
				break;
			}
		}
		if (attach == null)
		{
			return false;
		}
		long start = long.Parse(attach["id"]!.ToString());
		JsonArray side = new JsonArray();
		for (int i = 1; i <= 3; i++)
		{
			side.Add(new JsonObject
			{
				["id"] = i,
				["levelNumber"] = i,
				["score"] = 0,
				["rented"] = false,
				["data"] = new JsonObject
				{
					["context"] = "Level",
					["minigameId"] = "",
					["unlocked"] = false,
					["levelDifficulty"] = "Impossible",
					["levelFeatures"] = null,
					["levelGameMode"] = "Race",
					["levelItems"] = null,
					["levelPlayerUnit"] = "Any"
				}
			});
		}
		paths.Add(new JsonObject
		{
			["name"] = start + "_SidePath",
			["currentNode"] = 0,
			["lane"] = 1,
			["startNode"] = start,
			["parent"] = "MainPath",
			["nodes"] = side
		});
		Console.WriteLine("[Path] Insane side path added after node " + start + ".");
		return true;
	}

	// Noeud puzzle tel que le jeu l'enregistre lui-meme dans le chemin
	public static JsonObject PuzzleNode(long id, long levelNumber)
	{
		return new JsonObject
		{
			["id"] = id,
			["levelNumber"] = levelNumber,
			["score"] = 0,
			["rented"] = false,
			["data"] = new JsonObject
			{
				["context"] = "EditorPuzzle",
				["minigameId"] = PuzzleId,
				["unlocked"] = true,
				["puzzleLimitedItems"] = new JsonArray(PuzzleItems.Select((string it) => (JsonNode)JsonValue.Create(it)).ToArray()),
				["dialogueIdentifiers"] = new JsonArray("puzzle_enigma_intro"),
				["dialogueTriggers"] = new JsonArray("LoopStart")
			}
		};
	}

	// Controle rapide (sans serveur) de la numerotation apres l'ajout du puzzle : --selftest-path
	public static int SelfTest()
	{
		int failures = 0;
		Settings.PuzzleSection = "unlock_powerups";
		foreach (long current in new long[] { 3, 45, 47, 48, 49, 50 })
		{
			JsonArray nodes = new JsonArray();
			for (int id = 1; id <= 44; id++)
			{
				nodes.Add(new JsonObject { ["id"] = id, ["levelNumber"] = id, ["data"] = new JsonObject { ["context"] = id == 44 ? "Block" : "Level", ["unlockedProgression"] = id == 44 ? "unlock_bike" : "" } });
			}
			for (int id = 45; id <= 48; id++)
			{
				nodes.Add(new JsonObject { ["id"] = id, ["levelNumber"] = id, ["data"] = new JsonObject { ["context"] = "Level" } });
			}
			nodes.Add(new JsonObject { ["id"] = 49, ["levelNumber"] = -1, ["data"] = new JsonObject { ["context"] = "Block", ["unlockedProgression"] = "unlock_powerups" } });
			JsonObject path = new JsonObject { ["name"] = "MainPath", ["currentNode"] = current, ["nodes"] = nodes };
			bool done = InjectPuzzle(path);
			JsonArray res = (JsonArray)path["nodes"]!;
			bool ok = true;
			int puzzleAt = -1;
			for (int i = 0; i < res.Count; i++)
			{
				if (res[i]!["id"]!.ToString() != (i + 1).ToString())
				{
					ok = false;
				}
				if (res[i]!["data"]!["context"]!.ToString() == "EditorPuzzle")
				{
					puzzleAt = i + 1;
				}
			}
			Console.WriteLine($"current={current}: injected={done} puzzleAt={puzzleAt} count={res.Count} idsOk={ok}");
			if (!ok)
			{
				failures++;
			}
		}
		// joueur neuf : chemin synthetique (3 niveaux + coffre), puzzle automatique
		Settings.PuzzleSection = "";
		JsonArray fresh = new JsonArray();
		for (int id = 1; id <= 3; id++)
		{
			fresh.Add(new JsonObject { ["id"] = id, ["levelNumber"] = id, ["data"] = new JsonObject { ["context"] = "Level" } });
		}
		fresh.Add(new JsonObject { ["id"] = 4, ["levelNumber"] = 0, ["data"] = new JsonObject { ["context"] = "Block", ["unlockedProgression"] = "unlock_car" } });
		JsonObject freshPath = new JsonObject { ["name"] = "MainPath", ["currentNode"] = 1, ["nodes"] = fresh };
		bool freshDone = InjectPuzzle(freshPath);
		string freshOrder = string.Join(",", ((JsonArray)freshPath["nodes"]!).Select((JsonNode? n) => n!["data"]!["context"]!.ToString() + n["id"]));
		Console.WriteLine($"fresh player: injected={freshDone} order={freshOrder} numbers={string.Join(",", ((JsonArray)freshPath["nodes"]!).Select((JsonNode? n) => n!["levelNumber"]!.ToString()))}");
		if (!freshDone)
		{
			failures++;
		}
		return failures;
	}

	// Le jeu genere parfois deux fois le meme noeud television de suite (meme nom de section) : on retire le doublon qui n'a pas encore
	// ete atteint, puis on renumerote les noeuds suivants.
	public static int RemoveDuplicateTv(JsonObject mainPath)
	{
		if (!(mainPath["nodes"] is JsonArray nodes) || nodes.Count < 2)
		{
			return 0;
		}
		long Num(JsonNode? n, long def)
		{
			return long.TryParse(n?.ToString(), out long v) ? v : def;
		}
		long current = Num(mainPath["currentNode"], 1L);
		int removed = 0;
		for (int i = nodes.Count - 1; i >= 1; i--)
		{
			string prog = nodes[i]?["data"]?["unlockedProgression"]?.ToString() ?? "";
			if (prog == "" || (nodes[i]?["data"]?["context"]?.ToString() ?? "") != "Signal" || (nodes[i - 1]?["data"]?["context"]?.ToString() ?? "") != "Signal" || (nodes[i - 1]?["data"]?["unlockedProgression"]?.ToString() ?? "") != prog)
			{
				continue;
			}
			long id = Num(nodes[i]["id"], -1L);
			if (id <= current)
			{
				continue;
			}
			nodes.RemoveAt(i);
			for (int k = i; k < nodes.Count; k++)
			{
				nodes[k]["id"] = Num(nodes[k]["id"], 0L) - 1;
			}
			removed++;
		}
		if (removed > 0)
		{
			Console.WriteLine("[Path] Removed " + removed + " duplicate TV node(s) from the player's path.");
		}
		return removed;
	}

	private static int NextId(JsonArray nodes)
	{
		int max = 0;
		foreach (JsonNode n in nodes)
		{
			max = Math.Max(max, n["id"].GetValue<int>());
		}
		return max + 1;
	}

	public static int AddVariety(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray nodes) || !(graph["edges"] is JsonArray edges))
		{
			return 0;
		}
		int count = 0;
		foreach (string section in ConstructionSections)
		{
			List<JsonNode> chain = SectionChain(nodes, edges, section);
			JsonNode? last = chain.LastOrDefault();
			if (last?["data"] is JsonObject data && (data["nodeType"]?.ToString() ?? "") == "Block" && (data["blockType"]?.ToString() ?? "") == "Chest")
			{
				data["blockType"] = "Construction";
				count++;
			}
		}
		if (Settings.BuilderMission)
		{
			List<JsonNode> chain = SectionChain(nodes, edges, MissionSection);
			if (chain.Count >= 3 && (chain[0]["data"]?["nodeType"]?.ToString() ?? "") == "Level" && (chain[1]["data"]?["nodeType"]?.ToString() ?? "") == "Level")
			{
				int firstId = chain[0]["id"].GetValue<int>();
				int secondId = chain[1]["id"].GetValue<int>();
				int templateNodeId = NextId(nodes);
				nodes.Add(new JsonObject
				{
					["id"] = templateNodeId,
					["name"] = "",
					["x"] = 0.0,
					["y"] = 0.0,
					["data"] = new JsonObject
					{
						["nodeType"] = "LevelTemplate",
						["levelTemplateIdentifier"] = TemplateIdentifier,
						["levelPlayerUnit"] = "OffroadCar",
						["levelGameMode"] = "Race",
						["levelTemplateDomeSize"] = "Medium",
						["levelTemplateItems"] = new JsonArray(),
						["levelTemplateMinigames"] = new JsonArray(TemplateId)
					}
				});
				int dialogueId = NextId(nodes);
				nodes.Add(new JsonObject
				{
					["id"] = dialogueId,
					["name"] = "",
					["x"] = 0.0,
					["y"] = 0.0,
					["data"] = new JsonObject
					{
						["nodeType"] = "Dialogue",
						["dialogueTrigger"] = "LoopStart",
						["dialogueCharacter"] = "Mechanic",
						["dialogueCharacterPosition"] = "Left",
						["dialogueText"] = "Time to build! Make a level with the items you have, then publish it to move on.",
						["dialogueProceed"] = "Let's build!",
						["dialogueCancel"] = "",
						["dialogueIdentifier"] = "builder_mission_intro"
					}
				});
				for (int i = edges.Count - 1; i >= 0; i--)
				{
					if (edges[i]["source"].GetValue<int>() == firstId && edges[i]["target"].GetValue<int>() == secondId)
					{
						edges.RemoveAt(i);
					}
				}
				edges.Add(new JsonObject { ["source"] = firstId, ["target"] = templateNodeId });
				edges.Add(new JsonObject { ["source"] = templateNodeId, ["target"] = secondId });
				edges.Add(new JsonObject { ["source"] = dialogueId, ["target"] = templateNodeId });
				count += 3;
			}
		}
		if (Settings.PuzzleMission)
		{
			List<JsonNode> chain = SectionChain(nodes, edges, Settings.PuzzleSection);
			if (chain.Count >= 3 && (chain[0]["data"]?["nodeType"]?.ToString() ?? "") == "Level" && (chain[1]["data"]?["nodeType"]?.ToString() ?? "") == "Level")
			{
				int firstId = chain[0]["id"].GetValue<int>();
				int secondId = chain[1]["id"].GetValue<int>();
				int puzzleNodeId = NextId(nodes);
				nodes.Add(new JsonObject
				{
					["id"] = puzzleNodeId,
					["name"] = "",
					["x"] = 0.0,
					["y"] = 0.0,
					["data"] = new JsonObject
					{
						["nodeType"] = "EditorPuzzle",
						["levelMinigameId"] = PuzzleId,
						["puzzleLimitedItems"] = new JsonArray(PuzzleItems.Select((string i) => (JsonNode)JsonValue.Create(i)).ToArray())
					}
				});
				int dialogueId = NextId(nodes);
				nodes.Add(new JsonObject
				{
					["id"] = dialogueId,
					["name"] = "",
					["x"] = 0.0,
					["y"] = 0.0,
					["data"] = new JsonObject
					{
						["nodeType"] = "Dialogue",
						["dialogueTrigger"] = "LoopStart",
						["dialogueCharacter"] = "Mechanic",
						["dialogueCharacterPosition"] = "Left",
						["dialogueText"] = "A puzzle! There are two gaps in the way. Fill them in with the ground you can draw, collect the stars and reach the goal to move on.",
						["dialogueProceed"] = "Let's solve it!",
						["dialogueCancel"] = "",
						["dialogueIdentifier"] = "puzzle_enigma_intro"
					}
				});
				for (int i = edges.Count - 1; i >= 0; i--)
				{
					if (edges[i]["source"].GetValue<int>() == firstId && edges[i]["target"].GetValue<int>() == secondId)
					{
						edges.RemoveAt(i);
					}
				}
				edges.Add(new JsonObject { ["source"] = firstId, ["target"] = puzzleNodeId });
				edges.Add(new JsonObject { ["source"] = puzzleNodeId, ["target"] = secondId });
				edges.Add(new JsonObject { ["source"] = dialogueId, ["target"] = puzzleNodeId });
				count += 3;
			}
		}
		// Le noeud puzzle est ajoute au chemin enregistre du joueur (InjectPuzzle) : son dialogue d'introduction doit exister dans le graphe
		// (dialogue sans lien d'entree = enregistre globalement par son identifiant), sinon toucher le noeud ne fait rien.
		if (Settings.PuzzleMission && !nodes.Any((JsonNode n) => (n?["data"]?["dialogueIdentifier"]?.ToString() ?? "") == "puzzle_enigma_intro"))
		{
			nodes.Add(new JsonObject
			{
				["id"] = NextId(nodes),
				["name"] = "",
				["x"] = 0.0,
				["y"] = 0.0,
				["data"] = new JsonObject
				{
					["nodeType"] = "Dialogue",
					["dialogueTrigger"] = "LoopStart",
					["dialogueCharacter"] = "Mechanic",
					["dialogueCharacterPosition"] = "Left",
					["dialogueText"] = "A puzzle! There are two gaps in the way. Fill them in with the ground you can draw, collect the stars and reach the goal to move on.",
					["dialogueProceed"] = "Let's solve it!",
					["dialogueCancel"] = "",
					["dialogueIdentifier"] = "puzzle_enigma_intro"
				}
			});
			count++;
		}
		return count;
	}
}
