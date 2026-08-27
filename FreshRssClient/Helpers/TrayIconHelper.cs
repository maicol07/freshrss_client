using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using FreshRssClient.Services;

namespace FreshRssClient.Helpers
{
    public class TrayIconHelper : IDisposable
    {
        private readonly Window _window;
        private readonly IntPtr _hwnd;
        private readonly uint _uid = 1001;
        private readonly uint _wmTrayMessage = 0x8000 + 101; // WM_USER + 101
        private bool _isCreated = false;
        private int _lastUnreadCount = 0;
        private Icon? _currentIcon = null;
        private readonly SubclassProc _subclassProc;
        private readonly Action _onSyncNow;

        // Win32 API Imports
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_SETICON = 0x0080;
        private static readonly IntPtr ICON_SMALL = new IntPtr(0);
        private static readonly IntPtr ICON_BIG = new IntPtr(1);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("Comctl32.dll", CharSet = CharSet.Auto)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("Comctl32.dll", CharSet = CharSet.Auto)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass);

        [DllImport("Comctl32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        // Win32 Delegates
        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

        // Win32 Constants
        private const uint NIM_ADD = 0;
        private const uint NIM_MODIFY = 1;
        private const uint NIM_DELETE = 2;
        private const uint NIF_MESSAGE = 1;
        private const uint NIF_ICON = 2;
        private const uint NIF_TIP = 4;

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;
        private const int SW_HIDE = 0;

        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_COMMAND = 0x0111;

        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;

        private const uint MF_STRING = 0x0000;
        private const uint MF_SEPARATOR = 0x0800;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersionOrTimeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public TrayIconHelper(Window window, Action onSyncNow)
        {
            _window = window;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _onSyncNow = onSyncNow;
            _subclassProc = new SubclassProc(WindowSubclassWndProc);

            // Subclass the main window to intercept tray events
            SetWindowSubclass(_hwnd, _subclassProc, 1, IntPtr.Zero);
            
            CreateTrayIcon();
        }

        public void UpdateUnreadCount(int unreadCount)
        {
            _lastUnreadCount = unreadCount;
            UpdateTrayIcon();
        }

        private void CreateTrayIcon()
        {
            if (_isCreated) return;

            var icon = GenerateDynamicBadgeIcon(_lastUnreadCount);
            _currentIcon = icon;

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = _uid,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = _wmTrayMessage,
                hIcon = icon.Handle,
                szTip = "FreshRSS Client"
            };

            _isCreated = Shell_NotifyIconW(NIM_ADD, ref data);
            if (_isCreated)
            {
                SendMessage(_hwnd, WM_SETICON, ICON_SMALL, icon.Handle);
                SendMessage(_hwnd, WM_SETICON, ICON_BIG, icon.Handle);
            }
        }

        private void UpdateTrayIcon()
        {
            if (!_isCreated) return;

            var oldIcon = _currentIcon;
            var icon = GenerateDynamicBadgeIcon(_lastUnreadCount);
            _currentIcon = icon;

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = _uid,
                uFlags = NIF_ICON | NIF_TIP,
                hIcon = icon.Handle,
                szTip = $"FreshRSS Client - {_lastUnreadCount} {LocalizationManager.Current.UnreadTraySuffix}"
            };

            Shell_NotifyIconW(NIM_MODIFY, ref data);

            // Set window icon for Taskbar badge
            SendMessage(_hwnd, WM_SETICON, ICON_SMALL, icon.Handle);
            SendMessage(_hwnd, WM_SETICON, ICON_BIG, icon.Handle);

            // Safely clean up old icon memory
            if (oldIcon != null)
            {
                DestroyIcon(oldIcon.Handle);
                oldIcon.Dispose();
            }
        }

        public void MinimizeToTray()
        {
            ShowWindow(_hwnd, SW_HIDE);
        }

        public void RestoreFromTray()
        {
            _window.Activate();
            ShowWindow(_hwnd, SW_RESTORE);
            SetForegroundWindow(_hwnd);
        }

        // Brand palette, kept in sync with tools/generate-icons.ps1.
        private static readonly Color PlateTopColor = Color.FromArgb(255, 245, 144, 31);
        private static readonly Color PlateBottomColor = Color.FromArgb(255, 232, 99, 10);
        private static readonly Color BadgeFillColor = Color.FromArgb(255, 28, 28, 28);
        private static readonly Color BadgeRingColor = Color.FromArgb(255, 250, 250, 250);

        private Icon GenerateDynamicBadgeIcon(int unreadCount)
        {
            const int size = 32;
            using var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Same composition as the app icon (see tools/generate-icons.ps1): brand
            // plate at 86% of the canvas, white RSS mark at 62% of the plate.
            float plate = size * 0.86f;
            float plateOrigin = (size - plate) / 2f;

            var plateBounds = new RectangleF(plateOrigin, plateOrigin, plate, plate);
            using (var plateBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                plateBounds, PlateTopColor, PlateBottomColor, 90f))
            {
                // Brush rect equal to the fill rect otherwise leaves a wrap hairline
                // of the wrong color along the gradient's start edge.
                plateBrush.WrapMode = System.Drawing.Drawing2D.WrapMode.TileFlipXY;
                graphics.FillRoundedRectangle(plateBrush, plateOrigin, plateOrigin, plate, plate, plate * 0.22f);
            }

            float markSide = plate * 0.62f;
            float markOrigin = plateOrigin + ((plate - markSide) / 2f);
            DrawRssMark(graphics, markOrigin, markOrigin, markSide, Color.White);

            // Unread badge, overlapping the plate's top-right corner.
            if (unreadCount > 0)
            {
                // Capped at two glyphs: a third one fits only by shrinking the font back
                // to unreadable, and the exact count is already in the tooltip.
                string countStr = unreadCount > 99 ? "99" : unreadCount.ToString();

                // The digits are laid out as a filled path rather than DrawString: the
                // text line box wastes vertical room on ascender and descender leading,
                // which is what made the number look undersized inside the pill.
                using var glyphs = new System.Drawing.Drawing2D.GraphicsPath();
                using (var family = new FontFamily("Segoe UI"))
                using (var typographic = new StringFormat(StringFormat.GenericTypographic))
                {
                    glyphs.AddString(countStr, family, (int)FontStyle.Bold, 32f,
                        PointF.Empty, typographic);
                }

                var glyphBounds = glyphs.GetBounds();
                const float ring = 1.1f;
                float badgeHeight = 16.5f;
                float glyphHeight = badgeHeight * 0.72f;
                float scale = glyphHeight / glyphBounds.Height;
                float glyphWidth = glyphBounds.Width * scale;

                // Tight side padding: at this glyph size a generous one would stretch the
                // pill across most of the canvas.
                float badgeWidth = Math.Max(badgeHeight, glyphWidth + (badgeHeight * 0.42f));

                // Flush to the top-right corner, leaving exactly the ring outside.
                float badgeX = size - badgeWidth - ring;
                float badgeY = ring;

                // Near-white hairline: separates the dark pill from the orange plate
                // underneath and from the taskbar behind it, on light and dark alike.
                using (var ringBrush = new SolidBrush(BadgeRingColor))
                {
                    graphics.FillRoundedRectangle(ringBrush,
                        badgeX - ring,
                        badgeY - ring,
                        badgeWidth + (ring * 2f),
                        badgeHeight + (ring * 2f),
                        (badgeHeight + (ring * 2f)) / 2f);
                }

                // Carbon pill: red would sit too close to the plate's orange to read.
                using (var badgeBrush = new SolidBrush(BadgeFillColor))
                {
                    graphics.FillRoundedRectangle(badgeBrush, badgeX, badgeY, badgeWidth, badgeHeight, badgeHeight / 2f);
                }

                // Center the glyph bounding box (not its line box) inside the pill.
                // Matrix methods prepend, so these are applied to the path bottom-up:
                // bbox to origin, then scale, then move into place.
                using (var transform = new System.Drawing.Drawing2D.Matrix())
                using (var textBrush = new SolidBrush(Color.White))
                {
                    transform.Translate(
                        badgeX + ((badgeWidth - glyphWidth) / 2f),
                        badgeY + ((badgeHeight - glyphHeight) / 2f));
                    transform.Scale(scale, scale);
                    transform.Translate(-glyphBounds.X, -glyphBounds.Y);

                    glyphs.Transform(transform);
                    graphics.FillPath(textBrush, glyphs);
                }
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        // Draws the RSS mark (dot plus two concentric arcs) inside a square box of side
        // <paramref name="side"/> whose top-left corner sits at (<paramref name="x"/>,
        // <paramref name="y"/>). All metrics are ratios of the side, so the mark is
        // identical to the generated app icon at any resolution.
        private static void DrawRssMark(Graphics graphics, float x, float y, float side, Color color)
        {
            float originX = x + (side * 0.215f);
            float originY = y + (side * 0.785f);
            float dotRadius = side * 0.105f;
            float strokeWidth = side * 0.135f;

            using (var brush = new SolidBrush(color))
            {
                graphics.FillEllipse(brush, originX - dotRadius, originY - dotRadius, dotRadius * 2f, dotRadius * 2f);
            }

            using var pen = new Pen(color, strokeWidth)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };

            foreach (float radius in new[] { side * 0.34f, side * 0.60f })
            {
                graphics.DrawArc(pen, originX - radius, originY - radius, radius * 2f, radius * 2f, 270f, 90f);
            }
        }

        private IntPtr WindowSubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == _wmTrayMessage)
            {
                uint eventId = (uint)lParam.ToInt64();

                if (eventId == WM_LBUTTONDBLCLK)
                {
                    RestoreFromTray();
                    return IntPtr.Zero;
                }
                else if (eventId == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                    return IntPtr.Zero;
                }
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;

            string restoreText = LocalizationManager.Current.TrayRestore;
            string syncText = LocalizationManager.Current.TraySync;
            string exitText = LocalizationManager.Current.TrayExit;

            AppendMenuW(menu, MF_STRING, new IntPtr(1), restoreText);
            AppendMenuW(menu, MF_STRING, new IntPtr(2), syncText);
            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            AppendMenuW(menu, MF_STRING, new IntPtr(3), exitText);

            if (GetCursorPos(out POINT pt))
            {
                // Force foreground so clicking outside menu dismisses it
                SetForegroundWindow(_hwnd);
                
                int command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
                
                if (command == 1)
                {
                    RestoreFromTray();
                }
                else if (command == 2)
                {
                    _onSyncNow?.Invoke();
                }
                else if (command == 3)
                {
                    if (_window is MainWindow mainWindow)
                    {
                        mainWindow.IsExiting = true;
                    }
                    Application.Current.Exit();
                }
            }

            DestroyMenu(menu);
        }

        public void Dispose()
        {
            if (_isCreated)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = _uid
                };
                Shell_NotifyIconW(NIM_DELETE, ref data);
                _isCreated = false;
            }

            if (_currentIcon != null)
            {
                DestroyIcon(_currentIcon.Handle);
                _currentIcon.Dispose();
                _currentIcon = null;
            }

            // Remove subclassing
            RemoveWindowSubclass(_hwnd, _subclassProc, 1);
        }
    }

    // Helper class to draw rounded rectangles in GDI+
    public static class GraphicsExtensions
    {
        public static System.Drawing.Drawing2D.GraphicsPath GetRoundedRectanglePath(float x, float y, float width, float height, float radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            float diameter = radius * 2;
            
            // Prevent diameter from exceeding width or height
            if (diameter > width) diameter = width;
            if (diameter > height) diameter = height;

            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseAllFigures();
            return path;
        }

        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
        {
            using (var path = GetRoundedRectanglePath(x, y, width, height, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

    }
}
