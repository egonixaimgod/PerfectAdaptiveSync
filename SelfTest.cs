// Onteszt a kockazatos logikara (EDID parser, RTSS config atiras, registry merge, nativ felbontas). Futtatas: test.bat
// Semmit nem ir a gepre, csak memoriaban dolgozik.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

static class SelfTest {
  static int failed;
  static void Assert(bool ok, string msg) {
    Console.WriteLine((ok ? "ok    " : "HIBA  ") + msg);
    if (!ok) failed++;
  }

  // 128 byte-os EDID: 1 DTD 1920x1080@60 (148.5 MHz, 2200x1125) + range limits 48-280 Hz (max +255 flag)
  static byte[] BaseEdid() {
    var d = new byte[128];
    new byte[] { 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0 }.CopyTo(d, 0);
    for (int i = 38; i < 54; i++) d[i] = 1;  // ures standard timingok
    new byte[] { 0x02, 0x3A, 0x80, 0x18, 0x71, 0x38, 0x2D, 0x40 }.CopyTo(d, 54);
    new byte[] { 0, 0, 0, 0xFD, 0x02, 48, 25, 30, 160, 130 }.CopyTo(d, 72);
    return d;
  }

  // DRS rekord: A4 00 10 00 | id | 02 10 00 00 | ertek
  static byte[] DrsRun(params uint[] idVal) {
    var r = new List<byte>();
    for (int i = 0; i < idVal.Length; i += 2) {
      r.AddRange(new byte[] { 0xA4, 0, 0x10, 0 }); r.AddRange(BitConverter.GetBytes(idVal[i]));
      r.AddRange(new byte[] { 2, 0x10, 0, 0 }); r.AddRange(BitConverter.GetBytes(idVal[i + 1]));
    }
    return r.ToArray();
  }

  static void DrsTests() {
    var g = new Dictionary<uint, uint> { { 0x1057EB71, 1 }, { 0x00A879CF, 0x47814940 }, { 0x0064B541, 1 } };
    Func<byte[][], byte[]> file = parts => new byte[] { 9, 9, 9 }.Concat(parts.SelectMany(p => p.Concat(new byte[] { 7, 7, 7, 7, 7 }))).ToArray();
    var other = DrsRun(0x1057EB71, 1, Nv.LowLatencyCpl, 2);  // jatekprofil Ultra-val, de nem illeszkedik a globalisra
    Assert(Nv.LowLatency(file(new[] { other, DrsRun(0x1057EB71, 1, Nv.LowLatencyCpl, 1, 0x00A879CF, 0x47814940, 0x0064B541, 1) }), g) == 1, "DRS: globalis profil On");
    Assert(Nv.LowLatency(file(new[] { DrsRun(Nv.LowLatencyCpl, 2, 0x1057EB71, 1, 0x00A879CF, 0x47814940, 0x0064B541, 1) }), g) == 2, "DRS: Ultra");
    Assert(Nv.LowLatency(file(new[] { other, DrsRun(0x1057EB71, 1, 0x00A879CF, 0x47814940, 0x0064B541, 1) }), g) == 0, "DRS: nincs beirva = Off");
    Assert(Nv.LowLatency(file(new[] { other }), g) == null, "DRS: nincs illeszkedo profil -> null");
    var twice = DrsRun(0x1057EB71, 1, 0x00A879CF, 0x47814940, 0x0064B541, 1);
    Assert(Nv.LowLatency(file(new[] { twice, twice }), g) == null, "DRS: ket illeszkedo profil -> null");
    Assert(Nv.LowLatency(file(new[] { twice }), new Dictionary<uint, uint> { { 0x1057EB71, 1 } }) == null, "DRS: keves globalis ertek -> null");
  }

  static int Main() {
    DrsTests();
    var e = Edid.Parse(BaseEdid());
    Assert(e.Error == null, "EDID: ervenyes EDID hiba nelkul");
    var t = e.Timings.Single();
    Assert(t.W == 1920 && t.H == 1080 && Math.Abs(t.Hz - 60) < 1e-9 && t.HTotal == 2200 && t.VTotal == 1125, "EDID: DTD 1920x1080@60");
    Assert(e.Preferred == t, "EDID: az elso DTD a preferalt");
    Assert(e.MinV == 48 && e.MaxV == 280, "EDID: range 48-280 Hz (+255 flag)");
    Assert(e.Has(1920, 1080, 60) && !e.Has(1920, 1080, 144), "EDID: Has()");
    Assert(Edid.Parse(new byte[10]).Error != null, "EDID: tul rovid -> Error");

    // H tartomany +255 flag (0x0E = V max, H min+max): nyers 200-200 -> 455-455 kHz
    var hr = BaseEdid(); hr[76] = 0x0E; hr[79] = 200; hr[80] = 200;
    Assert(Edid.Parse(hr).Range.Contains("H 455-455 kHz"), "EDID: H range +255 flag");

    // interlaced DTD (CTA 1080i60): a field 540 soros, a mod 1920x1080i @ 60
    var il = BaseEdid();
    new byte[] { 0x01, 0x1D, 0x80, 0x18, 0x71, 0x1C, 0x16, 0x20, 0x58, 0x2C, 0x25, 0, 0, 0, 0, 0, 0, 0x9E }.CopyTo(il, 54);
    var it = Edid.Parse(il).Timings[0];
    Assert(it.W == 1920 && it.H == 1080 && it.Interlaced && Math.Abs(it.Hz - 60) < 1e-9 && it.VTotal == 1125, "EDID: interlaced DTD 1920x1080i@60");

    // csak DisplayID-ben van VRR tartomany (0x25 blokk, 48-240 Hz) -> MinV/MaxV abbol jon
    var di = new byte[256];
    BaseEdid().CopyTo(di, 0);
    for (int i = 72; i < 82; i++) di[i] = 0;  // nincs base range limits
    di[126] = 1; di[128] = 0x70; di[130] = 12; di[133] = 0x25; di[135] = 9; di[142] = 48; di[143] = 240;
    var de = Edid.Parse(di);
    Assert(de.Error == null && de.MinV == 48 && de.MaxV == 240, "EDID: DisplayID VRR range -> MinV/MaxV");

    // HDMI: a VRR tartomany a CTA blokk gyartoi adatblokkjaiban van (nincs base range limits)
    Func<byte[], Edid> cta = block => {
      var x = new byte[256];
      BaseEdid().CopyTo(x, 0);
      for (int i = 72; i < 82; i++) x[i] = 0;
      x[126] = 1; x[128] = 0x02; x[129] = 3; x[130] = (byte)(4 + block.Length);
      block.CopyTo(x, 132);
      return Edid.Parse(x);
    };
    var fs = cta(new byte[] { (3 << 5) | 7, 0x1A, 0, 0, 1, 1, 48, 144 });
    Assert(fs.Error == null && fs.MinV == 48 && fs.MaxV == 144 && fs.Range.Contains("FreeSync 48-144 Hz"), "EDID: FreeSync VSDB tartomany");
    // valodi v3 blokk (AOC AG276QZD2): a regi max mezo 240, a 10 bites 280
    var fs3 = cta(new byte[] { (3 << 5) | 20, 0x1A, 0, 0, 3, 1, 0x30, 0xF0, 0, 0, 0, 0, 0, 0, 0x18, 0x01, 0, 0, 0, 0, 0 });
    Assert(fs3.MinV == 48 && fs3.MaxV == 280, "EDID: FreeSync v3 10 bites max (48-280)");
    var hf = cta(new byte[] { (3 << 5) | 10, 0xD8, 0x5D, 0xC4, 1, 0, 0, 0, 0, 40, 0 });
    Assert(hf.Error == null && hf.MinV == 40 && hf.MaxV == 60, "EDID: HDMI VRR min 40, max nincs megadva -> a leggyorsabb mod (60)");
    var hf2 = cta(new byte[] { (3 << 5) | 10, 0xD8, 0x5D, 0xC4, 1, 0, 0, 0, 0, 0x40 | 48, 44 });
    Assert(hf2.MinV == 48 && hf2.MaxV == 300, "EDID: HDMI VRR 10 bites max (48-300)");

    // elo monitor: eloter > mostani app > legfrissebb; a regen nem frissult app kiesik
    Func<uint, uint, RtssLive.Sample> smp = (pid, t1) => new RtssLive.Sample { Pid = pid, T1 = t1 };
    var two = new List<RtssLive.Sample> { smp(1, 5000), smp(2, 5400) };
    Assert(RtssLive.Pick(two, 0, 0).Pid == 2 && RtssLive.Pick(two, 0, 1).Pid == 1 && RtssLive.Pick(two, 1, 2).Pid == 1, "RTSS live: nem ugral ket app kozott");
    Assert(RtssLive.Pick(new List<RtssLive.Sample> { smp(1, 1000), smp(2, 5400) }, 1, 1).Pid == 2 && RtssLive.Pick(new List<RtssLive.Sample>(), 1, 1) == null,
           "RTSS live: megallt app helyett a futo; ures lista -> null");

    // serult CTA blokk: a data block offset (200) tulmutat a tombon -> Error, nem kivetel
    var bad = new byte[256];
    BaseEdid().CopyTo(bad, 0);
    bad[126] = 1; bad[128] = 0x02; bad[130] = 200; bad[132] = (2 << 5) | 31;
    Assert(Edid.Parse(bad).Error != null, "EDID: serult extension block -> Error, nem crash");

    // DLDSR: a Windows listaban van nagyobb mod, mint a panel -> a nativ az EDID-bol jon
    var m = new Mon {
      WinEdid = e,
      Modes = new List<KeyValuePair<Size, SortedSet<int>>> {
        new KeyValuePair<Size, SortedSet<int>>(new Size(3840, 2160), new SortedSet<int> { 60 }),
        new KeyValuePair<Size, SortedSet<int>>(new Size(1920, 1080), new SortedSet<int> { 60 }) }
    };
    Assert(m.NativeRes == new Size(1920, 1080), "NativeRes: DLDSR mod nem szamit nativnak");

    var ini = new[] { "[OSD]", "X=1", "[Framerate]", "Limit=60", "LimitDenominator=1", "LimitTime=0", "LimitTimeDenominator=1", "SyncLimiter=1", "[Other]", "Y=2" };
    Assert(string.Join("|", Rtss.WithLimit(ini, 258.22)) == "[OSD]|X=1|[Framerate]|Limit=258220|LimitDenominator=1000|LimitTime=0|LimitTimeDenominator=1|SyncLimiter=1|[Other]|Y=2",
           "RTSS: csak a Limit/LimitDenominator sor valtozik");
    Assert(string.Join("|", Rtss.WithLimit(new[] { "[OSD]" }, 100)) == "[OSD]|[Framerate]|Limit=100000|LimitDenominator=1000", "RTSS: hianyzo [Framerate] szekcio letrejon");

    Assert(Win.DxMerge(null, "VRROptimizeEnable", "1") == "VRROptimizeEnable=1;", "DX: ures ertekbol");
    Assert(Win.DxMerge("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", "VRROptimizeEnable", "1") == "SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;", "DX: meglevo kulcs csere, a tobbi marad");

    Console.WriteLine(failed == 0 ? "\nMinden teszt atment." : "\n" + failed + " teszt HIBAS.");
    return failed == 0 ? 0 : 1;
  }
}
