# Evidence Majetku

**Evidence Majetku** je jednoduchá webová aplikace napsaná v C# (.NET 8), která slouží k evidenci dlouhodobého a krátkodobého majetku, včetně výpočtu odpisů a správy technických zhodnocení. Aplikace běží jako konzolová aplikace se zabudovaným webovým serverem a ukládá data o majetku do samostatných JSON souborů. Grafické rozhraní je vytvořeno v **Materialize** frameworku.

## Funkce

- **Seznam aktivního a vyřazeného majetku** – Možnost zobrazit všechny aktivní a vyřazené položky majetku.
- **Přidávání a editace majetku** – Uživatelé mohou přidávat nové položky majetku a zobrazovat detailní informace o existujícím majetku.
- **Výpočty odpisů** – Aplikace podporuje dva způsoby výpočtu odpisů (rovnoměrné a zrychlené) na základě odpisových skupin.
- **Zůstatková cena** – Výpočet zůstatkové ceny majetku na základě odpisů.
- **Přihlášení uživatelů** – Základní přihlašovací systém s uložením uživatelských účtů v JSON souborech.
- **Formuláře pro přidávání nového majetku** – Uživatelé mohou přidávat nové položky prostřednictvím formulářů, včetně možnosti zadání výrobce a dodavatele.
- **Ukládání dat** – Data o majetku jsou ukládána do samostatných JSON souborů.

## Požadavky

- .NET 8 SDK
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
- **data/** – Adresář, kde jsou uloženy JSON soubory pro jednotlivé položky majetku a uživatelská data.
- **assets/** – CSS a JavaScript soubory včetně knihovny Materialize.

## Použití

Po spuštění aplikace mohou uživatelé přidávat nové položky majetku, upravovat existující záznamy, zobrazovat detaily o majetku, generovat odpisy a spravovat přihlášení.
