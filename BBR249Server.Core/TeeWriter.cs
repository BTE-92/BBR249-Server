using System;
using System.IO;
using System.Text;

namespace BBR249Server.Core;

public sealed class TeeWriter : TextWriter
{
	private readonly TextWriter _a;

	private readonly TextWriter _b;

	private readonly object _lock = new object();

	public override Encoding Encoding => _a.Encoding;

	public TeeWriter(TextWriter a, TextWriter b)
	{
		_a = a;
		_b = b;
	}

	public override void Write(char value)
	{
		lock (_lock)
		{
			_a.Write(value);
			_b.Write(value);
		}
	}

	public override void Write(string? value)
	{
		lock (_lock)
		{
			_a.Write(value);
			_b.Write(value);
		}
	}

	public override void WriteLine(string? value)
	{
		lock (_lock)
		{
			string text = DateTime.Now.ToString("HH:mm:ss.fff");
			_a.WriteLine(value);
			_b.WriteLine(text + " " + value);
		}
	}
}
