using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BBR249Server.Net;

namespace BBR249Server.Api;

// Routes que le client 2.4.9 reference mais qu'aucun ecran n'atteint (tournois, reseaux sociaux, achats, notifications, recherche,
// jetons push, code mort...). Elles sont declarees ici avec une reponse vide mais valide, par securite : le jeu ne reçoit jamais
// d'erreur et le journal ne signale plus "not implemented".
public static class LeftoverApi
{
	private static readonly string[] Empty = new string[]
	{
		"/v1/analytics/trackPurchase", "/v1/customisation/add", "/v1/iap/config", "/v1/iap/purchase", "/v1/minigame/level/update",
		"/v1/minigame/like/save", "/v1/path/get", "/v1/planet/current/find", "/v1/player/check/social", "/v1/player/data/find",
		"/v1/player/data/setNumber", "/v1/player/data/setString", "/v1/player/follow/gameCenter", "/v1/player/follow/social",
		"/v1/player/join/ninja", "/v1/player/remove/social", "/v1/player/rent", "/v1/player/solve/social", "/v1/player/stats/change",
		"/v1/player/stats/set", "/v1/player/tester/purchase", "/v1/player/tester/setMax", "/v1/player/tester/use",
		"/v1/push/token/remove", "/v1/push/token/replace", "/v1/push/token/save", "/v1/research/cancel", "/v1/research/claim",
		"/v1/research/purchase", "/v1/research/start", "/v1/tournament/claim", "/v1/tournament/create", "/v1/tournament/score/next",
		"/v1/tournament/unlock", "/v2/path/db/update", "/v2/player/check/gameCenter", "/v2/player/join/facebook",
		"/v2/player/join/gameCenter", "/v2/tournament/score/send"
	};

	private static readonly string[] EmptyLists = new string[] { "/v1/notification/find", "/v2/tournament/score/find" };

	public static void Register()
	{
		foreach (string route in Empty)
		{
			Router.Map(route, (Ctx c) => c.Json(new JsonObject()));
		}
		foreach (string route in EmptyLists)
		{
			Router.Map(route, (Ctx c) => c.Json(new JsonObject { ["data"] = new JsonArray() }));
		}
		Router.Map("/v1/notification/count", (Ctx c) => c.Json(new JsonObject { ["count"] = 0 }));
	}
}
