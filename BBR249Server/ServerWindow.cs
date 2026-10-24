using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using BBR249Server.Core;
using BBR249Server.Data;

namespace BBR249Server;

// Etat du serveur, lu par la fenetre.
public static class ServerInfo
{
	public static volatile string State = "Starting";

	public static volatile string Ip = "";

	public static volatile string Error = "";
}

// Recupere ce que le serveur ecrit dans la console pour l'afficher dans la fenetre.
public sealed class GuiLogWriter : TextWriter
{
	public static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();

	public override Encoding Encoding => Encoding.UTF8;

	public override void Write(char value)
	{
	}

	public override void Write(string? value)
	{
	}

	public override void WriteLine(string? value)
	{
		Lines.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + value);
	}
}

public sealed class ServerWindow : Form
{
	private static readonly Color CBack = Color.FromArgb(24, 26, 32);

	private static readonly Color CPanel = Color.FromArgb(34, 37, 46);

	private static readonly Color CText = Color.FromArgb(230, 232, 238);

	private static readonly Color CMuted = Color.FromArgb(150, 155, 168);

	private static readonly Color CAccent = Color.FromArgb(255, 140, 26);

	private readonly Label _status = new Label();

	private readonly Label _ip = new Label();

	private readonly Label _stats = new Label();

	private readonly Label _warning = new Label();

	private readonly RichTextBox _log = new RichTextBox();

	private readonly CheckBox _showAll = new CheckBox();

	private readonly List<string> _all = new List<string>();

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

	private int _tick;

	public ServerWindow()
	{
		Text = "Big Bang Racing 2.4.9 - Offline Server";
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(860, 600);
		MinimumSize = new Size(700, 480);
		BackColor = CBack;
		ForeColor = Color.FromArgb(230, 232, 238);
		Font = new Font("Segoe UI", 9.5f);

		TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Controls.Add(root);

		// titre + etat
		TableLayoutPanel head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
		head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Label title = new Label { Text = "Big Bang Racing 2.4.9", AutoSize = true, ForeColor = CText, Font = new Font("Segoe UI Semibold", 17f) };
		Label sub = new Label { Text = "Offline server", AutoSize = true, ForeColor = CMuted, Margin = new Padding(3, 0, 3, 8) };
		FlowLayoutPanel titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
		titles.Controls.Add(title);
		titles.Controls.Add(sub);
		_status.AutoSize = true;
		_status.Padding = new Padding(12, 6, 12, 6);
		_status.Font = new Font("Segoe UI Semibold", 10f);
		_status.Anchor = AnchorStyles.Right;
		head.Controls.Add(titles, 0, 0);
		head.Controls.Add(_status, 1, 0);
		root.Controls.Add(head, 0, 0);

		// cartes d'information
		TableLayoutPanel cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
		cards.Controls.Add(Card("PC address (use it as DNS on your phone)", _ip, "Copy", delegate
		{
			if (ServerInfo.Ip != "")
			{
				Clipboard.SetText(ServerInfo.Ip);
			}
		}));
		Label cert = new Label { AutoSize = true, ForeColor = CText, Font = new Font("Segoe UI Semibold", 11f), Text = "BigBangRacing_RootCA.crt" };
		cards.Controls.Add(Card("Certificate (only for iPhone or the original game)", cert, "Show file", delegate
		{
			Reveal(Settings.RootCaCertDer);
		}));
		Label data = new Label { AutoSize = true, ForeColor = CText, Font = new Font("Segoe UI Semibold", 11f), Text = "Data folder (players, levels, scores, log)" };
		cards.Controls.Add(Card("Saved data", data, "Open folder", delegate
		{
			Open(Settings.DataDir);
		}));
		_stats.AutoSize = true;
		_stats.ForeColor = CMuted;
		_stats.Margin = new Padding(3, 8, 3, 0);
		cards.Controls.Add(_stats);
		root.Controls.Add(cards, 0, 1);

		_warning.AutoSize = true;
		_warning.ForeColor = Color.FromArgb(255, 190, 90);
		_warning.Margin = new Padding(3, 0, 3, 8);
		_warning.Visible = false;
		root.Controls.Add(_warning, 0, 2);

		// journal
		TableLayoutPanel logArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CBack };
		logArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		logArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_showAll.Text = "Show every request";
		_showAll.AutoSize = true;
		_showAll.ForeColor = CMuted;
		_showAll.CheckedChanged += delegate
		{
			Rebuild();
		};
		logArea.Controls.Add(_showAll, 0, 0);
		_log.Dock = DockStyle.Fill;
		_log.ReadOnly = true;
		_log.BackColor = Color.FromArgb(16, 18, 22);
		_log.ForeColor = Color.FromArgb(190, 195, 205);
		_log.BorderStyle = BorderStyle.None;
		_log.Font = new Font("Consolas", 9.5f);
		_log.WordWrap = true;
		_log.ScrollBars = RichTextBoxScrollBars.Vertical;
		logArea.Controls.Add(_log, 0, 1);
		root.Controls.Add(logArea, 0, 3);

		// boutons du bas
		FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
		Button quit = MakeButton("Stop server && quit", true);
		quit.Click += delegate
		{
			Close();
		};
		Button clear = MakeButton("Clear log", false);
		clear.Click += delegate
		{
			_all.Clear();
			_log.Clear();
		};
		Button logFile = MakeButton("Open log file", false);
		logFile.Click += delegate
		{
			Open(Path.Combine(Settings.DataDir, "server249.log"));
		};
		Button challenge = MakeButton("Challenge a player", false);
		challenge.Click += delegate
		{
			using ChallengeDialog dlg = new ChallengeDialog();
			dlg.ShowDialog(this);
		};
		bottom.Controls.Add(quit);
		bottom.Controls.Add(challenge);
		bottom.Controls.Add(clear);
		bottom.Controls.Add(logFile);
		root.Controls.Add(bottom, 0, 4);

		_timer.Interval = 250;
		_timer.Tick += delegate
		{
			Refresh_();
		};
		_timer.Start();
		FormClosing += OnClosing;
		Refresh_();
	}

	private static Button MakeButton(string text, bool primary)
	{
		Button b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(8, 0, 0, 0), Cursor = Cursors.Hand };
		b.FlatAppearance.BorderSize = 0;
		b.BackColor = primary ? CAccent : Color.FromArgb(52, 56, 68);
		b.ForeColor = primary ? Color.FromArgb(30, 20, 10) : CText;
		return b;
	}

	private static Control Card(string caption, Label value, string buttonText, Action click)
	{
		TableLayoutPanel card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, BackColor = CPanel, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 8) };
		card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		FlowLayoutPanel text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
		text.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = CMuted });
		value.AutoSize = true;
		value.ForeColor = CText;
		value.Font = new Font("Segoe UI Semibold", 11f);
		text.Controls.Add(value);
		Button b = MakeButton(buttonText, false);
		b.Click += delegate
		{
			click();
		};
		b.Anchor = AnchorStyles.Right;
		card.Controls.Add(text, 0, 0);
		card.Controls.Add(b, 1, 0);
		return card;
	}

	private static void Open(string path)
	{
		try
		{
			Directory.CreateDirectory(Settings.DataDir);
			Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "Cannot open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private static void Reveal(string file)
	{
		try
		{
			if (File.Exists(file))
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + file + "\"", UseShellExecute = true });
			}
			else
			{
				MessageBox.Show("The certificate is created when the server starts.", "Not created yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
		catch
		{
		}
	}

	private static bool Quiet(string line)
	{
		return line.Contains("[HTTP]") || line.Contains("[DNS] blocked") || line.Contains("[Minigame] official level");
	}

	private static Color LineColor(string line)
	{
		string l = line.ToLowerInvariant();
		if (l.Contains("could not") || l.Contains("error") || l.Contains("failed") || l.Contains("[warning]"))
		{
			return Color.FromArgb(255, 110, 110);
		}
		if (line.Contains("[Login]"))
		{
			return Color.FromArgb(120, 190, 255);
		}
		if (line.Contains("[Score]") || line.Contains("[Export]") || line.Contains("[Import]") || line.Contains("[Minigame]"))
		{
			return Color.FromArgb(130, 220, 150);
		}
		if (line.Contains("[HTTP]") || line.Contains("[DNS]"))
		{
			return Color.FromArgb(120, 125, 140);
		}
		return Color.FromArgb(190, 195, 205);
	}

	private void AppendLine(string line)
	{
		_log.SelectionStart = _log.TextLength;
		_log.SelectionLength = 0;
		_log.SelectionColor = LineColor(line);
		_log.AppendText(line + "\n");
	}

	private void Rebuild()
	{
		_log.SuspendLayout();
		_log.Clear();
		foreach (string line in _all)
		{
			if (_showAll.Checked || !Quiet(line))
			{
				AppendLine(line);
			}
		}
		_log.SelectionStart = _log.TextLength;
		_log.ScrollToCaret();
		_log.ResumeLayout();
	}

	private void Refresh_()
	{
		string state = ServerInfo.State;
		if (state == "Running")
		{
			_status.Text = "Server running";
			_status.BackColor = Color.FromArgb(32, 110, 60);
			_status.ForeColor = Color.White;
		}
		else if (state == "Error")
		{
			_status.Text = "Error";
			_status.BackColor = Color.FromArgb(150, 40, 40);
			_status.ForeColor = Color.White;
		}
		else
		{
			_status.Text = "Starting...";
			_status.BackColor = Color.FromArgb(120, 90, 20);
			_status.ForeColor = Color.White;
		}
		_ip.Text = ServerInfo.Ip == "" ? "..." : ServerInfo.Ip;
		if (ServerInfo.Error != "")
		{
			_warning.Text = ServerInfo.Error;
			_warning.Visible = true;
		}
		else if (!Program.IsAdministrator())
		{
			_warning.Text = "Not running as administrator: ports 53, 80 and 443 may be refused. Use \"Start BBR249Server.bat\".";
			_warning.Visible = true;
		}
		else
		{
			_warning.Visible = false;
		}
		bool added = false;
		int n = 0;
		string line;
		while (n < 400 && GuiLogWriter.Lines.TryDequeue(out line))
		{
			n++;
			_all.Add(line);
			if (_all.Count > 5000)
			{
				_all.RemoveRange(0, 1000);
			}
			if (_showAll.Checked || !Quiet(line))
			{
				AppendLine(line);
				added = true;
			}
		}
		if (added)
		{
			_log.SelectionStart = _log.TextLength;
			_log.ScrollToCaret();
			if (_log.Lines.Length > 3000)
			{
				Rebuild();
			}
		}
		if (state == "Running" && _tick++ % 12 == 0)
		{
			try
			{
				long players = Db.One("SELECT COUNT(*) AS c FROM players")?.L("c", 0L) ?? 0L;
				long levels = Db.One("SELECT COUNT(*) AS c FROM minigames WHERE state='public' AND template=0")?.L("c", 0L) ?? 0L;
				long scores = Db.One("SELECT COUNT(*) AS c FROM scores")?.L("c", 0L) ?? 0L;
				_stats.Text = $"{players} player(s)   -   {levels} published level(s)   -   {scores} score(s)";
			}
			catch
			{
			}
		}
	}

	private void OnClosing(object? sender, FormClosingEventArgs e)
	{
		if (e.CloseReason == CloseReason.UserClosing && MessageBox.Show("Stop the server? Nobody will be able to play until you start it again.", "Stop server", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			e.Cancel = true;
		}
	}
}

// Cree un defi "ami" entre deux joueurs du serveur : l'autre joueur voit une bulle de defi sur sa carte.
public sealed class ChallengeDialog : Form
{
	private sealed class Item
	{
		public string Id = "";

		public string Label = "";

		public override string ToString()
		{
			return Label;
		}
	}

	private readonly ComboBox _from = new ComboBox();

	private readonly ComboBox _to = new ComboBox();

	private readonly ComboBox _level = new ComboBox();

	public ChallengeDialog()
	{
		Text = "Challenge a player";
		StartPosition = FormStartPosition.CenterParent;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		ClientSize = new Size(460, 300);
		Font = new Font("Segoe UI", 9.5f);
		int y = 16;
		AddRow("Challenger (their time on the level is the time to beat)", _from, ref y);
		AddRow("Opponent (sees the challenge on the map)", _to, ref y);
		AddRow("Race level", _level, ref y);
		foreach (ComboBox cb in new ComboBox[] { _from, _to, _level })
		{
			cb.DropDownStyle = ComboBoxStyle.DropDownList;
		}
		try
		{
			foreach (Row p in Db.Query("SELECT id, name, lastLogin FROM players ORDER BY lastLogin DESC"))
			{
				string label = p.S("name") + "  (" + p.S("id").Substring(0, 6) + ", last seen " + p.S("lastLogin") + ")";
				_from.Items.Add(new Item { Id = p.S("id"), Label = label });
				_to.Items.Add(new Item { Id = p.S("id"), Label = label });
			}
			foreach (Row l in Db.Query("SELECT id, name, creatorName FROM minigames WHERE template=0 AND state='public' AND gameMode='Race' AND levelData IS NOT NULL ORDER BY publishTime DESC"))
			{
				_level.Items.Add(new Item { Id = l.S("id"), Label = l.S("name") + "  (by " + l.S("creatorName") + ")" });
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message);
		}
		if (_from.Items.Count > 0)
		{
			_from.SelectedIndex = 0;
		}
		if (_to.Items.Count > 1)
		{
			_to.SelectedIndex = 1;
		}
		if (_level.Items.Count > 0)
		{
			_level.SelectedIndex = 0;
		}
		Button send = new Button { Text = "Send challenge", AutoSize = true, Location = new Point(300, y + 6), Padding = new Padding(8, 3, 8, 3) };
		send.Click += delegate
		{
			Send();
		};
		Controls.Add(send);
		ClientSize = new Size(460, y + 56);
	}

	private void AddRow(string caption, Control box, ref int y)
	{
		Controls.Add(new Label { Text = caption, AutoSize = true, Location = new Point(16, y) });
		y += 20;
		box.Location = new Point(16, y);
		box.Width = 428;
		Controls.Add(box);
		y += 40;
	}

	private void Send()
	{
		if (_from.SelectedItem is not Item from || _to.SelectedItem is not Item to || _level.SelectedItem is not Item level)
		{
			MessageBox.Show("Pick two players and a level first (a race level must be published).", "Challenge", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		string? error = BBR249Server.Api.ChallengeApi.CreateFriendlyAdmin(from.Id, to.Id, level.Id);
		if (error != null)
		{
			MessageBox.Show(error, "Challenge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		MessageBox.Show("Challenge sent. A challenge bubble appears on the opponent's map (close and reopen the game, or wait a moment, if it is not there yet).", "Challenge", MessageBoxButtons.OK, MessageBoxIcon.Information);
		Close();
	}
}
