using BBR249Server.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace BBR249Server.Api;

public static class UnlockBuilder
{
	public const string FallbackUnlockName = "unlock_car";

	private static readonly string[] ItemTypes = new string[3] { "Unlockable", "UnlockableSimple", "UnlockableUpgradeable" };

	private static string _firstUnlockName = "unlock_car";

	public static string FirstUnlockName => _firstUnlockName;

	// Le client 2.4.9 ne connait que les modes Race et StarCollect pour les niveaux de joueur (enum PsGameMode).
	// La carte contient aussi une carte "GameModeSignal" : la toucher dans l'editeur plante le jeu
	// (Enum.Parse "Signal"). On retire du graphe tout mode de jeu non gere, ainsi que ses liens.
	public static int RemoveUnsupportedGameModes(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray jsonArray) || !(graph["edges"] is JsonArray jsonArray2))
		{
			return 0;
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (JsonNode item in jsonArray)
		{
			string text = item?["data"]?["itemClass"]?.ToString() ?? "";
			if (text.StartsWith("GameMode") && text != "GameModeRace" && text != "GameModeStarCollect")
			{
				hashSet.Add(item["id"].GetValue<int>());
			}
		}
		if (hashSet.Count == 0)
		{
			return 0;
		}
		for (int num = jsonArray.Count - 1; num >= 0; num--)
		{
			if (hashSet.Contains(jsonArray[num]["id"].GetValue<int>()))
			{
				jsonArray.RemoveAt(num);
			}
		}
		for (int num2 = jsonArray2.Count - 1; num2 >= 0; num2--)
		{
			if (hashSet.Contains(jsonArray2[num2]["source"].GetValue<int>()) || hashSet.Contains(jsonArray2[num2]["target"].GetValue<int>()))
			{
				jsonArray2.RemoveAt(num2);
			}
		}
		return hashSet.Count;
	}

	// Vrai si le noeud appartient a une section dont le nom est une sequence "cinematique seule" (on remonte les liens jusqu'au bloc Unlock).
	private static bool CutsceneSection(JsonArray nodes, JsonArray edges, int nodeId)
	{
		Dictionary<int, JsonNode> byId = new Dictionary<int, JsonNode>();
		foreach (JsonNode n in nodes)
		{
			byId[n["id"].GetValue<int>()] = n;
		}
		int current = nodeId;
		for (int guard = 0; guard < 200; guard++)
		{
			int prev = -1;
			foreach (JsonNode e in edges)
			{
				if (e["target"].GetValue<int>() == current && byId.TryGetValue(e["source"].GetValue<int>(), out JsonNode? src))
				{
					string st = src["data"]?["nodeType"]?.ToString() ?? "";
					if (st == "Unlock" || st == "Level" || st == "Block" || st == "Signal" || st == "EditorPuzzle" || st == "LevelTemplate" || st == "VersusRace")
					{
						prev = e["source"].GetValue<int>();
						break;
					}
				}
			}
			if (prev < 0)
			{
				return false;
			}
			JsonNode pn = byId[prev];
			if ((pn["data"]?["nodeType"]?.ToString() ?? "") == "Unlock")
			{
				return CutsceneNames.Contains(pn["name"]?.ToString() ?? "");
			}
			current = prev;
		}
		return false;
	}

	// Le jeu retient les sections deja passees d'apres le NOM des noeuds du chemin (un seul noeud par section porte le nom de sa section).
	// Renommer un noeud television sans renommer sa section ferait croire au jeu que la section n'est pas passee : au bout du chemin il
	// repartirait du debut. On renomme donc aussi les blocs "Unlock" du graphe, avec les memes noms que ceux donnes aux noeuds.
	public static int RenameTvSections(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray nodes))
		{
			return 0;
		}
		int count = 0;
		foreach (JsonNode item in nodes)
		{
			if ((item?["data"]?["nodeType"]?.ToString() ?? "") != "Unlock" || item["data"] is not JsonObject data)
			{
				continue;
			}
			string name = item["name"]?.ToString() ?? "";
			if (name == "unlock_begin" || !TvSequenceNames.TryGetValue(name, out string? newName))
			{
				continue;
			}
			item["name"] = newName;
			foreach (string key in data.Select<KeyValuePair<string, JsonNode>, string>((KeyValuePair<string, JsonNode> kv) => kv.Key).ToList())
			{
				if (data[key] is JsonValue jv && jv.TryGetValue<string>(out string? value) && value == name)
				{
					data[key] = newName;
				}
			}
			count++;
		}
		return count;
	}

	// Noeuds "Signal" (antenne / television) : le client non modifie ne sait pas les jouer dans deux cas.
	//  - antenne SANS niveau : le jeu ouvre la scene de jeu sans niveau puis plante (GameStartState / menu en jeu sans niveau)
	//  - dialogue "MinigameEnter" sur une antenne : plante l'effet de television avant qu'il soit initialise
	// On donne donc a chaque antenne un identifiant de niveau officiel (le serveur sert un niveau de remplacement, voir
	// MinigameApi.GetOrStandIn) et on retire les dialogues MinigameEnter. Le chemin joue est alors celui prevu par le jeu.
	public static int FixSignalNodes(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray jsonArray) || !(graph["edges"] is JsonArray jsonArray2))
		{
			return 0;
		}
		int num = 0;
		HashSet<int> hashSet = new HashSet<int>();
		foreach (JsonNode item in jsonArray)
		{
			if (!(item?["data"] is JsonObject jsonObject))
			{
				continue;
			}
			string text = jsonObject["nodeType"]?.ToString() ?? "";
			if (text == "Dialogue" && (jsonObject["dialogueTrigger"]?.ToString() ?? "") == "MinigameEnter")
			{
				hashSet.Add(item["id"].GetValue<int>());
			}
			else if (text == "Block" && (jsonObject["blockType"]?.ToString() ?? "") == "Antenna" && (jsonObject["levelMinigameId"]?.ToString() ?? "") == "" && !CutsceneSection(jsonArray, jsonArray2, item["id"].GetValue<int>()))
			{
				jsonObject["levelMinigameId"] = "5b1d0c" + item["id"].GetValue<int>().ToString("x6") + "0000" + "7a" + "5e1f00";
				num++;
			}
		}
		for (int num2 = jsonArray.Count - 1; num2 >= 0; num2--)
		{
			if (hashSet.Contains(jsonArray[num2]["id"].GetValue<int>()))
			{
				jsonArray.RemoveAt(num2);
			}
		}
		for (int num3 = jsonArray2.Count - 1; num3 >= 0; num3--)
		{
			if (hashSet.Contains(jsonArray2[num3]["source"].GetValue<int>()) || hashSet.Contains(jsonArray2[num3]["target"].GetValue<int>()))
			{
				jsonArray2.RemoveAt(num3);
			}
		}
		foreach (JsonNode item in jsonArray)
		{
			string t = item?["data"]?["nodeType"]?.ToString() ?? "";
			bool antenna = t == "Block" && (item["data"]["blockType"]?.ToString() ?? "") == "Antenna";
			if ((t == "Signal" || antenna) && (item["data"]["levelMinigameId"]?.ToString() ?? "") != "" && CutsceneSection(jsonArray, jsonArray2, item["id"].GetValue<int>()))
			{
				item["data"]["levelMinigameId"] = "";
				num++;
			}
		}
		num += AddTvDialogues(jsonArray);
		return num + hashSet.Count;
	}

	// Dialogues des sequences "television" que le jeu sait jouer avec une vraie image (au lieu du gris de la sequence par defaut).
	// Un dialogue sans entree dans le graphe est enregistre par le jeu sous son identifiant ; il en faut un par sequence.
	private static readonly (string Id, string Text, string Proceed)[] TvDialogues = new (string, string, string)[]
	{
		("treadmill_tv_dialogue_1", "Meh, next channel.", "OK"),
		("unlock_barrel_dialogue_1", "Whoa, something is blowing up on this channel!", "Cool!"),
		("unlock_boulder_dialogue_1", "Look at those rocks! Let's give this one a try.", "Rock on!"),
		("unlock_airbomb_dialogue_1", "Incoming! Bombs from above!", "Duck!"),
		("unlock_lava_dialogue_1", "Whoa, that channel is way too hot for me. Next!", "Phew!")
	};

	private static int AddTvDialogues(JsonArray nodes)
	{
		int added = 0;
		int nextId = 0;
		HashSet<string> existing = new HashSet<string>();
		foreach (JsonNode item in nodes)
		{
			nextId = System.Math.Max(nextId, item?["id"]?.GetValue<int>() ?? 0);
			string ident = item?["data"]?["dialogueIdentifier"]?.ToString() ?? "";
			if (ident != "")
			{
				existing.Add(ident);
			}
		}
		foreach ((string Id, string Text, string Proceed) dlg in TvDialogues)
		{
			if (existing.Contains(dlg.Id))
			{
				continue;
			}
			nodes.Add(new JsonObject
			{
				["id"] = ++nextId,
				["name"] = "",
				["x"] = 0.0,
				["y"] = 0.0,
				["data"] = new JsonObject
				{
					["nodeType"] = "Dialogue",
					["dialogueTrigger"] = "Manual",
					["dialogueCharacter"] = "Scientist",
					["dialogueCharacterPosition"] = "Right",
					["dialogueText"] = dlg.Text,
					["dialogueProceed"] = dlg.Proceed,
					["dialogueCancel"] = "",
					["dialogueIdentifier"] = dlg.Id
				}
			});
			added++;
		}
		return added;
	}

	// Noeuds television qui tomberaient sur la sequence par defaut (ecran gris de 2 secondes) : on leur donne le nom d'une sequence
	// qui a une image. Seul le nom du noeud change dans le chemin enregistre ; les noms des blocs "Unlock" du graphe restent intacts
	// (le jeu s'en sert pour enchainer les sections et pour certains tutoriels).
	// Sequences television qui se terminent par un retour direct a la carte (sans course) : le jeu les a prevues pour des noeuds SANS niveau.
	// Avec un niveau, l'etat de camera du jeu plante a chaque image apres le retour (le jeu gele). Ces noeuds doivent donc rester sans niveau.
	private static readonly HashSet<string> CutsceneNames = new HashSet<string>
	{
		"unlock_treadmill_signal", "unlock_barrel", "unlock_boulder", "unlock_airbomb", "unlock_lava", "unlock_timeattack_signal", "unlock_dynamics_signal"
	};

	private static readonly Dictionary<string, string> TvSequenceNames = new Dictionary<string, string>
	{
		["unlock_begin"] = "unlock_start",
		["unlock_upgrade"] = "unlock_treadmill_signal",
		["unlock_editor"] = "unlock_barrel",
		["unlock_timeattack"] = "unlock_boulder",
		["unlock_bike_signal"] = "unlock_airbomb",
		["unlock_rentals"] = "unlock_lava"
	};

	private static long AsLongLocal(JsonNode? n)
	{
		return long.TryParse(n?.ToString(), out long v) ? v : -1L;
	}

	// Meme correctif pour le chemin principal ENREGISTRE du joueur (c'est lui que le client utilise, pas le graphe) :
	// antenne sans niveau -> identifiant de niveau officiel (remplace par un niveau de remplacement), et on retire le
	// declencheur de dialogue "MinigameEnter" (avec son identifiant, meme position dans la liste).
	// Dialogues de test ("start", "claimed", "released"...) accroches au garage dans les donnees recreees : jamais vus dans le vrai jeu.
	private static bool IsTestDialogue(string? identifier)
	{
		return (identifier ?? "").StartsWith("test_");
	}

	public static int RemoveTestDialogues(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray nodes) || !(graph["edges"] is JsonArray edges))
		{
			return 0;
		}
		HashSet<int> ids = new HashSet<int>();
		for (int i = nodes.Count - 1; i >= 0; i--)
		{
			if ((nodes[i]?["data"]?["nodeType"]?.ToString() ?? "") == "Dialogue" && IsTestDialogue(nodes[i]?["data"]?["dialogueIdentifier"]?.ToString()))
			{
				ids.Add(nodes[i]["id"].GetValue<int>());
				nodes.RemoveAt(i);
			}
		}
		for (int i = edges.Count - 1; i >= 0; i--)
		{
			if (ids.Contains(edges[i]["source"].GetValue<int>()) || ids.Contains(edges[i]["target"].GetValue<int>()))
			{
				edges.RemoveAt(i);
			}
		}
		return ids.Count;
	}

	private static void StripTestDialogues(JsonObject data)
	{
		if (!(data["dialogueIdentifiers"] is JsonArray idents) || !(data["dialogueTriggers"] is JsonArray trigs) || idents.Count != trigs.Count)
		{
			return;
		}
		for (int i = idents.Count - 1; i >= 0; i--)
		{
			if (IsTestDialogue(idents[i]?.ToString()))
			{
				idents.RemoveAt(i);
				trigs.RemoveAt(i);
			}
		}
		if (idents.Count == 0)
		{
			data.Remove("dialogueIdentifiers");
			data.Remove("dialogueTriggers");
		}
	}

	public static int FixStoredSignalNodes(JsonArray? nodes)
	{
		if (nodes == null)
		{
			return 0;
		}
		int num = 0;
		foreach (JsonNode tdItem in nodes)
		{
			if (tdItem?["data"] is JsonObject tdData)
			{
				StripTestDialogues(tdData);
			}
		}
		// tous les noeuds (pas seulement les noeuds television) suivent le renommage des sections du graphe
		foreach (JsonNode item in nodes)
		{
			if (!(item?["data"] is JsonObject any))
			{
				continue;
			}
			string pName = any["unlockedProgression"]?.ToString() ?? "";
			bool isSignal = (any["context"]?.ToString() ?? "") == "Signal";
			if (TvSequenceNames.TryGetValue(pName, out string? renamed) && (pName != "unlock_begin" || isSignal))
			{
				any["unlockedProgression"] = renamed;
				num++;
			}
			string wName = any["willUnlockProgression"]?.ToString() ?? "";
			if (wName != "unlock_begin" && TvSequenceNames.TryGetValue(wName, out string? wRenamed))
			{
				any["willUnlockProgression"] = wRenamed;
			}
		}
		// L'introduction (noeud Signal "unlock_start") ne marque plus la section "unlock_begin" comme passee : c'est le coffre de depart
		// (juste avant) qui doit porter ce nom, sinon le jeu repartirait du debut au bout du chemin.
		int introId = int.MaxValue;
		bool hasBegin = false;
		foreach (JsonNode item in nodes)
		{
			string nm = item?["data"]?["unlockedProgression"]?.ToString() ?? "";
			int nid = (int)AsLongLocal(item?["id"]);
			if (nm == "unlock_begin")
			{
				hasBegin = true;
			}
			else if (nm == "unlock_start" && (item?["data"]?["context"]?.ToString() ?? "") == "Signal" && nid < introId)
			{
				introId = nid;
			}
		}
		if (!hasBegin && introId != int.MaxValue)
		{
			JsonNode? chest = null;
			foreach (JsonNode item in nodes)
			{
				int nid = (int)AsLongLocal(item?["id"]);
				if (nid < introId && (item?["data"]?["context"]?.ToString() ?? "") == "Block" && (item["data"]["unlockedProgression"]?.ToString() ?? "") == "unlock_car" && (chest == null || nid > (int)AsLongLocal(chest["id"])))
				{
					chest = item;
				}
			}
			if (chest?["data"] is JsonObject chestData)
			{
				chestData["unlockedProgression"] = "unlock_begin";
				num++;
			}
		}
		foreach (JsonNode item in nodes)
		{
			if (!(item?["data"] is JsonObject jsonObject) || (jsonObject["context"]?.ToString() ?? "") != "Signal")
			{
				continue;
			}
			string progression = jsonObject["unlockedProgression"]?.ToString() ?? "";
			if (CutsceneNames.Contains(jsonObject["unlockedProgression"]?.ToString() ?? ""))
			{
				if ((jsonObject["minigameId"]?.ToString() ?? "") != "")
				{
					jsonObject["minigameId"] = "";
					num++;
				}
			}
			else if ((jsonObject["minigameId"]?.ToString() ?? "") == "" && int.TryParse(item["id"]?.ToString(), out var id))
			{
				jsonObject["minigameId"] = "5b1d0c" + id.ToString("x6") + "0000" + "7a" + "5e1f01";
				num++;
			}
			if (jsonObject["dialogueTriggers"] is JsonArray triggers)
			{
				JsonArray ids = jsonObject["dialogueIdentifiers"] as JsonArray;
				for (int i = triggers.Count - 1; i >= 0; i--)
				{
					if (triggers[i]?.ToString() == "MinigameEnter")
					{
						triggers.RemoveAt(i);
						if (ids != null && i < ids.Count)
						{
							ids.RemoveAt(i);
						}
						num++;
					}
				}
			}
		}
		return num;
	}

	// Le client 2.4.9 n'a pas de table de traduction pour ces cles : il afficherait "EDITOR_GUI_VEHICLE_NAME_CAR" tel quel.
	private static readonly Dictionary<string, string> TextKeys = new Dictionary<string, string>
	{
		["EDITOR_GUI_VEHICLE_NAME_CAR"] = "Offroader",
		["EDITOR_GUI_VEHICLE_DESC_CAR"] = "Big wheels, big power. Crushes anything in its way!",
		["EDITOR_GUI_VEHICLE_NAME_BIKE"] = "Dirtbike",
		["EDITOR_GUI_VEHICLE_DESC_BIKE"] = "Light and nimble. Perfect for jumps and tight turns!"
	};

	public static int FixTextKeys(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray jsonArray))
		{
			return 0;
		}
		int num = 0;
		foreach (JsonNode item in jsonArray)
		{
			if (!(item?["data"] is JsonObject jsonObject))
			{
				continue;
			}
			foreach (string key in new string[2] { "itemName", "itemDescription" })
			{
				string text = jsonObject[key]?.ToString() ?? "";
				if (text.StartsWith("EDITOR_GUI_"))
				{
					jsonObject[key] = TextKeys.TryGetValue(text, out string value) ? value : text.Substring("EDITOR_GUI_".Length).Replace('_', ' ').ToLowerInvariant();
					num++;
				}
			}
		}
		return num;
	}

	// Donnees de la carte ajustees cote serveur (marche donc avec n'importe quel client, patche ou non) :
	//  - la categorie d'objets "DELETE LATER" s'appelle "Dev Items"
	//  - les objets de decor n'avaient pas d'icone dans les donnees recreees (le jeu cherchait l'image "" -> erreur) : on donne
	//    le nom des images deja presentes dans l'atlas du jeu
	//  - la caisse en bois "PsWoodenBlock" prend l'icone du carton (la caisse dure garde celle de la caisse en bois)
	public static int FixDevItemData(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray jsonArray))
		{
			return 0;
		}
		System.Collections.Generic.Dictionary<string, string> icons = new System.Collections.Generic.Dictionary<string, string>
		{
			["DecorationalTree"] = "item_deco_trees",
			["DecorationalSign"] = "item_deco_signs",
			["DecorationalRock"] = "item_deco_rocks",
			["DecorationalPyrotechnics"] = "item_pyro_confetti",
			["DecorationalPyroDragon"] = "item_pyro_fire",
			["DecorationalPyroChickoon"] = "item_pyro_chicken"
		};
		int num = 0;
		foreach (JsonNode item in jsonArray)
		{
			if (!(item?["data"] is JsonObject d))
			{
				continue;
			}
			if (d["itemName"]?.ToString() == "DELETE LATER")
			{
				d["itemName"] = "Dev Items";
				num++;
			}
			string cls = d["itemClass"]?.ToString() ?? "";
			if (icons.TryGetValue(cls, out string icon) && string.IsNullOrEmpty(d["itemIcon"]?.ToString()))
			{
				d["itemIcon"] = icon;
				num++;
			}
			if (cls == "PsWoodenBlock" && d["itemIcon"]?.ToString() != "item_carboardbox_icon")
			{
				d["itemIcon"] = "item_carboardbox_icon";
				num++;
			}
			// Un objet sans icone fait planter l'ecran "New items" (le jeu cherche une image au nom vide) : il lui faut toujours une icone
			// qui existe dans l'atlas du jeu d'origine. "Desert" (classe vide) et le capteur d'objectif n'en avaient pas.
			string nodeType = d["nodeType"]?.ToString() ?? "";
			if (ItemTypes.Contains(nodeType) && string.IsNullOrEmpty(d["itemIcon"]?.ToString()))
			{
				d["itemIcon"] = cls == "SensorGoal" ? "item_forcefield" : "item_dome_empty_icon";
				num++;
			}
		}
		return num;
	}

	public static int DisableTutorialStart(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray jsonArray))
		{
			return 0;
		}
		int num = 0;
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonObject jsonObject && jsonObject["name"]?.ToString() == "unlock_start")
			{
				jsonObject["name"] = "unlock_begin";
				num++;
			}
			if (!(item?["data"] is JsonObject jsonObject2))
			{
				continue;
			}
			foreach (string item2 in jsonObject2.Select<KeyValuePair<string, JsonNode>, string>((KeyValuePair<string, JsonNode> kv) => kv.Key).ToList())
			{
				if (jsonObject2[item2] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value) && value == "unlock_start")
				{
					jsonObject2[item2] = "unlock_begin";
					num++;
				}
			}
		}
		return num;
	}

	public static int LinkAllItemsToFirstUnlock(JsonObject graph)
	{
		if (!(graph["nodes"] is JsonArray jsonArray) || !(graph["edges"] is JsonArray jsonArray2))
		{
			return 0;
		}
		int? num = null;
		foreach (JsonNode item in jsonArray)
		{
			if (!(item?["data"]?["nodeType"]?.ToString() != "Unlock"))
			{
				string text = item["data"]["itemClass"]?.ToString() ?? "";
				if (!(text == ""))
				{
					num = item["id"].GetValue<int>();
					_firstUnlockName = text;
					break;
				}
			}
		}
		if (!num.HasValue)
		{
			return 0;
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (JsonNode item2 in jsonArray2)
		{
			if (item2["target"].GetValue<int>() == num)
			{
				hashSet.Add(item2["source"].GetValue<int>());
			}
		}
		int num2 = 0;
		foreach (JsonNode item3 in jsonArray)
		{
			string value = item3?["data"]?["nodeType"]?.ToString() ?? "";
			if (ItemTypes.Contains(value))
			{
				int value2 = item3["id"].GetValue<int>();
				if (!hashSet.Contains(value2))
				{
					jsonArray2.Add(new JsonObject
					{
						["target"] = num.Value,
						["source"] = value2
					});
					num2++;
				}
			}
		}
		return num2;
	}

	private static JsonObject LevelNode(int id, int levelNumber)
	{
		return new JsonObject
		{
			["id"] = id,
			["levelNumber"] = levelNumber,
			["score"] = 0,
			["rented"] = false,
			["data"] = new JsonObject
			{
				["context"] = "Level",
				["minigameId"] = "",
				["unlocked"] = true,
				["levelGameMode"] = "Race",
				["levelPlayerUnit"] = "Any"
			}
		};
	}

	public static JsonObject SyntheticMainPath()
	{
		JsonObject path = SyntheticMainPathBase();
		// joueur neuf : le puzzle est le 2e noeud (niveau 1, puzzle, niveau 2, niveau 3, coffre) ; les numeros de niveau suivent
		if (Settings.PuzzleMission && Settings.PuzzleSection == "" && path["nodes"] is JsonArray nodes && nodes.Count == 4)
		{
			nodes.Insert(1, MapContent.PuzzleNode(2, 2));
			nodes[2]["id"] = 3;
			nodes[2]["levelNumber"] = 3;
			nodes[3]["id"] = 4;
			nodes[3]["levelNumber"] = 4;
			nodes[4]["id"] = 5;
		}
		// Le premier noeud television (l'introduction) est fourni DEJA enregistre sous son nom de sequence ("unlock_start") :
		// si le jeu le creait lui-meme, il lui donnerait le nom de la section ("unlock_begin") et la sequence serait un ecran gris.
		if (path["nodes"] is JsonArray all && all.Count >= 4)
		{
			all.Add(new JsonObject
			{
				["id"] = all.Count + 1,
				["levelNumber"] = -1,
				["score"] = 0,
				["rented"] = true,
				["data"] = new JsonObject
				{
					["context"] = "Signal",
					["requiredStars"] = 0,
					["unlocked"] = false,
					["minigameId"] = "5549c475310000f82c7490fb",
					["unlockedProgression"] = "unlock_start",
					["willUnlockProgression"] = "",
					["blockCoins"] = 0,
					["blockDiamonds"] = 0,
					["blockKeys"] = 0,
					["blockBolts"] = 0,
					["blockType"] = "Antenna",
					["dialogueIdentifiers"] = new JsonArray("unlock_1_quest_2"),
					["dialogueTriggers"] = new JsonArray("LoopClaimed")
				}
			});
		}
		return path;
	}

	private static JsonObject SyntheticMainPathBase()
	{
		return new JsonObject
		{
			["name"] = "MainPath",
			["currentNode"] = 1,
			["nodes"] = new JsonArray
			{
				LevelNode(1, 1),
				LevelNode(2, 2),
				LevelNode(3, 3),
				new JsonObject
				{
					["id"] = 4,
					["levelNumber"] = 0,
					["score"] = 0,
					["rented"] = false,
					["data"] = new JsonObject
					{
						["context"] = "Block",
						["minigameId"] = "",
						["unlocked"] = false,
						["blockType"] = "Chest",
						["requiredStars"] = 0,
						["unlockedProgression"] = _firstUnlockName,
						["willUnlockProgression"] = "",
						// recompense du premier coffre : sans pieces ni gemmes, le jeu n'affiche pas l'animation d'ouverture
						["blockCoins"] = 250,
						["blockDiamonds"] = 20,
						["blockKeys"] = 0,
						["blockBolts"] = 0
					}
				}
			}
		};
	}
}
