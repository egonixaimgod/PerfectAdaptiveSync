// Perfect Adaptive Sync: pontos Hz, CRU timingok, EDID (Windows + CRU override), skalazas, szinformatum, modok, RTSS cap.
// Build: build.bat (a Windows beepitett C# 5 forditojaval, nem kell SDK)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

public static class Native {
  [StructLayout(LayoutKind.Sequential)]
  public struct PATH {
    public uint srcAdLo; public int srcAdHi; public uint srcId; public uint srcModeIdx; public uint srcStatus;
    public uint tgtAdLo; public int tgtAdHi; public uint tgtId; public uint tgtModeIdx; public uint outputTech;
    public uint rotation; public uint scaling; public uint rrNum; public uint rrDen; public uint scanOrder;
    public int targetAvailable; public uint tgtStatus; public uint flags;
  }
  [StructLayout(LayoutKind.Explicit, Size = 64)]
  public struct MODE {
    [FieldOffset(0)] public uint infoType; [FieldOffset(4)] public uint id;
    [FieldOffset(16)] public ulong pixelRate;
    [FieldOffset(24)] public uint hNum; [FieldOffset(28)] public uint hDen;
    [FieldOffset(32)] public uint vNum; [FieldOffset(36)] public uint vDen;
    [FieldOffset(40)] public uint activeCx; [FieldOffset(44)] public uint activeCy;
    [FieldOffset(48)] public uint totalCx; [FieldOffset(52)] public uint totalCy;
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct TARGET_NAME {
    public uint type; public uint size; public uint adLo; public int adHi; public uint id;
    public uint flags; public uint outputTech; public ushort mfg; public ushort product; public uint connector;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string name;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string path;
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct SOURCE_NAME {
    public uint type; public uint size; public uint adLo; public int adHi; public uint id;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdiName;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct COLOR_INFO {
    public uint type; public uint size; public uint adLo; public int adHi; public uint id;
    public uint flags; public uint encoding; public uint bpc;
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
    public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels;
    public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
  }
  [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint f, out uint np, out uint nm);
  [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint f, ref uint np, [Out] PATH[] p, ref uint nm, [Out] MODE[] m, IntPtr t);
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME r);
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME r);
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref COLOR_INFO r);
  // Win11 24H2+: GET_ADVANCED_COLOR_INFO_2 (15) / SET_WCG_STATE (17) = "Automatikus szinkezeles" (ACM)
  [StructLayout(LayoutKind.Sequential)]
  public struct COLOR_INFO2 {
    public uint type; public uint size; public uint adLo; public int adHi; public uint id;
    public uint flags; public uint encoding; public uint bpc; public uint activeColorMode;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct WCG_STATE { public uint type; public uint size; public uint adLo; public int adHi; public uint id; public uint enable; }
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref COLOR_INFO2 r);
  [DllImport("user32.dll")] public static extern int DisplayConfigSetDeviceInfo(ref WCG_STATE r);
  // GET_TARGET_PREFERRED_MODE (3): amit a driver/OS nativ modnak tart
  [StructLayout(LayoutKind.Explicit, Size = 80)]
  public struct PREF_MODE {
    [FieldOffset(0)] public uint type; [FieldOffset(4)] public uint size; [FieldOffset(8)] public uint adLo; [FieldOffset(12)] public int adHi;
    [FieldOffset(16)] public uint id; [FieldOffset(20)] public uint width; [FieldOffset(24)] public uint height;
  }
  [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref PREF_MODE r);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string dev, int n, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibrary(string path);
  [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] public static extern IntPtr GetProcAddress(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
}

public class Timing {
  public string Src; public int W, H; public double Hz, ClockMHz; public int HTotal, VTotal; public bool Interlaced;
  public bool Exact { get { return ClockMHz > 0; } }
}

public class Edid {
  public string Name = "", Range = "", Error;
  public int MinV, MaxV;  // range limits (DP Adaptive-Sync / G-Sync Compatible ebbol dolgozik)
  public Timing Preferred;  // base blokk elso DTD-je: ezt hiszi a driver nativnak
  public List<Timing> Timings = new List<Timing>();

  static readonly int[] Est = { 720,400,70, 720,400,88, 640,480,60, 640,480,67, 640,480,72, 640,480,75, 800,600,56, 800,600,60,
                                800,600,72, 800,600,75, 832,624,75, 1024,768,87, 1024,768,60, 1024,768,70, 1024,768,75, 1280,1024,75 };
  // shortcut: csak a gyakori CTA VIC-ek vannak benne, egy ritka VIC kimarad a listabol
  static readonly Dictionary<int, int[]> Vics = new Dictionary<int, int[]> {
    {1,new[]{640,480,60}}, {2,new[]{720,480,60}}, {3,new[]{720,480,60}}, {4,new[]{1280,720,60}}, {16,new[]{1920,1080,60}},
    {17,new[]{720,576,50}}, {18,new[]{720,576,50}}, {19,new[]{1280,720,50}}, {31,new[]{1920,1080,50}}, {32,new[]{1920,1080,24}},
    {33,new[]{1920,1080,25}}, {34,new[]{1920,1080,30}}, {41,new[]{1280,720,100}}, {47,new[]{1280,720,120}}, {63,new[]{1920,1080,120}},
    {64,new[]{1920,1080,100}}, {93,new[]{3840,2160,24}}, {94,new[]{3840,2160,25}}, {95,new[]{3840,2160,30}}, {96,new[]{3840,2160,50}},
    {97,new[]{3840,2160,60}}, {117,new[]{3840,2160,100}}, {118,new[]{3840,2160,120}} };

  public bool Has(int w, int h, int hz) { return Timings.Any(t => t.W == w && t.H == h && Math.Abs(t.Hz - hz) < 1); }

  void Add(string src, int w, int h, double hz, double mhz, int ht, int vt, bool il) {
    Timings.Add(new Timing { Src = src, W = w, H = h, Hz = hz, ClockMHz = mhz, HTotal = ht, VTotal = vt, Interlaced = il });
  }
  void AddRange(string r) { Range = Range.Length == 0 ? r : Range + "  ·  " + r; }

  // VRR tartomany nem a base blokkbol (DisplayID / FreeSync / HDMI VRR); a base range limits az elsodleges. max 0 = nincs megadva
  void Vrr(string src, int min, int max) {
    if (MaxV == 0) { MinV = min; MaxV = max; }
    AddRange(src + " " + min + "-" + (max > 0 ? max.ToString() : "?") + " Hz");
  }

  void Dtd(byte[] d, int o, string src) {
    int khz = (d[o] | d[o + 1] << 8) * 10;
    int ha = d[o + 2] | ((d[o + 4] & 0xF0) << 4), hb = d[o + 3] | ((d[o + 4] & 0x0F) << 8);
    int va = d[o + 5] | ((d[o + 7] & 0xF0) << 4), vb = d[o + 6] | ((d[o + 7] & 0x0F) << 8);
    if (ha + hb == 0 || va + vb == 0) return;
    bool il = (d[o + 17] & 0x80) != 0;
    int vt = va + vb;
    // interlaced: a DTD egy field-et ir le (1080i = 540 aktiv sor, 562.5 total)
    Add(src, ha, il ? va * 2 : va, khz * 1000.0 / ((ha + hb) * (il ? vt + 0.5 : vt)), khz / 1000.0, ha + hb, il ? vt * 2 + 1 : vt, il);
  }

  // DisplayID Type I (10 kHz egyseg) / Type VII (1 kHz egyseg), 20 byte-os leiro, minden mezo "ertek-1"
  void Did(byte[] d, int q, bool vii) {
    int raw = (d[q] | d[q + 1] << 8 | d[q + 2] << 16) + 1;
    double khz = vii ? raw : raw * 10.0;
    int ha = (d[q + 4] | d[q + 5] << 8) + 1, hb = (d[q + 6] | d[q + 7] << 8) + 1;
    int va = (d[q + 12] | d[q + 13] << 8) + 1, vb = (d[q + 14] | d[q + 15] << 8) + 1;
    Add(vii ? "DisplayID VII" : "DisplayID I", ha, va, khz * 1000 / ((ha + hb) * (double)(va + vb)), khz / 1000, ha + hb, va + vb, (d[q + 3] & 0x10) != 0);
  }

  public static Edid Parse(byte[] d) {
    try { return ParseRaw(d); }
    catch (IndexOutOfRangeException) { return new Edid { Error = "Hibás EDID (egy blokk túlnyúlik a végén)" }; }
  }

  static Edid ParseRaw(byte[] d) {
    var e = new Edid();
    if (d == null || d.Length < 128 || d[0] != 0 || d[1] != 0xFF || d[7] != 0) { e.Error = "Nincs / ervenytelen EDID"; return e; }

    int est = (d[35] << 8) | d[36];
    for (int i = 0; i < 16; i++)
      if ((est & (0x8000 >> i)) != 0) e.Add("Established", Est[i * 3], Est[i * 3 + 1], Est[i * 3 + 2], 0, 0, 0, i == 11);

    bool first = true;
    for (int o = 54; o <= 108; o += 18) {
      if (d[o] != 0 || d[o + 1] != 0) {
        int before = e.Timings.Count;
        e.Dtd(d, o, first ? "DTD (preferalt)" : "DTD");
        if (first && e.Timings.Count > before) e.Preferred = e.Timings[before];
        first = false;
      }
      else if (d[o + 3] == 0xFC) e.Name = Encoding.ASCII.GetString(d, o + 5, 13).Split('\n')[0].Trim();
      else if (d[o + 3] == 0xFD) {
        int f = d[o + 4], minV = d[o + 5], maxV = d[o + 6], minH = d[o + 7], maxH = d[o + 8];
        if ((f & 3) >= 2) maxV += 255;
        if ((f & 3) == 3) minV += 255;
        if ((f & 12) >= 8) maxH += 255;
        if ((f & 12) == 12) minH += 255;
        e.MinV = minV; e.MaxV = maxV;
        e.AddRange(string.Format("V {0}-{1} Hz, H {2}-{3} kHz, max {4} MHz", minV, maxV, minH, maxH, d[o + 9] * 10));
      }
    }

    for (int i = 0; i < 8; i++) {
      int b0 = d[38 + i * 2], b1 = d[39 + i * 2];
      if (b0 == 0 || (b0 == 1 && b1 == 1)) continue;
      int w = (b0 + 31) * 8, ar = b1 >> 6;
      int h = ar == 0 ? w * 10 / 16 : ar == 1 ? w * 3 / 4 : ar == 2 ? w * 4 / 5 : w * 9 / 16;
      e.Add("Standard", w, h, (b1 & 0x3F) + 60, 0, 0, 0, false);
    }

    for (int k = 1; k <= d[126] && 128 * k + 128 <= d.Length; k++) {
      int b = 128 * k;
      if (d[b] == 0x02) {  // CTA-861
        int dOff = d[b + 2];
        if (dOff < 4) continue;
        for (int i = b + 4; i < b + dOff; ) {
          int tag = d[i] >> 5, len = d[i] & 31;
          if (tag == 2)
            for (int j = 1; j <= len; j++) {
              int svd = d[i + j], vic = svd >= 129 && svd <= 192 ? svd & 0x7F : svd;
              int[] v;
              if (Vics.TryGetValue(vic, out v)) e.Add("CTA VIC " + vic, v[0], v[1], v[2], 0, 0, 0, false);
            }
          else if (tag == 3 && len >= 7 && d[i + 1] == 0x1A && d[i + 2] == 0 && d[i + 3] == 0) {
            // AMD FreeSync VSDB: 8 bites min/max; a v3 blokkban 10 bites max is van (255 Hz folott csak az a helyes)
            int max10 = len >= 15 ? d[i + 14] | (d[i + 15] & 3) << 8 : 0;
            e.Vrr("FreeSync", d[i + 6], max10 > 0 ? max10 : d[i + 7]);
          }
          else if (len >= 10 && ((tag == 3 && d[i + 1] == 0xD8 && d[i + 2] == 0x5D && d[i + 3] == 0xC4) || (tag == 7 && d[i + 1] == 0x79))) {
            // HDMI Forum VSDB / SCDB: VRRmin 6 bit (0 = nincs VRR), VRRmax 10 bit (0 = nincs megadva)
            int lo = d[i + 9] & 0x3F;
            if (lo > 0) e.Vrr("HDMI VRR", lo, (d[i + 9] & 0xC0) << 2 | d[i + 10]);
          }
          i += len + 1;
        }
        for (int o = b + dOff; o + 18 <= b + 127 && (d[o] != 0 || d[o + 1] != 0); o += 18) e.Dtd(d, o, "CTA DTD");
      } else if (d[b] == 0x70) {  // DisplayID
        int end = Math.Min(b + 5 + d[b + 2], b + 127);
        for (int i = b + 5; i + 3 <= end; ) {
          int tag = d[i], rev = d[i + 1], len = d[i + 2], p = i + 3;
          if (tag == 0 && len == 0) break;
          if (tag == 0x03 || tag == 0x22) {
            int size = tag == 0x22 ? 20 + ((rev >> 4) & 7) : 20;
            for (int q = p; q + size <= p + len && q + size <= end; q += size) e.Did(d, q, tag == 0x22);
          } else if (tag == 0x25 && len >= 9) {
            e.Vrr("VRR (DisplayID)", d[p + 6], d[p + 7] | ((d[p + 8] & 3) << 8));
          }
          i = p + len;
        }
      }
    }
    // HDMI VRR megadott max nelkul: a leggyorsabb mod a felso hatar
    if (e.MinV > 0 && e.MaxV == 0 && e.Timings.Count > 0) e.MaxV = (int)Math.Round(e.Timings.Max(t => t.Hz));
    return e;
  }
}

public class Mon {
  public string Name, Gdi, Output;
  public uint RrNum, RrDen, Scaling, ScanOrder, Rotation, ActiveW, ActiveH, TotalW, TotalH;
  public ulong PixelRate;
  public double HFreq;
  public int DeskW, DeskH, CurHz;
  public bool ColorOk, HdrOn, HdrSup; public uint ColorEnc, Bpc;
  public bool? AcmOn;
  public Size? DriverNative;
  public List<KeyValuePair<Size, SortedSet<int>>> Modes;
  public Edid WinEdid, Over;  // WinEdid: amit a Windows eltarolt (override utan mar nem a gyari)
  public Edid Active { get { return Over ?? WinEdid; } }
  public double WinHz { get { return (double)RrNum / RrDen; } }
  // a valodi frekvencia a timingbol jon (a Windows kerekithet, pl. 279.9996 -> 280/1)
  public double Hz { get { return PixelRate > 0 && TotalW > 0 && TotalH > 0 ? PixelRate / ((double)TotalW * TotalH) : WinHz; } }
  // a jelenlegi asztali felbontas legnagyobb Hz-e
  public int MaxHz { get { return Modes.Where(kv => kv.Key.Width == DeskW && kv.Key.Height == DeskH).Select(kv => kv.Value.Max).DefaultIfEmpty(CurHz).First(); } }
  // a panel valodi felbontasa: a legnagyobb pontos EDID timing (a DSR/DLDSR modok nincsenek benne), kulonben amit a driver mond
  public Size NativeRes {
    get {
      var e = Active;
      var t = e == null || e.Error != null ? null : e.Timings.Where(x => x.Exact && !x.Interlaced).OrderByDescending(x => x.W * x.H).FirstOrDefault();
      return t != null ? new Size(t.W, t.H) : DriverNative ?? (Modes.Count > 0 ? Modes[0].Key : new Size((int)ActiveW, (int)ActiveH));
    }
  }
  public override string ToString() { return Name + "  (" + Gdi + ")"; }
}

public static class Probe {
  static Native.DEVMODE NewDevMode() { var d = new Native.DEVMODE(); d.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE)); return d; }

  static string OutputName(uint t) {
    switch (t) {
      case 0: return "VGA"; case 4: return "DVI"; case 5: return "HDMI"; case 10: return "DisplayPort"; case 11: return "eDP (laptop)";
      case 0x80000000: return "Belso"; default: return "Egyeb (" + t + ")";
    }
  }

  // \\?\DISPLAY#GSM5B09#5&xxxx&0&UID4352#{guid} -> HKLM\...\Enum\DISPLAY\GSM5B09\5&xxxx&0&UID4352\Device Parameters
  static void ReadEdid(string devPath, Mon m) {
    var parts = (devPath ?? "").Replace(@"\\?\", "").Split('#');
    if (parts.Length < 3) return;
    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + parts[0] + "\\" + parts[1] + "\\" + parts[2] + @"\Device Parameters")) {
      if (k == null) return;
      var real = k.GetValue("EDID") as byte[];
      if (real != null) m.WinEdid = Edid.Parse(real);
      using (var o = k.OpenSubKey("EDID_OVERRIDE")) {
        if (o == null) return;
        var all = new List<byte>();
        byte[] blk;
        for (int i = 0; (blk = o.GetValue(i.ToString()) as byte[]) != null; i++) all.AddRange(blk);
        if (all.Count > 0) m.Over = Edid.Parse(all.ToArray());
      }
    }
  }

  public static void SetMode(string gdi, int w, int h, int hz) {
    var cur = NewDevMode();
    bool had = Native.EnumDisplaySettings(gdi, -1, ref cur);  // -1 = ENUM_CURRENT_SETTINGS
    var dm = NewDevMode();
    for (int k = 0; Native.EnumDisplaySettings(gdi, k, ref dm); k++) {
      if (dm.dmPelsWidth != w || dm.dmPelsHeight != h || dm.dmDisplayFrequency != hz || dm.dmBitsPerPel != 32) continue;
      dm.dmFields = 0x80000 | 0x100000 | 0x400000;  // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY
      int r = Native.ChangeDisplaySettingsEx(gdi, ref dm, IntPtr.Zero, 1, IntPtr.Zero);  // 1 = CDS_UPDATEREGISTRY
      if (r != 0) throw new Exception("Módváltás hiba: " + r);
      if (had) Undo.Save("mode|" + gdi, cur.dmPelsWidth + "|" + cur.dmPelsHeight + "|" + cur.dmDisplayFrequency);
      return;
    }
    throw new Exception("Nincs ilyen mód: " + w + "x" + h + " @ " + hz);
  }

  static Native.PATH[] Paths(out Native.MODE[] modes) {
    uint np, nm;
    int r = Native.GetDisplayConfigBufferSizes(2, out np, out nm);  // 2 = QDC_ONLY_ACTIVE_PATHS
    if (r != 0) throw new Exception("GetDisplayConfigBufferSizes hiba: " + r);
    var paths = new Native.PATH[np]; modes = new Native.MODE[nm];
    r = Native.QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero);
    if (r != 0) throw new Exception("QueryDisplayConfig hiba: " + r);
    return paths.Take((int)np).ToArray();
  }

  // ACM allapot; null ha a Windows/monitor nem tamogatja
  public static bool? Acm(uint adLo, int adHi, uint id) {
    Native.COLOR_INFO2 ci;
    if (!Info2(adLo, adHi, id, out ci) || (ci.flags & 0x40) == 0) return null;  // bit6 = wideColorSupported
    return (ci.flags & 0x80) != 0;  // bit7 = wideColorUserEnabled
  }

  // false, ha a Windows meg nem ismeri (24H2 elott)
  static bool Info2(uint adLo, int adHi, uint id, out Native.COLOR_INFO2 ci) {
    ci = new Native.COLOR_INFO2 { type = 15, adLo = adLo, adHi = adHi, id = id };
    ci.size = (uint)Marshal.SizeOf(typeof(Native.COLOR_INFO2));
    return Native.DisplayConfigGetDeviceInfo(ref ci) == 0;
  }

  public static void SetAcm(uint adLo, int adHi, uint id, bool on) {
    var st = new Native.WCG_STATE { type = 17, adLo = adLo, adHi = adHi, id = id, enable = on ? 1u : 0u };
    st.size = (uint)Marshal.SizeOf(typeof(Native.WCG_STATE));
    int r = Native.DisplayConfigSetDeviceInfo(ref st);
    if (r != 0) throw new Exception("ACM állítás hiba: " + r);
  }

  // minden aktiv kijelzon lekapcsolja az ACM-et, ha a Windows visszakapcsolta; true ha valtoztatott
  public static bool EnforceAcmOff() {
    Native.MODE[] modes;
    bool changed = false;
    foreach (var p in Paths(out modes))
      if (Acm(p.tgtAdLo, p.tgtAdHi, p.tgtId) == true) { SetAcm(p.tgtAdLo, p.tgtAdHi, p.tgtId, false); changed = true; }
    return changed;
  }

  public static List<Mon> All() {
    Native.MODE[] modes;
    var paths = Paths(out modes);
    uint nm = (uint)modes.Length;

    var list = new List<Mon>();
    for (int i = 0; i < paths.Length; i++) {
      var p = paths[i];
      var m = new Mon { RrNum = p.rrNum, RrDen = p.rrDen == 0 ? 1 : p.rrDen, Scaling = p.scaling, ScanOrder = p.scanOrder, Rotation = p.rotation, Output = OutputName(p.outputTech) };

      var tn = new Native.TARGET_NAME { type = 2, adLo = p.tgtAdLo, adHi = p.tgtAdHi, id = p.tgtId };
      tn.size = (uint)Marshal.SizeOf(typeof(Native.TARGET_NAME));
      Native.DisplayConfigGetDeviceInfo(ref tn);
      m.Name = string.IsNullOrEmpty(tn.name) ? "Monitor" : tn.name;

      var sn = new Native.SOURCE_NAME { type = 1, adLo = p.srcAdLo, adHi = p.srcAdHi, id = p.srcId };
      sn.size = (uint)Marshal.SizeOf(typeof(Native.SOURCE_NAME));
      Native.DisplayConfigGetDeviceInfo(ref sn);
      m.Gdi = sn.gdiName;

      var ci = new Native.COLOR_INFO { type = 9, adLo = p.tgtAdLo, adHi = p.tgtAdHi, id = p.tgtId };
      ci.size = (uint)Marshal.SizeOf(typeof(Native.COLOR_INFO));
      m.ColorOk = Native.DisplayConfigGetDeviceInfo(ref ci) == 0;
      m.ColorEnc = ci.encoding; m.Bpc = ci.bpc;
      // a regi API "advanced color" bitjei ACM (WCG) mellett is 1-ek; a 24H2-es kulon jelzi a HDR-t (bit4 = tamogatott, colorMode 2 = HDR aktiv)
      Native.COLOR_INFO2 c2;
      bool has2 = Info2(p.tgtAdLo, p.tgtAdHi, p.tgtId, out c2);
      m.HdrSup = has2 ? (c2.flags & 0x10) != 0 : (ci.flags & 1) != 0;
      m.HdrOn = has2 ? c2.activeColorMode == 2 : (ci.flags & 2) != 0;
      m.AcmOn = Acm(p.tgtAdLo, p.tgtAdHi, p.tgtId);
      var pm = new Native.PREF_MODE { type = 3, size = 80, adLo = p.tgtAdLo, adHi = p.tgtAdHi, id = p.tgtId };
      if (Native.DisplayConfigGetDeviceInfo(ref pm) == 0) m.DriverNative = new Size((int)pm.width, (int)pm.height);

      if (p.tgtModeIdx < nm && modes[p.tgtModeIdx].infoType == 2) {  // 2 = TARGET mod
        var t = modes[p.tgtModeIdx];
        m.PixelRate = t.pixelRate; m.ActiveW = t.activeCx; m.ActiveH = t.activeCy; m.TotalW = t.totalCx; m.TotalH = t.totalCy;
        m.HFreq = t.hDen == 0 ? 0 : (double)t.hNum / t.hDen;
      }

      var dm = NewDevMode();
      Native.EnumDisplaySettings(m.Gdi, -1, ref dm);  // -1 = ENUM_CURRENT_SETTINGS
      m.DeskW = dm.dmPelsWidth; m.DeskH = dm.dmPelsHeight; m.CurHz = dm.dmDisplayFrequency;

      var all = new Dictionary<Size, SortedSet<int>>();
      dm = NewDevMode();
      for (int k = 0; Native.EnumDisplaySettings(m.Gdi, k, ref dm); k++) {
        var key = new Size(dm.dmPelsWidth, dm.dmPelsHeight);
        SortedSet<int> set;
        if (!all.TryGetValue(key, out set)) all[key] = set = new SortedSet<int>();
        set.Add(dm.dmDisplayFrequency);
      }
      m.Modes = all.OrderByDescending(kv => kv.Key.Width * kv.Key.Height).ThenByDescending(kv => kv.Key.Width).ToList();

      ReadEdid(tn.path, m);
      list.Add(m);
    }
    return list;
  }
}

// NVIDIA driver profilok (amit az NVCP "Manage 3D settings" allit) olvasasa / irasa NVAPI DRS-sel
public static class Nv {
  [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr QI(uint id);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D0();
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D1(out IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D2(IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D3(IntPtr s, out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DGet(IntPtr s, IntPtr p, uint id, IntPtr setting);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DSet(IntPtr s, IntPtr p, IntPtr setting);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DEnum(IntPtr s, uint i, out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DFind(IntPtr s, IntPtr name, out IntPtr p);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DDel(IntPtr s, IntPtr p, uint id);
  static T F<T>(uint id) {
    IntPtr f = QI(id);
    if (f == IntPtr.Zero) throw new Exception("NVAPI fuggveny hianyzik: 0x" + id.ToString("X8"));
    return (T)(object)Marshal.GetDelegateForFunctionPointer(f, typeof(T));
  }
  // NVDRS_SETTING_V1 / NVDRS_PROFILE_V1 meretek es offsetek
  const int SettingSize = 12320, IdOffset = 4100, LocationOffset = 4108, PredefinedOffset = 4112, CurrentValueOffset = 8220, ProfileSize = 4116;

  public const uint GsyncGlobal = 0x1194F158, GsyncApp = 0x10A879CF, Vsync = 0x00A879CF, PreRender = 0x007BA09E, Power = 0x1057EB71, RefreshRate = 0x0064B541;
  public const uint VsyncOn = 0x47814940, VsyncOff = 0x08416747, VsyncApp = 0x60925292, VsyncFast = 0x18888888;

  public class Override { public string Profile; public uint Id, Value; }
  public static bool Saved;  // a nyitott NVCP nem olvassa ujra -> a UI szol, hogy inditsa ujra

  static void Check(int r, string what) {
    if (r == -137) throw new UnauthorizedAccessException("NVAPI: admin jog kell (" + what + ")");  // NVAPI_INVALID_USER_PRIVILEGE
    if (r != 0) throw new Exception("NVAPI hiba " + r + " (" + what + ")");
  }

  // body(session, globalis profil, setting buffer)
  static T Session<T>(Func<IntPtr, IntPtr, IntPtr, T> body) {
    Check(F<D0>(0x0150E828)(), "init");
    IntPtr s;
    Check(F<D1>(0x0694D52E)(out s), "session");
    IntPtr buf = Marshal.AllocHGlobal(SettingSize);
    try {
      Check(F<D2>(0x375DBD6B)(s), "load");
      IntPtr p;
      Check(F<D3>(0x617BFF9F)(s, out p), "global profile");
      return body(s, p, buf);
    } finally {
      Marshal.FreeHGlobal(buf);
      F<D2>(0xDAD9CFF8)(s);
    }
  }

  // id -> ertek; ami nincs benne, az alapertelmezett a driverben
  public static Dictionary<uint, uint> Global(params uint[] ids) {
    return Session((s, p, buf) => {
      var r = new Dictionary<uint, uint>();
      var get = F<DGet>(0x73BF8338);
      foreach (uint id in ids) {
        Marshal.WriteInt32(buf, 0, SettingSize | (1 << 16));
        if (get(s, p, id, buf) == 0) r[id] = (uint)Marshal.ReadInt32(buf, CurrentValueOffset);
      }
      return r;
    });
  }

  static void Put(IntPtr s, IntPtr p, IntPtr buf, uint id, uint value) {
    Marshal.Copy(new byte[SettingSize], 0, buf, SettingSize);
    Marshal.WriteInt32(buf, 0, SettingSize | (1 << 16));
    Marshal.WriteInt32(buf, IdOffset, (int)id);  // settingType 0 = DWORD
    Marshal.WriteInt32(buf, CurrentValueOffset, (int)value);
    Check(F<DSet>(0x577DD202)(s, p, buf), "set");
  }

  // a profil sajat (kezzel beallitott) erteke; null, ha a driver alapertelmezese el
  static uint? Own(IntPtr s, IntPtr p, IntPtr buf, uint id) {
    Marshal.WriteInt32(buf, 0, SettingSize | (1 << 16));
    if (F<DGet>(0x73BF8338)(s, p, id, buf) != 0 || Marshal.ReadInt32(buf, PredefinedOffset) != 0) return null;
    return (uint)Marshal.ReadInt32(buf, CurrentValueOffset);
  }

  static void Save(IntPtr s) { Check(F<D2>(0xFCBC7E14)(s), "save"); Saved = true; }

  public static void SetGlobal(uint id, uint value) {
    Session<object>((s, p, buf) => {
      uint? orig = Own(s, p, buf, id);
      Put(s, p, buf, id, value);
      Save(s);
      Undo.Save("nv|" + id.ToString("X8"), orig.ToString());  // "" = nem volt beallitva
      return null;
    });
  }

  // a globalis profil sajat erteket torli -> driver alapertelmezes
  public static void DeleteGlobal(uint id) {
    Session<object>((s, p, buf) => {
      int r = F<DDel>(0xE4A26362)(s, p, id);
      if (r != -160) Check(r, "delete");  // -160 = nincs is beallitva
      Save(s);
      return null;
    });
  }

  // Az NVCP Low Latency kapcsoloja (0 Off, 1 On, 2 Ultra) NVAPI-val rejtett (-160), ezert a DRS fajlbol olvassuk.
  // Rekord: A4 00 10 00 | id | 02 10 00 00 | ertek; a globalis profil az a rekordsor, amiben minden NVAPI-tol kapott globalis ertek benne van.
  // shortcut: nem hivatalos fajlformatum, ha egy driver atirja -> null (a UI kezi ellenorzest ker)
  public const uint LowLatencyCpl = 0x0005F543;
  public static int? LowLatency(Dictionary<uint, uint> global) {
    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"NVIDIA Corporation\Drs");
    byte sel = ReadShared(System.IO.Path.Combine(dir, "nvdrssel.bin"))[0];
    return LowLatency(ReadShared(System.IO.Path.Combine(dir, "nvdrsdb" + (sel >= '0' ? sel - '0' : sel) + ".bin")), global);
  }

  public static int? LowLatency(byte[] b, Dictionary<uint, uint> global) {
    if (global.Count < 3) return null;  // kevés ertekkel nem azonosithato biztosan a globalis profil
    Func<int, bool> rec = i => b[i] == 0xA4 && b[i + 1] == 0 && b[i + 2] == 0x10 && b[i + 3] == 0 && b[i + 8] == 2 && b[i + 9] == 0x10 && b[i + 10] == 0 && b[i + 11] == 0;
    int? found = null;
    for (int i = 0; i + 16 <= b.Length; ) {
      if (!rec(i)) { i++; continue; }
      var run = new Dictionary<uint, uint>();
      for (; i + 16 <= b.Length && rec(i); i += 16) run[BitConverter.ToUInt32(b, i + 4)] = BitConverter.ToUInt32(b, i + 12);
      uint v;
      if (!global.All(kv => run.TryGetValue(kv.Key, out v) && v == kv.Value)) continue;
      if (found != null) return null;  // tobb profil is illeszkedik -> nem egyertelmu
      found = run.TryGetValue(LowLatencyCpl, out v) ? (int)v : 0;  // nincs beirva = Off (driver default)
    }
    return found;
  }

  static byte[] ReadShared(string path) {
    using (var f = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete)) {
      var buf = new byte[f.Length];
      for (int n = 0; n < buf.Length; ) { int r = f.Read(buf, n, buf.Length - n); if (r <= 0) throw new System.IO.EndOfStreamException(path); n += r; }
      return buf;
    }
  }

  // jatekprofilokban kezzel atallitott ertekek (az NVIDIA altal elore beallitottak nem)
  public static List<Override> UserOverrides(params uint[] ids) {
    return Session((s, global, buf) => {
      var list = new List<Override>();
      var en = F<DEnum>(0xBC371EE0); var get = F<DGet>(0x73BF8338); var info = F<DSet>(0x61CD6FD6);
      IntPtr pb = Marshal.AllocHGlobal(ProfileSize);
      try {
        IntPtr p;
        for (uint i = 0; en(s, i, out p) == 0; i++) {
          if (p == global) continue;
          foreach (uint id in ids) {
            Marshal.WriteInt32(buf, 0, SettingSize | (1 << 16));
            if (get(s, p, id, buf) != 0 || Marshal.ReadInt32(buf, LocationOffset) != 0 || Marshal.ReadInt32(buf, PredefinedOffset) != 0) continue;
            Marshal.WriteInt32(pb, 0, ProfileSize | (1 << 16));
            info(s, p, pb);
            list.Add(new Override { Profile = Marshal.PtrToStringUni(pb + 4), Id = id, Value = (uint)Marshal.ReadInt32(buf, CurrentValueOffset) });
          }
        }
      } finally { Marshal.FreeHGlobal(pb); }
      return list;
    });
  }

  // body a nev szerinti jatekprofilon fut, utana ment
  static void InProfile(string profile, Action<IntPtr, IntPtr, IntPtr> body) {
    Session<object>((s, g, buf) => {
      IntPtr name = Marshal.AllocHGlobal(4096);
      try {
        Marshal.Copy(new byte[4096], 0, name, 4096);
        var chars = profile.ToCharArray();
        Marshal.Copy(chars, 0, name, Math.Min(chars.Length, 2047));
        IntPtr p;
        Check(F<DFind>(0x7E4A9A0B)(s, name, out p), "find profile");
        body(s, p, buf);
        Save(s);
      } finally { Marshal.FreeHGlobal(name); }
      return null;
    });
  }

  // jatekprofil sajat erteket torli -> a globalis ervenyes ra
  public static void DeleteOverride(string profile, uint id) {
    uint? orig = null;
    InProfile(profile, (s, p, buf) => { orig = Own(s, p, buf, id); Check(F<DDel>(0xE4A26362)(s, p, id), "delete"); });
    if (orig != null) Undo.Save("nvprofile|" + id.ToString("X8") + "|" + profile, orig.ToString());
  }

  public static void SetOverride(string profile, uint id, uint value) { InProfile(profile, (s, p, buf) => Put(s, p, buf, id, value)); }
}

// RTSS profilok: [Framerate] Limit/LimitDenominator, SyncLimiter (0 async, 1 front edge, 2 back edge)
public static class Rtss {
  public class Profile { public string Name, Path; public double Cap; public int Mode = -1; public bool HasLimit; }

  public static string Dir {
    get {
      return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Unwinder\RTSS", "InstallDir", null) as string
             ?? @"C:\Program Files (x86)\RivaTuner Statistics Server";
    }
  }
  public static string GlobalPath { get { return System.IO.Path.Combine(Dir, @"Profiles\Global"); } }

  public static Profile Parse(string path) {
    if (!System.IO.File.Exists(path)) return null;
    var pr = new Profile { Path = path, Name = System.IO.Path.GetFileName(path).Replace(".cfg", "") };
    bool inFr = false; double lim = 0, den = 1;
    foreach (var raw in System.IO.File.ReadAllLines(path)) {
      string line = raw.Trim();
      if (line.StartsWith("[")) { inFr = line == "[Framerate]"; continue; }
      if (!inFr) continue;
      var kv = line.Split(new[] { '=' }, 2);
      double v;
      if (kv.Length != 2 || !double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
      if (kv[0] == "Limit") { lim = v; pr.HasLimit = true; }
      else if (kv[0] == "LimitDenominator" && v > 0) den = v;
      else if (kv[0] == "SyncLimiter") pr.Mode = (int)v;
    }
    pr.Cap = lim / den;
    return pr;
  }

  // jatekonkenti profilok (Profiles\*.cfg), amik sajat limitet adnak meg
  public static List<Profile> Games() {
    var dir = System.IO.Path.Combine(Dir, "Profiles");
    if (!System.IO.Directory.Exists(dir)) return new List<Profile>();
    return System.IO.Directory.GetFiles(dir, "*.cfg").Select(Parse).Where(p => p != null && p.HasLimit).ToList();
  }

  // a [Framerate] szekcio Limit/LimitDenominator sorait cseréli, minden mas marad
  public static List<string> WithLimit(IEnumerable<string> lines, double cap) {
    string[] mine = { "Limit=" + (long)Math.Round(cap * 1000), "LimitDenominator=1000" };
    var outp = new List<string>();
    bool inFr = false, done = false;
    foreach (var raw in lines) {
      string t = raw.Trim();
      if (t.StartsWith("[")) inFr = t == "[Framerate]";
      else if (inFr && (t.StartsWith("Limit=") || t.StartsWith("LimitDenominator="))) continue;
      outp.Add(raw);
      if (inFr && t == "[Framerate]") { outp.AddRange(mine); done = true; }
    }
    if (!done) { outp.Add("[Framerate]"); outp.AddRange(mine); }
    return outp;
  }

  public static void SetLimit(string path, double cap, bool apply = true) {
    string backup = path + ".pas-backup";
    if (!System.IO.File.Exists(backup)) System.IO.File.Copy(path, backup);
    var before = Parse(path);
    var enc = Encoding.Default;  // az RTSS ANSI-ban ir; UTF-8-kent olvasva az ekezetes ertekek elromlananak
    System.IO.File.WriteAllLines(path, WithLimit(System.IO.File.ReadAllLines(path, enc), cap), enc);
    if (before != null) Undo.Save("rtss|" + path, before.Cap.ToString("R", CultureInfo.InvariantCulture));
    if (!apply) return;
    var running = Process.GetProcessesByName("RTSS");
    if (running.Length == 0 || UpdateProfiles()) return;
    // regi RTSS profil API nelkul: csak indulaskor olvassa be a profilokat
    foreach (var p in running) { p.Kill(); p.WaitForExit(3000); }
    Process.Start(System.IO.Path.Combine(Dir, "RTSS.exe"));
  }

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
  // RTSS SDK (RTSSProfileInterface): profil a lemezre, majd UpdateProfiles -> a futo jatekok ujraolvassak, nem kell az RTSS-t kiloni.
  // false, ha ez az RTSS meg nem tudja
  public static bool UpdateProfiles() {
    IntPtr h = Native.LoadLibrary(System.IO.Path.Combine(Dir, "RTSSHooks64.dll"));
    IntPtr f = h == IntPtr.Zero ? IntPtr.Zero : Native.GetProcAddress(h, "UpdateProfiles");
    if (f == IntPtr.Zero) return false;
    ((VoidFn)Marshal.GetDelegateForFunctionPointer(f, typeof(VoidFn)))();
    return true;
  }
}

// RTSS shared memory: az eppen futo jatek FPS-e es frametime-ja
public static class RtssLive {
  public class Sample { public string App; public double Fps, FrameTimeMs; public uint Pid, T1; }
  static MemoryMappedFile mmf;
  static MemoryMappedViewAccessor acc;
  static uint lastT1, curPid;
  static int lastChange, failedAt = Environment.TickCount - 1000;

  // tobb hookolt app kozul: az eloterben levo > a mostani (amig frissul) > a legfrissebb; kulonben a grafikon ugralna koztuk
  public static Sample Pick(List<Sample> all, uint fg, uint cur) {
    Sample best = null, f = null, c = null;
    foreach (var s in all) {
      if (best == null || s.T1 >= best.T1) best = s;
      if (s.Pid == fg) f = s;
      if (s.Pid == cur) c = s;
    }
    if (f != null && best.T1 - f.T1 < 2000) return f;
    if (c != null && best.T1 - c.T1 < 2000) return c;
    return best;
  }

  // shortcut: ~60/s mintavetelezi az RTSS utolso frametime-jat, nem minden frame-et; frame-pontos elemzeshez PresentMon kellene
  public static Sample Read() {
    if (acc == null && Environment.TickCount - failedAt < 1000) return null;  // RTSS nem fut: eleg masodpercenkent ujraprobalni
    try {
      if (acc == null) {
        mmf = MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2", MemoryMappedFileRights.Read);
        acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
      }
      if (acc.ReadUInt32(0) != 0x52545353) return null;  // 'RTSS'
      uint es = acc.ReadUInt32(8), ao = acc.ReadUInt32(12), n = acc.ReadUInt32(16);
      var all = new List<Sample>();
      for (uint i = 0; i < n; i++) {
        long o = ao + (long)i * es;  // RTSS_SHARED_MEMORY_APP_ENTRY
        uint pid = acc.ReadUInt32(o);
        if (pid == 0) continue;
        uint t0 = acc.ReadUInt32(o + 268), t1 = acc.ReadUInt32(o + 272), fr = acc.ReadUInt32(o + 276), ft = acc.ReadUInt32(o + 280);
        if (fr == 0 || t1 <= t0) continue;
        var nb = new byte[260];
        acc.ReadArray(o + 4, nb, 0, 260);
        all.Add(new Sample { Pid = pid, T1 = t1, App = System.IO.Path.GetFileName(Encoding.ASCII.GetString(nb).Split('\0')[0]), Fps = 1000.0 * fr / (t1 - t0), FrameTimeMs = ft / 1000.0 });
      }
      uint fg;
      Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out fg);
      Sample best = Pick(all, fg, curPid);
      uint bestT = best == null ? 0 : best.T1;
      if (best != null) curPid = best.Pid;
      int now = Environment.TickCount;
      if (bestT != lastT1) { lastT1 = bestT; lastChange = now; }
      return best != null && now - lastChange < 2000 ? best : null;  // 2 mp ota nem frissult -> nincs aktiv jatek
    } catch {
      if (acc != null) acc.Dispose();
      if (mmf != null) mmf.Dispose();
      acc = null; mmf = null;
      failedAt = Environment.TickCount;
      return null;
    }
  }
}

// a program sajat beallitasai
public static class Settings {
  const string Key = @"HKEY_CURRENT_USER\Software\PerfectAdaptiveSync";
  public static bool BlockAcm {
    get { return (Registry.GetValue(Key, "BlockAcm", null) as int? ?? 0) == 1; }
    set { Registry.SetValue(Key, "BlockAcm", value ? 1 : 0, RegistryValueKind.DWord); }
  }
}

// A javitasok elotti eredeti ertekek: HKCU\Software\PerfectAdaptiveSync\Undo; nev = "fajta|mit", ertek = az eredeti ("" = nem volt beallitva)
public static class Undo {
  const string Key = @"Software\PerfectAdaptiveSync\Undo";
  public static bool On = true;  // a verify.bat maga allit vissza mindent, ott nem kell

  // csak az elso iras elotti allapotot orzi meg (mint a .pas-backup), es csak sikeres iras utan hivjuk
  public static void Save(string what, string orig) {
    if (!On) return;
    using (var k = Registry.CurrentUser.CreateSubKey(Key)) if (k.GetValue(what) == null) k.SetValue(what, orig);
  }

  public static Dictionary<string, string> All() {
    using (var k = Registry.CurrentUser.OpenSubKey(Key))
      return k == null ? new Dictionary<string, string>() : k.GetValueNames().ToDictionary(n => n, n => k.GetValue(n) as string ?? "");
  }

  public static void Restore(string what) {
    string v;
    if (!All().TryGetValue(what, out v)) return;
    var a = what.Split(new[] { '|' }, 3);
    if (a[0] == "nv") { uint id = Convert.ToUInt32(a[1], 16); if (v == "") Nv.DeleteGlobal(id); else Nv.SetGlobal(id, uint.Parse(v)); }
    else if (a[0] == "nvprofile") Nv.SetOverride(a[2], Convert.ToUInt32(a[1], 16), uint.Parse(v));
    else if (a[0] == "reg") Win.Restore(a[1], a[2], v);
    else if (a[0] == "rtss") Rtss.SetLimit(a[1], double.Parse(v, CultureInfo.InvariantCulture));
    else if (a[0] == "mode") { var m = v.Split('|'); Probe.SetMode(a[1], int.Parse(m[0]), int.Parse(m[1]), int.Parse(m[2])); }
    using (var k = Registry.CurrentUser.OpenSubKey(Key, true)) k.DeleteValue(what, false);
  }
}

// Windows jatekos beallitasok
public static class Win {
  const string Gfx = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
  const string GameBar = @"HKEY_CURRENT_USER\Software\Microsoft\GameBar";
  const string Dx = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences";

  public static int? Hags() { return Registry.GetValue(Gfx, "HwSchMode", null) as int?; }
  // val: int (DWORD) vagy string
  public static void Set(string key, string name, object val) {
    object orig = Registry.GetValue(key, name, null);
    Registry.SetValue(key, name, val);
    Undo.Save("reg|" + key + "|" + name, orig == null ? "" : (orig is int ? "d:" : "s:") + orig);
  }
  public static void Restore(string key, string name, string orig) {
    if (orig != "") { Registry.SetValue(key, name, orig.StartsWith("d:") ? (object)int.Parse(orig.Substring(2)) : orig.Substring(2)); return; }
    var root = key.StartsWith("HKEY_LOCAL_MACHINE") ? Registry.LocalMachine : Registry.CurrentUser;
    using (var k = root.OpenSubKey(key.Substring(key.IndexOf('\\') + 1), true)) if (k != null) k.DeleteValue(name, false);
  }

  public static void SetHags() { Set(Gfx, "HwSchMode", 2); }
  public static int? GameMode() { return Registry.GetValue(GameBar, "AutoGameModeEnabled", null) as int?; }
  public static void SetGameMode() { Set(GameBar, "AutoGameModeEnabled", 1); }

  // DirectXUserGlobalSettings: "VRROptimizeEnable=1;SwapEffectUpgradeEnable=1;"
  public static string DxGet(string key) {
    var s = Registry.GetValue(Dx, "DirectXUserGlobalSettings", null) as string ?? "";
    var kv = s.Split(';').Select(x => x.Split('=')).FirstOrDefault(x => x.Length == 2 && x[0] == key);
    return kv == null ? null : kv[1];
  }
  public static string DxMerge(string s, string key, string val) {
    var parts = (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith(key + "=")).ToList();
    parts.Add(key + "=" + val);
    return string.Join(";", parts) + ";";
  }
  public static void DxSet(string key) {
    Set(Dx, "DirectXUserGlobalSettings", DxMerge(Registry.GetValue(Dx, "DirectXUserGlobalSettings", null) as string, key, "1"));
  }
}

class MainForm : Form {
  static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
  static readonly Color Bg = Color.FromArgb(18, 18, 22), CardBg = Color.FromArgb(28, 28, 35), ChipBg = Color.FromArgb(44, 44, 56),
    DimChip = Color.FromArgb(33, 33, 41), Fg = Color.FromArgb(232, 232, 238), Muted = Color.FromArgb(139, 139, 153),
    Accent = Color.FromArgb(74, 222, 128), Yellow = Color.FromArgb(250, 204, 21), Orange = Color.FromArgb(251, 146, 60), Warn = Color.FromArgb(248, 113, 113);

  float scale = 1;
  ComboBox combo;
  TableLayoutPanel content;
  ToolTip tip = new ToolTip();
  int bad, lastStats, lastAcm;
  bool blockAcm = Settings.BlockAcm;
  Label summary, liveStatus, liveApp, liveFps, liveFt, liveLow, liveCapPct, liveIn, liveLfc, liveOver;
  Graph graph;
  List<double> samples = new List<double>();
  string liveAppName;
  List<Nv.Override> nvOverrides;  // ~0.3 mp a beolvasas, ezert csak Frissites / javitas utan ujra
  string nvOvErr;
  double liveCapFt, liveMinFt, liveLowFt;
  Button undo, fixAll;
  ContextMenuStrip undoMenu = new ContextMenuStrip();
  List<Action> fixes = new List<Action>();  // a piros sorok javitasai a "Mindet javítsd" gombhoz

  public MainForm() {
    using (var g = CreateGraphics()) scale = g.DpiX / 96f;
    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    Text = "Perfect Adaptive Sync"; BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 10f);
    AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.CenterScreen;
    var wa = Screen.PrimaryScreen.WorkingArea; ClientSize = new Size(Math.Min(S(1250), wa.Width - 40), Math.Min(S(1100), wa.Height - 80)); MinimumSize = new Size(S(900), S(700));

    var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(S(12)), ColumnCount = 1, RowCount = 2 };
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(S(6), 0, S(6), S(6)) };
    header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    var title = L("Perfect Adaptive Sync", Fg, 18f, FontStyle.Bold); title.Anchor = AnchorStyles.Left;
    combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = ChipBg, ForeColor = Fg, Width = S(340), Margin = new Padding(S(6)) };
    combo.SelectedIndexChanged += (s, e) => { if (combo.SelectedItem != null) ShowMon((Mon)combo.SelectedItem); };
    var refresh = Btn("Frissítés"); refresh.Click += (s, e) => { nvOverrides = null; Reload(); };
    var right = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty };
    undo = Btn("Visszaállítás"); undo.Visible = false;
    undo.Click += (s, e) => {
      undoMenu.Items.Clear();
      var all = Undo.All().Keys.ToList();
      foreach (var w in all) { var ww = w; undoMenu.Items.Add(UndoLabel(w), null, (a, b) => RunFix(() => Undo.Restore(ww))); }
      if (all.Count > 1) undoMenu.Items.Add("Mindet vissza", null, (a, b) => RunFix(() => RunAll(all.Select(w => (Action)(() => Undo.Restore(w))))));
      undoMenu.Show(undo, new Point(0, undo.Height));
    };
    right.Controls.Add(combo); right.Controls.Add(undo); right.Controls.Add(refresh);
    header.Controls.Add(title, 0, 0); header.Controls.Add(right, 2, 0);

    content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
    content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
    content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    page.Controls.Add(content);
    root.Controls.Add(header, 0, 0); root.Controls.Add(page, 0, 1);
    Controls.Add(root);

    // CRU restart64 / modvaltas utan magatol frissul
    SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    FormClosed += (s, e) => SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
    Load += (s, e) => Reload();

    var timer = new Timer { Interval = 15 };
    timer.Tick += LiveTick;
    timer.Start();
    FormClosed += (s, e) => timer.Dispose();
  }

  protected override void OnHandleCreated(EventArgs e) {
    base.OnHandleCreated(e);
    int on = 1;
    Native.DwmSetWindowAttribute(Handle, 20, ref on, 4);  // sotet cimsor
  }

  void OnDisplayChanged(object s, EventArgs e) { if (IsHandleCreated) BeginInvoke((Action)Reload); }

  void Reload() {
    if (blockAcm) try { Probe.EnforceAcmOff(); } catch { }
    int undos = Undo.All().Count;
    undo.Visible = undos > 0; undo.Text = "Visszaállítás (" + undos + ")";
    List<Mon> mons;
    try { mons = Probe.All(); }
    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Hiba", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
    var prev = combo.SelectedItem as Mon;
    combo.Items.Clear();
    foreach (var m in mons) combo.Items.Add(m);
    if (mons.Count > 0) combo.SelectedIndex = prev == null ? 0 : Math.Max(0, mons.FindIndex(x => x.Gdi == prev.Gdi));
  }

  // ---------- UI helperek ----------
  int S(int px) { return (int)Math.Round(px * scale); }
  static string F2(double v) { return v.ToString("0.00", Inv); }
  static string F3(double v) { return v.ToString("0.000", Inv); }
  static string Res(long w, long h) { return w + "x" + h; }

  Label L(string text, Color c, float size = 10f, FontStyle st = FontStyle.Regular) {
    return new Label { Text = text, ForeColor = c, AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", size, st), Anchor = AnchorStyles.Left | AnchorStyles.Top };
  }

  Button Btn(string text) {
    var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = ChipBg, ForeColor = Fg, AutoSize = true, Cursor = Cursors.Hand,
                         Padding = new Padding(S(6), S(1), S(6), S(1)), Margin = new Padding(S(4), S(2), 0, S(2)) };
    b.FlatAppearance.BorderSize = 0;
    b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 76);
    return b;
  }

  Button CopyBtn(string val) {
    var b = Btn("Másol");
    b.Anchor = AnchorStyles.Right;
    b.Click += (s, e) => {
      try { Clipboard.SetText(val); b.Text = "Másolva ✓"; }
      catch (ExternalException) { b.Text = "Foglalt – újra"; }  // mas program epp fogja a vagolapot
    };
    return b;
  }

  TableLayoutPanel Card(string title) {
    var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = CardBg,
                                   Padding = new Padding(S(16), S(12), S(16), S(14)), Margin = new Padding(S(6)), ColumnCount = 3 };
    t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    var h = L(title.ToUpper(), Muted, 8.5f, FontStyle.Bold);
    h.Margin = new Padding(0, 0, 0, S(8));
    Span(t, h, SizeType.AutoSize);
    return t;
  }

  void FixedHeight(TableLayoutPanel card) { card.AutoSize = false; card.Height = S(520); card.MinimumSize = new Size(0, S(520)); }

  void Span(TableLayoutPanel t, Control c, SizeType rowType) {
    int r = t.RowCount++;
    t.RowStyles.Add(rowType == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
    t.Controls.Add(c, 0, r);
    t.SetColumnSpan(c, 3);
  }

  Label Row(TableLayoutPanel t, string key, string val, Color? col = null, Control extra = null) {
    var v = L(val, col ?? Fg);
    RowCtl(t, key, v, extra);
    return v;
  }

  void RowCtl(TableLayoutPanel t, string key, Control val, Control extra = null) {
    int r = t.RowCount++;
    t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    var k = L(key, Muted); k.Margin = new Padding(0, S(4), S(18), S(4));
    val.Margin = new Padding(0, S(4), 0, S(4));
    t.Controls.Add(k, 0, r); t.Controls.Add(val, 1, r);
    if (extra != null) t.Controls.Add(extra, 2, r);
  }

  Panel Scroller(TableLayoutPanel inner) {
    var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    inner.Dock = DockStyle.Top; inner.AutoSize = true; inner.Margin = Padding.Empty;
    p.Controls.Add(inner);
    return p;
  }

  void GridRow(TableLayoutPanel g, Color c, bool bold, params string[] cells) {
    int r = g.RowCount++;
    g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    for (int i = 0; i < cells.Length; i++) {
      var l = L(cells[i], c, 9.5f, bold ? FontStyle.Bold : FontStyle.Regular);
      l.Margin = new Padding(0, S(3), S(18), S(3));
      g.Controls.Add(l, i, r);
    }
  }

  // ---------- checklist + javitas ----------
  static bool CapOk(double cap, double max) { return cap > 0 && cap <= max + 0.005; }
  static string CapText(double cap, double max) {
    return cap <= 0 ? "Nincs limit → max " + F2(max) : cap.ToString("0.###", Inv) + (CapOk(cap, max) ? " ≤ " + F2(max) : " túl magas → max " + F2(max));
  }
  static string VsyncName(uint v) {
    return v == Nv.VsyncOn ? "On" : v == Nv.VsyncOff ? "Off" : v == Nv.VsyncFast ? "Fast Sync" : v == Nv.VsyncApp ? "Alkalmazás vezérli" : "Egyéb (0x" + v.ToString("X") + ")";
  }

  void Check(TableLayoutPanel t, string name, bool? ok, string detail, Action fix = null) {
    if (ok == false) bad++;
    if (ok == false && fix != null) fixes.Add(fix);
    Button b = null;
    if (ok != true && fix != null) {
      b = Btn("Javítás");
      b.Anchor = AnchorStyles.Right;
      b.Click += (s, e) => RunFix(fix);
    }
    Row(t, name, (ok == true ? "✔  " : ok == false ? "✖  " : "•  ") + detail, ok == true ? Accent : ok == false ? Warn : Muted, b);
  }

  // mindet lefuttatja; az elso hibat a vegen dobja tovabb (pl. admin jog kell), hogy a tobbi ne maradjon ki miatta
  static void RunAll(IEnumerable<Action> acts) {
    Exception first = null;
    foreach (var a in acts) try { a(); } catch (Exception ex) { first = first ?? ex; }
    if (first != null) throw first;
  }

  static string UndoLabel(string what) {
    var a = what.Split(new[] { '|' }, 3);
    if (a[0] == "nv" || a[0] == "nvprofile") {
      uint id = Convert.ToUInt32(a[1], 16);
      string n = id == Nv.GsyncGlobal ? "G-SYNC (NVCP)" : id == Nv.GsyncApp ? "G-SYNC engedélyezés" : id == Nv.Vsync ? "V-SYNC" : id == Nv.Power ? "Power management"
               : id == Nv.RefreshRate ? "Preferred refresh rate" : id == Nv.PreRender ? "Low Latency" : "0x" + a[1];
      return a[0] == "nv" ? "NVIDIA: " + n : "NVCP profil " + a[2] + ": " + n;
    }
    if (a[0] == "reg") return a[2] == "HwSchMode" ? "HAGS" : a[2] == "AutoGameModeEnabled" ? "Windows Game Mode" : a[2] == "DirectXUserGlobalSettings" ? "Windows VRR / ablakos optimalizálás" : a[2];
    if (a[0] == "rtss") return "RTSS limit: " + System.IO.Path.GetFileName(a[1]);
    if (a[0] == "mode") return "Frissítési ráta: " + a[1];
    return what;
  }

  static bool IsAdmin() { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }

  void RunFix(Action fix) {
    try {
      Nv.Saved = false;
      fix();
      if (Nv.Saved && Process.GetProcessesByName("nvcplui").Length > 0)
        MessageBox.Show(this, "Elmentve. A nyitva lévő NVIDIA Control Panel még a régi értékeket mutatja: zárd be és nyisd meg újra.\nAddig ne nyomj benne Apply-t, mert visszaírja a régit!",
                        "NVIDIA Control Panel", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex) {
      bool denied = ex is UnauthorizedAccessException || ex is System.Security.SecurityException || (ex is Win32Exception && ((Win32Exception)ex).NativeErrorCode == 5);
      if (denied && !IsAdmin()) {
        if (MessageBox.Show(this, "Ehhez admin jog kell. Újraindítsam a programot adminként?", "Admin jog", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
          try {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
            Close();
          } catch (Win32Exception) { }  // a UAC ablakot elutasitotta
        }
        return;
      }
      MessageBox.Show(this, ex.Message, "Hiba", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    nvOverrides = null;
    BeginInvoke((Action)Reload);  // a gomb a sajat Click-jeben ne dobodjon ki
  }

  // ---------- elo VRR monitor ----------
  void LiveTick(object s, EventArgs e) {
    // a Windows bitmelyseg-valtaskor visszakapcsolja az ACM-et -> 2 mp-enkent ellenorizzuk
    if (blockAcm && Environment.TickCount - lastAcm > 2000) {
      lastAcm = Environment.TickCount;
      try { if (Probe.EnforceAcmOff()) Reload(); } catch { }
    }
    if (liveStatus == null || liveStatus.IsDisposed) return;
    var smp = RtssLive.Read();
    if (smp == null) {
      if (liveAppName != null) {
        liveAppName = null; samples.Clear(); graph.Invalidate();
        foreach (var l in new[] { liveApp, liveFps, liveFt, liveLow, liveCapPct, liveIn, liveLfc, liveOver }) { l.Text = "–"; l.ForeColor = Fg; }
      }
      liveStatus.Text = "Nincs aktív játék – indítsd el az RTSS-t és egy játékot";
      liveStatus.ForeColor = Muted;
      return;
    }
    if (smp.App != liveAppName) { samples.Clear(); liveAppName = smp.App; }
    if (smp.FrameTimeMs > 0) samples.Add(smp.FrameTimeMs);
    if (samples.Count > Graph.Capacity) samples.RemoveAt(0);
    graph.Invalidate();

    int now = Environment.TickCount;
    if (now - lastStats < 250 || samples.Count == 0) return;
    lastStats = now;
    int n = samples.Count;
    double over = samples.Count(f => f < liveMinFt * 0.98) * 100.0 / n;
    double lfc = samples.Count(f => f > liveLowFt) * 100.0 / n;
    double atCap = samples.Count(f => Math.Abs(f - liveCapFt) <= liveCapFt * 0.03) * 100.0 / n;
    double p99 = samples.OrderBy(f => f).ElementAt(Math.Min(n - 1, (int)(n * 0.99)));
    liveApp.Text = smp.App;
    liveFps.Text = smp.Fps.ToString("0.0", Inv);
    liveFt.Text = F2(smp.FrameTimeMs) + " ms";
    liveLow.Text = (1000 / p99).ToString("0.0", Inv) + " fps";
    if (smp.Fps > 1000 / liveMinFt) { over = 100; lfc = 0; }  // az atlag FPS a refresh folott -> a V-SYNC fogja, nem a VRR
    liveCapPct.Text = atCap.ToString("0", Inv) + " %";
    liveIn.Text = (100 - over - lfc).ToString("0", Inv) + " %";
    liveLfc.Text = lfc.ToString("0", Inv) + " %";
    liveLfc.ForeColor = lfc > 5 ? Yellow : Fg;
    liveOver.Text = over.ToString("0", Inv) + " %";
    liveOver.ForeColor = over > 1 ? Warn : Fg;
    if (over > 1) { liveStatus.Text = "✖ Az FPS a refresh fölé megy → RTSS cap " + F2(1000 / liveCapFt); liveStatus.ForeColor = Warn; }
    else if (lfc > 5) { liveStatus.Text = "▲ Gyakran a VRR minimum alatt (LFC duplázza a frame-eket)"; liveStatus.ForeColor = Yellow; }
    else { liveStatus.Text = "✔ VRR tartományban – sima"; liveStatus.ForeColor = Accent; }
  }

  // Hz chipek egy vezerlokent rajzolva: chipenkent egy Label ablakkal tobb szaz gyerekablak lett, attol a talca nem frissitette az ikont
  class Chips : Control {
    public class Chip { public string Text, Tip; public Color Fg, Bg; public bool Bold; }
    public List<Chip> Items = new List<Chip>();
    public ToolTip Tip;
    public float Dpi = 1;
    readonly List<Rectangle> rects = new List<Rectangle>();
    readonly Font normal = new Font("Segoe UI", 9f), bold = new Font("Segoe UI", 9f, FontStyle.Bold);
    Chip hover;
    public Chips() { DoubleBuffered = true; ResizeRedraw = true; }
    int P(int px) { return (int)Math.Round(px * Dpi); }

    // sortoressel, mint a FlowLayoutPanel; a magassag a tartalomhoz igazodik
    protected override void OnSizeChanged(EventArgs e) {
      base.OnSizeChanged(e);
      rects.Clear();
      int x = 0, y = 0;
      foreach (var c in Items) {
        var sz = TextRenderer.MeasureText(c.Text, c.Bold ? bold : normal);
        var r = new Rectangle(0, 0, sz.Width + P(14), sz.Height + P(6));
        if (x > 0 && x + r.Width + P(4) > Width) { x = 0; y += r.Height + P(4); }
        r.Offset(x + P(2), y + P(2)); rects.Add(r);
        x += r.Width + P(4);
      }
      int h = rects.Count > 0 ? rects.Last().Bottom + P(2) : 0;
      if (Height != h) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e) {
      for (int i = 0; i < rects.Count; i++) {
        using (var b = new SolidBrush(Items[i].Bg)) e.Graphics.FillRectangle(b, rects[i]);
        TextRenderer.DrawText(e.Graphics, Items[i].Text, Items[i].Bold ? bold : normal, rects[i], Items[i].Fg,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
      }
    }

    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      int i = rects.FindIndex(r => r.Contains(e.Location));
      var h = i < 0 ? null : Items[i];
      if (h != hover && Tip != null) Tip.SetToolTip(this, h == null ? null : h.Tip);
      hover = h;
    }

    protected override void Dispose(bool disposing) {
      if (disposing) { normal.Dispose(); bold.Dispose(); }
      base.Dispose(disposing);
    }
  }

  class Graph : Panel {
    public const int Capacity = 400;
    public List<double> Data;
    public double CapFt, MinFt, LowFt;
    public Graph() { DoubleBuffered = true; ResizeRedraw = true; }

    protected override void OnPaint(PaintEventArgs e) {
      var g = e.Graphics;
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.Clear(BackColor);
      if (CapFt <= 0 || Data == null) return;
      double yMax = Math.Max(CapFt * 2.5, Data.Count > 0 ? Data.Max() * 1.15 : 0);
      Func<double, float> y = v => (float)(Height - 6 - v / yMax * (Height - 12));
      using (var f = new Font("Segoe UI", 8f)) {
        Guide(g, f, y(CapFt), Accent, "cap " + (1000 / CapFt).ToString("0.0", Inv) + " fps", false);
        Guide(g, f, y(MinFt), Yellow, "max Hz", true);
        if (LowFt < yMax) Guide(g, f, y(LowFt), Warn, "VRR min", false);
      }
      if (Data.Count < 2) return;
      float dx = (float)Width / (Capacity - 1);
      var pts = Data.Select((v, i) => new PointF(i * dx, y(v))).ToArray();
      using (var pen = new Pen(Fg, 1.5f)) g.DrawLines(pen, pts);
      using (var red = new SolidBrush(Warn))
        for (int i = 0; i < Data.Count; i++)
          if (Data[i] < MinFt * 0.98 || Data[i] > LowFt) g.FillEllipse(red, pts[i].X - 2, pts[i].Y - 2, 4, 4);
    }

    void Guide(Graphics g, Font f, float yy, Color c, string text, bool right) {
      using (var pen = new Pen(Color.FromArgb(140, c)) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, 0, yy, Width, yy);
      using (var b = new SolidBrush(c)) g.DrawString(text, f, b, right ? Width - g.MeasureString(text, f).Width - 4 : 4, yy - 15);
    }
  }

  // ---------- fo nezet ----------
  void ShowMon(Mon m) {
    content.SuspendLayout();
    foreach (Control c in content.Controls.Cast<Control>().ToList()) c.Dispose();
    tip.RemoveAll();
    double hz = m.Hz;

    // Frissitesi rata + timing + CRU kalkulator
    var rate = Card("Frissítési ráta & timing");
    var big = L(hz.ToString("0.0000", Inv) + " Hz", Accent, 30f, FontStyle.Bold);
    big.Margin = new Padding(0, 0, 0, S(6));
    Span(rate, big, SizeType.AutoSize);
    Row(rate, "Windows szerint", m.RrNum + " / " + m.RrDen + "  =  " + F3(m.WinHz) + " Hz");
    Row(rate, "Frametime", F3(1000 / hz) + " ms");
    if (m.ScanOrder == 2) Row(rate, "Pásztázás", "INTERLACED", Warn);
    if (m.PixelRate > 0 && m.TotalW > 0 && m.TotalH > 0) {
      Row(rate, "Pixel clock", F2(m.PixelRate / 1e6) + " MHz");
      Row(rate, "Total", Res(m.TotalW, m.TotalH) + "   (blank " + (m.TotalW - m.ActiveW) + " / " + (m.TotalH - m.ActiveH) + ")");
      Row(rate, "Vízszintes frekv.", F3(m.HFreq / 1000) + " kHz");
      var target = new NumericUpDown { DecimalPlaces = 3, Minimum = 1, Maximum = 1000, Value = (decimal)Math.Round(hz), BackColor = ChipBg, ForeColor = Fg,
                                       BorderStyle = BorderStyle.FixedSingle, Width = S(110) };
      RowCtl(rate, "CRU cél Hz", target);
      var result = L("", Fg);
      Span(rate, result, SizeType.AutoSize);
      EventHandler calc = (s, e) => {
        double t = (double)target.Value, tot = (double)m.TotalW * m.TotalH;
        double clk = Math.Round(tot * t / 1e4) / 100;  // CRU 10 kHz-es lepesben allitja a pixel clockot
        double got = clk * 1e6 / tot;
        result.Text = "→ pixel clock " + F2(clk) + " MHz  =  " + F3(got) + " Hz  (eltérés " + (got - t).ToString("+0.000;-0.000;0.000", Inv) + ")";
      };
      target.ValueChanged += calc;
      calc(null, null);
    }
    content.Controls.Add(rate, 0, 0);

    // Kep: felbontas, skalazas, kapcsolat, szin
    var img = Card("Kép");
    var nat = m.NativeRes;
    Row(img, "Asztal", Res(m.DeskW, m.DeskH));
    Row(img, "Kimenő jel", Res(m.ActiveW, m.ActiveH));
    Row(img, "Natív", Res(nat.Width, nat.Height));
    // asztal != kimeno jel -> GPU skalaz; kimeno jel != nativ -> a monitor skalaz
    bool rot = m.Rotation == 2 || m.Rotation == 4;  // 90/270 fok: az asztal a jelhez kepest el van forgatva
    bool deskIsSignal = (rot ? m.DeskH : m.DeskW) == m.ActiveW && (rot ? m.DeskW : m.DeskH) == m.ActiveH;
    bool signalNative = m.ActiveW == nat.Width && m.ActiveH == nat.Height;
    if (!deskIsSignal) Row(img, "Skálázás", "GPU skáláz", Yellow);
    else if (!signalNative) Row(img, "Skálázás", "Monitor (display) skáláz", Orange);
    else Row(img, "Skálázás", "Nincs (natív jel)", Accent);
    string[] scalings = { "?", "Nincs (identity)", "Középre", "Nyújtás", "Képarány megtartása", "Egyedi" };
    Row(img, "Skálázási mód", m.Scaling < scalings.Length ? scalings[m.Scaling] : m.Scaling == 128 ? "Driver alapértelmezett" : m.Scaling.ToString());
    Row(img, "Kapcsolat", m.Output);
    if (m.ColorOk) {
      string[] enc = { "RGB", "YCbCr 4:4:4", "YCbCr 4:2:2", "YCbCr 4:2:0" };
      Row(img, "Színformátum", (m.ColorEnc < enc.Length ? enc[m.ColorEnc] : "?") + "  ·  " + m.Bpc + " bpc");
      Row(img, "HDR", m.HdrOn ? "BE" : m.HdrSup ? "Ki (támogatott)" : "Nem támogatott");
    }
    if (m.AcmOn != null) {
      var cb = new CheckBox { Text = "mindig tiltva", Checked = blockAcm, AutoSize = true, ForeColor = Fg, Anchor = AnchorStyles.Right, Cursor = Cursors.Hand };
      cb.CheckedChanged += (s, e) => {
        blockAcm = cb.Checked;
        Settings.BlockAcm = cb.Checked;
        if (cb.Checked) RunFix(() => Probe.EnforceAcmOff());
      };
      Row(img, "Auto színkezelés (ACM)", m.AcmOn == true ? "Be" : "Ki", m.AcmOn == true ? Yellow : Accent, cb);
    }
    content.Controls.Add(img, 1, 0);

    // Elo VRR monitor (RTSS shared memory)
    double vrrCap = Math.Floor((hz - hz * hz / 3600) * 100) / 100;
    var ae = m.Active != null && m.Active.Error == null ? m.Active : null;
    liveCapFt = 1000 / vrrCap;
    liveMinFt = 1000 / Math.Min(hz, ae != null && ae.MaxV > 0 ? ae.MaxV : hz);
    liveLowFt = 1000.0 / (ae != null && ae.MinV > 0 ? ae.MinV : 48);
    var live = Card("Élő VRR monitor  ·  RTSS-en át, játék közben");
    liveStatus = L("Nincs aktív játék – indítsd el az RTSS-t és egy játékot", Muted, 12f, FontStyle.Bold);
    liveStatus.Margin = new Padding(0, 0, 0, S(6));
    Span(live, liveStatus, SizeType.AutoSize);
    var stats = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, S(24), 0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
    for (int i = 0; i < 3; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    liveApp = Row(stats, "Játék", "–");
    liveFps = Row(stats, "FPS (átlag)", "–");
    liveFt = Row(stats, "Frametime", "–");
    liveLow = Row(stats, "1% low ≈", "–");
    liveCapPct = Row(stats, "Cap-en (" + F2(vrrCap) + ")", "–");
    liveIn = Row(stats, "VRR tartományban", "–");
    liveLfc = Row(stats, "VRR min alatt (LFC)", "–");
    liveOver = Row(stats, "Refresh fölött", "–");
    graph = new Graph { Dock = DockStyle.Fill, MinimumSize = new Size(0, S(240)), BackColor = Bg, Margin = Padding.Empty,
                        Data = samples, CapFt = liveCapFt, MinFt = liveMinFt, LowFt = liveLowFt };
    var split = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
    split.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    split.Controls.Add(stats, 0, 0); split.Controls.Add(graph, 1, 0);
    Span(live, split, SizeType.AutoSize);
    samples.Clear(); liveAppName = null;
    content.Controls.Add(live, 0, 1);
    content.SetColumnSpan(live, 2);

    // G-SYNC checklist (Blur Busters G-SYNC 101 + EDID + Windows)
    var chk = Card("G-SYNC / FreeSync checklist  ·  Blur Busters");
    bad = 0; fixes.Clear();
    summary = L("", Fg, 11f, FontStyle.Bold);
    summary.Margin = new Padding(0, 0, 0, S(6));
    fixAll = Btn("Mindet javítsd"); fixAll.Anchor = AnchorStyles.Right; fixAll.Visible = false;
    fixAll.Click += (s, e) => RunFix(() => RunAll(fixes.ToList()));
    int sr = chk.RowCount++;
    chk.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    chk.Controls.Add(summary, 0, sr); chk.SetColumnSpan(summary, 2); chk.Controls.Add(fixAll, 2, sr);

    Dictionary<uint, uint> nv = null; string nvErr = null;
    try { nv = Nv.Global(Nv.GsyncGlobal, Nv.GsyncApp, Nv.Vsync, Nv.PreRender, Nv.Power, Nv.RefreshRate); }
    catch (Exception ex) { nvErr = ex is DllNotFoundException ? "nincs NVIDIA driver" : ex.Message; }
    if (nv != null) {
      uint v;
      bool gs = !nv.TryGetValue(Nv.GsyncGlobal, out v) || v != 0;  // nincs beallitva = driver default (be)
      Check(chk, "G-SYNC (NVCP)", gs, !gs ? "Ki van kapcsolva" : v == 2 ? "Be (fullscreen + ablakos)" : "Be (fullscreen)", () => Nv.SetGlobal(Nv.GsyncGlobal, 1));
      bool app = !nv.TryGetValue(Nv.GsyncApp, out v) || v == 0;
      Check(chk, "G-SYNC engedélyezve", app, app ? "Engedélyezve" : "Tiltva / fix refresh (" + v + ")", () => Nv.SetGlobal(Nv.GsyncApp, 0));
      uint rr = nv.TryGetValue(Nv.RefreshRate, out v) ? v : 0;  // nincs beallitva = Alkalmazás vezérli; a felso bitek (low latency RR mask) maradnak
      Check(chk, "Preferred refresh rate", (rr & 1) != 0, (rr & 1) != 0 ? "Highest available" : "Alkalmazás vezérli → Highest available kell",
            () => Nv.SetGlobal(Nv.RefreshRate, rr | 1));
      uint vs = nv.TryGetValue(Nv.Vsync, out v) ? v : Nv.VsyncApp;
      Check(chk, "V-SYNC (NVCP)", vs == Nv.VsyncOn, vs == Nv.VsyncOn ? "On" : VsyncName(vs) + " → On kell", () => Nv.SetGlobal(Nv.Vsync, Nv.VsyncOn));
      // az NVCP kapcsolója rejtett: NVAPI-val nem írható (-160), ezert nincs Javítás gomb; olvasni a DRS fajlbol tudjuk
      int? ll = null;
      try { ll = Nv.LowLatency(nv); } catch { }
      if (ll != null)
        Check(chk, "Low Latency Mode", ll == 1, ll == 1 ? "On" : (ll == 2 ? "Ultra" : ll == 0 ? "Off" : "Egyéb (" + ll + ")") + " → NVCP-ben kézzel: On (NEM Ultra)");
      else Check(chk, "Low Latency Mode", null, "nem olvasható – kézzel ellenőrizd az NVCP-ben: On legyen (NEM Ultra)");
      uint pw = nv.TryGetValue(Nv.Power, out v) ? v : 5;  // nincs beallitva = driver default (Normál)
      Check(chk, "Power management", pw == 1, pw == 1 ? "Prefer maximum performance"
            : (pw == 0 ? "Adaptive" : pw == 5 ? "Normál" : "Egyéb (" + pw + ")") + " → Prefer maximum performance kell", () => Nv.SetGlobal(Nv.Power, 1));
    } else Check(chk, "NVIDIA beállítások", null, "nem olvasható: " + nvErr);
    Check(chk, "Játékban V-SYNC", null, "kézzel: legyen OFF (a V-SYNC az NVCP-ben legyen On)");

    Rtss.Profile gp = null; string gpErr = null;
    try { gp = Rtss.Parse(Rtss.GlobalPath); } catch (Exception ex) { gpErr = ex.Message; }
    if (gp != null) {
      // jóval a cap alatt: nem hiba, de valoszinuleg nem szandekos
      bool farBelow = gp.Cap > 0 && gp.Cap < vrrCap * 0.8;
      Check(chk, "FPS limit (RTSS)", farBelow ? (bool?)null : CapOk(gp.Cap, vrrCap),
            farBelow ? gp.Cap.ToString("0.###", Inv) + " – jóval a " + F2(vrrCap) + " alatt, ha nem szándékos: Javítás" : CapText(gp.Cap, vrrCap),
            () => Rtss.SetLimit(gp.Path, vrrCap));
      string[] modesTxt = { "Async", "Front edge sync", "Back edge sync" };
      Check(chk, "RTSS limiter mód", null, gp.Mode < 0 ? "?" : gp.Mode < 3 ? modesTxt[gp.Mode] : "Egyéb (" + gp.Mode + ")");
    } else if (gpErr != null) Check(chk, "FPS limit (RTSS)", null, "az RTSS profil nem olvasható: " + gpErr);
    else Check(chk, "FPS limit (RTSS)", null, "RTSS nincs telepítve – akkor NVCP Max Frame Rate vagy játékbeli limit, max " + F2(vrrCap));

    int maxHz = m.MaxHz;
    Check(chk, "Max Hz-en megy", m.CurHz >= maxHz, m.CurHz >= maxHz ? m.CurHz + " Hz" : m.CurHz + " Hz → " + maxHz + " Hz kell",
          () => Probe.SetMode(m.Gdi, m.DeskW, m.DeskH, maxHz));
    {
      var nat0 = m.NativeRes;
      var pref = ae != null ? ae.Preferred : null;
      bool edidOk = pref != null && pref.W == nat0.Width && pref.H == nat0.Height;
      bool drvOk = m.DriverNative.HasValue ? m.DriverNative.Value == nat0 : edidOk;  // a driver velemenye szamit, az EDID csak tartalek
      string want = Res(nat0.Width, nat0.Height);
      Check(chk, "Natív felbontás", drvOk,
            !drvOk ? "A driver natívnak hiszi: " + (m.DriverNative.HasValue ? Res(m.DriverNative.Value.Width, m.DriverNative.Value.Height) : "?") + " → CRU: Detailed resolutions 1. helyére " + want
            : edidOk ? want + " (driver + EDID preferált timing)"
            : want + " a driver szerint  ·  az EDID base blokkja üres (boot/BIOS képen más lehet)");
    }
    if (ae != null && ae.MaxV > 0) {
      bool lfc = ae.MaxV >= 2 * ae.MinV, top = ae.MaxV >= Math.Floor(hz);
      Check(chk, "VRR tartomány (EDID)", lfc && top, ae.MinV + "–" + ae.MaxV + " Hz" + (!top ? " → a max kisebb, mint a refresh" : !lfc ? " → túl szűk, nincs LFC (max ≥ 2× min kell)" : "  ·  LFC ok"));
    } else if (ae == null) Check(chk, "VRR tartomány (EDID)", null, "az EDID nem olvasható");
    else Check(chk, "VRR tartomány (EDID)", false, "Nincs range limits az EDID-ben → CRU-ban add hozzá");
    Check(chk, "Reflex-es játék", null, "Reflex On (+Boost) – felülírja a Low Latency Mode-ot");

    int? hags = Win.Hags();
    Check(chk, "HW GPU ütemezés (HAGS)", hags == 2 ? true : hags == 1 ? false : (bool?)null,
          hags == 2 ? "Be" : hags == 1 ? "Ki (DLSS frame gen-hez kell)" : "Windows alapértelmezett",
          () => { Win.SetHags(); MessageBox.Show(this, "HAGS bekapcsolva – a gép újraindítása után él.", "HAGS"); });
    int? gm = Win.GameMode();
    Check(chk, "Windows Game Mode", gm != 0, gm != 0 ? "Be" : "Ki", Win.SetGameMode);
    string wvrr = Win.DxGet("VRROptimizeEnable"), weo = Win.DxGet("SwapEffectUpgradeEnable");
    Check(chk, "Windows VRR (ablakos DX11)", wvrr == "1" ? true : wvrr == "0" ? false : (bool?)null, wvrr == "1" ? "Be" : wvrr == "0" ? "Ki" : "alapértelmezett",
          () => Win.DxSet("VRROptimizeEnable"));
    Check(chk, "Ablakos játék optimalizálás", weo == "1" ? true : weo == "0" ? false : (bool?)null, weo == "1" ? "Be" : weo == "0" ? "Ki" : "alapértelmezett",
          () => Win.DxSet("SwapEffectUpgradeEnable"));
    content.Controls.Add(chk, 0, 2);

    // RTSS limitek + jatekonkenti profilok
    var rtss = Card("RTSS limit & játékprofilok");
    string vrr = F2(vrrCap);
    string lowlag = F3(Math.Floor(hz * 1000) / 1000 - 0.01);
    string off = F3(Math.Floor(hz * 1000) / 1000);
    Row(rtss, "G-Sync/FreeSync + VSync", vrr, Accent, CopyBtn(vrr));
    Row(rtss, "Fix Hz, VSync ON (low-lag)", lowlag, null, CopyBtn(lowlag));
    Row(rtss, "Fix Hz, VSync OFF", off + "  vagy Scanline Sync", null, CopyBtn(off));
    var note = L("VRR cap = Hz − Hz²/3600 (Reflex képlet)  ·  low-lag = pontos Hz − 0.01\n" +
                 "Async: legkisebb lag, szigorú cap → G-SYNC-hez ez a jó.\n" +
                 "Back edge sync: ~async lag, de hitch után átlépheti a capet.\n" +
                 "Front edge sync: legegyenletesebb képkiadás, de több lag.\n" +
                 "NVIDIA Reflex mód: DLSS Frame Generation-ös játékokhoz.", Muted, 9f);
    note.Margin = new Padding(0, S(8), 0, 0);
    Span(rtss, note, SizeType.AutoSize);
    var gh = L("JÁTÉKONKÉNTI PROFILOK (felülírják a globálisat)", Muted, 8.5f, FontStyle.Bold);
    gh.Margin = new Padding(0, S(16), 0, S(4));
    Span(rtss, gh, SizeType.AutoSize);
    int profiles = 0;
    try {
      foreach (var p in Rtss.Games()) {
        var pp = p;
        Check(rtss, "RTSS: " + p.Name, CapOk(p.Cap, vrrCap), CapText(p.Cap, vrrCap), () => Rtss.SetLimit(pp.Path, vrrCap));
        profiles++;
      }
    } catch (Exception ex) { Check(rtss, "RTSS profilok", null, ex.Message); }
    if (nvOverrides == null)
      try { nvOverrides = Nv.UserOverrides(Nv.Vsync, Nv.GsyncApp, Nv.PreRender); nvOvErr = null; }
      catch (Exception ex) { nvOverrides = new List<Nv.Override>(); nvOvErr = ex is DllNotFoundException ? null : ex.Message; }
    if (nvOvErr != null) Check(rtss, "NVCP profilok", null, nvOvErr);
    foreach (var o in nvOverrides) {
      var oo = o;
      bool ok; string what;
      if (o.Id == Nv.Vsync) { ok = o.Value == Nv.VsyncOn; what = "V-SYNC: " + VsyncName(o.Value); }
      else if (o.Id == Nv.GsyncApp) { ok = o.Value == 0; what = "G-SYNC: " + (ok ? "engedélyezve" : "tiltva (" + o.Value + ")"); }
      else { ok = o.Value == 1; what = "Low Latency: " + (o.Value == 1 ? "On" : o.Value == 0 ? "Off" : o.Value.ToString()); }
      Check(rtss, "NVCP: " + o.Profile, ok, what + (ok ? "" : "  (javítás = globális érvényes)"), () => Nv.DeleteOverride(oo.Profile, oo.Id));
      profiles++;
    }
    if (profiles == 0) Check(rtss, "Profilok", true, "nincs saját beállítású játékprofil");
    content.Controls.Add(rtss, 1, 2);

    // Elerheto modok
    var modes = Card("Elérhető módok (Windows)");
    FixedHeight(modes);
    var legend = L("zöld = aktív  ·  halvány = nincs az EDID-ben, a driver adta hozzá (valószínűleg GPU skálázza)", Muted, 9f);
    legend.Margin = new Padding(0, 0, 0, S(6));
    Span(modes, legend, SizeType.AutoSize);
    var mg = new TableLayoutPanel { ColumnCount = 2 };
    mg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); mg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    var edid = m.Active != null && m.Active.Error == null ? m.Active : null;
    foreach (var kv in m.Modes) {
      bool curRes = kv.Key.Width == m.DeskW && kv.Key.Height == m.DeskH;
      int r = mg.RowCount++;
      mg.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      var rl = L(Res(kv.Key.Width, kv.Key.Height), curRes ? Accent : Fg, 10f, curRes ? FontStyle.Bold : FontStyle.Regular);
      rl.Anchor = AnchorStyles.Left | AnchorStyles.Top;
      rl.Margin = new Padding(0, S(5), S(14), 0);
      var flow = new Chips { Tip = tip, Dpi = scale, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Margin = Padding.Empty };
      foreach (int f in kv.Value) {
        bool cur = curRes && f == m.CurHz, inEdid = edid == null || edid.Has(kv.Key.Width, kv.Key.Height, f);
        flow.Items.Add(new Chips.Chip { Text = f.ToString(), Bold = cur, Fg = cur ? Color.Black : inEdid ? Fg : Color.FromArgb(95, 95, 108), Bg = cur ? Accent : inEdid ? ChipBg : DimChip,
                                        Tip = Res(kv.Key.Width, kv.Key.Height) + " @ " + f + " Hz" + (inEdid ? "  ·  EDID-ben van" : "  ·  nincs az EDID-ben") });
      }
      mg.Controls.Add(rl, 0, r); mg.Controls.Add(flow, 1, r);
    }
    Span(modes, Scroller(mg), SizeType.Percent);
    content.Controls.Add(modes, 0, 3);

    // EDID
    var ec = Card("EDID  ·  ami a monitorban / CRU-ban van");
    FixedHeight(ec);
    var eg = new TableLayoutPanel { ColumnCount = 5 };
    for (int i = 0; i < 4; i++) eg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    eg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    FlowLayoutPanel toggle = null;
    Button bo = null, br = null;
    if (m.Over != null && m.WinEdid != null) {
      toggle = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
      bo = Btn("CRU override"); br = Btn("Windows");
      toggle.Controls.Add(bo); toggle.Controls.Add(br);
    }
    var srcL = Row(ec, "Forrás", "", null, toggle);
    var nameL = Row(ec, "Név", "");
    var rangeL = Row(ec, "Tartomány", "");
    Action<Edid, string> fill = (e, label) => {
      srcL.Text = label;
      nameL.Text = e == null ? "–" : e.Name.Length > 0 ? e.Name : "–";
      rangeL.Text = e == null ? "–" : e.Range.Length > 0 ? e.Range : "nincs megadva";
      eg.SuspendLayout();
      foreach (Control c in eg.Controls.Cast<Control>().ToList()) c.Dispose();
      eg.RowStyles.Clear(); eg.RowCount = 0;
      if (e != null && e.Error == null) {
        GridRow(eg, Muted, true, "Felbontás", "Hz", "Pixel clock", "Total", "Forrás");
        foreach (var t in e.Timings) {
          bool cur = t.W == m.ActiveW && t.H == m.ActiveH && Math.Abs(t.Hz - hz) < 0.5;
          GridRow(eg, cur ? Accent : t.Exact ? Fg : Muted, cur, Res(t.W, t.H) + (t.Interlaced ? "i" : ""),
                  t.Exact ? F3(t.Hz) : t.Hz.ToString(Inv), t.Exact ? F2(t.ClockMHz) + " MHz" : "–", t.Exact ? Res(t.HTotal, t.VTotal) : "–", t.Src);
        }
      } else srcL.Text = label + "  –  " + (e == null ? "nem olvasható" : e.Error);
      eg.ResumeLayout();
    };
    if (bo != null) {
      bo.Click += (s, e) => fill(m.Over, "CRU override (registry) – restart64 / reboot után él");
      br.Click += (s, e) => fill(m.WinEdid, "Amit a Windows lát (override után már nem a gyári!)");
    }
    if (m.Over != null) fill(m.Over, "CRU override (registry) – restart64 / reboot után él");
    else fill(m.WinEdid, m.WinEdid == null ? "Nincs EDID a registryben" : "Gyári EDID (nincs CRU override)");
    Span(ec, Scroller(eg), SizeType.Percent);
    content.Controls.Add(ec, 1, 3);

    summary.Text = bad == 0 ? "✔ Minden rendben" : "✖ " + bad + " dolog nem stimmel";
    summary.ForeColor = bad == 0 ? Accent : Warn;
    fixAll.Visible = fixes.Count > 0; fixAll.Text = "Mindet javítsd (" + fixes.Count + ")";
    content.ResumeLayout();
  }

  [STAThread]
  static void Main() {
    Native.SetProcessDPIAware();
    Application.EnableVisualStyles();
    Application.Run(new MainForm());
  }
}
