using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MDViewerLite;

internal sealed partial class MainForm
{
	private const int WsCaption = 0x00C00000;
	private const int WsThickFrame = 0x00040000;
	private const int WsSystemMenu = 0x00080000;
	private const int WsMinimizeBox = 0x00020000;
	private const int WsMaximizeBox = 0x00010000;
	private const int SnapWindowStyles = WsCaption | WsThickFrame | WsSystemMenu | WsMinimizeBox | WsMaximizeBox;
	private const int WmNcCalcSize = 0x0083;
	private const int WmStyleChanging = 0x007C;

	private void EnableNativeSnap()
	{
		// Apply the styles to the HWND after creation. Keep WinForms' logical
		// FormBorderStyle.None so its client-to-window size calculations do not
		// add invisible caption/border pixels when restoring or changing DPI.
		int style = GetWindowLong(Handle, -16);
		SetWindowLong(Handle, -16, style | SnapWindowStyles);
		SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
	}

	private bool TryHandleNativeFrameMessage(ref Message message)
	{
		// WinForms refreshes HWND styles when showing, scaling, or changing
		// other window properties. Preserve the native snap capabilities.
		if (message.Msg == WmStyleChanging && message.WParam.ToInt64() == -16 && message.LParam != IntPtr.Zero)
		{
			NativeStyle style = Marshal.PtrToStructure<NativeStyle>(message.LParam);
			style.NewStyle |= SnapWindowStyles;
			Marshal.StructureToPtr(style, message.LParam, false);
			message.Result = IntPtr.Zero;
			return true;
		}

		if (message.Msg != WmNcCalcSize)
		{
			return false;
		}

		// The first RECT is the proposed client rectangle in both forms of
		// WM_NCCALCSIZE. Returning zero removes the system's visible frame.
		if (message.LParam != IntPtr.Zero && IsZoomed(message.HWnd))
		{
			NativeRect rectangle = Marshal.PtrToStructure<NativeRect>(message.LParam);
			Rectangle proposedBounds = Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
			Rectangle workArea = Screen.FromRectangle(proposedBounds).WorkingArea;
			rectangle.Left = workArea.Left;
			rectangle.Top = workArea.Top;
			rectangle.Right = workArea.Right;
			rectangle.Bottom = workArea.Bottom;
			Marshal.StructureToPtr(rectangle, message.LParam, false);
		}

		message.Result = IntPtr.Zero;
		return true;
	}

	private static IntPtr CursorPositionLParam()
	{
		Point cursor = Cursor.Position;
		// Preserve signed 16-bit screen coordinates, including monitors to
		// the left or above the primary display.
		return new IntPtr(unchecked((cursor.Y << 16) | (cursor.X & 0xFFFF)));
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeStyle
	{
		public int OldStyle;
		public int NewStyle;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsZoomed(IntPtr window);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(IntPtr window, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(IntPtr window, int index, int value);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
