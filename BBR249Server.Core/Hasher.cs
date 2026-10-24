using System;
using System.Security.Cryptography;
using System.Text;

namespace BBR249Server.Core;

public static class Hasher
{
	private sealed class UnityRandom
	{
		private uint x;

		private uint y;

		private uint z;

		private uint w;

		public UnityRandom(int seed)
		{
			x = (uint)seed;
			y = 1812433253 * x + 1;
			z = 1812433253 * y + 1;
			w = 1812433253 * z + 1;
		}

		private uint Next()
		{
			uint num = x ^ (x << 11);
			x = y;
			y = z;
			z = w;
			w = w ^ (w >> 19) ^ num ^ (num >> 8);
			return w;
		}

		public int Range(int min, int max)
		{
			if (max == min)
			{
				return min;
			}
			uint num = Next();
			if (max < min)
			{
				return (int)(min - num % (uint)(min - max));
			}
			return (int)(min + num % (uint)(max - min));
		}
	}

	public const string Hpw = "bfid3Z53SFib325PJGFasae";

	public const int Seed = 97139634;

	public static readonly string Gpw = Shuffle("bfid3Z53SFib325PJGFasae", 97139634);

	public static string Shuffle(string s, int seed)
	{
		UnityRandom unityRandom = new UnityRandom(seed);
		char[] array = s.ToCharArray();
		for (int num = array.Length; num > 1; num--)
		{
			int num2 = unityRandom.Range(0, num);
			ref char reference = ref array[num2];
			ref char reference2 = ref array[num - 1];
			char c = array[num - 1];
			char c2 = array[num2];
			reference = c;
			reference2 = c2;
		}
		return new string(array);
	}

	private static string Hex(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
		foreach (byte b in bytes)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	public static string HashString(string s)
	{
		return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(s + Gpw)));
	}

	public static string HashBytes(string uri, byte[] data)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(uri + Gpw);
		byte[] array = new byte[data.Length + bytes.Length];
		Buffer.BlockCopy(data, 0, array, 0, data.Length);
		Buffer.BlockCopy(bytes, 0, array, data.Length, bytes.Length);
		return Hex(SHA256.HashData(array));
	}

	public static string HashPlayer(string playerId, long coins, long diamonds, long bolts, long cups, long keys, long maxKeys, long rank)
	{
		return HashString(playerId + coins + diamonds + bolts + cups + keys + maxKeys + rank);
	}
}
