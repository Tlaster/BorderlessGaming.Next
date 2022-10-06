using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.UI.Xaml.Media.Imaging;
using static Windows.Win32.PInvoke;

namespace BorderlessGaming.Next.Common;

internal class Native
{
    public static string GetWindowTitle(HWND handle)
    {
        var titleLength = GetWindowTextLength(handle);
        var title = new char[titleLength + 1].AsSpan();
        unsafe
        {
            fixed (char* pText = title)
            {
                if (GetWindowText(handle, pText, title.Length) == 0)
                {
                    return string.Empty;
                }
            }
        }

        return string.Join(null, title.ToArray());
    }

    public static async Task<BitmapSource?> GetWindowIcon(HWND handle)
    {
        uint pid = 0;
        unsafe
        {
            if (GetWindowThreadProcessId(handle, &pid) == 0)
            {
                return null;
            }
        }

        var proc = Process.GetProcessById((int)pid);
        if (proc.MainModule?.ModuleName == "ApplicationFrameHost.exe")
        {
            return await GetModernAppLogo(handle);
        }

        const uint wmGeticon = 0x007F;
        const int iconBig = 1;
        IntPtr hIcon = SendMessage(handle, wmGeticon, iconBig, IntPtr.Zero);

        if (hIcon == IntPtr.Zero)
        {
            hIcon = IntPtr.Size switch
            {
                8 => new IntPtr((long)GetClassLongPtr(handle, GET_CLASS_LONG_INDEX.GCL_HICON)),
                _ => new IntPtr(GetClassLong(handle, GET_CLASS_LONG_INDEX.GCL_HICON))
            };
        }
        
        // if (hIcon == IntPtr.Zero)
        // {
        //     using var icon = LoadIcon(null, "#32512");
        //     hIcon = icon.DangerousGetHandle();
        // }

        if (hIcon != IntPtr.Zero)
        {
            using var bitmap = Bitmap.FromHicon(hIcon);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Seek(0, SeekOrigin.Begin);
            var bitmapImage = new BitmapImage();
            using var randomAccessStream = stream.AsRandomAccessStream();
            await bitmapImage.SetSourceAsync(randomAccessStream);
            return bitmapImage;
        }

        return null;
    }

    private static async Task<BitmapSource?> GetModernAppLogo(HWND hwnd)
    {
        // get folder where actual app resides
        var exePath = GetModernAppProcessPath(hwnd);
        if (exePath == null)
        {
            return null;
        }
        var dir = Path.GetDirectoryName(exePath);
        if (dir == null)
        {
            return null;
        }

        var manifestPath = Path.Combine(dir, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var fs = File.OpenRead(manifestPath);
        var manifest = XDocument.Load(fs);
        const string ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var pathToLogo = manifest.Root?.Element(XName.Get("Properties", ns))?.Element(XName.Get("Logo", ns))?.Value;
        var finalLogo = Directory.GetFiles(Path.Combine(dir, Path.GetDirectoryName(pathToLogo) ?? string.Empty),
            Path.GetFileNameWithoutExtension(pathToLogo) + "*" + Path.GetExtension(pathToLogo)).FirstOrDefault();
        if (!File.Exists(finalLogo))
        {
            return null;
        }

        await using var stream = File.OpenRead(finalLogo);
        var bitmapImage = new BitmapImage();
        using var randomAccessStream = stream.AsRandomAccessStream();
        await bitmapImage.SetSourceAsync(randomAccessStream);
        return bitmapImage;
    }


    private static string? GetModernAppProcessPath(HWND hwnd)
    {
        uint pid = 0;
        unsafe
        {
            if (GetWindowThreadProcessId(hwnd, &pid) == 0)
            {
                return null;
            }
        }

        var children = GetChildWindows(hwnd);
        foreach (var childHwnd in children)
        {
            uint childPid = 0;
            unsafe
            {
                if (GetWindowThreadProcessId(childHwnd, &childPid) == 0)
                {
                    return null;
                }
            }

            if (childPid != pid)
            {
                var childProc = Process.GetProcessById((int)childPid);
                return childProc.MainModule?.FileName ?? string.Empty;
            }
        }

        return null;
    }

    private static List<HWND> GetChildWindows(HWND parent)
    {
        var result = new List<HWND>();
        var listHandle = GCHandle.Alloc(result);
        try
        {
            EnumChildWindows(parent, EnumWindow, GCHandle.ToIntPtr(listHandle));
        }
        finally
        {
            if (listHandle.IsAllocated)
            {
                listHandle.Free();
            }
        }

        return result;
    }

    private static BOOL EnumWindow(HWND handle, LPARAM pointer)
    {
        var gch = GCHandle.FromIntPtr(pointer);
        if (gch.Target is not List<HWND> list)
        {
            throw new InvalidCastException("GCHandle Target could not be cast as List<IntPtr>");
        }

        list.Add(handle);
        return true;
    }

    public static Task<List<ProcessData>> QueryProcessesWithWindows()
    {
        return Task.Run(() =>
        {
            var source = new TaskCompletionSource<List<ProcessData>>();
            var processes = new List<ProcessData>();
            var ptrList = new List<HWND>();

            BOOL Del(HWND hwnd, LPARAM lParam)
            {
                return GetMainWindowForProcess_EnumWindows(ptrList, hwnd, lParam);
            }

            EnumWindows(Del, 0);
            EnumWindows(Del, 1);
            foreach (var ptr in ptrList.Where(ptr => ptr != HWND.Null))
            {
                if (!GetWindowRect(ptr, out var rect))
                {
                    continue;
                }

                if (rect.IsEmpty)
                {
                    continue;
                }

                uint processId = 0;
                unsafe
                {
                    if (GetWindowThreadProcessId(ptr, &processId) == 0)
                    {
                        continue;
                    }
                }

                var process = Process.GetProcessById((int)processId);
                if (string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    continue;
                }

                if (processes.Any(it => it.Process.Id == process.Id && it.Handle == ptr))
                {
                    continue;
                }

                processes.Add(
                    new ProcessData(
                        process,
                        ptr
                    )
                );
            }

            source.SetResult(processes);
            return source.Task;
        });
    }

    private static bool GetMainWindowForProcess_EnumWindows(List<HWND> ptrList, HWND hWndEnumerated,
        LPARAM lParam)
    {
        var styleCurrentWindowStandard = IntPtr.Size switch
        {
            8 => GetWindowLongPtr(hWndEnumerated, WINDOW_LONG_PTR_INDEX.GWL_STYLE),
            _ => GetWindowLong(hWndEnumerated, WINDOW_LONG_PTR_INDEX.GWL_STYLE)
        };

        switch (lParam.Value)
        {
            case 0:
                if (IsWindowVisible(hWndEnumerated))
                {
                    if
                    (
                        (styleCurrentWindowStandard & (uint)WindowStyleFlags.Caption) > 0
                        && (
                            (styleCurrentWindowStandard & (uint)WindowStyleFlags.Border) > 0
                            || (styleCurrentWindowStandard & (uint)WindowStyleFlags.ThickFrame) > 0
                        )
                    )
                    {
                        ptrList.Add(hWndEnumerated);
                    }
                }

                break;
            case 1:
                if (IsWindowVisible(hWndEnumerated))
                {
                    if (styleCurrentWindowStandard != 0)
                    {
                        ptrList.Add(hWndEnumerated);
                    }
                }

                break;
        }

        return true;
    }


    public static async Task SetWindowBorderless(ProcessData data, Rectangle targetFrame = default)
    {
        // If no target frame was specified, assume the entire space on the primary screen
        if (targetFrame.Width == 0 || targetFrame.Height == 0)
        {
            
            targetFrame = Screen.FromHandle(data.Handle).Bounds;
        }

        // Get window styles
        var styleCurrentWindowStandard = GetWindowLong(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var styleCurrentWindowExtended = GetWindowLong(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        // Compute new styles (XOR of the inverse of all the bits to filter)
        var styleNewWindowStandard =
            styleCurrentWindowStandard
            & ~(
                (uint)WindowStyleFlags.Caption // composite of Border and DialogFrame
                //   | WindowStyleFlags.Border
                //   | WindowStyleFlags.DialogFrame                  
                | (uint)WindowStyleFlags.ThickFrame
                | (uint)WindowStyleFlags.SystemMenu
                | (uint)WindowStyleFlags.MaximizeBox // same as TabStop
                | (uint)WindowStyleFlags.MinimizeBox // same as Group
            );

        var styleNewWindowExtended =
            styleCurrentWindowExtended
            & ~(
                (uint)WindowStyleFlags.ExtendedDlgModalFrame
                | (uint)WindowStyleFlags.ExtendedComposited
                | (uint)WindowStyleFlags.ExtendedWindowEdge
                | (uint)WindowStyleFlags.ExtendedClientEdge
                | (uint)WindowStyleFlags.ExtendedLayered
                | (uint)WindowStyleFlags.ExtendedStaticEdge
                | (uint)WindowStyleFlags.ExtendedToolWindow
                | (uint)WindowStyleFlags.ExtendedAppWindow
            );

        data.OriginalStyleFlagsStandard = styleCurrentWindowStandard;
        data.OriginalStyleFlagsExtended = styleCurrentWindowExtended;
        GetWindowRect(data.Handle, out var rect);
        data.OriginalLocation = rect;

        if (NeedsDelay(data.Handle))
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
        }

        // update window styles
        switch (IntPtr.Size)
        {
            case 8:
                SetWindowLongPtr(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, Convert.ToInt32(styleNewWindowStandard));
                SetWindowLongPtr(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, Convert.ToInt32(styleNewWindowExtended));
                break;
            default:
                SetWindowLong(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, Convert.ToInt32(styleNewWindowStandard));
                SetWindowLong(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, Convert.ToInt32(styleNewWindowExtended));
                break;
        }


        SetWindowPos
        (
            data.Handle,
            HWND.Null,
            targetFrame.X,
            targetFrame.Y,
            targetFrame.Width,
            targetFrame.Height,
            SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING
        );
        ShowWindow(data.Handle, SHOW_WINDOW_CMD.SW_MAXIMIZE);
    }


    private static bool IsEngine(HWND handle, string name)
    {
        var title = new char[256].AsSpan();
        unsafe
        {
            fixed (char* pText = title)
            {
                if (GetClassName(handle, pText, title.Length) == 0)
                {
                    return false;
                }
            }
        }

        var className = string.Join(null, title.ToArray());
        return name.Equals(className, StringComparison.OrdinalIgnoreCase) ||
               className.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsDelay(HWND handle)
    {
        return IsEngine(handle, "unreal") || IsEngine(handle, "YYGameMakerYY");
    }
    
    
    public static void RestoreWindow(ProcessData data)
    {
        if (data.OriginalStyleFlagsStandard == 0)
        {
            return;
        }

        // update window styles
        switch (IntPtr.Size)
        {
            case 8:
                SetWindowLongPtr(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, data.OriginalStyleFlagsStandard);
                SetWindowLongPtr(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, data.OriginalStyleFlagsExtended);
                break;
            default:
                SetWindowLong(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE, data.OriginalStyleFlagsStandard);
                SetWindowLong(data.Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, data.OriginalStyleFlagsExtended);
                break;
        }
        
        SetWindowPos(data.Handle, HWND.Null, data.OriginalLocation.X, data.OriginalLocation.Y,
            data.OriginalLocation.Width, data.OriginalLocation.Height,
            SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        SetWindowPos(data.Handle, new HWND(new IntPtr(-2)), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    internal record ProcessData(Process Process, HWND Handle)
    {
        public int OriginalStyleFlagsStandard { get; set; } = 0;
        public int OriginalStyleFlagsExtended { get; set; } = 0;
        public RECT OriginalLocation { get; set; }
    }


    [Flags]
    private enum WindowStyleFlags : uint
    {
        Overlapped = 0x00000000,
        Popup = 0x80000000,
        Child = 0x40000000,
        Minimize = 0x20000000,
        Visible = 0x10000000,
        Disabled = 0x08000000,
        ClipSiblings = 0x04000000,
        ClipChildren = 0x02000000,
        Maximize = 0x01000000,
        Border = 0x00800000,
        DialogFrame = 0x00400000,
        Vscroll = 0x00200000,
        Hscroll = 0x00100000,
        SystemMenu = 0x00080000,
        ThickFrame = 0x00040000,
        Group = 0x00020000,
        Tabstop = 0x00010000,

        MinimizeBox = 0x00020000,
        MaximizeBox = 0x00010000,

        Caption = Border | DialogFrame,
        Tiled = Overlapped,
        Iconic = Minimize,
        SizeBox = ThickFrame,
        TiledWindow = Overlapped,

        OverlappedWindow = Overlapped | Caption | SystemMenu | ThickFrame | MinimizeBox | MaximizeBox,
        ChildWindow = Child,

        ExtendedDlgModalFrame = 0x00000001,
        ExtendedNoParentNotify = 0x00000004,
        ExtendedTopmost = 0x00000008,
        ExtendedAcceptFiles = 0x00000010,
        ExtendedTransparent = 0x00000020,
        ExtendedMDIChild = 0x00000040,
        ExtendedToolWindow = 0x00000080,
        ExtendedWindowEdge = 0x00000100,
        ExtendedClientEdge = 0x00000200,
        ExtendedContextHelp = 0x00000400,
        ExtendedRight = 0x00001000,
        ExtendedLeft = 0x00000000,
        ExtendedRTLReading = 0x00002000,
        ExtendedLTRReading = 0x00000000,
        ExtendedLeftScrollbar = 0x00004000,
        ExtendedRightScrollbar = 0x00000000,
        ExtendedControlParent = 0x00010000,
        ExtendedStaticEdge = 0x00020000,
        ExtendedAppWindow = 0x00040000,
        ExtendedOverlappedWindow = ExtendedWindowEdge | ExtendedClientEdge,
        ExtendedPaletteWindow = ExtendedWindowEdge | ExtendedToolWindow | ExtendedTopmost,
        ExtendedLayered = 0x00080000,
        ExtendedNoinheritLayout = 0x00100000,
        ExtendedLayoutRTL = 0x00400000,
        ExtendedComposited = 0x02000000,
        ExtendedNoActivate = 0x08000000
    }
}