using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace BBR249Server.Core;

public static class FilePacker
{
	public static byte[] ZipBytes(byte[] input)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (ZipArchive zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
		{
			ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry("LevelData", CompressionLevel.Optimal);
			using Stream stream = zipArchiveEntry.Open();
			stream.Write(input, 0, input.Length);
		}
		byte[] array = memoryStream.ToArray();
		byte[] array2 = new byte[4 + array.Length];
		BitConverter.GetBytes(input.Length).CopyTo(array2, 0);
		Buffer.BlockCopy(array, 0, array2, 4, array.Length);
		return array2;
	}

	public static byte[] UnZipBytes(byte[] input)
	{
		int num = BitConverter.ToInt32(input, 0);
		using MemoryStream stream = new MemoryStream(input, 4, input.Length - 4);
		using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
		using Stream stream2 = zipArchive.Entries[0].Open();
		byte[] array = new byte[num];
		int num2;
		for (int i = 0; i < num; i += num2)
		{
			num2 = stream2.Read(array, i, num - i);
			if (num2 <= 0)
			{
				break;
			}
		}
		return array;
	}

	public static (byte[] Body, string FileSizes) Combine(params byte[][] parts)
	{
		int num = parts.Sum((byte[] p) => p.Length);
		byte[] array = new byte[num];
		int num2 = 0;
		foreach (byte[] array2 in parts)
		{
			Buffer.BlockCopy(array2, 0, array, num2, array2.Length);
			num2 += array2.Length;
		}
		return (Body: array, FileSizes: string.Join(",", parts.Select((byte[] p) => p.Length)));
	}

	public static List<byte[]> Split(byte[] body, string? fileSizes)
	{
		List<byte[]> list = new List<byte[]>();
		if (string.IsNullOrWhiteSpace(fileSizes))
		{
			list.Add(body);
			return list;
		}
		int num = 0;
		string[] array = fileSizes.Split(',', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (!int.TryParse(text.Trim(), out var result) || result < 0 || num + result > body.Length)
			{
				break;
			}
			byte[] array2 = new byte[result];
			Buffer.BlockCopy(body, num, array2, 0, result);
			list.Add(array2);
			num += result;
		}
		return list;
	}

	public static byte[] Utf8(string s)
	{
		return Encoding.UTF8.GetBytes(s);
	}
}
