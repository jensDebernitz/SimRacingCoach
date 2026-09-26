# Sim Racing Coach für Automobilista 2

[![CI](https://github.com/jensDebernitz/SimRacingCoach/actions/workflows/ci.yml/badge.svg)](https://github.com/jensDebernitz/SimRacingCoach/actions/workflows/ci.yml)
[![Release](https://github.com/jensDebernitz/SimRacingCoach/actions/workflows/release.yml/badge.svg)](https://github.com/jensDebernitz/SimRacingCoach/actions/workflows/release.yml)

Ein Fahrtrainer als Overlay: er zeigt live, wie viel Zeit du gerade gegenüber
deiner besten Runde gewinnst oder verlierst, sagt dir nach jeder Runde konkret,
in welcher Kurve du was verschenkst, erkennt **wie** du die Kurve fährst
(zu früh eingelenkt, noch auf der Bremse, zu früh aufs Gas), und warnt dich
während der Fahrt vor blockierenden Rädern, durchdrehenden Reifen und zu frühem
Hochschalten.

Gebaut für den Einstieg ins Sim-Racing – die Hinweise sind Klartext
("Kurve 4: 12 m später bremsen"), nicht Telemetriekurven zum Selbstdeuten.

Dazu zeichnet er dir die **Ideallinie perspektivisch auf den Asphalt**, sobald
er die Strecke ein paar Runden lang gesehen hat – siehe
[Ideallinie auf der Strecke](#ideallinie-auf-der-strecke).

Optional lässt sich ein eigener Gemini-Schlüssel hinterlegen. Dann kommt nach
jeder Runde eine Besprechung in ganzen Sätzen dazu, nach der Session ein Fazit
mit Trainingsplan, und du kannst dem Coach Fragen stellen. Ohne Schlüssel
funktioniert alles andere unverändert – siehe [KI-Coach](#ki-coach-optional).

---

## Was du brauchst

| | |
|---|---|
| Spiel | Automobilista 2 (Shared Memory auf "Project CARS 2") |
| Betriebssystem | Windows 10 (1809) oder Windows 11, x64 |
| Laufzeit | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) – bringt das Setup bei Bedarf selbst mit |
| Optional | Ein deutsches Sprachpaket für die Ansagen |

Das Lenkrad ist egal – die Moza R9 oder irgendein anderes Rad, der Coach liest
nur, was im Spiel ankommt.

---

## Einrichtung in drei Schritten

### 1. Shared Memory in AMS2 aktivieren

Ohne diesen Schritt bekommt der Coach keine Daten und die Statuszeile bleibt
rot.

> **AMS2 → OPTIONS → SYSTEM → SHARED MEMORY → `Project CARS 2`**

Danach AMS2 einmal neu starten.

### 2. AMS2 im Fenstermodus laufen lassen

> **AMS2 → OPTIONS → VIDEO → `Borderless` oder `Windowed`**

Das ist keine Schikane, sondern eine harte Grenze von Windows: über eine
Vollbild-Anwendung im *Fullscreen Exclusive*-Modus kann sich kein normales
Fenster legen. Im randlosen Fenstermodus kostet das praktisch keine Bildrate.

**In VR funktioniert das Overlay nicht.** Dort siehst du das Bild im Headset,
nicht auf dem Desktop. Die Sprachausgabe funktioniert aber weiterhin – wenn du
in VR fährst, schalte das Overlay aus (`Strg+Alt+O`) und höre nur zu.

### 3. Coach installieren und starten

Lade `SimRacingCoach-win-Setup.exe` von der
[Releases-Seite](https://github.com/jensDebernitz/SimRacingCoach/releases) und
führe es aus. Es installiert ohne Adminrechte nach `%LocalAppData%`, legt
Verknüpfungen auf dem Desktop und im Startmenü an und bringt die .NET-Laufzeit
mit, falls sie fehlt.

Wer nichts installieren will, nimmt `SimRacingCoach-win-Portable.zip` –
auspacken, `DrivingCoach.exe` starten. Dann gibt es allerdings keine Updates.

Erst AMS2 starten, dann den Coach – oder umgekehrt, das ist egal. Der Coach
verbindet sich von selbst, sobald AMS2 läuft, und wartet geduldig, wenn nicht.

### Updates

Der Coach sucht beim Start nebenher nach einer neuen Version und lädt sie im
Hintergrund. **Installiert wird erst beim Beenden.** Das ist Absicht: Ein
Neustart mitten in der Runde wäre der denkbar schlechteste Moment. Unten im
Overlay steht, woran du bist – `Version 1.2.0` oder `Version 1.3.0 bereit –
beim Beenden installiert`.

Ist kein Netz da, steht das dort ebenfalls, und sonst ändert sich nichts.

---

## Die erste Session

1. Ab auf die Strecke, egal ob Training oder Rennen.
2. **Fahr eine saubere Runde.** Sie wird automatisch deine Referenz – nicht
   deine schnellste jemals, sondern die erste gültige auf dieser Kombination
   aus Strecke und Auto.
3. Ab der zweiten Runde läuft der Delta-Balken mit, und am Ende jeder Runde
   bekommst du die drei teuersten Kurven vorgerechnet.
4. Wird eine Runde schneller, ersetzt sie die Referenz von selbst.
5. Ab der dritten gültigen Runde liegt zusätzlich die
   [Ideallinie](#ideallinie-auf-der-strecke) auf dem Asphalt – einmal
   eingemessen sein will sie allerdings.

Runden, die im Spiel als ungültig gewertet werden oder durch die Boxengasse
führen, wirft der Coach weg – eine abgekürzte Runde als Referenz wäre nutzlos.

Referenzrunden liegen unter
`%LocalAppData%\DrivingCoach\laps\<Strecke>_<Layout>_<Länge>_<Auto>.json`
und überleben einen Neustart.

---

## Was du im Overlay siehst

```
● Verbunden mit AMS2          Interlagos – GP · Formula Trainer   🔊
┌──────────────────────────────────────────────┐
│          ███████│                            │        +0,34
└──────────────────────────────────────────────┘
RUNDE          PROGNOSE        REFERENZ
1:32,481       1:34,102        1:33,762

Kurve 4 · langsame · Scheitel 78 km/h · in 61 m

 ╭─╮   ╭────╮      ╭──╮                          4
─╯ ╰───╯    ╰──────╯  ╰─                       184 km/h
 ▂▂▂▃▃▄▄▅▅▆▆▇▇██

▌ Kurve 4: 12 m später bremsen
▌ Vorderrad blockiert – Bremsdruck früher lösen

Letzte Runde 1:34,102 · +0,34 s
Kurve 4   12 m später bremsen                    +0,21 s
Kurve 7   9 km/h mehr Scheiteltempo              +0,09 s
```

**Delta-Balken** – wächst nach links und wird grün, wenn du schneller bist als
die Referenz; nach rechts und rot, wenn langsamer. Voller Ausschlag ist eine
Sekunde. Die Richtung ist mit Absicht so gewählt: beim Fahren erfasst der Blick
Farbe und Richtung, kein Vorzeichen.

**PROGNOSE** – was die laufende Runde wird, wenn du so weiterfährst.

**Kurve voraus** – welche Kurve kommt, wie schnell sie ist und wie weit der
Bremspunkt noch weg ist. Hast du dort in der letzten Runde Zeit verloren, kommt
rund 2,5 Sekunden vor dem Bremspunkt der passende Tipp – früh genug zum
Reagieren, spät genug zum Merken.

**Eingaben** – Gas (grün) und Bremse (rot) der letzten sechs Sekunden, darunter
die Lenkung. Hier siehst du das, was Rundenzeit kostet, ohne dass es sich so
anfühlt: zappelnde Lenkung, Gas und Bremse gleichzeitig, ruckartiges Lösen der
Bremse. Der schmale Balken darunter ist die Drehzahl; er wird gelb, wenn du
schalten solltest.

**Meldungen** – Fahrfehler und Kurventipps, die neuesten oben. Nach 25 Sekunden
verschwinden sie wieder.

**Letzte Runde** – die drei Kurven, die am meisten gekostet haben.

---

## Wie du die Kurve fährst

Der Zeitverlust sagt dir *wo* etwas fehlt, nicht *was*. Deshalb misst der Coach
zusätzlich deine Fahrweise und vergleicht sie mit der Referenzrunde:

| Befund | Woran er es merkt |
|---|---|
| zu früh / zu spät eingelenkt | wo der Lenkeinschlag einsetzt, verglichen mit der Referenz |
| unter vollem Bremsdruck eingelenkt | wie viel Bremse beim Einlenken noch anliegt |
| Bremse vor dem Einlenken ganz gelöst | wie viele Meter du gleichzeitig bremst und lenkst |
| Scheitelpunkt zu früh / zu spät | wo im Bogen der langsamste Punkt liegt |
| am Ausgang nachgelenkt | wenn der Lenkeinschlag nach dem Scheitel noch einmal zunimmt |
| zu früh / zu spät am Gas | Gasannahme gemessen am größten Lenkeinschlag, nicht am langsamsten Punkt |
| Gas und Bremse gleichzeitig | beide Pedale über mehrere Meter zusammen |
| unruhig am Lenkrad | Zahl der Richtungswechsel im Bogen |

Die Kette dahinter wird als Ursache benannt, nicht als Liste: *"40 m zu früh
eingelenkt, dadurch Scheitelpunkt zu früh und am Ausgang nachgelenkt – länger
geradeaus, dann entschlossener einlenken."*

### Kurvenkombinationen

Liegen zwei Kurven weniger als 130 m auseinander, bewertet der Coach sie
zusammen. In einer Schikane ist die einzelne Kurve die falsche Einheit: Wer die
erste perfekt trifft, steht für die zweite falsch und verliert auf der Geraden
danach mehr, als er im Bogen davor gewonnen hat.

Wie viel der Ausgang wert ist, hängt davon ab, was dahinter kommt – deshalb
misst der Coach die Gerade bis zum nächsten Bremspunkt mit:

> *"Kurven 4–5 (rechts-links): der Verlust steckt fast ganz in Kurve 5, und
> danach kommen 640 m Gerade. Opfere den Eingang – langsamer und weiter außen
> in Kurve 4, damit die letzte Kurve stimmt."*

Die Ansage während der Fahrt nimmt immer den Technikbefund, wenn es einen gibt:
"du lenkst zu früh ein" kannst du umsetzen, "0,21 s verloren" nicht.

---

## Ideallinie auf der Strecke

Der Coach legt dir ein blaues Band perspektivisch auf den Asphalt – dort, wo die
Linie durch die nächsten 140 Meter laufen sollte. In der Ferne wird es blasser
und verschwindet; wo es nur noch ein paar Pixel breit wäre, flimmert es sonst
mehr, als es hilft.

Ein-/ausschalten mit `Strg+Alt+L`. Die Einstellung bleibt gespeichert.

### Woher die Linie kommt

**Nicht aus deiner besten Runde.** Sie wird gerechnet:

1. Der Coach merkt sich aus jeder gefahrenen Runde, wo du langgefahren bist, und
   baut daraus eine Mittellinie der Strecke.
2. Wie breit es dort ist, verrät kein Datenfeld – die Schnittstelle von AMS2
   liefert **keine Streckenränder, keine Mittellinie, nicht einmal die Breite**.
   Der Coach leitet den nutzbaren Bereich deshalb daraus ab, wo du schon warst
   und wo Räder ins Gras gerieten, plus anderthalb Meter Zuschlag.
3. In diesem Korridor wird die Linie mit der geringsten Krümmung gesucht und mit
   dem Tempoprofil verheiratet, das dein Grip hergibt.

Daraus folgt: **die Linie ist erst nach drei gültigen Runden da** und wird mit
jeder weiteren besser. Solange steht im Overlay, woran es gerade fehlt
("Ideallinie: wird gelernt (2 Runden)"). Fährst du breiter, lernt der Coach
Strecke dazu – wer immer dieselbe enge Spur fährt, bekommt eine Linie, die nur
diese Spur kennt.

Gelernte Strecken liegen unter `%LocalAppData%\DrivingCoach\strecken\` und
gelten fürs nächste Mal. Sie hängen an der Strecke, nicht am Auto: Was in einer
Klasse über den Asphalt gelernt wurde, gilt in der nächsten weiter.

### Einmessen (einmal pro Auto)

Damit ein Punkt der Strecke am richtigen Fleck des Bildschirms landet, muss der
Coach wissen, wo deine Kamera sitzt. **Im Shared Memory steht davon nichts** –
weder Blickwinkel noch Sitzposition noch Neigung. Also einmal von Hand:

**Vorher in AMS2 abschalten**, sonst wandert die Linie dauernd:

> **OPTIONS → CAMERA → `World Movement`, `G-Force Effect`, Kopfbewegung → 0**

Diese Regler verschieben das Bild, ohne dass die Telemetrie davon erzählt.

Dann im Stand auf einer Geraden:

1. `Strg+Alt+E` – der Einmess-Kasten erscheint, die Linie wird eingeblendet.
2. `Strg+Alt+←` / `→` wählt den Wert, `Strg+Alt+↑` / `↓` verstellt ihn.
3. Schieben, bis das Band mittig und flach auf der Fahrbahn liegt.
4. `Strg+Alt+E` beendet das Einmessen.

| Wert | Wirkung |
|---|---|
| Blickwinkel | Wie stark die Linie zum Horizont zusammenläuft. **Zuerst einstellen** – am besten der Wert aus den Kameraeinstellungen von AMS2. |
| Augenhöhe | Schiebt den Horizont, also das ferne Ende des Bandes, hoch und runter |
| Sitz längs | Wo das Band vor der Motorhaube anfängt |
| Sitz seitlich | Verschiebt das Band nach links oder rechts |
| Neigung | Kippt das ganze Band, wenn Kamera und Fahrbahn nicht parallel laufen |

Gespeichert wird sofort, je Fahrzeug unter
`%LocalAppData%\DrivingCoach\kameras\`. Ein unbekanntes Auto startet mit der
zuletzt eingestellten Kalibrierung – der Blickwinkel stimmt dann schon, und es
bleibt meist beim Nachschieben von Höhe und Sitz. Nur die Verstellwege sind
begrenzt, damit dir die Linie beim Einmessen nicht plötzlich ganz abhandenkommt.

### Warum manchmal nichts zu sehen ist

Der Coach zeichnet lieber nichts als etwas Falsches – eine Linie an der falschen
Stelle führt in die Wand. Der Grund steht jeweils im Overlay:

| Text | Bedeutung |
|---|---|
| "noch keine gültige Runde gefahren" | Fahr eine saubere Runde. |
| "wird gelernt (n von 3 Runden)" | Ab drei gelernten Runden ist die Linie da. Gezählt wird, was in der Streckenkarte steht – nicht, was du gefahren bist. |
| "n Runden gefahren, aber keine Streckendaten" | AMS2 liefert keine Fahrzeugposition. Ohne sie kann keine Karte entstehen. Prüf in AMS2, ob die Shared Memory auf *Project CARS 2* steht. |
| "wartet auf eine Referenzrunde" | Die Strecke ist gelernt, aber es gibt keine Bestzeit, an der sich das Auto messen lässt. Eine saubere Runde genügt. |
| "wird berechnet …" | Steht nur ein paar Sekunden nach der dritten Runde. |
| "Blickrichtung noch nicht eingeordnet" | Wie AMS2 den Gierwinkel zählt, leitet der Coach aus einer Runde mit einem längeren geraden Stück ab. Kommt meist mit der ersten Runde von selbst. |
| "Kalibrierung unplausibel" | Ein Wert steht im Unsinn. `Strg+Alt+E` und geradeziehen. |

Die beiden Zahlen – gefahrene und gelernte Runden – laufen absichtlich getrennt.
Eine Runde zählt als gefahren, sobald sie sauber zu Ende ist; in die Karte kommt
sie nur mit Weltkoordinaten. Stehen da fünf gefahrene und null gelernte, liegt es
nicht an der Geduld, sondern an den Daten.

Meldet AMS2 dieselbe Strecke einmal in anderer Länge – andere Variante unter
gleichem Namen –, passt die gespeicherte Karte nicht mehr. Der Coach sagt dann
"Die gespeicherte Streckenkarte passt nicht mehr zu dieser Strecke" und fängt von
vorn an; nach drei Runden ist die Linie wieder da.

**In VR geht das nicht.** Das Band liegt auf dem Desktop-Bild, nicht im Headset.
Und Vollbild-Exclusive schließt es genauso aus wie das übrige Overlay.

---

## KI-Coach (optional)

Ohne Schlüssel läuft alles oben Beschriebene unverändert. Mit Schlüssel kommen
vier Dinge dazu.

### Einrichten

1. Einen API-Schlüssel im [Google AI Studio](https://aistudio.google.com/apikey)
   erzeugen. **Ein Gemini-Abo in der App (Google One) reicht nicht** – das lässt
   sich von außen nicht ansprechen.
2. Den Schlüssel eintragen, wahlweise:
   - in `%LocalAppData%\DrivingCoach\ai.json` (legt der Coach beim ersten Start
     kommentiert an), oder
   - als Umgebungsvariable `GEMINI_API_KEY` – die gewinnt, dann liegt nichts
     auf der Platte.
3. Coach neu starten – die Datei wird nur beim Start gelesen. Unten im Overlay
   steht dann `KI aktiv · gemini-3.8-flash`.

Der Schlüssel wird als HTTP-Header geschickt, nicht als Teil der Adresse – so
landet er nicht in irgendwelchen Zwischenspeichern oder Proxy-Protokollen.
**Leg ihn nicht ins Repository.**

### Modellname

Voreingestellt ist `gemini-3.8-flash`. Google nimmt alte Modelle regelmäßig vom
Netz – `gemini-2.5-flash` etwa antwortet neuen Schlüsseln nur noch mit 404.
Meldet der Coach "Modell nicht gefunden oder abgekündigt", trag in `ai.json`
`gemini-flash-latest` ein: der Name zeigt immer auf das aktuelle Flash-Modell.

Ganz frische Modelle sind in den ersten Tagen oft überlastet (503). Der Coach
versucht es dann dreimal und fährt sonst mit seinen gerechneten Hinweisen
weiter – kaputt geht dabei nichts.

### Was die KI macht

| | |
|---|---|
| **Rundenbesprechung** | Nach jeder Runde ein paar Sätze im Overlay, auf Wunsch vorgelesen |
| **Session-Fazit** | `Strg+Alt+F` – Trainingsplan über alle Runden, zusätzlich als Datei unter `%LocalAppData%\DrivingCoach\berichte\` |
| **Freie Fragen** | `Strg+Alt+K` – "warum verliere ich in Kurve 4 Zeit?", mit deiner Telemetrie als Grundlage |
| **Live-Ansagen** | Gemini formuliert die Kurventipps und Fehlerwarnungen aus |

Jedes davon lässt sich in `ai.json` einzeln abschalten.

### Warum das trotzdem nicht ruckelt

Während der Fahrt wird **nichts** angefragt. Am Rundenende steht fest, welche
Kurven in der nächsten Runde einen Tipp bekommen – genau diese Sätze werden im
Voraus geholt und abgelegt. Beim Auslösen findet nur noch ein Nachschlag in
einer Tabelle statt. Die Wartezeit fällt vor die Runde, nicht in die Kurve.

Und: **gerechnet wird weiter selbst.** Die KI bekommt die fertigen Messwerte als
Tabelle und formuliert daraus; sie rechnet keine Zeiten aus. Fällt das Netz aus,
fehlt der Schlüssel oder antwortet Gemini nicht, fährt der Coach unverändert mit
seinen gerechneten Hinweisen weiter – du merkst nur, dass die Sätze wieder
knapper werden.

Geschickt werden Strecke, Auto, Rundenzeiten und die Kurvenauswertung. Kein
Name, kein Konto, keine Datei.

---

## Tastenkürzel

Sie gelten systemweit, also auch während AMS2 den Fokus hat.

| Kürzel | Ausweichtasten | Wirkung |
|---|---|---|
| `Strg+Alt+O` | `H`, `Y` | Overlay ein-/ausblenden |
| `Strg+Alt+M` | `V`, `W`, `J` | Verschiebemodus – Rahmen wird gelb, Overlay mit der Maus ziehen, nochmal drücken zum Festsetzen |
| `Strg+Alt+S` | `P`, `U` | Sprachausgabe ein/aus |
| `Strg+Alt+L` | `I`, `G` | Ideallinie auf der Strecke ein/aus |
| `Strg+Alt+E` | `N`, `B` | Linie einmessen – dann `Strg+Alt+←` `→` Wert wählen, `↑` `↓` verstellen |
| `Strg+Alt+R` | `Z`, `X` | Referenzrunde verwerfen und neu aufbauen |
| `Strg+Alt+K` | `A`, `D` | Frage-Fenster öffnen (Eingabe braucht einen Gemini-Schlüssel) |
| `Strg+Alt+F` | `C`, `T` | Fazit der Session holen |
| `Strg+Alt+Q` | `Ende`, `Entf` | Coach beenden |

**Wenn ein Kürzel schon vergeben ist.** Tastenkürzel gelten systemweit, und wer
zuerst kommt, mahlt zuerst: Lenkrad-Software, Aufnahmeprogramme und
Herstellerwerkzeuge bedienen sich gern in derselben Ecke – `Strg+Alt+M` und
`Strg+Alt+R` trifft es besonders oft. Der Coach probiert deshalb die
Ausweichtasten der Reihe nach durch und schreibt unten ins Overlay, was daraus
geworden ist ("Belegt von einem anderen Programm, deshalb verlegt: …"). Die
Zeile mit der Belegung zeigt immer die Tasten, die wirklich gelten.

Auch die Pfeiltasten liegen auf `Strg+Alt`, obwohl sie nur beim Einmessen etwas
tun: Blanke Pfeiltasten systemweit zu belegen, würde jedes andere Programm
lahmlegen, solange der Coach läuft. Sind auch sie vergeben, rückt das Einmessen
auf den Ziffernblock (`4` `6` `8` `2`).

Das Frage-Fenster ist das einzige, das den Fokus von AMS2 wegzieht – anders
ließe sich nichts tippen. `Esc` schließt es wieder, der Gesprächsverlauf bleibt
stehen. Nimm es also besser in der Box und nicht auf der Gegengeraden.

Bewusst `Strg+Alt+Buchstabe` und keine F-Tasten: die sind in AMS2 mit Kameras,
Boxenfunk und Ansichten belegt.

`Strg+Alt+Q` ist der einzige Weg, den Coach wieder loszuwerden: das Fenster
steht weder in der Taskleiste noch in Alt-Tab und hat keinen Schließen-Knopf –
genau deshalb stört es beim Fahren nicht.

Position, Sprach-Einstellung und ob die Linie an ist, merkt sich der Coach in
`%LocalAppData%\DrivingCoach\overlay.json`.

Außerhalb des Verschiebemodus ist das Overlay **durchklickbar** – Mausklicks
landen im Spiel, nicht im Overlay. Es taucht auch nicht in Alt-Tab auf und
zieht nie den Fokus von AMS2 weg.

---

## Sprachausgabe

Die Ansagen laufen über SAPI, die Sprachausgabe von Windows – kein Cloud-Dienst
und kein zusätzliches Paket. Der Coach sucht sich automatisch eine deutsche
Stimme.

Findet er keine, steht das unten im Overlay. Nachrüsten:

> **Windows-Einstellungen → Zeit und Sprache → Sprache und Region → Deutsch →
> Sprachoptionen → Sprache (Text-zu-Sprache)**

Gesprochen wird sparsam und nach Dringlichkeit: Fahrfehler immer sofort, alles
andere mit mindestens vier Sekunden Abstand, derselbe Satz frühestens nach
20 Sekunden erneut. Während einer harten Bremsung oder beim starken Einlenken
hält der Coach die Klappe – wer gerade das Auto fängt, kann keinen Ratschlag
verarbeiten.

---

## Ohne AMS2 ausprobieren

```
DrivingCoach.Overlay.exe --sim
```

Startet einen eingebauten Simulator, der eine Testrunde mit realistischen
Schwankungen fährt. Damit lässt sich das Overlay einrichten und positionieren,
ohne das Spiel zu starten. Eine Runde dauert knapp 100 Sekunden; danach steht
die Referenz und der Delta-Balken lebt.

Die Simulator-Runde landet unter dem Schlüssel `Testkurs_Simulator_…` im
Rundenspeicher und stört deine echten Referenzrunden nicht.

---

## Selber bauen

```powershell
dotnet build  DrivingCoach.slnx -c Release
dotnet test   DrivingCoach.slnx
```

Es braucht nur das .NET-10-SDK. Die Projektmappe hat **eine einzige**
NuGet-Abhängigkeit – [Velopack](https://velopack.io) für Installer und Updates;
sonst kommt alles aus .NET und Windows selbst.

Das mitgelieferte `NuGet.config` setzt die Paketquellen per `<clear />` zurück
und nimmt nur nuget.org. Ohne das erbt der Build interne Firmenquellen aus
`%AppData%\NuGet\NuGet.Config` und bricht außerhalb des Firmennetzes ab.

### Veröffentlichen

Releases entstehen ausschließlich über einen Tag – nichts wird von Hand
hochgeladen:

```powershell
git tag -a v1.2.0 -m "Ideallinie und Einmess-Modus"
git push origin v1.2.0
```

[`.github/workflows/release.yml`](.github/workflows/release.yml) übernimmt den
Rest: testen, veröffentlichen, mit `vpk` paketieren und als GitHub-Release
hochladen. Ab dann finden installierte Coaches die Version von selbst.

| | |
|---|---|
| Tag-Format | `v1.2.0`, SemVer. Der Workflow bricht bei allem anderen ab, statt ein Release zu bauen, das die Anwendung nicht einordnen kann. |
| Vorabversion | `v1.3.0-beta.1` – alles mit Bindestrich wird automatisch als Pre-Release markiert und von normalen Installationen nicht gefunden. |
| Release-Notizen | Die Beschreibung eines annotierten Tags (`git tag -a`) landet im Release und in der Update-Anzeige. |
| Delta-Pakete | Der Workflow lädt das vorherige Release herunter, damit `vpk` Deltas bauen kann – ein Update sind dann ein paar hundert Kilobyte statt des ganzen Pakets. |
| Zugangsdaten | Keine. Es reicht das `GITHUB_TOKEN`, das GitHub dem Lauf ohnehin gibt. |

Die Version im Paket kommt aus dem Tag, nicht aus der Projektdatei – das
`<Version>` dort ist nur der Stand fürs Entwickeln.

**Nicht signiert.** Windows SmartScreen wird beim ersten Start warnen. Das
abzustellen kostet ein Code-Signing-Zertifikat; `vpk pack` nimmt es über
`--signParams` entgegen, wenn eines da ist.

### Aufbau

```
src/DrivingCoach.Telemetry/   Shared-Memory-Anbindung und Simulator
  Ams2/                       Struct-Layout v14, Reader mit Tear-Detection
  Simulation/                 Synthetische Strecke und Geschwindigkeitslöser
src/DrivingCoach.Coaching/    Die Fahrlogik – plattformfrei und testbar
src/DrivingCoach.Ai/          Gemini-Anbindung, Prompts, Ansagen-Vorrat
src/DrivingCoach.Overlay/     WPF-Fenster, Win32-Interop, Sprachausgabe
  Updates/                    Velopack: Suche im Hintergrund, Einbau beim Beenden
tests/DrivingCoach.Tests/     186 Tests über virtuelle Runden
.github/workflows/            CI bei jedem Push, Release bei jedem Tag
```

Die Fahrlogik hängt nicht an Windows und nicht an WPF. Sie bekommt
`TelemetryFrame`-Werte und gibt Meldungen zurück – deshalb lässt sie sich
vollständig gegen einen virtuellen Fahrer prüfen, ohne dass AMS2 installiert
sein muss.

### Wie die Tests ohne Spiel funktionieren

Fahrfehler werden nicht als Pedalwerte in die Daten gemalt, sondern als
Tempolimits in ein Geschwindigkeitsprofil gegeben. Ein Vorwärts-/Rückwärts-Löser
– dieselbe Technik wie in Rundenzeit-Simulationen – leitet daraus Bremspunkte,
Pedalstellungen und Rundenzeit ab. Nur so **kostet ein zu früher Bremspunkt im
Test auch wirklich Zeit**; sonst würde die Analyse gegen Daten geprüft, die es
im Spiel nie geben kann.

Die einzige Ausnahme ist der Einlenkpunkt: Ein zu frühes Einlenken lässt sich
nicht als Tempolimit ausdrücken. Dafür wird allein der Lenkkanal gegen die
Streckengeometrie verschoben – die Gierrate bleibt geometrisch, denn aus ihr
bestimmt der Coach die Kurvengrenzen, und die kommen immer aus der Referenz.

Die KI-Schicht wird ohne Netz geprüft: Getestet wird nicht die Anfrage, sondern
das Auswerten der Antwort – abgeschnittene Texte, fehlende Felder, Zahlen als
String – und die Filterung des Ansagen-Vorrats. Ein zu langer oder als
Aufzählung geratener Satz darf nicht mitten in der Kurve vorgelesen werden.

---

## Wenn etwas nicht geht

| Symptom | Ursache |
|---|---|
| Statuszeile bleibt rot, "AMS2 nicht gefunden" | Shared Memory steht nicht auf "Project CARS 2", oder AMS2 lief schon vor dem Umstellen. AMS2 neu starten. |
| "Verbunden (Versionswarnung)" | AMS2 hat das Speicherlayout geändert. Der Coach liest weiter, einzelne Werte können falsch sein. |
| Overlay ist unsichtbar über dem Spiel | AMS2 läuft im Fullscreen-Exclusive-Modus. Auf Borderless umstellen. |
| Overlay liegt hinter AMS2 | Sollte sich binnen einer Sekunde von selbst erledigen: Der Coach holt sich seinen Platz zurück, wenn ein anderes Fenster ihn beansprucht. Bleibt es dahinter, läuft AMS2 im Fullscreen-Exclusive-Modus (siehe Zeile darüber). |
| Overlay ist ganz weg | Vermutlich mit `Strg+Alt+O` ausgeblendet. Nochmal drücken. |
| Ein Tastenkürzel tut nichts | Ein anderes Programm hält die Kombination. Der Coach weicht selbsttätig auf eine andere Taste aus und schreibt unten ins Overlay, auf welche. |
| Keine Ansagen | Mit `Strg+Alt+S` prüfen (Symbol oben rechts), sonst fehlt ein deutsches Sprachpaket. |
| Es kommt nie eine Referenzrunde | Jede Runde wurde verworfen. Der Grund steht als Meldung im Overlay – meist "im Spiel als ungültig gewertet" (Strecke verlassen) oder "Boxengasse". |
| Keine Ideallinie zu sehen | Der Grund steht als eigene Zeile im Overlay – meist fehlen noch Runden. Sonst mit `Strg+Alt+L` prüfen, ob sie überhaupt an ist. |
| "n Runden gefahren, aber keine Streckendaten" | AMS2 meldet keine Fahrzeugposition, deshalb wächst die Streckenkarte nicht. Shared Memory in AMS2 auf *Project CARS 2* stellen und das Spiel neu starten. |
| Die Linie kommt auf einer Strecke nie, auf anderen schon | Die gespeicherte Karte passte nicht mehr. Der Coach sagt das jetzt und lernt neu – nach drei Runden ist sie da. Notfalls die Datei unter `%LocalAppData%\DrivingCoach\strecken\` löschen. |
| Die Linie liegt neben der Fahrbahn oder in der Luft | Noch nicht eingemessen: `Strg+Alt+E`, im Stand auf einer Geraden. |
| Die Linie wandert beim Bremsen und in Kurven | In AMS2 stehen `World Movement`, `G-Force Effect` oder die Kopfbewegung noch nicht auf 0. |
| Die Linie sitzt, aber nur in einem Auto | Kalibriert wird je Fahrzeug. In der anderen Klasse einmal `Strg+Alt+E`. |
| Die Ideallinie fährt eine enge Spur | Der Coach kennt nur, wo du warst. Fahr ein paar Runden bewusst breiter, dann wächst der Korridor. |
| "KI aus" unten im Overlay | Kein Schlüssel in `ai.json` und keine Umgebungsvariable `GEMINI_API_KEY`. Nach dem Eintragen den Coach neu starten. |
| Frage-Fenster nimmt keine Eingabe an | Ohne Schlüssel bleibt es gesperrt; oben im Fenster steht der Grund. |
| "Noch zu wenig Runden für ein Fazit" | Das Fazit braucht mindestens zwei ausgewertete Runden auf derselben Kombination aus Strecke und Auto. |
| "Modell nicht gefunden oder abgekündigt" | Google hat das Modell abgeräumt. In `ai.json` `gemini-flash-latest` eintragen und neu starten. |
| "Das Modell ist gerade überlastet" | Vorübergehend, nichts zu tun. Wer nicht warten will, trägt in `ai.json` ein älteres Flash-Modell ein. |
| "Schlüssel wird nicht akzeptiert" | Schlüssel abgelaufen oder aus dem falschen Projekt. Neu erzeugen unter [aistudio.google.com/apikey](https://aistudio.google.com/apikey). |
| Keine Versionszeile im Overlay | Der Coach läuft nicht aus einer Installation, sondern portabel oder aus dem Build-Verzeichnis. Dort gibt es keine Updates. |
| "Update nicht erreichbar" | Kein Netz oder GitHub gerade nicht erreichbar. Beim nächsten Start wird erneut gesucht; sonst ändert sich nichts. |
| Windows warnt beim Setup vor einer unbekannten App | Die Releases sind nicht signiert. Über "Weitere Informationen" lässt sich der Start erlauben. |
| Beim Öffnen des Frage-Fensters minimiert sich AMS2 | AMS2 läuft im Fullscreen-Exclusive-Modus. Auf Borderless umstellen. |

---

## Quellen zum Speicherlayout

Das Shared-Memory-Layout von AMS2 ist nicht offiziell dokumentiert. Die hier
verwendete Fassung (Version 14, 20 700 Byte, 64 Teilnehmer) stammt aus:

- [viper4gh/CREST2-AMS2](https://github.com/viper4gh/CREST2-AMS2)
- [Domaslau/AMS2SharedMemoryNet](https://github.com/Domaslau/AMS2SharedMemoryNet)
- [viper4gh/CREST2](https://github.com/viper4gh/CREST2)

Der Reader prüft die Versionsnummer beim Verbinden und meldet eine Abweichung,
statt stillschweigend Unsinn zu lesen.
