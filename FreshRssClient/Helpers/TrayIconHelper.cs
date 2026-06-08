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
                szTip = $"FreshRSS Client - {_lastUnreadCount} " + (LocalizationManager.CurrentLanguageCode == "it" ? "non letti" : "unread")
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

        private Icon GenerateDynamicBadgeIcon(int unreadCount)
        {
            const int size = 32;
            using var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Monochromatic RSS Symbol (white with subtle dark silhouette background for high contrast on light/dark themes)
            var darkPenColor = Color.FromArgb(160, 15, 15, 15);
            var whitePenColor = Color.White;
            
            using (var darkBrush = new SolidBrush(darkPenColor))
            using (var whiteBrush = new SolidBrush(whitePenColor))
            {
                // 1. Bottom-left dot (concentric around (9f, 23f), padded by 5.5px from left and bottom margins)
                // Shadow / Outline (radius = 4.5f)
                graphics.FillEllipse(darkBrush, 4.5f, size - 13.5f, 9f, 9f);
                // White center (radius = 3.5f)
                graphics.FillEllipse(whiteBrush, 5.5f, size - 12.5f, 7f, 7f);
            }

            // 2. Concentric curves (concentric around (9f, 23f))
            using (var shadowPen = new Pen(darkPenColor, 5f))
            using (var whitePen = new Pen(whitePenColor, 3f))
            {
                // Shadow arcs first
                graphics.DrawArc(shadowPen, -1f, size - 19f, 20f, 20f, 270f, 90f);
                graphics.DrawArc(shadowPen, -8f, size - 26f, 34f, 34f, 270f, 90f);

                // White arcs on top
                graphics.DrawArc(whitePen, -1f, size - 19f, 20f, 20f, 270f, 90f);
                graphics.DrawArc(whitePen, -8f, size - 26f, 34f, 34f, 270f, 90f);
            }

            // 3. Draw premium notification badge with unread count text if unreadCount > 0
            if (unreadCount > 0)
            {
                string countStr = unreadCount > 99 ? "99+" : unreadCount.ToString();
                
                // Fine-tune font size based on text length (compact and highly legible at 32x32 canvas)
                float fontSize = countStr.Length > 2 ? 6.0f : (countStr.Length > 1 ? 7.0f : 8.0f);
                using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
                
                // Measure text
                var textSize = graphics.MeasureString(countStr, font);
                
                // Determine badge dimensions based on text length
                float badgeHeight = 14f;
                float badgeWidth = Math.Max(14f, textSize.Width + 3f);
                
                // Position at top right (ensure it doesn't clip outside the 32x32 canvas boundaries)
                float badgeX = size - badgeWidth - 1f;
                float badgeY = 1f;

                // Draw premium white cutout border first (provides 1px outer separation)
                float borderPadding = 1.2f;
                using (var borderBrush = new SolidBrush(Color.White))
                {
                    graphics.FillRoundedRectangle(borderBrush, 
                        badgeX - borderPadding, 
                        badgeY - borderPadding, 
                        badgeWidth + (borderPadding * 2), 
                        badgeHeight + (borderPadding * 2), 
                        (badgeHeight + borderPadding * 2) / 2f);
                }

                // Draw badge background (Vibrant Fluent iOS Red)
                using (var badgeBrush = new SolidBrush(Color.FromArgb(255, 59, 48)))
                {
                    graphics.FillRoundedRectangle(badgeBrush, badgeX, badgeY, badgeWidth, badgeHeight, badgeHeight / 2f);
                }

                // Draw unread count text centered inside the badge
                using (var textBrush = new SolidBrush(Color.White))
                using (var stringFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    graphics.DrawString(countStr, font, textBrush, 
                        new RectangleF(badgeX, badgeY + 0.5f, badgeWidth, badgeHeight), 
                        stringFormat);
                }
            }

            return Icon.FromHandle(bitmap.GetHicon());
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

            bool isIt = LocalizationManager.CurrentLanguageCode == "it";
            
            string restoreText = isIt ? "Ripristina Lettore" : "Restore Reader";
            string syncText = isIt ? "Sincronizza Ora" : "Sync Now";
            string exitText = isIt ? "Esci" : "Exit";

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

        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
        {
            using (var path = GetRoundedRectanglePath(x, y, width, height, radius))
            {
                graphics.DrawPath(pen, path);
            }
        }
    }
}
