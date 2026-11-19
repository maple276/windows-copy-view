using System.Runtime.InteropServices;

namespace CopyHint;

/// <summary>
/// Windows 11 下让托盘/复制成功后的系统气泡看起来是圆角新版，
/// 需要给进程打两个私有 API 的补丁（官方没有公开开关）。失败就算了，不影响功能。
/// </summary>
internal static class ToolTipIconHelper
{
    [DllImport("ntdll.dll", SetLastError = true)]
    static extern int RtlGetVersion(ref OSVERSIONINFOEXW versionInfo);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfoForDpi(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    struct OSVERSIONINFOEXW
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    public static void EnableModernTooltips()
    {
        try
        {
            var vi = new OSVERSIONINFOEXW { dwOSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOEXW>() };
            if (RtlGetVersion(ref vi) != 0 || vi.dwBuildNumber < 22523) return;
            SystemParametersInfoForDpi(0x2011, 0, IntPtr.Zero, 0, 96); // SPI_SETTOOLTIPSCLOSEONTOUCH
            SystemParametersInfoForDpi(0x2012, 1, IntPtr.Zero, 0, 96); // SPI_SETTOOLTIPSROUNDED...
        }
        catch (Exception ex)
        {
            Program.Log("ModernTooltip unavailable: " + ex.Message);
        }
    }
}
