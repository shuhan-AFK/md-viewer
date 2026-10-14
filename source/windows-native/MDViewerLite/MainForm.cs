using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MDViewerLite;

internal sealed partial class MainForm : Form
{
	private readonly WebView2 _webView = new WebView2();

	private readonly DocumentStore _documents = new DocumentStore();

	private readonly JavaScriptSerializer _json = new JavaScriptSerializer
	{
		MaxJsonLength = 33554432
	};

	private readonly List<string> _pendingPaths = new List<string>();

	private bool _pageReady;

	private string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MD Viewer");

	private string ThemePath => Path.Combine(SettingsDirectory, "theme.txt");

	public MainForm(IEnumerable<string> initialPaths)
	{
		Text = "MD Viewer";
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(480, 360);
		base.Size = new Size(1280, 820);
		base.FormBorderStyle = FormBorderStyle.None;
		BackColor = Color.FromArgb(237, 245, 255);
		AllowDrop = true;
		_pendingPaths.AddRange(initialPaths);
		_webView.Dock = DockStyle.Fill;
		base.Controls.Add(_webView);
		base.DragEnter += HandleDragEnter;
		base.DragDrop += HandleDragDrop;
		_webView.DragEnter += HandleDragEnter;
		_webView.DragDrop += HandleDragDrop;
		_documents.DocumentEvent += HandleDocumentStoreEvent;
		base.Shown += async delegate
		{
			await InitializeWebViewAsync();
		};
		base.FormClosed += delegate
		{
			_documents.Dispose();
		};
	}

	protected override void OnHandleCreated(EventArgs eventArgs)
	{
		base.OnHandleCreated(eventArgs);
		EnableNativeSnap();
		ApplyNativeWindowStyle();
	}

	private void ApplyNativeWindowStyle()
	{
		try
		{
			int value = 3;
			DwmSetWindowAttribute(base.Handle, 33, ref value, 4);
			uint value2 = 4294967294u;
			DwmSetWindowAttributeColor(base.Handle, 34, ref value2, 4);
		}
		catch (DllNotFoundException)
		{
		}
		catch (EntryPointNotFoundException)
		{
		}
	}

	public void ActivateFromExternalRequest()
	{
		if (base.WindowState == FormWindowState.Minimized)
		{
			base.WindowState = FormWindowState.Normal;
		}
		Show();
		Activate();
	}

	public void OpenExternalPaths(IEnumerable<string> paths)
	{
		ActivateFromExternalRequest();
		OpenPaths(paths);
	}

	private async Task InitializeWebViewAsync()
	{
		_ = 2;
		try
		{
			Directory.CreateDirectory(SettingsDirectory);
			CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(SettingsDirectory, "WebView2"));
			await _webView.EnsureCoreWebView2Async(environment);
			_webView.AllowExternalDrop = false;
			CoreWebView2 core = _webView.CoreWebView2;
			core.Settings.AreDefaultContextMenusEnabled = true;
			core.Settings.AreDevToolsEnabled = false;
			core.Settings.AreBrowserAcceleratorKeysEnabled = true;
			core.Settings.IsStatusBarEnabled = false;
			core.Settings.IsZoomControlEnabled = true;
			core.SetVirtualHostNameToFolderMapping("app.mdviewer.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
			string javaScript = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge.js"), Encoding.UTF8);
			await core.AddScriptToExecuteOnDocumentCreatedAsync(javaScript);
			core.WebMessageReceived += HandleWebMessage;
			core.NavigationStarting += HandleNavigationStarting;
			core.NavigationCompleted += delegate(object _, CoreWebView2NavigationCompletedEventArgs args)
			{
				if (args.IsSuccess)
				{
					_pageReady = true;
					if (_pendingPaths.Count > 0)
					{
						string[] paths = _pendingPaths.ToArray();
						_pendingPaths.Clear();
						OpenPaths(paths);
					}
				}
			};
			core.AddWebResourceRequestedFilter("https://md-viewer.local/*", CoreWebView2WebResourceContext.Image);
			core.WebResourceRequested += HandleLocalAssetRequest;
			core.Navigate("https://app.mdviewer.local/index.html");
		}
		catch (Exception ex)
		{
			MessageBox.Show("MD Viewer 无法启动 WebView2。请确认 Microsoft Edge WebView2 Runtime 已安装。\n\n" + ex.Message, "MD Viewer", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			Close();
		}
	}

	private void HandleNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
	{
		if (!args.Uri.StartsWith("https://app.mdviewer.local/", StringComparison.OrdinalIgnoreCase))
		{
			args.Cancel = true;
			if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var result) && (result.Scheme == "http" || result.Scheme == "https"))
			{
				OpenExternal(result.ToString());
			}
		}
	}

	private void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
	{
		try
		{
			if (!(_json.DeserializeObject(args.WebMessageAsJson) is Dictionary<string, object> dictionary))
			{
				return;
			}
			string text = GetString(dictionary, "kind");
			string text2 = GetString(dictionary, "method");
			IList list3;
			if (!dictionary.TryGetValue("args", out var value) || !(value is IList list))
			{
				IList list2 = new object[0];
				list3 = list2;
			}
			else
			{
				list3 = list;
			}
			IList args2 = list3;
			if (text == "event" && text2 == "startWindowDrag")
			{
				BeginWindowDrag();
			}
			else if (text == "event" && text2 == "startWindowResize")
			{
				BeginWindowResize(Arg(args2, 0));
			}
			else if (!(text != "invoke"))
			{
				int id = Convert.ToInt32(dictionary["id"]);
				try
				{
					object result = InvokeMethod(text2, args2);
					SendResponse(id, result, null);
					return;
				}
				catch (Exception ex)
				{
					SendResponse(id, null, ex.Message);
					return;
				}
			}
		}
		catch (Exception ex2)
		{
			Post("app:error", ex2.Message);
		}
	}

	private object? InvokeMethod(string method, IList args)
	{
		switch (method)
		{
		case "pickFiles":
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "打开 Markdown 文件",
				Filter = "Markdown 文件 (*.md)|*.md",
				Multiselect = true,
				CheckFileExists = true
			};
			return (openFileDialog.ShowDialog(this) == DialogResult.OK) ? ((object?)_documents.OpenPaths(openFileDialog.FileNames)) : ((object?)new List<object>());
		}
		case "saveDocument":
			return _documents.Save(Arg(args, 0), Arg(args, 1));
		case "exportDocument":
			return ExportDocument(Arg(args, 0), Arg(args, 1));
		case "closeDocument":
			_documents.Close(Arg(args, 0));
			return null;
		case "getTheme":
			return LoadTheme();
		case "setTheme":
		{
			string text = Arg(args, 0);
			if (text != "system" && text != "light" && text != "dark")
			{
				throw new InvalidOperationException("无效的主题选项");
			}
			Directory.CreateDirectory(SettingsDirectory);
			File.WriteAllText(ThemePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return text;
		}
		case "openExternal":
			return OpenExternal(Arg(args, 0));
		case "windowAction":
			PerformWindowAction(Arg(args, 0));
			return null;
		default:
			throw new InvalidOperationException("未知操作：" + method);
		}
	}

	private string LoadTheme()
	{
		try
		{
			string text = File.ReadAllText(ThemePath).Trim();
			object result;
			switch (text)
			{
			default:
				result = "system";
				break;
			case "light":
			case "dark":
			case "system":
				result = text;
				break;
			}
			return (string)result;
		}
		catch
		{
			return "system";
		}
	}

	private object? ExportDocument(string id, string content)
	{
		string filePath = _documents.GetFilePath(id);
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "导出为 Word 文档",
			Filter = "Word 文档 (*.docx)|*.docx",
			DefaultExt = "docx",
			AddExtension = true,
			FileName = Path.GetFileNameWithoutExtension(filePath) + ".docx",
			InitialDirectory = Path.GetDirectoryName(filePath),
			OverwritePrompt = true
		};
		if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
		{
			return null;
		}
		MarkdownDocxExporter.Export(content, filePath, saveFileDialog.FileName);
		return new Dictionary<string, object>
		{
			["path"] = saveFileDialog.FileName,
			["name"] = Path.GetFileName(saveFileDialog.FileName)
		};
	}

	private bool OpenExternal(string rawUrl)
	{
		if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var result) || (result.Scheme != "http" && result.Scheme != "https"))
		{
			return false;
		}
		Process.Start(new ProcessStartInfo(result.ToString())
		{
			UseShellExecute = true
		});
		return true;
	}

	private void PerformWindowAction(string action)
	{
		switch (action)
		{
		case "minimize":
			base.WindowState = FormWindowState.Minimized;
			break;
		case "maximize":
			base.WindowState = ((base.WindowState != FormWindowState.Maximized) ? FormWindowState.Maximized : FormWindowState.Normal);
			break;
		case "close":
			Close();
			break;
		}
	}

	private void OpenPaths(IEnumerable<string> paths)
	{
		if (!_pageReady)
		{
			_pendingPaths.AddRange(paths);
			return;
		}
		List<object> list = _documents.OpenPaths(paths);
		if (list.Count > 0)
		{
			Post("documents:opened", list);
		}
	}

	private void HandleDocumentStoreEvent(Dictionary<string, object> message)
	{
		if (base.IsDisposed)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke((Action)delegate
			{
				HandleDocumentStoreEvent(message);
			});
		}
		else
		{
			PostObject(message);
		}
	}

	private void HandleLocalAssetRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
	{
		try
		{
			Uri uri = new Uri(args.Request.Uri);
			string[] array = uri.AbsolutePath.Trim('/').Split('/');
			if (array.Length != 2 || array[0] != "asset")
			{
				return;
			}
			string text = HttpUtility.ParseQueryString(uri.Query)["path"];
			if (!string.IsNullOrWhiteSpace(text))
			{
				string text2 = _documents.ResolveAsset(Uri.UnescapeDataString(array[1]), text);
				if (text2 != null)
				{
					MemoryStream content = new MemoryStream(File.ReadAllBytes(text2));
					string headers = "Content-Type: " + MimeType(text2) + "\r\nCache-Control: no-cache\r\n";
					args.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(content, 200, "OK", headers);
				}
			}
		}
		catch
		{
		}
	}

	private static string MimeType(string filePath)
	{
		string text = Path.GetExtension(filePath).ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					char c = text[1];
					if (c != 'a')
					{
						if (c != 'j')
						{
							if (c == 'w' && text == ".webp")
							{
								return "image/webp";
							}
						}
						else if (text == ".jpeg")
						{
							goto IL_0120;
						}
					}
					else if (text == ".avif")
					{
						return "image/avif";
					}
				}
			}
			else
			{
				switch (text[1])
				{
				case 'p':
					if (!(text == ".png"))
					{
						goto IL_014a;
					}
					return "image/png";
				case 'j':
					break;
				case 'g':
					if (!(text == ".gif"))
					{
						goto IL_014a;
					}
					return "image/gif";
				case 's':
					if (!(text == ".svg"))
					{
						goto IL_014a;
					}
					return "image/svg+xml";
				case 'b':
					if (!(text == ".bmp"))
					{
						goto IL_014a;
					}
					return "image/bmp";
				case 'i':
					if (!(text == ".ico"))
					{
						goto IL_014a;
					}
					return "image/x-icon";
				default:
					goto IL_014a;
				}
				if (text == ".jpg")
				{
					goto IL_0120;
				}
			}
		}
		goto IL_014a;
		IL_014a:
		return "application/octet-stream";
		IL_0120:
		return "image/jpeg";
	}

	private void HandleDragEnter(object? sender, DragEventArgs args)
	{
		IDataObject data = args.Data;
		args.Effect = ((data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
	}

	private void HandleDragDrop(object? sender, DragEventArgs args)
	{
		if (args.Data?.GetData(DataFormats.FileDrop) is string[] paths)
		{
			OpenExternalPaths(paths);
		}
	}

	private void SendResponse(int id, object? result, string? error)
	{
		PostObject(new Dictionary<string, object>
		{
			["type"] = "response",
			["id"] = id,
			["result"] = result,
			["error"] = error
		});
	}

	private void Post(string type, object payload)
	{
		PostObject(new Dictionary<string, object>
		{
			["type"] = type,
			["payload"] = payload
		});
	}

	private void PostObject(object payload)
	{
		if (_pageReady && _webView.CoreWebView2 != null)
		{
			_webView.CoreWebView2.PostWebMessageAsJson(_json.Serialize(payload));
		}
	}

	private static string Arg(IList args, int index)
	{
		if (index >= args.Count || args[index] == null)
		{
			throw new InvalidOperationException("缺少操作参数");
		}
		return Convert.ToString(args[index]) ?? "";
	}

	private static string GetString(Dictionary<string, object> values, string key)
	{
		if (!values.TryGetValue(key, out object value))
		{
			return "";
		}
		return Convert.ToString(value) ?? "";
	}

	private void BeginWindowDrag()
	{
		ReleaseCapture();
		SendMessage(base.Handle, 161, new IntPtr(2), CursorPositionLParam());
	}

	private void BeginWindowResize(string direction)
	{
		if (base.WindowState != FormWindowState.Normal)
		{
			return;
		}
		int num = 0;
		if (direction == null)
		{
			return;
		}
		switch (direction.Length)
		{
		default:
			return;
		case 4:
			if (!(direction == "left"))
			{
				return;
			}
			num = 10;
			break;
		case 5:
			if (!(direction == "right"))
			{
				return;
			}
			num = 11;
			break;
		case 3:
			if (!(direction == "top"))
			{
				return;
			}
			num = 12;
			break;
		case 8:
			if (!(direction == "top-left"))
			{
				return;
			}
			num = 13;
			break;
		case 9:
			if (!(direction == "top-right"))
			{
				return;
			}
			num = 14;
			break;
		case 6:
			if (!(direction == "bottom"))
			{
				return;
			}
			num = 15;
			break;
		case 11:
			if (!(direction == "bottom-left"))
			{
				return;
			}
			num = 16;
			break;
		case 12:
			if (!(direction == "bottom-right"))
			{
				return;
			}
			num = 17;
			break;
		case 7:
		case 10:
			return;
		}
		ReleaseCapture();
		SendMessage(base.Handle, 161, new IntPtr(num), CursorPositionLParam());
	}

	protected override void WndProc(ref Message message)
	{
		if (TryHandleNativeFrameMessage(ref message))
		{
			return;
		}
		if (message.Msg == 132 && base.WindowState == FormWindowState.Normal)
		{
			base.WndProc(ref message);
			short num = (short)(message.LParam.ToInt64() & 0xFFFF);
			short num2 = (short)((message.LParam.ToInt64() >> 16) & 0xFFFF);
			Point point = PointToClient(new Point(num, num2));
			int resizeBorder = Math.Max(7, (int)Math.Round(7.0 * DeviceDpi / 96.0));
			bool flag = point.X <= resizeBorder;
			bool flag2 = point.X >= base.ClientSize.Width - resizeBorder;
			bool flag3 = point.Y <= resizeBorder;
			bool flag4 = point.Y >= base.ClientSize.Height - resizeBorder;
			if (flag && flag3)
			{
				message.Result = new IntPtr(13);
			}
			else if (flag2 && flag3)
			{
				message.Result = new IntPtr(14);
			}
			else if (flag && flag4)
			{
				message.Result = new IntPtr(16);
			}
			else if (flag2 && flag4)
			{
				message.Result = new IntPtr(17);
			}
			else if (flag)
			{
				message.Result = new IntPtr(10);
			}
			else if (flag2)
			{
				message.Result = new IntPtr(11);
			}
			else if (flag3)
			{
				message.Result = new IntPtr(12);
			}
			else if (flag4)
			{
				message.Result = new IntPtr(15);
			}
		}
		else
		{
			base.WndProc(ref message);
		}
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

	[DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
	private static extern int DwmSetWindowAttributeColor(IntPtr window, int attribute, ref uint value, int valueSize);
}
