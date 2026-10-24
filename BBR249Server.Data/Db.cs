using System;
using System.Collections.Generic;
using System.Linq;
using BBR249Server.Core;
using Microsoft.Data.Sqlite;

namespace BBR249Server.Data;

public static class Db
{
	private static string ConnStr => "Data Source=" + Settings.DbFile + ";Pooling=True";

	public static SqliteConnection Open()
	{
		SqliteConnection sqliteConnection = new SqliteConnection(ConnStr);
		sqliteConnection.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=OFF;";
		sqliteCommand.ExecuteNonQuery();
		return sqliteConnection;
	}

	private static void Bind(SqliteCommand cmd, object?[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			cmd.Parameters.AddWithValue("$" + i, args[i] ?? DBNull.Value);
		}
	}

	public static long Exec(string sql, params object?[] args)
	{
		using SqliteConnection sqliteConnection = Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = sql;
		Bind(sqliteCommand, args);
		return sqliteCommand.ExecuteNonQuery();
	}

	public static List<Row> Query(string sql, params object?[] args)
	{
		using SqliteConnection sqliteConnection = Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = sql;
		Bind(sqliteCommand, args);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		List<Row> list = new List<Row>();
		while (sqliteDataReader.Read())
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
			for (int i = 0; i < sqliteDataReader.FieldCount; i++)
			{
				dictionary[sqliteDataReader.GetName(i)] = (sqliteDataReader.IsDBNull(i) ? null : sqliteDataReader.GetValue(i));
			}
			list.Add(new Row(dictionary));
		}
		return list;
	}

	public static Row? One(string sql, params object?[] args)
	{
		List<Row> list = Query(sql, args);
		if (list.Count <= 0)
		{
			return null;
		}
		return list[0];
	}

	public static void Init()
	{
		Exec("CREATE TABLE IF NOT EXISTS players (\r\n            id TEXT PRIMARY KEY,\r\n            name TEXT NOT NULL DEFAULT '',\r\n            coins INTEGER NOT NULL DEFAULT 1000,\r\n            diamonds INTEGER NOT NULL DEFAULT 50,\r\n            bolts INTEGER NOT NULL DEFAULT 0,\r\n            stars INTEGER NOT NULL DEFAULT 0,\r\n            keys INTEGER NOT NULL DEFAULT 5,\r\n            maxKeys INTEGER NOT NULL DEFAULT 5,\r\n            itemLevel INTEGER NOT NULL DEFAULT 0,\r\n            level INTEGER NOT NULL DEFAULT 1,\r\n            cups INTEGER NOT NULL DEFAULT 0,\r\n            rank INTEGER NOT NULL DEFAULT 0,\r\n            keyRefresh INTEGER NOT NULL DEFAULT 0,\r\n            upgrades TEXT NOT NULL DEFAULT '{}',\r\n            boosters TEXT NOT NULL DEFAULT '{}',\r\n            data TEXT NOT NULL DEFAULT '{}',\r\n            claimedTutorials TEXT NOT NULL DEFAULT '[]',\r\n            customisation TEXT NOT NULL DEFAULT '{}',\r\n            progression TEXT NOT NULL DEFAULT '{}',\r\n            facebookId TEXT NOT NULL DEFAULT '',\r\n            gameCenterId TEXT NOT NULL DEFAULT '',\r\n            countryCode TEXT NOT NULL DEFAULT 'US',\r\n            udid TEXT NOT NULL DEFAULT '',\r\n            lastPathSync TEXT NOT NULL DEFAULT '0',\r\n            createdAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,\r\n            lastLogin TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,\r\n            layoutVersion INTEGER NOT NULL DEFAULT 0,\r\n            achievements TEXT NOT NULL DEFAULT '{}'\r\n        )");
		Exec("CREATE TABLE IF NOT EXISTS minigames (\r\n            id TEXT PRIMARY KEY,\r\n            name TEXT NOT NULL DEFAULT 'Unnamed Track',\r\n            description TEXT NOT NULL DEFAULT '',\r\n            creatorId TEXT NOT NULL DEFAULT '',\r\n            creatorName TEXT NOT NULL DEFAULT '??',\r\n            creatorFacebookId TEXT NOT NULL DEFAULT '',\r\n            creatorGameCenterId TEXT NOT NULL DEFAULT '',\r\n            state TEXT NOT NULL DEFAULT 'saved',\r\n            gameMode TEXT NOT NULL DEFAULT 'Race',\r\n            playerUnit TEXT NOT NULL DEFAULT 'Any',\r\n            difficulty TEXT NOT NULL DEFAULT 'New',\r\n            clientVersion INTEGER NOT NULL DEFAULT 0,\r\n            gameQuality REAL NOT NULL DEFAULT 1.0,\r\n            levelRequirement INTEGER NOT NULL DEFAULT 0,\r\n            complexity INTEGER NOT NULL DEFAULT 0,\r\n            researchIdentifier TEXT NOT NULL DEFAULT '',\r\n            itemsUsed TEXT NOT NULL DEFAULT '[]',\r\n            creatorUpgrades TEXT NOT NULL DEFAULT '{}',\r\n            publishTime TEXT NOT NULL DEFAULT '',\r\n            timesPlayed INTEGER NOT NULL DEFAULT 0,\r\n            timesLiked INTEGER NOT NULL DEFAULT 0,\r\n            timesRated INTEGER NOT NULL DEFAULT 0,\r\n            bestTime INTEGER NOT NULL DEFAULT 0,\r\n            medianTime INTEGER NOT NULL DEFAULT 0,\r\n            totalWinners INTEGER NOT NULL DEFAULT 0,\r\n            oneStarWinners INTEGER NOT NULL DEFAULT 0,\r\n            twoStarWinners INTEGER NOT NULL DEFAULT 0,\r\n            threeStarWinners INTEGER NOT NULL DEFAULT 0,\r\n            participantCount INTEGER NOT NULL DEFAULT 0,\r\n            rewardCoins INTEGER NOT NULL DEFAULT 0,\r\n            totalCoinsEarned INTEGER NOT NULL DEFAULT 0,\r\n            editSessionCount INTEGER NOT NULL DEFAULT 0,\r\n            groundsModificationCount INTEGER NOT NULL DEFAULT 0,\r\n            itemsModificationCount INTEGER NOT NULL DEFAULT 0,\r\n            lastPlaySessionStartCount INTEGER NOT NULL DEFAULT 0,\r\n            timeSpentInEditMode INTEGER NOT NULL DEFAULT 0,\r\n            timeSpentEditing INTEGER NOT NULL DEFAULT 0,\r\n            template INTEGER NOT NULL DEFAULT 0,\r\n            levelData BLOB,\r\n            screenshot BLOB,\r\n            creatorGhost BLOB,\r\n            createdAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,\r\n            updatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP\r\n        )");
		Exec("CREATE TABLE IF NOT EXISTS scores (\r\n            gameId TEXT NOT NULL,\r\n            playerId TEXT NOT NULL,\r\n            name TEXT NOT NULL DEFAULT '',\r\n            time INTEGER NOT NULL DEFAULT 0,\r\n            stars INTEGER NOT NULL DEFAULT 0,\r\n            ghost BLOB,\r\n            createdAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,\r\n            PRIMARY KEY (gameId, playerId)\r\n        )");
		Exec("CREATE TABLE IF NOT EXISTS ratings (\r\n            gameId TEXT NOT NULL,\r\n            playerId TEXT NOT NULL,\r\n            rating TEXT NOT NULL DEFAULT 'Unrated',\r\n            PRIMARY KEY (gameId, playerId)\r\n        )");
		Exec("CREATE TABLE IF NOT EXISTS comments (\r\n            id INTEGER PRIMARY KEY AUTOINCREMENT,\r\n            gameId TEXT NOT NULL,\r\n            playerId TEXT NOT NULL,\r\n            name TEXT NOT NULL DEFAULT '',\r\n            comment TEXT NOT NULL DEFAULT '',\r\n            tag TEXT NOT NULL DEFAULT '',\r\n            createdAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP\r\n        )");
		Exec("CREATE TABLE IF NOT EXISTS follows (\r\n            followerId TEXT NOT NULL,\r\n            followeeId TEXT NOT NULL,\r\n            PRIMARY KEY (followerId, followeeId)\r\n        )");
		Exec("CREATE TABLE IF NOT EXISTS challenges (\r\n            id TEXT PRIMARY KEY,\r\n            kind TEXT NOT NULL,\r\n            gameId TEXT NOT NULL,\r\n            p1 TEXT NOT NULL,\r\n            p2 TEXT NOT NULL DEFAULT '',\r\n            challenged TEXT NOT NULL DEFAULT '',\r\n            status TEXT NOT NULL DEFAULT 'open',\r\n            currentPlayer TEXT NOT NULL DEFAULT '',\r\n            winner TEXT NOT NULL DEFAULT '',\r\n            t1 INTEGER NOT NULL DEFAULT 0,\r\n            t2 INTEGER NOT NULL DEFAULT 0,\r\n            stars1 INTEGER NOT NULL DEFAULT 0,\r\n            stars2 INTEGER NOT NULL DEFAULT 0,\r\n            tries1 INTEGER NOT NULL DEFAULT 0,\r\n            tries2 INTEGER NOT NULL DEFAULT 0,\r\n            ghost1 BLOB,\r\n            ghost2 BLOB,\r\n            claimed1 INTEGER NOT NULL DEFAULT 0,\r\n            claimed2 INTEGER NOT NULL DEFAULT 0,\r\n            comment TEXT NOT NULL DEFAULT '',\r\n            createdAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,\r\n            updatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP\r\n        )");
		// anciens records de chaque joueur (on garde les meilleurs : le ghost actuel est dans "scores")
		Exec("CREATE TABLE IF NOT EXISTS ghost_history (gameId TEXT NOT NULL, playerId TEXT NOT NULL, time INTEGER NOT NULL DEFAULT 0, stars INTEGER NOT NULL DEFAULT 0, ghost BLOB, createdAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)");
		Exec("CREATE INDEX IF NOT EXISTS idx_ghost_history ON ghost_history(gameId, playerId)");
		Exec("CREATE INDEX IF NOT EXISTS idx_challenges_p1 ON challenges(p1)");
		Exec("CREATE INDEX IF NOT EXISTS idx_challenges_p2 ON challenges(p2)");
		if (!Query("PRAGMA table_info(players)").Any((Row r) => r.S("name") == "layoutVersion"))
		{
			Exec("ALTER TABLE players ADD COLUMN layoutVersion INTEGER NOT NULL DEFAULT 0");
		}
		if (!Query("PRAGMA table_info(players)").Any((Row r) => r.S("name") == "achievements"))
		{
			Exec("ALTER TABLE players ADD COLUMN achievements TEXT NOT NULL DEFAULT '{}'");
		}
		Exec("CREATE INDEX IF NOT EXISTS idx_minigames_creator ON minigames(creatorId)");
		Exec("CREATE INDEX IF NOT EXISTS idx_minigames_state ON minigames(state)");
		Exec("CREATE INDEX IF NOT EXISTS idx_scores_game ON scores(gameId)");
	}
}
