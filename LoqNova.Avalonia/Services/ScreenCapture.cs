using System;
using System.Runtime.InteropServices;
using System.Threading;
using LoqNova.Lib;
using LoqNova.Lib.Controllers;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Avalonia's implementation of the library's screen capture contract, used by
/// <c>SpectrumKeyboardBacklightController</c> to drive the keyboard from the
/// screen. WPF supplies the same contract through WinForms
/// <c>Graphics.CopyFromScreen</c>; this uses GDI directly so no WinForms or
/// System.Drawing dependency is added to the Avalonia front end.
/// </summary>
public sealed class ScreenCapture : SpectrumKeyboardBacklightController.IScreenCapture
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const int STRETCH_DELETESCANS = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors0;
        public uint bmiColors1;
        public uint bmiColors2;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint dwOffset);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int cxSrc, int cySrc,
        int rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    public void CaptureScreen(ref RGBColor[,] buffer, int width, int height, CancellationToken token)
    {
        if (buffer is null || width <= 0 || height <= 0)
            return;

        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);

        if (screenWidth <= 0 || screenHeight <= 0)
            return;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return;

        var memoryDc = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;
        var dib = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
                return;

            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB
                }
            };

            dib = CreateDIBSection(screenDc, ref info, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                return;

            previousBitmap = SelectObject(memoryDc, dib);
            SetStretchBltMode(memoryDc, STRETCH_DELETESCANS);

            if (!StretchBlt(memoryDc, 0, 0, width, height, screenDc, 0, 0, screenWidth, screenHeight, SRCCOPY))
                return;

            token.ThrowIfCancellationRequested();

            var maxX = Math.Min(width, buffer.GetLength(0));
            var maxY = Math.Min(height, buffer.GetLength(1));
            var stride = width * 4;

            for (var y = 0; y < maxY; y++)
            {
                var rowOffset = y * stride;

                for (var x = 0; x < maxX; x++)
                {
                    var offset = rowOffset + (x * 4);

                    var b = Marshal.ReadByte(bits, offset);
                    var g = Marshal.ReadByte(bits, offset + 1);
                    var r = Marshal.ReadByte(bits, offset + 2);

                    buffer[x, y] = new RGBColor(r, g, b);
                }

                if ((y & 0x3F) == 0)
                    token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            if (memoryDc != IntPtr.Zero)
            {
                if (previousBitmap != IntPtr.Zero)
                    SelectObject(memoryDc, previousBitmap);

                DeleteDC(memoryDc);
            }

            if (dib != IntPtr.Zero)
                DeleteObject(dib);

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
