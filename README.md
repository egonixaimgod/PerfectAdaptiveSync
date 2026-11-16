# Perfect Adaptive Sync

Windows tool that checks (and fixes with one click) everything a G-SYNC / FreeSync setup needs: exact refresh rate, EDID, NVIDIA driver settings, RTSS frame cap, Windows gaming settings. The UI is in Hungarian.

Egyetlen ablakban megmutatja, hogy a G-SYNC / FreeSync beállításod rendben van-e, és ami nincs, azt egy gombbal kijavítja.

## Mit csinál

- **Pontos frissítési ráta** a tényleges timingból (pl. 279.9997 Hz), pixel clock, total, CRU kalkulátor.
- **Checklist** a Blur Busters G-SYNC 101 alapján: G-SYNC, NVCP V-SYNC, Low Latency Mode, power management, RTSS limit, max Hz, natív felbontás, VRR tartomány, HAGS, Game Mode, Windows VRR. A piros sorok mellett **Javítás** gomb van, felül **Mindet javítsd**.
- **FPS cap** kiszámolva: `Hz − Hz²/3600`, és beírja az RTSS-be (az RTSS újraindítása nélkül).
- **Visszaállítás**: minden, amit a program átírt, visszaállítható az eredeti értékre (a fejlécben, egyenként vagy egyszerre).
- **EDID nézet**: amit a monitor / a CRU override mond (DTD, CTA, DisplayID, FreeSync és HDMI VRR tartomány).
- **Élő VRR monitor**: játék közben mutatja, hogy a frametime a VRR tartományban marad-e (RTSS kell hozzá).
- Játékonkénti RTSS és NVCP profilok, amik felülírják a globális beállítást.

## Követelmények

- Windows 10 / 11, 64 bit (.NET Framework 4.x, a Windows része)
- A driver-ellenőrzések és -javítások csak **NVIDIA** kártyával működnek; a többi rész (EDID, timing, Windows beállítások, RTSS) bármivel.
- [RTSS](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) az FPS limithez és az élő monitorhoz (nem kötelező).
- A javítások egy részéhez **admin jog** kell (NVIDIA profil, HAGS, RTSS profil) – a program rákérdez, és újraindítja magát adminként.

## Használat

Töltsd le a `PerfectAdaptiveSync.exe`-t a [Releases](../../releases) oldalról, vagy fordítsd le magad:

```
build.bat
```

Nem kell hozzá SDK, a Windows beépített C# fordítóját használja.

## Mit ír át a gépen

Csak akkor ír, ha megnyomsz egy Javítás gombot (vagy bepipálod az „ACM mindig tiltva" jelölőt, ami kikapcsolva tartja a Windows automatikus színkezelését): NVIDIA globális / játékprofil beállítás, RTSS profil `Limit` sora (első íráskor `.pas-backup` másolat készül mellé), a kijelző frissítési rátája, és három Windows registry érték (HAGS, Game Mode, DirectX VRR / ablakos optimalizálás). Az eredeti értékeket a `HKCU\Software\PerfectAdaptiveSync\Undo` kulcs őrzi a visszaállításhoz.

## Tesztek

- `test.bat` – önteszt (EDID parser, RTSS config, registry merge); semmit nem ír a gépre.
- `rebuild_es_github_push.bat` – önteszt, build, commit + push, és az exe GitHub Release-ként (`build-N`).
- `verify.bat` – minden javítást ténylegesen lefuttat: rossz értéket ír, javít, független forrásból visszaolvas, majd visszaállítja az eredetit. Adminként futtasd.

## Licenc

[MIT](LICENSE)
