# Yakuza 6 Localization Tool

Questo strumento automatizza l'estrazione, la traduzione e l'iniezione di testi (file `.bin`) e texture (`.dds`) per Yakuza 6, sfruttando `reARMP` e `ParTool`.

- [Yakuza 6 Localization Tool](#yakuza-6-localization-tool)
  - [Come usare il programma](#come-usare-il-programma)
    - [Requisiti](#requisiti)
    - [Fase 1: Estrazione](#fase-1-estrazione)
    - [Fase 2: Traduzione e Modifica](#fase-2-traduzione-e-modifica)
    - [Fase 3: Ricreazione e Iniezione](#fase-3-ricreazione-e-iniezione)
    - [Uso Avanzato (Argomenti da riga di comando)](#uso-avanzato-argomenti-da-riga-di-comando)
  - [Flusso Logico e Architettura](#flusso-logico-e-architettura)
    - [1. `Pipeline.cs` - L'Orchestratore](#1-pipelinecs---lorchestratore)
    - [2. `PoConverter.cs` - Il Parser dei Testi](#2-poconvertercs---il-parser-dei-testi)
    - [3. `CmnTextManager.cs` - Lo Scanner Binario](#3-cmntextmanagercs---lo-scanner-binario)
    - [4. Struttura del `dictionary.json`](#4-struttura-del-dictionaryjson)
  - [Dipendenze Esterne e Ringraziamenti](#dipendenze-esterne-e-ringraziamenti)


## Come usare il programma

### Requisiti
- Assicurati che `reARMP.exe` e `ParTool.exe` si trovino nella stessa cartella del programma.
- Assicurati che il file `dictionary.json` sia presente.

### Fase 1: Estrazione
1. Avvia il programma.
2. Inserisci il percorso della cartella principale del gioco (es. `C:\SteamLibrary\steamapps\common\Yakuza 6 - The Song of Life`).
3. Scegli l'**Opzione 1 (Estrazione)**.
   - Il tool scompatterà automaticamente l'archivio `data\ui.par`.
   - Estrarrà i file `.bin` e `.dds` necessari in base al dizionario e convertirà i testi in file `.po` pronti da tradurre.
4. Al termine, troverai una nuova cartella chiamata `Yakuza 6 - Patch`. I tuoi file su cui lavorare saranno in `Yakuza 6 - Patch\workspace`.

### Fase 2: Traduzione e Modifica
- Apri i file `.po` in `workspace\data\db\e` con un editor di traduzioni (come Poedit).
- Modifica le texture `.dds` in `workspace\data\ui.par.unpack\e\texture` con il tuo editor di immagini preferito (es. Photoshop).

### Fase 3: Ricreazione e Iniezione
1. Avvia nuovamente il programma.
2. Inserisci il percorso del gioco e scegli l'**Opzione 2 (Ricreazione)**.
   - Il tool inietterà i tuoi testi tradotti nei file `.json` e ricreerà i file `.bin` tramite `reARMP`.
   - Copierà le tue texture modificate e re-impacchetterà il file `ui.par` finale tramite `ParTool`.
3. Prendi il contenuto generato in `Yakuza 6 - Patch\output` e incollalo direttamente nella cartella di gioco per vedere la tua mod in azione!
4. **Nota:** Se un testo iniettato nei file `.cmn` supera il limite massimo di byte, il tool lo troncherà in modo sicuro e annoterà i dettagli in un file `warnings.txt` all'interno della cartella `Yakuza 6 - Patch`.

### Uso Avanzato (Argomenti da riga di comando)
Puoi avviare il tool da terminale o tramite uno script `.bat` usando queste opzioni per automatizzare tutto:

- `-g "PATH"`, `--game-path "PATH"`: Imposta direttamente il percorso del gioco.
- `-e`, `--extract`: Avvia in automatico la Fase 1 (Estrazione).
- `-r`, `--recreate`: Avvia in automatico la Fase 2 (Ricreazione).
- `-t`, `--skip-textures`: Salta l'estrazione e ricompressione delle texture con ParTool (velocizza i test sui testi).
- `-l "CODE"`, `--lang "CODE"`: Cambia la lingua dell'header nei file Gettext (es. `en`, `es`, `fr`). Il default è `it`.
- `-c`, `--clean-all`: Alla fine della Fase 2, elimina tutte le cartelle di lavoro (`og file`, `workspace`, ecc.) mantenendo SOLO `output`.
- `-y`, `--yes`: Salta tutte le domande di conferma Y/N e chiude in automatico alla fine.
- `-q`, `--quiet`: Nasconde i log di output dei tool esterni (reARMP, ParTool).
- `-d "PATH"`, `--dict "PATH"`: Specifica un file dizionario personalizzato (default: `dictionary.json`).

**Esempio (script .bat):**
`PoConverter.exe -g "C:\Steam\steamapps\common\Yakuza 6" -r -t -y -d "mio_dizionario.json"`



## Flusso Logico e Architettura

### 1. `Pipeline.cs` - L'Orchestratore
Questa classe gestisce il routing dei file e l'esecuzione dei tool esterni (`ParTool.exe` e `reARMP.exe`).

- **Inizializzazione**: Carica `dictionary.json` per creare due HashSet dei file consentiti: testi (`.bin`) e texture (`.dds`).
- **Estrazione (Fase 1)**:
  1. Esegue `ParTool.exe extract` per scompattare `ui.par`, `talk.par` e automaticamente tutti i file `.par` trovati all'interno della cartella `auth`.
  2. Scansiona i file nella cartella del gioco.
  3. Copia tutto l'albero originale in `og file` per conservare i dati base non modificati.
  4. Se un file rientra nel dizionario, lo copia anche in `workspace`.
  5. Per i file `.bin`, invoca `reARMP.exe` per generare il JSON, e successivamente chiama `PoConverter.JsonToPo` per estrarre le stringhe in un comodo file `.po`. Per i file `.cmn.bin`, utilizza lo scanner binario interno.
- **Ricreazione (Fase 2)**:
  1. Copia l'intero albero dei file originali da `og file` in `output`.
  2. Itera sui file `.po` tradotti nel `workspace`, chiamando `PoConverter.PoToJson` per aggiornare i JSON originali.
  3. Invoca `reARMP.exe` sul JSON tradotto per compilare il nuovo binario, sostituisce il vecchio `.bin` in `output` e sposta il JSON aggiornato in `converted json`.
  4. Copia le texture `.dds` modificate dal workspace in `output`, aggirando eventuali flag di "Sola Lettura".
  5. Avvia `ParTool.exe create` sulle cartelle estratte per generare archivi `.par` impacchettati e pronti all'uso, cancellando poi le cartelle temporanee.
  6. Pulisce la cartella `output` eliminando tutti i file `.bin` non previsti dal dizionario, mantenendo l'archivio pulito e pronto per la mod.

### 2. `PoConverter.cs` - Il Parser dei Testi
Gestisce la conversione bidirezionale tra l'output JSON di reARMP e il formato standard Gettext `.po`.

- **`JsonToPo`**: Usa un algoritmo ricorsivo (`ExtractValues`) per navigare l'albero JSON usando percorsi dinamici (con jolly `*`) definiti in `dictionary.json`. Scrive le stringhe trovate associando il percorso al `msgctxt` e il testo al `msgid`, effettuando l'escape dei caratteri (`\r\n`).
- **`PoToJson`**: Legge il file `.po` riga per riga, supportando le stringhe multi-riga. Usa un blocco `try-catch` robusto che previene il crash in caso di sintassi PO errata, segnalando la riga all'utente e proseguendo. Recupera il percorso JSON dal `msgctxt`, trova il token esatto e lo sovrascrive. Scrive infine il file JSON forzando ritorni a capo LF e indentazione a 2 spazi per renderlo digeribile a reARMP.

### 3. `CmnTextManager.cs` - Lo Scanner Binario
Uno scanner binario grezzo personalizzato per i file delle cutscene `.cmn`. Legge i file `.bin` byte per byte per estrarre le stringhe traducibili senza dipendere da decompilatori esterni, filtrando automaticamente gli ID interni del motore, stringhe ripetitive e i testi di debug puramente giapponesi. Durante la ricreazione, inietta in modo sicuro il testo tradotto nell'offset di memoria esatto, troncandolo se supera il limite di byte originale per prevenire la corruzione del file.

### 4. Struttura del `dictionary.json`
Il file dizionario indica al tool quali file elaborare e dove trovare il testo all'interno dei JSON. È diviso in due sezioni principali:

```json
{
  "texts": {
    "nome_file.bin.json": [
      ["percorso", "verso", "nodo_testo"],
      ["*", "jolly_supportato", "nodo"]
    ]
  },
  "textures": {
    "nome_texture.dds": []
  }
}
```
- `texts`: La chiave è il nome del file JSON. Il valore è un array di percorsi (che sono array di stringhe) che puntano alle stringhe traducibili. Puoi usare `*` come carattere jolly per iterare attraverso tutti gli elementi di un array o oggetto.
- `textures`: La chiave è il nome del file DDS. Il valore è un array vuoto `[]`. Funge da semplice whitelist delle texture da estrarre e re-impacchettare.



## Dipendenze Esterne e Ringraziamenti

- **[reARMP](https://github.com/Ret-HZ/reARMP)**: Script utilizzato per convertire i file `.bin` di Yakuza in `.json` e viceversa. Ringraziamenti a Ret-HZ.
- **[ParTool](https://github.com/Kaplas80/ParManager)**: Utilizzato per estrarre e ricomprimere gli archivi `.par`. Ringraziamenti a Kaplas80.
- **[Newtonsoft.Json](https://github.com/Newtonsoft/Json)**: Potente libreria .NET per la manipolazione dei file JSON.