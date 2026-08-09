# Aether Forge

**Lokaler Mod-Editor für [Age of Mythology: Retold](https://www.ageofempires.com/games/age-of-mythology-retold/)**

Aether Forge ist ein API-first Desktop-Tool zum Lesen, Bearbeiten und Veröffentlichen von **additiven Gameplay-Mods**. Du änderst Einheiten, Gebäude und verwandte Daten über eine schema-getriebene Oberfläche — ohne Retail-Dateien zu überschreiben und ohne Speicher- oder Multiplayer-Manipulation.

Die Anwendung erzeugt gültige Local-Mods im Retold-Format (`proto_mods.xml` mit `mergeMode="replace"`) und kann sie direkt in den lokalen Mods-Ordner installieren.

---

## Features

- **Entity Browser** — durchsuchbare, gruppierbare Übersicht über Units, Buildings und weitere Entitäten
- **Schema-getriebener Editor** — Tabs u. a. für Stats, Armor (%), Costs, Attack, Work; nur vorhandene Werte editierbar
- **Lossless XML** — unbekannte Felder bleiben erhalten; Diffs sind minimal und additiv
- **Mod-Generierung** — Ausgabe als `ModName/game/data/gameplay/proto_mods.xml`
- **Generate + Install** — schreibt direkt unter `%USERPROFILE%\Games\Age of Mythology Retold\<SteamID64>\mods\local`
- **Auto-Discovery** — erkennt den Local-Mods-Pfad beim Start (Steam-Account-abhängig)
- **Embedded Gameplay Pack** — beim Build aus lokalem Extract oder synthetischen Fixtures; Retail-XML wird **nicht** committed
- **API-first** — Avalonia-GUI spricht ausschließlich die lokale REST-API an (Loopback + Ephemeral Token)
- **Standalone Backend** — optional als reine API (`Backend+API.exe`) für externe Clients / Automatisierung

---

## Screenshots / Workflow

```text
Setup  →  Entity Browser  →  Unit/Building Editor  →  Generation  →  Local Mod
```

1. Optional eigenen Gameplay-Ordner importieren (sonst eingebettetes Pack)
2. Entität suchen und per Doppelklick / Enter öffnen
3. Werte anpassen (nur geänderte Leaves landen im Mod)
4. Mod-Namen wählen → **Generate** oder **Generate + Install**

---

## Anforderungen

| | |
|---|---|
| OS | Windows 10/11 (x64) |
| Runtime | Self-contained Release: **kein** separates .NET nötig |
| Entwickeln | [.NET 10 SDK](https://dotnet.microsoft.com/download) |
| Spiel | Age of Mythology: Retold (für Local-Mod-Installation) |

---

## Schnellstart (Release-Binaries)

Aus dem Repo:

```powershell
.\build\publish-release.ps1
```

Ergebnis in `release\`:

| Datei | Zweck |
|-------|--------|
| `GUI.exe` | Desktop-App (empfohlen) — startet eingebettete Local-API |
| `Backend+API.exe` | Nur REST-API / Backend |

Details: [`build/README.md`](build/README.md)

> **Hinweis:** `release/` und das eingebettete Pack sind gitignored. Pack wird beim Build aus `aomr_extract/original` (falls vorhanden) oder `fixtures/synthetic` erzeugt.

---

## Entwickeln

```powershell
# Build
dotnet build AomrModEditor.sln

# Tests
dotnet test AomrModEditor.sln

# GUI starten
dotnet run --project src/AomrModEditor.App

# API allein
dotnet run --project src/AomrModEditor.Api
```

Weitere Hinweise: [`docs/SETUP.md`](docs/SETUP.md)

### Eingebettetes Gameplay-Pack

Beim Build der App:

1. Wenn `aomr_extract/original/gameplay/proto.xml` existiert → Pack daraus
2. Sonst → synthetische Fixtures unter `fixtures/synthetic`

Das Pack (`EmbeddedGameplayPack.json.gz`) wird **nicht** versioniert — siehe `.gitignore`.

---

## Architektur

```text
┌─────────────────┐     REST (loopback)     ┌──────────────────┐
│  Avalonia GUI   │ ◄─────────────────────► │  ASP.NET Core    │
│  (App)          │   typed API client      │  API             │
└─────────────────┘                         └────────┬─────────┘
                                                     │
                    ┌────────────────────────────────┼────────────────┐
                    ▼                                ▼                ▼
             Application                    GameData / XML      ModGeneration
             (Use Cases)                    Persistence         (additive mods)
```

| Projekt | Rolle |
|---------|--------|
| `AomrModEditor.App` | Avalonia-Desktop-UI |
| `AomrModEditor.Api` | REST-API (auch embedded) |
| `AomrModEditor.Api.Client` | Typisierter HTTP-Client |
| `AomrModEditor.Api.Contracts` | DTOs / Verträge |
| `AomrModEditor.Application` | Use Cases, Schema, Klassifikation |
| `AomrModEditor.Domain` | Domänenmodell |
| `AomrModEditor.GameData` | Installation Discovery, Import |
| `AomrModEditor.ModGeneration` | Diff → `proto_mods.xml`, Installer |
| `AomrModEditor.Persistence` | SQLite / lokale State |
| `AomrModEditor.Cli` | Kommandozeile |

Stack: **C# / .NET 10**, ASP.NET Core, Avalonia 11, SQLite, System.Xml.Linq, xUnit.

Design-Notizen: [`design/aether_forge/DESIGN.md`](design/aether_forge/DESIGN.md)

---

## Mod-Ausgabe

Beispiel-Layout:

```text
MyCoolMod/
  game/
    data/
      gameplay/
        proto_mods.xml
```

Geänderte XML-Leaves erhalten `mergeMode="replace"`, analog zu gängigen Retold-Local-Mods. Es werden **nur** Differenzen geschrieben — kein Full-Replace der Basisdaten.

---

## Was dieses Tool **nicht** macht

- Keine Manipulation von laufenden Prozessen, Speicher oder Anti-Cheat
- Kein Eingriff in Multiplayer-Traffic oder Executables
- Kein Bundling / Commit von proprietären Retail-Gameplay-XMLs
- Kein „Cheat Injector“ — nur Editor + additive Mod-Pakete

---

## Wichtige Hinweise vor dem Push zu GitHub

**Nicht committen:**

- `aomr_extract/original/` — extrahierte Retail-Gameplay-Daten
- `src/.../EmbeddedGameplayPack.json.gz` — aus Retail abgeleitet
- `release/` — lokale Publish-Ausgaben
- `bin/`, `obj/`, `.vs/`, `__pycache__/`

Die Research-Markdowns unter `aomr_extract/*.md` und synthetische Fixtures sind fürs Repo gedacht; die Original-XML-Extrakte bleiben lokal.

---

## Lizenz & Spielinhalte

Dieses Repository enthält **keinen** offiziellen Age-of-Mythology-Inhalt von Microsoft / World's Edge / Xbox Game Studios.

Du brauchst eine legale Kopie von AoM: Retold, um:

- Gameplay-Daten für ein vollständiges Pack zu extrahieren, und
- Mods in den Local-Mods-Ordner zu installieren und im Spiel zu testen.

„Age of Mythology“ und zugehörige Marken gehören ihren jeweiligen Rechteinhabern. Dieses Projekt ist ein **unoffizielles Fan-/Community-Tool**.

---

## Mitwirken

1. Fork / Branch
2. `dotnet test AomrModEditor.sln` grün halten
3. Keine Retail-XMLs oder generierten Pack-Blobs einchecken
4. PR mit kurzer Beschreibung der Änderung

CI: [`.github/workflows/ci.yml`](.github/workflows/ci.yml) (`dotnet test` auf `windows-latest`).

---

## Dokumentation im Repo

| Pfad | Inhalt |
|------|--------|
| [`docs/SETUP.md`](docs/SETUP.md) | Setup & sicherer Workflow |
| [`build/README.md`](build/README.md) | Release-Publish |
| [`aomr_extract/`](aomr_extract/) | Research / Mod-Guide (ohne `original/`) |
| [`design/`](design/) | UI-/Produkt-Design |
| [`fixtures/synthetic/`](fixtures/synthetic/) | Synthetische Testdaten |

---

## Credits

Gebaut für die AoM: Retold Modding-Community — API-first, local-first, additive Mods first.
