using System;
using System.Collections.Generic;

namespace BBR249Server.Data;

public sealed class Row
{
	private readonly Dictionary<string, object?> _d;

	public Row(Dictionary<string, object?> d)
	{
		_d = d;
	}

	public bool Has(string k)
	{
		if (_d.TryGetValue(k, out object value))
		{
			return value != null;
		}
		return false;
	}

	public string S(string k, string def = "")
	{
		if (!_d.TryGetValue(k, out object value) || value == null)
		{
			return def;
		}
		return Convert.ToString(value) ?? def;
	}

	public long L(string k, long def = 0L)
	{
		if (!_d.TryGetValue(k, out object value) || value == null)
		{
			return def;
		}
		return Convert.ToInt64(value);
	}

	public int I(string k, int def = 0)
	{
		return (int)L(k, def);
	}

	public double D(string k, double def = 0.0)
	{
		if (!_d.TryGetValue(k, out object value) || value == null)
		{
			return def;
		}
		return Convert.ToDouble(value);
	}

	public byte[]? B(string k)
	{
		if (!_d.TryGetValue(k, out object value))
		{
			return null;
		}
		return value as byte[];
	}
}
