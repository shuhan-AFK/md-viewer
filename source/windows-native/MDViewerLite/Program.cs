using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MDViewerLite;

internal static class Program
{
	private const string MutexName = "MDViewerLite.7CBB2AB6.SingleInstance";

	private const string PipeName = "MDViewerLite.7CBB2AB6.FilePipe";

	private static Mutex? _mutex;

	private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);

	[STAThread]
	private static void Main(string[] args)
	{
		try
		{
			SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
		}
		catch (EntryPointNotFoundException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		if (args.Length == 3 && string.Equals(args[0], "--export-docx", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				string fullPath = Path.GetFullPath(args[1]);
				string fullPath2 = Path.GetFullPath(args[2]);
				if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("输入文件必须是存在的 .md 文件");
				}
				MarkdownDocxExporter.Export(File.ReadAllText(fullPath, Encoding.UTF8).TrimStart('\ufeff'), fullPath, fullPath2);
				return;
			}
			catch (Exception ex3)
			{
				Console.Error.WriteLine(ex3.Message);
				Environment.ExitCode = 1;
				return;
			}
		}
		string[] array = ValidMarkdownPaths(args).ToArray();
		_mutex = new Mutex(initiallyOwned: true, "MDViewerLite.7CBB2AB6.SingleInstance", out var createdNew);
		if (!createdNew)
		{
			SendPathsToExisting(array);
			return;
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		using (MainForm mainForm = new MainForm(array))
		{
			StartPipeServer(mainForm);
			Application.Run(mainForm);
		}
		_mutex.ReleaseMutex();
	}

	[DllImport("user32.dll")]
	private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

	private static IEnumerable<string> ValidMarkdownPaths(IEnumerable<string> args)
	{
		foreach (string arg in args)
		{
			if (!string.IsNullOrWhiteSpace(arg))
			{
				string fullPath;
				try
				{
					fullPath = Path.GetFullPath(arg.Trim('"'));
				}
				catch
				{
					continue;
				}
				if (File.Exists(fullPath) && string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
				{
					yield return fullPath;
				}
			}
		}
	}

	private static void SendPathsToExisting(string[] files)
	{
		try
		{
			using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "MDViewerLite.7CBB2AB6.FilePipe", PipeDirection.Out);
			namedPipeClientStream.Connect(1500);
			using StreamWriter streamWriter = new StreamWriter(namedPipeClientStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			foreach (string value in files)
			{
				streamWriter.WriteLine(value);
			}
		}
		catch
		{
		}
	}

	private static void StartPipeServer(MainForm form)
	{
		Task.Run(delegate
		{
			while (!form.IsDisposed)
			{
				try
				{
					using NamedPipeServerStream namedPipeServerStream = new NamedPipeServerStream("MDViewerLite.7CBB2AB6.FilePipe", PipeDirection.In, 1, PipeTransmissionMode.Byte);
					namedPipeServerStream.WaitForConnection();
					using StreamReader streamReader = new StreamReader(namedPipeServerStream, Encoding.UTF8);
					List<string> paths = new List<string>();
					string item;
					while ((item = streamReader.ReadLine()) != null)
					{
						paths.Add(item);
					}
					if (paths.Count > 0 && !form.IsDisposed)
					{
						form.BeginInvoke((Action)delegate
						{
							form.OpenExternalPaths(paths);
						});
					}
					else if (!form.IsDisposed)
					{
						form.BeginInvoke(new Action(form.ActivateFromExternalRequest));
					}
				}
				catch
				{
					if (form.IsDisposed)
					{
						break;
					}
					Thread.Sleep(150);
				}
			}
		});
	}
}
