# DesktopPager.VR — modulo VR opzionale (StereoKit / OpenXR)

Versione in **realtà virtuale** della "Vista 3D Game": la stanza (pavimento,
pareti, porte delle cartelle, pannelli dei file) è ricostruita con
[StereoKit](https://stereokit.net/), un motore XR C# basato su **OpenXR**.

È un **eseguibile separato** (`DesktopPager3D-VR.exe`) dalla tray principale:
sul PC senza visore non viene mai lanciato, quindi non introduce regressioni.

## Requisiti

- Un runtime OpenXR + visore. Testato come target: **Meta Quest via Link/Air Link**
  (funzionano anche SteamVR e Windows Mixed Reality).
- GPU adeguata al VR. **La Intel HD 3000 non supporta OpenXR**: lì il modulo non
  parte (o si usa solo il Simulatore a schermo di StereoKit, non un visore).

## Avvio

```powershell
dotnet run --project src/DesktopPager.VR
```

Se non trova un runtime OpenXR, StereoKit apre il **Simulatore** a schermo
(mouse + WASD) — utile per lo sviluppo senza visore.

## Comandi (visore)

| Azione | Comando |
|---|---|
| Guardarsi intorno | Testa (tracking del visore) |
| Muoversi | Stick sinistro (avanti/laterale rispetto allo sguardo) |
| Girare a scatti | Stick destro (comfort VR) |
| Puntare un banco | Raggio del controller destro |
| Aprire / entrare | Grilletto destro |
| Cartella superiore | Pulsante X / A |
| Uscire | Esc (simulatore) |

## Stato — MVP

Fatto: stanza navigabile, dischi/cartelle/file come banchi, apertura di cartelle
e file, locomozione + snap-turn, selezione col raggio.

Da fare (parità con la vista WPF): anteprime reali dei file (ponte da
`ThumbnailProvider` a texture StereoKit), porte animate che si aprono
all'avvicinarsi, portale d'uscita col desktop, ingresso cinematografico,
integrazione della voce "Apri in VR" nella tray (vedi `OpenXrRuntime`).

Il modulo è **fuori da CI e dall'installer MSI**: la pipeline di release resta
invariata finché non lo si vuole includere.
