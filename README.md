# Midian Launcher

Il launcher di **Midian**, shard italiano di Ultima Online: `Avvia Midian.exe`.

- **Installa** il gioco: scaricato da solo dal sito, mette il client in `%USERPROFILE%\Midian` e crea il collegamento sul desktop.
- **Aggiorna** il client a ogni avvio con [Hina](https://github.com/Arutosio/Hina) (sottomodulo in `external/Hina`):
  scarica solo i pezzi dei file che sono cambiati. L'elenco dei file è firmato (Ed25519), e se l'aggiornamento
  si interrompe i file tornano com'erano prima.
- Aspetta che ClassicUO sia chiuso, imposta ip e porta del server, si aggiorna da solo e poi **avvia** ClassicUO.
- La grafica è quella di Ultima Online: la schermata col baule e l'ankh, la pergamena, le barre e i pulsanti originali.

Senza internet, o se qualcosa va storto, avvia comunque il gioco. Il diario è in `Launcher\launcher.log`.

## Compilare

Servono .NET 10 SDK e gli strumenti C++ di Visual Studio (NativeAOT).

```
git clone --recursive https://github.com/MidianUO/Midian-Launcher
node strumenti/estrai-gump.js <cartella UO> grafica.bin risorsa 1280,2520,2521,2522,2053,2054,5540,5541,5542,5513,5514,5515
dotnet publish -c Release -o <uscita>
```

`grafica.bin` contiene le gump del gioco (da `Gumpart.mul`) e non è nella repository: si crea con il comando qui sopra.
L'uscita è `Avvia Midian.exe` (circa 8 MB, non serve .NET installato) più `libsodium.dll`.

## Pubblicare un aggiornamento

Gli script sono in [Midian-ClassicUO/midian-launcher](https://github.com/MidianUO/Midian-ClassicUO/tree/main/midian-launcher):
`pubblica-aggiornamento.ps1` chiama `pubblica-hina.ps1`, che prepara i file del client e crea con `Hina.Builder`
l'elenco firmato e i pezzi, poi li carica sul server. La chiave privata di firma resta sul PC di chi pubblica.
La chiave pubblica è in `Aggiornatore.cs` (`CHIAVE_PUBBLICA`).

Opzioni per le prove: `--indirizzo <url>` (altre patch), `--radice <cartella>`, `--qui`, `--non-avviare`.

## Licenza

AGPL-3.0, come Hina (vedi `LICENSE`).
