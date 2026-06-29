# Evidence Majetku

**Evidence Majetku** je jednoduchá webová aplikace napsaná v C# (.NET 10), která slouží k evidenci dlouhodobého a krátkodobého majetku, včetně výpočtu odpisů a správy technických zhodnocení. Aplikace běží jako konzolová aplikace se zabudovaným webovým serverem a ukládá data o majetku do vestavěné databáze **LiteDB** (jeden soubor `data/evidence.db`). Grafické rozhraní je vytvořeno v **Materialize** frameworku.

## Funkce

- **Seznam aktivního a vyřazeného majetku** – Možnost zobrazit všechny aktivní a vyřazené položky majetku.
- **Přidávání a editace majetku** – Uživatelé mohou přidávat nové položky majetku a zobrazovat detailní informace o existujícím majetku.
- **Výpočty odpisů** – Aplikace podporuje rovnoměrné, zrychlené i mimořádné odpisy a evidenci bez odpisů, vždy na základě odpisových skupin.
- **Zůstatková cena** – Výpočet zůstatkové ceny majetku na základě odpisů a technického zhodnocení.

## Legislativa (stav 2026)

Výpočty a kontroly vycházejí ze zákona č. 586/1992 Sb., o daních z příjmů (ZDP). Daňové
parametry jsou soustředěny na jednom místě ve třídě `Legislation` v souboru `Program.cs`,
takže při změně zákona se upravují centrálně:

- **Rovnoměrné odpisy** – roční odpisové sazby dle § 31 odst. 1 ZDP.
- **Zrychlené odpisy** – koeficienty dle § 32 odst. 1 ZDP.
- **Hranice hmotného majetku 80 000 Kč** (§ 26 odst. 2 ZDP, od roku 2021). Movitý majetek
  pod tuto hranici je drobný majetek a daňově se neodpisuje.
- **Hranice technického zhodnocení 80 000 Kč** (§ 33 ZDP). Technické zhodnocení zvyšuje
  vstupní (zůstatkovou) cenu majetku.
- **Nehmotný majetek** – daňové odpisy byly zrušeny od roku 2021 (zrušení § 32a ZDP),
  uplatní se účetní odpis.
- **Mimořádné odpisy (§ 30a ZDP)** – pro majetek v odpisové skupině 1 a 2 pořízený
  v letech 2020–2023; od roku 2024 pouze pro bezemisní vozidla.

Aplikace při generování odpisů tyto podmínky kontroluje a v případě rozporu se zákonem
vrátí srozumitelné upozornění místo vygenerování neplatného odpisového plánu.
- **Přihlášení uživatelů** – Základní přihlašovací systém s uložením uživatelských účtů v JSON souborech.
- **Formuláře pro přidávání nového majetku** – Uživatelé mohou přidávat nové položky prostřednictvím formulářů, včetně možnosti zadání výrobce a dodavatele.
- **Ukládání dat** – Data o majetku jsou uložena ve vestavěné databázi LiteDB (`data/evidence.db`).
- **Import existujících JSON** – Soubory z dřívější verze (`data/*.json`) se automaticky naimportují při startu a lze je importovat i ručně tlačítkem **Import JSON** v seznamu majetku.

## Ukládání dat a databáze

Aplikace používá vestavěnou (embedded) databázi **LiteDB** – běží zcela lokálně, bez nutnosti
samostatného databázového serveru, a ukládá vše do jediného souboru `data/evidence.db`.

### Migrace ze starší verze (import JSON)

Dřívější verze ukládala každý majetek do samostatného souboru `data/<číslo>.json`. Migrace je
automatická:

- **Při startu** aplikace naimportuje všechny `data/*.json` do databáze a zpracované soubory
  přesune do `data/imported/` (aby se neimportovaly opakovaně). Čísla majetku zůstávají zachována.
- **Ručně** lze import kdykoli spustit tlačítkem *Import JSON* v seznamu majetku, případně
  voláním `POST /import-json`.

## Požadavky

- .NET 10 SDK
- Materialize CSS a JavaScript knihovna (lokálně přidána do projektu)
- Webový prohlížeč (pro uživatelské rozhraní)

## Instalace a spuštění

1. Naklonujte repozitář:
    ```bash
    git clone https://github.com/mirapavlicek/evidCZFree.git
    ```

2. Otevřete projekt v IDE (Visual Studio nebo jiný C# editor).

3. Spusťte aplikaci jako konzolovou aplikaci:
    ```bash
    dotnet run
    ```

4. Aplikace poběží na `http://localhost:8080`.

## Struktura projektu

- **Program.cs** – Hlavní serverový kód aplikace, který spravuje zpracování požadavků a routování.
- **HTML soubory** – Frontend soubory pro zobrazení dat, včetně přihlašovacího formuláře, seznamu majetku a formuláře pro přidávání majetku.
- **data/** – Adresář s databází `evidence.db` (LiteDB) a podsložkou `imported/` s již naimportovanými JSON soubory.
- **assets/** – CSS a JavaScript soubory včetně knihovny Materialize.

## Použití

Po spuštění aplikace mohou uživatelé přidávat nové položky majetku, upravovat existující záznamy, zobrazovat detaily o majetku, generovat odpisy a spravovat přihlášení.

## Typy na závěr

Pokud je potřeba naslouchat na všech IP adresách, je potřeba na řádku
```c++
listener.Prefixes.Add("http://localhost:8080/");
#na
listener.Prefixes.Add("http://+:<cisloportu>/");
```

Je tam navržené i přihlašování ze způsobu použití aplikace je to celkem nepotřebné. 
Data jsou nově uložena ve vestavěné databázi LiteDB (`data/evidence.db`) – aplikace tak
nepotřebuje samostatný databázový server a běží čistě lokálně. Pro zálohu stačí zkopírovat
soubor `data/evidence.db`. Data z původní verze ve formátu JSON se automaticky naimportují
(viz sekce *Ukládání dat a databáze*).
