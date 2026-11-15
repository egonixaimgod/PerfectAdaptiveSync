// Minden "Javítás" gomb muveletet tenylegesen lefuttat: elotte rossz erteket ir, lefuttatja ugyanazt a javitast,
// amit a gomb, majd FUGGETLEN forrasbol (uj NVAPI session / reg.exe / fajl) visszaolvas, vegul visszaallitja az eredetit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

static class VerifyFixes {
  static int fails;
  static void Ok(string name, bool ok, string detail) {
    if (!ok) fails++;
    Console.WriteLine((ok ? "[OK]   " : "[HIBA] ") + name + "  " + detail);
  }

  static string Reg(string key, string val) {
    var p = Process.Start(new ProcessStartInfo("reg.exe", "query \"" + key + "\" /v " + val) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
    var line = o.Split('\n').FirstOrDefault(l => l.Trim().StartsWith(val + " "));
    return line == null ? null : line.Trim().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries).Last().Trim();
  }

  static void NvCase(string name, uint id, uint bad, Action fix, uint want) {
    uint orig; bool had = Nv.Global(id).TryGetValue(id, out orig);
    try {
      Nv.SetGlobal(id, bad);
      fix();
      uint got; bool has = Nv.Global(id).TryGetValue(id, out got);  // uj session = a driver adatbazisbol olvas
      Ok(name, has && got == want, "rossz=" + bad + " -> javitas utan=" + (has ? got.ToString() : "nincs") + " (kell " + want + ")");
    } finally { Nv.SetGlobal(id, had ? orig : want); }
  }

  static void RegCase(string name, string key, string val, object bad, Action fix, string wantText) {
    object orig = Registry.GetValue(key, val, null);
    try {
      Registry.SetValue(key, val, bad);
      fix();
      string got = Reg(key, val);
      Ok(name, got != null && got.Contains(wantText), "reg.exe szerint: " + got);
    } finally {
      if (orig != null) Registry.SetValue(key, val, orig);
      else using (var k = Registry.CurrentUser.OpenSubKey(key.Replace(@"HKEY_CURRENT_USER\", ""), true)) if (k != null) k.DeleteValue(val, false);
    }
  }

  // a Visszaallitas kore a program sajat teszt kulcsan: iras -> eredeti megjegyezve -> visszaallitas -> eredeti allapot
  static void UndoCase() {
    const string key = @"HKEY_CURRENT_USER\Software\PerfectAdaptiveSync\SelfTest", what = "reg|" + key + "|x";
    Registry.SetValue(key, "x", 3);
    Win.Set(key, "x", 5); Win.Set(key, "x", 7);  // a masodik iras nem irhatja felul az eredetit
    bool saved = Undo.All().ContainsKey(what);
    Undo.Restore(what);
    bool back = (Registry.GetValue(key, "x", null) as int?) == 3 && !Undo.All().ContainsKey(what);
    Win.Set(key, "y", "a");  // elotte nem letezett -> a visszaallitas torli
    Undo.Restore("reg|" + key + "|y");
    bool gone = Registry.GetValue(key, "y", "nincs") as string == "nincs";
    Registry.CurrentUser.DeleteSubKey(@"Software\PerfectAdaptiveSync\SelfTest", false);
    Ok("Visszaallitas (registry)", saved && back && gone, "megjegyezve=" + saved + " visszaallt=" + back + " torolve=" + gone);
  }

  static int Main() {
    try { UndoCase(); } catch (Exception ex) { Ok("Visszaallitas (registry)", false, ex.Message); }
    Undo.On = false;  // innentol a teszt maga allit vissza mindent
    try {
      NvCase("G-SYNC (NVCP)", Nv.GsyncGlobal, 0, () => Nv.SetGlobal(Nv.GsyncGlobal, 1), 1);
      NvCase("G-SYNC engedelyezve", Nv.GsyncApp, 1, () => Nv.SetGlobal(Nv.GsyncApp, 0), 0);
      NvCase("Preferred refresh rate", Nv.RefreshRate, 0, () => { uint rr; Nv.Global(Nv.RefreshRate).TryGetValue(Nv.RefreshRate, out rr); Nv.SetGlobal(Nv.RefreshRate, rr | 1); }, 1);
      NvCase("V-SYNC (NVCP)", Nv.Vsync, Nv.VsyncApp, () => Nv.SetGlobal(Nv.Vsync, Nv.VsyncOn), Nv.VsyncOn);
      NvCase("Power management", Nv.Power, 5, () => Nv.SetGlobal(Nv.Power, 1), 1);
    } catch (Exception ex) { Ok("NVIDIA beallitasok", false, ex.Message); }

    const string GameBar = @"HKEY_CURRENT_USER\Software\Microsoft\GameBar", Dx = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences";
    RegCase("Windows Game Mode", GameBar, "AutoGameModeEnabled", 0, Win.SetGameMode, "0x1");
    RegCase("Windows VRR", Dx, "DirectXUserGlobalSettings", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", () => Win.DxSet("VRROptimizeEnable"), "VRROptimizeEnable=1");
    RegCase("Ablakos jatek optimalizalas", Dx, "DirectXUserGlobalSettings", "VRROptimizeEnable=1;SwapEffectUpgradeEnable=0;", () => Win.DxSet("SwapEffectUpgradeEnable"), "SwapEffectUpgradeEnable=1");

    // HAGS: HKLM, admin kell es ujrainditas -> csak olvasas
    string hags = Reg(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
    Ok("HAGS (csak olvasva)", hags == "0x2", "reg.exe szerint: " + hags);

    // RTSS: a globalis profil masolatan
    if (File.Exists(Rtss.GlobalPath)) {
      string tmp = Path.Combine(Path.GetTempPath(), "pas-rtss-Global");
      File.Copy(Rtss.GlobalPath, tmp, true);
      Rtss.SetLimit(tmp, 258.22, false);  // masolaton dolgozunk: a futo RTSS-t nem kell ujrainditani
      var txt = File.ReadAllLines(tmp);
      Ok("RTSS limit", txt.Contains("Limit=258220") && txt.Contains("LimitDenominator=1000") && Math.Abs(Rtss.Parse(tmp).Cap - 258.22) < 1e-9,
         "fajlban: " + string.Join(" ", txt.Where(l => l.StartsWith("Limit"))));
      File.Delete(tmp); File.Delete(tmp + ".pas-backup");
    } else Console.WriteLine("[--]   RTSS nincs telepitve");

    Console.WriteLine(fails == 0 ? "MINDEN JAVITAS VALOBAN BEIRODIK" : fails + " HIBA");
    return fails;
  }
}
