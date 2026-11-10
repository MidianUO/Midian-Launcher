using System.Runtime.InteropServices;

namespace MidianLauncher
{
    // Le poche funzioni di Windows che servono alla finestra (niente WinForms: non funziona con NativeAOT).
    internal static unsafe partial class Win32
    {
        public const int WS_POPUP = unchecked((int)0x80000000), WS_MINIMIZEBOX = 0x00020000, WS_SYSMENU = 0x00080000;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const uint WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014, WM_SETCURSOR = 0x0020,
            WM_NCHITTEST = 0x0084, WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
            WM_MOUSELEAVE = 0x02A3, WM_APP = 0x8000;
        public const int HTCLIENT = 1, HTCAPTION = 2;
        public const int SW_SHOW = 5;
        public const int SRCCOPY = 0x00CC0020;
        public const int TRANSPARENT = 1;
        public const uint DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_END_ELLIPSIS = 0x8000, DT_NOPREFIX = 0x800, DT_LEFT = 0;
        public const int IDC_ARROW = 32512, IDC_HAND = 32649;
        public const uint MB_OK = 0, MB_ICONWARNING = 0x30, MB_ICONERROR = 0x10;
        public const uint TME_LEAVE = 2;

        public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEXW
        {
            public uint cbSize, style;
            public nint lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public nint hInstance, hIcon, hCursor, hbrBackground;
            public char* lpszMenuName, lpszClassName;
            public nint hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT { public nint hdc; public int fErase; public RECT rc; public int fRestore, fIncUpdate; public fixed byte rgb[32]; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")] public static partial ushort RegisterClassEx(WNDCLASSEXW* wc);
        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint CreateWindowEx(int exStyle, string cls, string title, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] public static partial nint DefWindowProc(nint hwnd, uint msg, nint w, nint l);
        [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] public static partial int GetMessage(MSG* m, nint hwnd, uint min, uint max);
        [LibraryImport("user32.dll")] public static partial int TranslateMessage(MSG* m);
        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] public static partial nint DispatchMessage(MSG* m);
        [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PostMessage(nint hwnd, uint msg, nint w, nint l);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint hwnd, int cmd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UpdateWindow(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyWindow(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
        [LibraryImport("user32.dll")] public static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* ps);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool EndPaint(nint hwnd, PAINTSTRUCT* ps);
        [LibraryImport("user32.dll")] public static partial nint GetDC(nint hwnd);
        [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint hwnd, nint hdc);
        [LibraryImport("user32.dll")] public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint fn);
        [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")] public static partial nint LoadCursor(nint inst, nint id);
        [LibraryImport("user32.dll")] public static partial nint SetCursor(nint cur);
        [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int i);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ScreenToClient(nint hwnd, POINT* p);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool TrackMouseEvent(TRACKMOUSEEVENT* t);
        [LibraryImport("user32.dll")] public static partial nint SetCapture(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ReleaseCapture();
        [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)] public static partial int MessageBox(nint hwnd, string text, string caption, uint type);
        [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)] public static partial int DrawText(nint hdc, string text, int len, RECT* rc, uint format);
        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] public static partial nint SendMessage(nint hwnd, uint msg, nint w, nint l);
        [LibraryImport("shell32.dll", EntryPoint = "ExtractIconW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint ExtractIcon(nint inst, string file, uint index);
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")] public static partial nint GetModuleHandle(nint name);

        [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint hdc);
        [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* bmi, uint usage, void** bits, nint section, uint offset);
        [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint hdc, nint obj);
        [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(nint obj);
        [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int rop);
        [LibraryImport("gdi32.dll")] public static partial int SetBkMode(nint hdc, int mode);
        [LibraryImport("gdi32.dll")] public static partial uint SetTextColor(nint hdc, uint color);
        [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GdiFlush();

        public static int LoWord(nint v) => (short)((long)v & 0xFFFF);
        public static int HiWord(nint v) => (short)(((long)v >> 16) & 0xFFFF);
        public static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));
    }
}
