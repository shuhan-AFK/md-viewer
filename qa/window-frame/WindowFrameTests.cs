using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MDViewerLite;

internal static class WindowFrameTests
{
	[STAThread]
	private static int Main(string[] args)
	{
		try
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			using MainForm form = new MainForm(Array.Empty<string>());
			form.TestNativeFrame();
			if (Array.IndexOf(args, "--webview-smoke") >= 0) form.TestWebView();
			Console.WriteLine("PASS: all native window frame checks");
			return 0;
		}
		catch (Exception error)
		{
			Console.Error.WriteLine(error);
			return 1;
		}
	}
}

internal sealed partial class MainForm
{
	internal void TestNativeFrame()
	{
		// This test owns its form. Do not start the WebView or modify a running
		// user session; these checks exercise the native window implementation.
		FieldInfo shownKey = typeof(Form).GetField("EVENT_SHOWN", BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("Form.Shown event key was not found");
		object key = shownKey.GetValue(null);
		Events.RemoveHandler(key, Events[key]);
		ShowInTaskbar = false;
		Opacity = 0;
		Show();
		Application.DoEvents();

		int style = GetWindowLong(Handle, -16);
		int snapStyles = WsCaption | WsThickFrame | WsSystemMenu | WsMinimizeBox | WsMaximizeBox;
		Require((style & snapStyles) == snapStyles, "native caption/resize/system/minimize/maximize styles");
		Require(MinimumSize.Width <= 500 * DeviceDpi / 96, "minimum width permits common snap zones");
		CheckClientFillsWindow("normal window has no native title bar");
		RecreateHandle();
		Application.DoEvents();
		Require((GetWindowLong(Handle, -16) & snapStyles) == snapStyles, "native snap styles survive handle recreation");
		CheckClientFillsWindow("recreated window remains frameless");

		Bounds = new Rectangle(150, 150, 540, 400);
		Application.DoEvents();
		Require(Width == 540 && Height == 400, "window can fit a narrow snap zone");
		CheckClientFillsWindow("resized window remains frameless");

		int border = Math.Max(7, (int)Math.Round(7.0 * DeviceDpi / 96.0));
		CheckHitTest(new Point(0, 0), 13, "top-left resize");
		CheckHitTest(new Point(ClientSize.Width - 1, 0), 14, "top-right resize");
		CheckHitTest(new Point(0, ClientSize.Height - 1), 16, "bottom-left resize");
		CheckHitTest(new Point(ClientSize.Width - 1, ClientSize.Height - 1), 17, "bottom-right resize");
		CheckHitTest(new Point(0, ClientSize.Height / 2), 10, "left resize");
		CheckHitTest(new Point(ClientSize.Width - 1, ClientSize.Height / 2), 11, "right resize");
		CheckHitTest(new Point(ClientSize.Width / 2, 0), 12, "top resize");
		CheckHitTest(new Point(ClientSize.Width / 2, ClientSize.Height - 1), 15, "bottom resize");
		CheckHitTest(new Point(border + 20, border + 20), 1, "content remains clickable");

		Location = new Point(-900, -600);
		Application.DoEvents();
		CheckHitTest(new Point(0, 0), 13, "signed coordinates on a left/upper monitor");
		Location = new Point(150, 150);

		Rectangle restoreBounds = Bounds;
		WindowState = FormWindowState.Maximized;
		Application.DoEvents();
		Require(IsZoomed(Handle), "native maximize state");
		GetClientRect(Handle, out NativeRect client);
		Point origin = PointToScreen(Point.Empty);
		Rectangle actualClient = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
		Rectangle workArea = Screen.FromHandle(Handle).WorkingArea;
		Require(actualClient == workArea, "maximized content exactly fits monitor work area");
		WindowState = FormWindowState.Normal;
		Application.DoEvents();
		Require(Bounds == restoreBounds, "maximize restores previous window bounds");
		CheckClientFillsWindow("restored window remains frameless");

		WindowState = FormWindowState.Minimized;
		Application.DoEvents();
		Require(WindowState == FormWindowState.Minimized, "minimize still works");
		WindowState = FormWindowState.Normal;
		Application.DoEvents();
		Require(Bounds == restoreBounds, "minimize restores previous bounds");
		Hide();
	}

	internal void TestWebView()
	{
		Show();
		Application.DoEvents();
		// Console test runners have no Application.Run message loop to install
		// the UI synchronization context used by async WebView2 callbacks.
		SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
		Task initialize = InitializeWebViewAsync();
		WaitForTask(initialize);
		Require(_webView.CoreWebView2 != null, "WebView2 initializes with the rebuilt native host");
		DateTime deadline = DateTime.UtcNow.AddSeconds(20);
		while (!_pageReady && DateTime.UtcNow < deadline)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
		Require(_pageReady, "original Markdown interface loads");
		Task<string> script = _webView.CoreWebView2.ExecuteScriptAsync(
			"JSON.stringify({titlebar: !!document.querySelector('.window-titlebar'), drag: typeof window.mdViewer?.startWindowDrag, ready: document.readyState})");
		WaitForTask(script);
		Require(script.Result.Contains("titlebar\\\":true") && script.Result.Contains("drag\\\":\\\"function"), "custom title bar and native drag bridge are available");
		Console.WriteLine("WebView: " + script.Result);
		Hide();
	}

	private static void WaitForTask(Task task)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(20);
		while (!task.IsCompleted && DateTime.UtcNow < deadline)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
		if (!task.IsCompleted) throw new TimeoutException("WebView smoke test timed out");
		task.GetAwaiter().GetResult();
	}

	private void CheckHitTest(Point clientPoint, int expected, string name)
	{
		Point screenPoint = PointToScreen(clientPoint);
		IntPtr coordinates = new IntPtr(unchecked((screenPoint.Y << 16) | (screenPoint.X & 0xFFFF)));
		Message message = Message.Create(Handle, 0x0084, IntPtr.Zero, coordinates);
		WndProc(ref message);
		Require(message.Result.ToInt32() == expected, name);
	}

	private void CheckClientFillsWindow(string name)
	{
		GetWindowRect(Handle, out NativeRect window);
		GetClientRect(Handle, out NativeRect client);
		Point origin = PointToScreen(Point.Empty);
		Require(origin.X == window.Left && origin.Y == window.Top
			&& client.Right - client.Left == window.Right - window.Left
			&& client.Bottom - client.Top == window.Bottom - window.Top, name);
	}

	private static void Require(bool condition, string name)
	{
		if (!condition) throw new InvalidOperationException("FAIL: " + name);
		Console.WriteLine("PASS: " + name);
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
}
