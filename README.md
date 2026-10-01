# DesktopPager3D-OS

Utility per Windows che nasce per **sfogliare le pagine delle icone del desktop**
quando sono più di quante ne stanno sullo schermo, e che si è evoluta in un piccolo
"sistema" a scomparsa: barra stile Windows, terminali a tendina, rotazione dello
schermo, riavvio di Explorer e due viste 3D per esplorare i file.

Resta residente in memoria con un'icona nell'area di notifica.

---

## Funzionalità

**Paginazione icone del desktop**
- `Ctrl+Alt+PgGiù` pagina avanti · `Ctrl+Alt+PgSu` pagina indietro · `Ctrl+Alt+Home` prima pagina
- Non sposta mai le icone: fa scorrere la vista della ListView del desktop
  (`LVM_SCROLL`, con rimozione temporanea di `LVS_NOSCROLL`), quindi il layout
  resta intatto e funziona anche con la disposizione automatica.

**Rotazione dello schermo** (come le hotkey dei driver Intel)
- `Ctrl+Alt+Su` sottosopra (180°) · `Ctrl+Alt+Giù` normale
- `Ctrl+Alt+Sinistra` (90°) · `Ctrl+Alt+Destra` (270°)
- `Ctrl+Alt+Shift+^` emergenza: ripristina l'orientamento normale

**Riavvio di Explorer**
- `Ctrl+Alt+Fine` (anche dal menu della tray): chiude e rilancia la shell.

**Barra a scomparsa stile Windows**
- Di default in alto, ridotta a una linguetta che si espande al passaggio del mouse;
  i pulsanti freccia la agganciano a sinistra/destra/alto. Colori dal tema di Windows.
- Avvii rapidi (trascina file o usa `＋`), terminali **PowerShell** e **CMD** a
  tendina con la console reale incorporata, Esplora file e orologio.

**Vista 3D (cover flow)**
- Anteprime dei file a schermo intero in 3D reale (WPF Viewport3D), con riflessi.
  Frecce/rotella per scorrere, Invio per aprire, Esc per chiudere.

**Vista 3D Game** 🎮
- Esplorazione in prima persona di dischi e cartelle come dentro un negozio, con
  un ingresso cinematografico (caricamento → il tesseratto colorato si apre come una
  porta a 4 parti → vista dall'alto → atterraggio). Movimento frecce/WASD, Invio o clic per
  entrare/aprire, Backspace per risalire.
- **Uscita**: in fondo a ogni stanza c'è una **porta-tesseratto** che si apre man mano
  che ti avvicini, scoprendo il desktop oltre il varco; attraversandola parte uno zoom
  che ti riporta al desktop. In alternativa, `Esc`.

**Modalità VR (visore)** 🥽 (opzionale, di default spenta)
- Menu della tray → **"Vista 3D Game in VR (visore)"**. Da accesa, aprire la Vista 3D
  Game lancia il **modulo VR separato** (`DesktopPager3D-VR`, basato su
  [StereoKit](https://stereokit.net/)/OpenXR), che ricostruisce la stanza in realtà
  virtuale: testa tracciata dal visore, spostamento con lo stick, selezione col raggio
  del controller.
- Richiede un **runtime OpenXR + visore** (es. **Meta Quest via Link/Air Link**, SteamVR,
  Windows Mixed Reality). Se manca il modulo o il visore, l'app **avvisa e ripiega** sulla
  vista 3D su schermo. Sulle GPU non adatte al VR (es. Intel HD 3000) resta semplicemente
  spenta: **nessun impatto** sull'uso normale.
- È un eseguibile a sé, **fuori dall'installer MSI**: chi non usa il VR non lo scarica
  nemmeno. Dettagli e comandi in [`src/DesktopPager.VR/README.md`](src/DesktopPager.VR/README.md).

**Effetti desktop 3D** (opzionali, di default spenti)
- 🧊 **Cubo del desktop**: tieni **Ctrl + tasto destro** e trascina sul desktop per
  far ruotare le pagine delle icone come le facce di un cubo 3D; al rilascio scatta
  alla faccia più vicina e cambia pagina.
- 🫧 **Finestre "gelatina"** (stile Compiz): spostando una finestra questa ondeggia
  in modo elastico. Il contenuto è un fermo-immagine durante il movimento e torna
  vivo al rilascio (limite tecnico di un'app non-compositor).
- Si attivano dal menu tasto-destro della barra o dal menu della tray, alla voce
  **"Effetti desktop 3D"**; lo stato viene ricordato tra un avvio e l'altro.

**Barra di Windows** (opzionale, di default spenta)

Menu della tray → **"Barra di Windows"**.

- 🖱️ **Sfoglia con la rotellina**: quando hai troppe applicazioni aperte per una riga
  sola, Windows impagina l'elenco e ti costringe a cliccare le freccette per cambiare
  pagina. Con questa opzione basta la **rotellina** sopra l'elenco: girandola, oppure
  **inclinandola** di lato per chi ha il mouse col tilt. Oppure **disattivato**, e
  restano le freccette di Windows.
  Attivandola, **le freccette bianche vengono nascoste** perché stonano col tema; resta
  la striscia da 17px dove stavano, che Explorer non ci lascia riutilizzare.

  > **Le pagine sono sempre verticali, anche col tilt.** Le due modalità cambiano solo
  > *quale* rotellina usi, non come Windows dispone le icone. La barra delle applicazioni
  > **non ha lo scorrimento orizzontale**: `WS_HSCROLL` è assente e `SB_HORZ` ha corsa
  > zero: Explorer manda le icone a capo in righe e impila le righe. Metterle in fila
  > orizzontale vorrebbe dire rifare l'impaginazione di Explorer a ogni finestra aperta o
  > chiusa, ed è il genere di lotta che fa cadere la shell.
- 🎨 **Colorala come la barra a linguetta**: tinge la barra di Windows con lo stesso
  colore scelto per la nostra. È una **tinta piatta**: Windows lascia impostare un colore
  di sfondo della sua barra, non il gradiente in rilievo (per quello servirebbe disegnare
  dentro Explorer, cosa che questo programma non fa di proposito).

Nessuno dei due inietta codice in Explorer: si usano solo messaggi e chiamate che
Windows consente verso finestre di altri processi (lo scorrimento manda alla barra
lo **stesso** `WM_VSCROLL` del clic sulla freccetta). All'uscita dal programma la barra
di Windows viene rimessa com'era; se il programma viene terminato di forza, basta
riavviare Explorer (`Ctrl+Alt+Fine`).

---

## Installazione (utente finale)

1. Scarica `DesktopPager3D-OS-1.3.0-Setup.msi` dalla pagina
   [Releases](https://github.com/fabionet/DesktopPager3D-OS/releases).
2. Doppio clic sull'MSI e segui la procedura guidata (richiede i privilegi di
   amministratore perché installa in `C:\Program Files\DesktopPager3D-OS`).
3. Avvia **DesktopPager3D-OS** dal menu Start.

L'installer è **autonomo (self-contained)**: include il runtime .NET 8, quindi
**non serve installare nulla** oltre all'MSI. L'MSI è firmato con il certificato
di FabioNET.

> Avvio automatico con Windows: apri l'app, clic destro sull'icona nella tray e
> attiva "Avvio automatico con Windows" (crea una voce in
> `HKCU\...\CurrentVersion\Run`).

---

## Compilazione dai sorgenti

### Prerequisiti
- **.NET 8 SDK** (per l'app principale, C# WinForms + WPF): <https://dotnet.microsoft.com/download/dotnet/8.0>
- **WiX Toolset v5** (solo per generare l'MSI), come strumento .NET:
  ```
  dotnet tool install --global wix --version 5.0.2
  wix extension add -g WixToolset.UI.wixext/5.0.2
  ```
- **w64devkit** (GCC/MinGW, solo per la versione nativa C++ opzionale): <https://github.com/skeeto/w64devkit>

### Compilare ed eseguire l'app
```
git clone https://github.com/fabionet/DesktopPager3D-OS.git
cd DesktopPager3D-OS
dotnet build src/DesktopPager.Tray/DesktopPager.Tray.csproj -c Release
dotnet run  --project src/DesktopPager.Tray/DesktopPager.Tray.csproj -c Release
```

### Eseguire i test
```
dotnet test tests/DesktopPager.Tray.Tests/DesktopPager.Tray.Tests.csproj -c Release
```

### Modulo VR opzionale (StereoKit/OpenXR)
Eseguibile separato, non incluso nell'MSI. Il pacchetto NuGet **StereoKit** porta con
sé le librerie native OpenXR.
```
dotnet build src/DesktopPager.VR/DesktopPager.VR.csproj -c Release
dotnet run  --project src/DesktopPager.VR/DesktopPager.VR.csproj -c Release
```
Senza un runtime OpenXR, StereoKit apre il **Simulatore** a schermo (mouse + WASD).
Per lanciarlo dall'app, accendi **"Vista 3D Game in VR (visore)"** nel menu della tray.

### Generare l'installer MSI
Lo script `scripts/build-and-package.ps1` esegue l'intero percorso (pubblicazione
self-contained + build dell'MSI) con percorsi relativi al repository. Versione e
nome dell'eseguibile vengono letti dal `.csproj`, quindi non serve aggiornarli a mano.

```powershell
# MSI non firmato (non serve il certificato)
.\scripts\build-and-package.ps1

# firma anche l'eseguibile e l'MSI (certificato CN=FabioNET nello store personale)
.\scripts\build-and-package.ps1 -Sign
```

L'MSI viene prodotto in `installer\DesktopPager3D-OS-<versione>-Setup.msi`.

> La firma usa `Set-AuthenticodeSignature` con SHA256 e marca temporale. L'eseguibile
> viene firmato **prima** della build dell'MSI, in modo che l'MSI incorpori un binario
> già firmato. Le release pubblicate sono sempre generate con `-Sign`.

<details>
<summary>Comandi equivalenti, senza lo script</summary>

```
# 1) pubblicazione self-contained
dotnet publish src/DesktopPager.Tray/DesktopPager.Tray.csproj -c Release -r win-x64 --self-contained true -o publish

# 2) build dell'MSI (percorsi ASSOLUTI per le variabili)
wix build installer/Product.wxs -ext WixToolset.UI.wixext ^
  -d "PublishDir=%CD%\publish" ^
  -d "IconPath=%CD%\src\DesktopPager.Tray\Assets\DesktopPager.ico" ^
  -d "LicenseRtf=%CD%\installer\License.rtf" ^
  -o "installer\DesktopPager3D-OS-1.3.0-Setup.msi"
```
</details>

> **Nota sulla versione:** il numero è duplicato in `DesktopPager.Tray.csproj`
> (`<Version>`) e in `installer/Product.wxs` (`Version=`). Lo script si ferma con un
> errore se i due divergono: aggiornali insieme.

### Versione nativa C++ (opzionale, componente leggero solo-pager)
In `src/DesktopPager.NativeCpp`. Build con MinGW/w64devkit:
```
cd src/DesktopPager.NativeCpp
windres resources.rc res.o
g++ -O2 -std=c++17 -municode -mwindows -DUNICODE -D_UNICODE -DWIN32_LEAN_AND_MEAN -DNOMINMAX main.cpp res.o -o DesktopPagerNative.exe -luser32 -lshell32 -lgdi32 -ladvapi32 -lcomctl32
```
oppure con CMake (generatore Visual Studio o MinGW). Hotkey identiche all'app .NET
(paginazione, rotazione, riavvio Explorer). Le viste 3D e la barra sono solo nell'app .NET.

---

## Struttura del progetto
```
src/DesktopPager.Tray/        app principale C# (WinForms + WPF)
src/DesktopPager.VR/          modulo VR opzionale (StereoKit/OpenXR) — eseguibile a sé
src/DesktopPager.NativeCpp/   versione nativa C++ Win32 (pager leggero)
tests/DesktopPager.Tray.Tests/ test xUnit
installer/                    sorgenti WiX dell'MSI + licenza
scripts/                      script di build/icona/packaging
```

---

## Requisiti di sistema
- Windows 10 o 11 (x64).
- Le viste 3D usano WPF/Direct3D; su GPU molto datate il cover flow ripiega su una
  resa 2D (GDI+). Testato su NVIDIA GeForce GT 540M / Intel HD 3000.

---

## Licenze

**DesktopPager3D-OS** è distribuito con licenza **MIT** — vedi il file
[LICENSE](LICENSE).

Componenti di terze parti coinvolti:

| Componente | Uso | Licenza |
|---|---|---|
| .NET 8 Runtime, Windows Desktop (WPF, Windows Forms) | runtime dell'app, incluso nel pacchetto self-contained | MIT |
| WiX Toolset v5 | generazione dell'installer MSI (solo build) | Microsoft Reciprocal License (MS-RL) |
| API di Windows (user32, shell32, gdi32, dwmapi, comctl32, ecc.) | funzioni di sistema tramite P/Invoke | Windows SDK / EULA Microsoft |
| Segoe UI (font) | testo dell'interfaccia | font di sistema Windows (licenza Microsoft) |
| Icone di sistema, anteprime shell, icone di dischi/cartelle | mostrate a runtime | fornite dal sistema operativo Windows |

Le icone e le anteprime dei file mostrate nelle viste sono quelle del sistema
operativo/dei relativi handler e restano di proprietà dei rispettivi titolari.

### Marchi

L'emblema di DesktopPager3D-OS — la moneta della barra, la porta dell'intro 3D e
la porta di uscita — è un **tesseratto** (ipercubo) di **disegno originale**.
**Il logo Windows non è più utilizzato in alcun punto del programma**: nessun
elemento grafico riproduce o imita marchi figurativi Microsoft.

**Windows®** e **Segoe®** sono marchi registrati di **Microsoft Corporation**.
Questo progetto è un'iniziativa **indipendente**, **non è affiliato, sponsorizzato
né approvato da Microsoft**. Il nome «Windows» compare solo con scopo descrittivo
e di interoperabilità (l'app gira su Windows e ne usa API, font e icone di
sistema, elencati nella tabella qui sopra). Tutti gli altri marchi citati
appartengono ai rispettivi titolari.
