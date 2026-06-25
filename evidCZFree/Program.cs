using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

class Program
{
    static void Main(string[] args)
    {
        string htmlFolder = Path.Combine(Directory.GetCurrentDirectory(), "html");
        string jsonFolder = Path.Combine(Directory.GetCurrentDirectory(), "data");

        // Zajištění existence složky s daty
        if (!Directory.Exists(jsonFolder))
        {
            Directory.CreateDirectory(jsonFolder);
        }

        // Inicializace databáze LiteDB (jediný soubor data/evidence.db).
        Db.Init(Path.Combine(jsonFolder, "evidence.db"));

        // Automatický import dříve uložených JSON souborů (migrace ze staré verze).
        int importedCount = ImportJsonFiles(jsonFolder);
        if (importedCount > 0)
        {
            Console.WriteLine($"Importováno {importedCount} majetků z JSON souborů do databáze.");
        }

        // Spuštění jednoduchého webového serveru
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();
        Console.WriteLine("Server is listening...");

        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

          try
          {
            // Dynamické načítání HTML souborů
            if (request.HttpMethod == "GET" && request.Url.AbsolutePath.EndsWith(".html"))
            {
               ServeHtmlFile(request, response, htmlFolder);
            }
            // Endpoint pro načtení konkrétního assetu podle assetNumber
            else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/get-asset")
            {
                HandleGetAsset(request, response, jsonFolder);
            }
            // Endpoint pro aktualizaci assetu s vyřazením
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/update-asset")
            {
                HandleUpdateAsset(request, response, jsonFolder);
            }
            // Endpoint pro načtení unikátních výrobců a dodavatelů
            else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/get-manufacturers-suppliers")
            {
                HandleGetManufacturersAndSuppliers(request, response, jsonFolder);
            }
            // Načítání úvodní stránky (asset-list.html)
            else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/")
            {
                if (!IsAuthenticated(request))
                {
                   // response.Redirect("/login.html");
                    //response.OutputStream.Close();
                    //return;
                }
                HandleGetAssetList(request, response, htmlFolder, jsonFolder);
            }
            // Endpoint pro načtení všech assetů
            else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/get-assets")
            {
                HandleGetAssets(request, response, jsonFolder);
            }
            // Obsluha formuláře pro přidávání nového majetku
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/add-item")
            {
                HandleAddItem(request, response, jsonFolder);
            }
            // Endpoint pro generování odpisů
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/generate-depreciations")
            {
                HandleGenerateDepreciations(request, response, jsonFolder);
            }
            // Endpoint pro manuální import JSON souborů do databáze
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/import-json")
            {
                HandleImportJson(request, response, jsonFolder);
            }
            // Dynamické načítání statických souborů (CSS, JS, fonty, obrázky)
            else if (request.HttpMethod == "GET" && request.Url.AbsolutePath.StartsWith("/assets/"))
            {
                ServeStaticFile(request, response);
            }
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/login")
            {
                HandleLogin(request, response);
            }
            else
            {
                HandleNotFound(response);
            }
          }
          catch (Exception ex)
          {
              // Jedna chybná žádost nesmí shodit celý server.
              Console.WriteLine("Neošetřená chyba při zpracování požadavku: " + ex.Message);
              try
              {
                  response.StatusCode = (int)HttpStatusCode.InternalServerError;
                  response.OutputStream.Close();
              }
              catch
              {
                  // Odpověď už mohla být odeslána / uzavřena – ignorujeme.
              }
          }
        }
    }

    // Robustní parsování data zadaného uživatelem. Frontend (Materialize
    // datepicker) posílá datum ve formátu dd/mm/yyyy, proto preferujeme
    // českou kulturu. Pro zpětnou kompatibilitu zkoušíme i další formáty.
    public static bool TryParseDate(string value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] formats =
        {
            "dd/MM/yyyy", "d/M/yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd"
        };

        var cs = System.Globalization.CultureInfo.GetCultureInfo("cs-CZ");
        if (DateTime.TryParseExact(value.Trim(), formats, cs,
                System.Globalization.DateTimeStyles.None, out result))
        {
            return true;
        }

        // Poslední pokus – obecné parsování v české kultuře.
        return DateTime.TryParse(value.Trim(), cs,
            System.Globalization.DateTimeStyles.None, out result);
    }

    // Bezpečné získání roku z data; vrací aktuální rok, pokud se nepodaří parsovat.
    public static int GetYearOrCurrent(string value)
    {
        return TryParseDate(value, out var d) ? d.Year : DateTime.Now.Year;
    }

    // =====================================================================
    //  Datová vrstva – úložiště majetku v databázi LiteDB.
    //  Nahrazuje původní ukládání po jednotlivých JSON souborech.
    //  Pozn.: LiteDB má vlastní JsonSerializer, proto jsou jeho typy níže
    //  plně kvalifikované (LiteDB.*), aby nekolidovaly se System.Text.Json.
    // =====================================================================
    public static class Db
    {
        private static LiteDB.LiteDatabase _database;
        private static LiteDB.ILiteCollection<Asset> _assets;
        private static readonly object _lock = new object();

        public static void Init(string dbPath)
        {
            // Číslo majetku (AssetNumber) je primární klíč. Auto-id nepoužíváme –
            // číslo přidělujeme sami (viz Insert), aby nedošlo ke kolizi po importu.
            LiteDB.BsonMapper.Global.Entity<Asset>().Id(a => a.AssetNumber, autoId: false);
            _database = new LiteDB.LiteDatabase($"Filename={dbPath};Connection=direct");
            _assets = _database.GetCollection<Asset>("assets");
        }

        public static List<Asset> GetAll()
        {
            lock (_lock) { return _assets.FindAll().ToList(); }
        }

        public static Asset Get(int assetNumber)
        {
            lock (_lock) { return _assets.FindById(assetNumber); }
        }

        public static bool Exists(int assetNumber)
        {
            lock (_lock) { return _assets.FindById(assetNumber) != null; }
        }

        // Vloží nový majetek a přidělí mu další volné číslo. Vrací přidělené číslo.
        public static int Insert(Asset asset)
        {
            lock (_lock)
            {
                var all = _assets.FindAll().ToList();
                int next = all.Count == 0 ? 1 : all.Max(a => a.AssetNumber) + 1;
                asset.AssetNumber = next;
                _assets.Insert(asset);
                return next;
            }
        }

        public static bool Update(Asset asset)
        {
            lock (_lock) { return _assets.Update(asset); }
        }

        // Vloží nebo přepíše majetek se zachováním jeho čísla (použito při importu).
        public static void Upsert(Asset asset)
        {
            lock (_lock) { _assets.Upsert(asset); }
        }
    }

    // Import dříve uložených JSON souborů (data/*.json) do databáze LiteDB.
    // Zpracované soubory se přesouvají do data/imported/, aby se neimportovaly
    // znovu. Vrací počet úspěšně naimportovaných majetků.
    public static int ImportJsonFiles(string folder)
    {
        if (!Directory.Exists(folder)) return 0;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        string importedDir = Path.Combine(folder, "imported");
        int imported = 0;

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            // Přeskočíme uživatelský soubor s přihlášením, je-li ve složce.
            if (string.Equals(Path.GetFileName(file), "user.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                string json = File.ReadAllText(file);
                Asset asset = JsonSerializer.Deserialize<Asset>(json, options);
                if (asset == null) continue;

                // Bez platného čísla přidělíme nové; jinak zachováme původní číslo.
                if (asset.AssetNumber <= 0)
                {
                    Db.Insert(asset);
                }
                else
                {
                    Db.Upsert(asset);
                }
                imported++;

                // Zpracovaný soubor přesuneme do podsložky 'imported'.
                Directory.CreateDirectory(importedDir);
                string target = Path.Combine(importedDir, Path.GetFileName(file));
                if (File.Exists(target)) File.Delete(target);
                File.Move(file, target);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba při importu souboru {file}: {ex.Message}");
            }
        }

        return imported;
    }

    // Endpoint pro manuální spuštění importu JSON souborů.
    public static void HandleImportJson(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        int count = ImportJsonFiles(jsonFolder);
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = true, imported = count }));
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    public static decimal CalculateResidualValue(Asset asset)
    {
        // Počáteční zůstatková hodnota je pořizovací cena
        decimal residualValue = asset.AcquisitionCost;

        // Získáme aktuální rok
        int currentYear = DateTime.Now.Year;

        // Technické zhodnocení (§ 33 ZDP) zvyšuje vstupní (zůstatkovou) cenu
        // majetku v roce, kdy bylo dokončeno a uvedeno do stavu způsobilého užívání.
        if (asset.TechnicalAppreciations != null)
        {
            foreach (var ta in asset.TechnicalAppreciations)
            {
                if (ta.Year <= currentYear)
                {
                    residualValue += ta.Amount;
                }
            }
        }

        // Projdeme všechny odpisy a odečteme ty, které jsou do aktuálního roku včetně
        foreach (var depreciation in asset.Depreciations)
        {
            if (depreciation.Year <= currentYear)
            {
                residualValue -= depreciation.Amount;
            }
        }

        return residualValue;
    }

    public static void HandleLogin(HttpListenerRequest request, HttpListenerResponse response)
    {
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "user.json");



        if (request.HttpMethod == "POST")
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string json = reader.ReadToEnd();
                var loginData = JsonSerializer.Deserialize<LoginData>(json);

                var users = LoadUsers(filePath);

                // Ověření uživatelského jména a hesla
                bool isAuthenticated = users.Any(user => user.Username == loginData.Username && user.Password == loginData.Password);

                if (isAuthenticated)
                {
                    // Vygenerování jednoduchého tokenu (pro demonstraci)
                    string authToken = GenerateAuthToken(loginData.Username);

                    // Nastavení cookie
                    response.AddHeader("Set-Cookie", $"authToken={authToken}; Path=/; HttpOnly");

                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.OutputStream.Close();
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    response.OutputStream.Close();
                }
            }
        }

    }

    public static string GenerateAuthToken(string username)
    {
        // Pro jednoduchost používáme kombinaci username a aktuálního času
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{DateTime.Now.Ticks}"));
    }

    public static bool IsAuthenticated(HttpListenerRequest request)
    {
        // Načtení cookies z požadavku
        string authToken = null;

        if (request.Headers["Cookie"] != null)
        {
            string[] cookies = request.Headers["Cookie"].Split(';');

            foreach (var cookie in cookies)
            {
                var cookieParts = cookie.Trim().Split('=');
                if (cookieParts[0] == "authToken" && cookieParts.Length == 2)
                {
                    authToken = cookieParts[1];
                    break;
                }
            }
        }

        // Pokud je token nalezen a validní, uživatel je přihlášen
        if (!string.IsNullOrEmpty(authToken) && ValidateAuthToken(authToken))
        {
            return true;
        }

        return false; // Nepřihlášený uživatel
    }

    // Jednoduché ověření tokenu (může být nahrazeno bezpečnějším řešením)
    public static bool ValidateAuthToken(string authToken)
    {
        try
        {
            string decodedToken = Encoding.UTF8.GetString(Convert.FromBase64String(authToken));
            string[] parts = decodedToken.Split(':');

            // Ověříme pouze formát (username a časová značka)
            if (parts.Length == 2)
            {
                return true; // Token je validní
            }
        }
        catch
        {
            return false; // Token je neplatný
        }

        return false; // Token je neplatný
    }

    // Načítání uživatelů z JSON
    public static List<User> LoadUsers(string filePath)
    {
        if (!File.Exists(filePath)) return new List<User>();

        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<User>>(json);
    }

    public class User
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class LoginData
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public static void HandleViewAsset(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        int.TryParse(request.QueryString["assetNumber"], out int assetNumberId);
        Asset asset = Db.Get(assetNumberId);

        if (asset != null)
        {
            // Spočítáme zůstatkovou cenu
            decimal residualValue = CalculateResidualValue(asset);

            // Přidáme zůstatkovou cenu do odpovědi
            var assetWithResidualValue = new
            {
                asset.AssetNumber,
                asset.Name,
                asset.AcquisitionCost,
                asset.Depreciations,
                residualValue // Přidáme zůstatkovou cenu
            };

            // Odpověď ve formátu JSON
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;

            using (var writer = new StreamWriter(response.OutputStream))
            {
                writer.Write(JsonSerializer.Serialize(assetWithResidualValue, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.OutputStream.Close();
        }
    }

    public static void HandleGetAssetList(HttpListenerRequest request, HttpListenerResponse response, string htmlFolder, string jsonFolder)
    {
        string htmlFilePath = Path.Combine(htmlFolder, "asset-list.html");

        if (File.Exists(htmlFilePath))
        {
            string html = File.ReadAllText(htmlFilePath, Encoding.UTF8);

            // Pokud máš implementaci pro vkládání seznamu aktiv do HTML, můžeš ji tady upravit.

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            string responseString = "<html><body><h1>Soubor nenalezen</h1></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
    }

    public static void ServeStaticFile(HttpListenerRequest request, HttpListenerResponse response)
    {
        // Získání cesty k souboru podle URL
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), request.Url.AbsolutePath.TrimStart('/'));

        // Ověření, zda soubor existuje
        if (File.Exists(filePath))
        {
            // Nastavení MIME typu podle přípony souboru
            string extension = Path.GetExtension(filePath).ToLower();
            string mimeType = extension switch
            {
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".woff2" => "font/woff2",
                ".woff" => "font/woff",
                ".ttf" => "font/ttf",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream" // Výchozí typ pro neznámé přípony
            };

            // Načtení obsahu souboru
            byte[] fileData = File.ReadAllBytes(filePath);

            // Nastavení MIME typu a délky obsahu
            response.ContentType = mimeType;
            response.ContentLength64 = fileData.Length;

            // Odeslání souboru do výstupního streamu
            response.OutputStream.Write(fileData, 0, fileData.Length);
            response.OutputStream.Close();
        }
        else
        {
            // Pokud soubor neexistuje, vrátíme chybu 404 (Not Found)
            response.StatusCode = (int)HttpStatusCode.NotFound;
            string responseString = "<html><body><h1>Soubor nenalezen</h1></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
    }

    public static void ServeHtmlFile(HttpListenerRequest request, HttpListenerResponse response, string htmlFolder)
    {
        // Získání cesty k požadovanému HTML souboru podle URL
        string filePath = Path.Combine(htmlFolder, request.Url.AbsolutePath.TrimStart('/'));

        // Ověření, zda soubor existuje
        if (File.Exists(filePath))
        {
            // Načtení obsahu HTML souboru
            byte[] fileData = File.ReadAllBytes(filePath);

            // Nastavení MIME typu na text/html
            response.ContentType = "text/html";
            response.ContentLength64 = fileData.Length;

            // Odeslání souboru do výstupního streamu
            response.OutputStream.Write(fileData, 0, fileData.Length);
            response.OutputStream.Close();
        }
        else
        {
            // Pokud soubor neexistuje, vrátíme chybovou stránku
            response.StatusCode = (int)HttpStatusCode.NotFound;
            string responseString = "<html><body><h1>HTML soubor nenalezen</h1></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
    }


    public static void HandleGetAssets(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        var assets = Db.GetAll();
        var json = JsonSerializer.Serialize(assets);

        // Nastavení odpovědi
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        using (var writer = new StreamWriter(response.OutputStream))
        {
            writer.Write(json);
        }
    }

    // Pomocná funkce pro načtení všech majetků (z databáze).
    public static List<Asset> GetAllAssets(string folder)
    {
        return Db.GetAll();
    }


  

    public static void HandleNotFound(HttpListenerResponse response)
    {
        try
        {
            // Pokud soubor neexistuje
            response.StatusCode = (int)HttpStatusCode.NotFound;
            string responseString = "<html><body><h1>Stránka nenalezena</h1></body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;

            // Zápis odpovědi do streamu
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Chyba: " + ex.Message);
        }
        finally
        {
            // Uzavření streamu, pokud není již uzavřený
            if (response.OutputStream != null && response.OutputStream.CanWrite)
            {
                response.OutputStream.Close();
            }
        }
    }

    public static void HandleAddItem(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        // Načtení dat z POST požadavku
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            string json = reader.ReadToEnd();

            // Ladicí výpis JSON dat pro kontrolu
            Console.WriteLine("Přijatý JSON:");
            Console.WriteLine(json);
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Ignorování velikosti písmen
                };

                Asset newAsset = JsonSerializer.Deserialize<Asset>(json, options);

                if (newAsset != null)
                {
                    // Uložení nového majetku do databáze (číslo přidělí Db.Insert)
                    Db.Insert(newAsset);

                    // Odpověď na POST požadavek
                    string responseString = "<html><body><h1>Položka byla úspěšně přidána!</h1></body></html>";
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    string responseString = "<html><body><h1>Neplatná data</h1></body></html>";
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine("Chyba při deserializaci JSON: " + ex.Message);
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                string responseString = "<html><body><h1>Chyba při zpracování JSON!</h1></body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }
    }

    // Pomocná funkce pro načtení všech majetků (z databáze).
    public static List<Asset> LoadAllAssets(string folder)
    {
        return Db.GetAll();
    }

    // Pomocná funkce pro získání dalšího čísla majetku
    public static int GetNextAssetNumber(List<Asset> assets)
    {
        if (assets.Count == 0) return 1;
        return assets.Max(a => a.AssetNumber) + 1;
    }

    public static void HandleGetManufacturersAndSuppliers(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        // Získání výrobců a dodavatelů
        var data = GetManufacturersAndSuppliers(jsonFolder);
        var json = JsonSerializer.Serialize(data);

        // Nastavení odpovědi
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        using (var writer = new StreamWriter(response.OutputStream))
        {
            writer.Write(json);
        }
    }

    // Pomocná funkce pro načtení všech výrobců a dodavatelů z JSON souborů
    public static ManufacturersAndSuppliers GetManufacturersAndSuppliers(string folder)
    {
        var manufacturers = new HashSet<string>();
        var suppliers = new HashSet<string>();

        foreach (var asset in Db.GetAll())
        {
            if (!string.IsNullOrEmpty(asset.Manufacturer))
            {
                manufacturers.Add(asset.Manufacturer);
            }

            if (!string.IsNullOrEmpty(asset.Supplier))
            {
                suppliers.Add(asset.Supplier);
            }
        }

        return new ManufacturersAndSuppliers
        {
            Manufacturers = manufacturers.ToList(),
            Suppliers = suppliers.ToList()
        };
    }

    // Struktura pro vracení výrobců a dodavatelů
    public class ManufacturersAndSuppliers
    {
        public List<string> Manufacturers { get; set; }
        public List<string> Suppliers { get; set; }
    }

    // Funkce pro načtení konkrétního assetu
    public static void HandleGetAsset(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        if (!int.TryParse(request.QueryString["assetNumber"], out int assetNumberId))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.OutputStream.Close();
            return;
        }

        // Načíst majetek z databáze
        Asset asset = Db.Get(assetNumberId);
        if (asset == null)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.OutputStream.Close();
            return;
        }

        string json = JsonSerializer.Serialize(asset, new JsonSerializerOptions { WriteIndented = true });
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    // Funkce pro aktualizaci assetu
    public static void HandleUpdateAsset(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        int.TryParse(request.QueryString["assetNumber"], out int assetNumberId);
        Asset existingAsset = Db.Get(assetNumberId);

        if (existingAsset != null)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string json = reader.ReadToEnd();
                    Asset updatedData = JsonSerializer.Deserialize<Asset>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // Aktualizace polí vyřazení
                    existingAsset.DisposalMethod = updatedData.DisposalMethod;
                    existingAsset.DisposalDate = string.IsNullOrWhiteSpace(updatedData.DisposalDate) ? null : updatedData.DisposalDate;
                    existingAsset.DisposalPrice = updatedData.DisposalPrice.HasValue ? updatedData.DisposalPrice : null;
                    existingAsset.DocumentNumber = updatedData.DocumentNumber;

                    Db.Update(existingAsset);

                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.OutputStream.Close();
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine("Chyba při deserializaci JSON: " + ex.Message);
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                byte[] buffer = Encoding.UTF8.GetBytes("<html><body><h1>Chyba při zpracování JSON!</h1></body></html>");
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.OutputStream.Close();
        }
    }

    // Funkce pro generování odpisů
    public static void HandleGenerateDepreciations(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        if (!int.TryParse(request.QueryString["assetNumber"], out int assetNumberId))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.OutputStream.Close();
            return;
        }

        // Načíst majetek z databáze
        Asset asset = Db.Get(assetNumberId);
        if (asset == null)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.OutputStream.Close();
            return;
        }

        // Kontrola, zda lze přegenerovat odpisy
        bool forceRegenerate = request.QueryString["force"] == "true";

        if (asset.Depreciations != null && asset.Depreciations.Count > 0)
        {
            if (forceRegenerate)
            {
                // Zkontrolujeme, zda je nějaký odpis již "aplikován" (uzavřené daňové období)
                // Odpis za rok Y se aplikuje do 31.3. Y+1
                bool anyApplied = false;
                DateTime now = DateTime.Now;
                foreach (var dep in asset.Depreciations)
                {
                    DateTime applicationDeadline = new DateTime(dep.Year + 1, 3, 31);
                    if (now > applicationDeadline)
                    {
                        anyApplied = true;
                        break;
                    }
                }

                if (anyApplied)
                {
                    response.StatusCode = (int)HttpStatusCode.Conflict; // 409 Conflict
                    byte[] errorBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = false, message = "Nelze přegenerovat odpisy, některá období jsou již uzavřena." }));
                    response.ContentLength64 = errorBuffer.Length;
                    response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                    response.OutputStream.Close();
                    return;
                }

                // Pokud nejsou žádné aplikované, můžeme je smazat a přegenerovat
                asset.Depreciations.Clear();
            }
            else
            {
                // Pokud není force=true, vrátíme existující (původní chování)
                response.StatusCode = (int)HttpStatusCode.OK;
                byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = true, depreciations = asset.Depreciations }));
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                return;
            }
        }

        // Legislativní kontrola způsobilosti k daňovému odpisování.
        if (!ValidateDepreciationEligibility(asset, out string validationMessage))
        {
            response.StatusCode = (int)HttpStatusCode.Conflict; // 409 Conflict
            byte[] errBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = false, message = validationMessage }));
            response.ContentLength64 = errBuffer.Length;
            response.OutputStream.Write(errBuffer, 0, errBuffer.Length);
            response.OutputStream.Close();
            return;
        }

        // Generování odpisů podle metody odpisování
        if (asset.DepreciationMethod == "rovnoměrný")
        {
            GenerateDepreciations(asset);
        }
        else if (asset.DepreciationMethod == "zrychlený")
        {
            GenerateAcceleratedDepreciations(asset);
        }
        else if (asset.DepreciationMethod == "mimořádný")
        {
            GenerateExtraordinaryDepreciations(asset);
        }
        else if (asset.DepreciationMethod == "bez_odpisů")
        {
            GenerateNoDepraciations(asset);
        }

        // Uložit aktualizovaný asset s odpisy do databáze
        Db.Update(asset);

        // Vrátit úspěch a nové odpisy
        response.StatusCode = (int)HttpStatusCode.OK;
        byte[] responseBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = true, depreciations = asset.Depreciations }));
        response.ContentLength64 = responseBuffer.Length;
        response.OutputStream.Write(responseBuffer, 0, responseBuffer.Length);
        response.OutputStream.Close();
    }


    // Ověření, zda lze majetek daňově odpisovat zvolenou metodou podle ZDP.
    // Vrací false a vyplní 'message', pokud generování není v souladu se zákonem.
    public static bool ValidateDepreciationEligibility(Asset asset, out string message)
    {
        message = string.Empty;

        if (asset == null || asset.DepreciationGroup == null)
        {
            message = "Chybí odpisová skupina majetku.";
            return false;
        }

        string method = asset.DepreciationMethod ?? string.Empty;
        string type = asset.AssetType ?? string.Empty;
        int acquisitionYear = GetYearOrCurrent(asset.AcquisitionDate);
        decimal baseValue = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost;
        int group = asset.DepreciationGroup.GroupNumber;

        bool isRealDepreciation = method == "rovnoměrný" || method == "zrychlený" || method == "mimořádný";

        // Leasing: daňovým nákladem je nájemné/splátky, majetek odpisuje pronajímatel.
        if (method == "leasing")
        {
            message = "U leasingu se daňové odpisy negenerují – daňovým nákladem je nájemné dle § 24 ZDP. Pro evidenci použijte způsob „Bez odpisů“.";
            return false;
        }

        // Nehmotný majetek – § 32a ZDP zrušen od 1. 1. 2021.
        if (type == "nehmotný" && isRealDepreciation && acquisitionYear >= Legislation.NehmotnyMajetekDanoveOdpisyZrusenyOdRoku)
        {
            message = $"Daňové odpisy nehmotného majetku byly zrušeny od roku {Legislation.NehmotnyMajetekDanoveOdpisyZrusenyOdRoku} (§ 32a ZDP). U majetku pořízeného od tohoto roku se uplatní účetní odpis – zvolte způsob „Bez odpisů“.";
            return false;
        }

        // Hranice vstupní ceny hmotného movitého majetku (skupiny 1–3) dle § 26 ZDP.
        // Nemovitý majetek (skupiny 4–6) se odpisuje bez ohledu na cenu.
        if (type == "hmotný" && isRealDepreciation && group >= 1 && group <= 3 && baseValue < Legislation.HmotnyMajetekHranice)
        {
            message = $"Vstupní cena {baseValue:N0} Kč nedosahuje hranice {Legislation.HmotnyMajetekHranice:N0} Kč pro hmotný majetek (§ 26 odst. 2 ZDP). Jde o drobný majetek – daňově se neodpisuje a uplatní se jako jednorázový náklad. Zvolte způsob „Bez odpisů“.";
            return false;
        }

        // Mimořádné odpisy – § 30a ZDP.
        if (method == "mimořádný")
        {
            if (group != 1 && group != 2)
            {
                message = "Mimořádné odpisy lze dle § 30a ZDP uplatnit pouze u majetku v odpisové skupině 1 a 2.";
                return false;
            }

            bool obecneObdobi = acquisitionYear >= Legislation.MimoradneOdpisyObecneOd && acquisitionYear <= Legislation.MimoradneOdpisyObecneDo;
            bool bezemisniObdobi = acquisitionYear >= Legislation.MimoradneOdpisyBezemisniVozidloOd && acquisitionYear <= Legislation.MimoradneOdpisyBezemisniVozidloDo;

            if (bezemisniObdobi && !asset.IsZeroEmissionVehicle)
            {
                message = $"Od roku {Legislation.MimoradneOdpisyBezemisniVozidloOd} lze mimořádné odpisy (§ 30a ZDP) uplatnit pouze u bezemisních vozidel. Označte majetek jako bezemisní vozidlo, nebo zvolte jiný způsob odpisování.";
                return false;
            }

            if (!obecneObdobi && !bezemisniObdobi)
            {
                message = $"Mimořádné odpisy (§ 30a ZDP) lze uplatnit jen u majetku pořízeného v letech {Legislation.MimoradneOdpisyObecneOd}–{Legislation.MimoradneOdpisyObecneDo}, resp. u bezemisních vozidel pořízených {Legislation.MimoradneOdpisyBezemisniVozidloOd}–{Legislation.MimoradneOdpisyBezemisniVozidloDo}. Pro rok {acquisitionYear} zvolte rovnoměrný nebo zrychlený odpis.";
                return false;
            }
        }

        return true;
    }

    public static void GenerateExtraordinaryDepreciations(Asset asset)
    {
        // Mimořádné odpisy dle § 30a ZDP
        // Pouze pro 1. a 2. odpisovou skupinu
        if (asset.DepreciationGroup.GroupNumber != 1 && asset.DepreciationGroup.GroupNumber != 2)
        {
            // Fallback na rovnoměrné, pokud není skupina 1 nebo 2 (nebo vyhodit chybu/log)
            GenerateDepreciations(asset);
            return;
        }

        DateTime startDate;
        if (!TryParseDate(asset.CommissioningDate, out startDate))
        {
             if (!TryParseDate(asset.AcquisitionDate, out startDate))
             {
                 return; // Nelze určit datum
             }
        }

        decimal baseValue = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost;
        int startYear = startDate.Year;
        int startMonth = startDate.Month;

        // Mimořádné odpisy se počítají po měsících
        // Skupina 1: 12 měsíců
        // Skupina 2: 24 měsíců (12 měsíců 60%, 12 měsíců 40%)

        if (asset.DepreciationGroup.GroupNumber == 1)
        {
            decimal monthlyDepreciation = baseValue / 12m;
            
            // Rok 1 (zbytek měsíců od pořízení)
            int monthsInFirstYear = 12 - startMonth + 1; // Např. pořízení v prosinci (12) -> 12-12+1 = 1 měsíc
            decimal firstYearAmount = Math.Ceiling(monthlyDepreciation * monthsInFirstYear);
            
            asset.Depreciations.Add(new Depreciation { Year = startYear, Amount = firstYearAmount });

            // Rok 2 (zbytek do 12 měsíců)
            int monthsInSecondYear = 12 - monthsInFirstYear;
            if (monthsInSecondYear > 0)
            {
                // Zbytek hodnoty, aby to sedělo přesně (kvůli zaokrouhlování)
                decimal secondYearAmount = baseValue - firstYearAmount;
                asset.Depreciations.Add(new Depreciation { Year = startYear + 1, Amount = secondYearAmount });
            }
        }
        else if (asset.DepreciationGroup.GroupNumber == 2)
        {
            decimal firstPhaseAmount = baseValue * 0.60m;
            decimal secondPhaseAmount = baseValue * 0.40m;
            
            decimal monthlyRate1 = firstPhaseAmount / 12m;
            decimal monthlyRate2 = secondPhaseAmount / 12m;

            // Slovník pro sčítání odpisů v jednotlivých letech
            Dictionary<int, decimal> yearlyDepreciations = new Dictionary<int, decimal>();

            for (int i = 0; i < 24; i++)
            {
                int currentMonthIndex = startMonth + i; // 1-based index měsíce od začátku roku pořízení (může přesáhnout 12)
                int currentYearOffset = (currentMonthIndex - 1) / 12;
                int currentYear = startYear + currentYearOffset;
                
                decimal currentMonthlyAmount;
                if (i < 12)
                {
                    currentMonthlyAmount = monthlyRate1;
                }
                else
                {
                    currentMonthlyAmount = monthlyRate2;
                }

                if (!yearlyDepreciations.ContainsKey(currentYear))
                {
                    yearlyDepreciations[currentYear] = 0;
                }
                yearlyDepreciations[currentYear] += currentMonthlyAmount;
            }

            // Uložení a zaokrouhlení
            decimal totalDepreciated = 0;
            foreach (var kvp in yearlyDepreciations.OrderBy(x => x.Key))
            {
                decimal amount = Math.Ceiling(kvp.Value);
                
                // Korekce posledního roku, aby součet seděl přesně na vstupní cenu (pokud by zaokrouhlení způsobilo přešvihnutí nebo nedošvihnutí)
                // U mimořádných odpisů se zaokrouhluje na celé Kč nahoru, takže součet může být vyšší než vstupní cena?
                // Ne, odpisy nesmí překročit vstupní cenu.
                // Ale zákon říká "odpisy se stanoví s přesností na celé Kč nahoru".
                // Obvykle se poslední splátka upraví.
                
                if (totalDepreciated + amount > baseValue)
                {
                    amount = baseValue - totalDepreciated;
                }
                
                // Pokud je to poslední rok a chybí pár korun, dopodepíšeme zbytek?
                // U mimořádných odpisů je to specifické. Ale pro jednoduchost a bezpečnost:
                if (kvp.Key == yearlyDepreciations.Keys.Max() && totalDepreciated + amount < baseValue)
                {
                     amount = baseValue - totalDepreciated;
                }

                asset.Depreciations.Add(new Depreciation { Year = kvp.Key, Amount = amount });
                totalDepreciated += amount;
            }
        }
    }

    public static void GenerateNoDepraciations(Asset asset)
    {
        if (asset.DepreciationMethod == "bez_odpisů")
        {
            // Vygenerujeme jeden záznam odpisu s celou daňovou hodnotou
            asset.Depreciations.Add(new Depreciation
            {
                Year = GetYearOrCurrent(asset.AcquisitionDate),
                Amount = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost
            });
            return; // Ukončíme funkci, protože žádné další odpisy se negenerují
        }
    }

    // Funkce pro rovnoměrné odpisování
    public static void GenerateDepreciations(Asset asset)
    {
        // Zkontrolujeme, zda je druh odpisů "Bez odpisů"
        if (asset.DepreciationMethod == "bez_odpisů")
        {
            // Vygenerujeme jeden záznam odpisu s celou daňovou hodnotou
            asset.Depreciations.Add(new Depreciation
            {
                Year = GetYearOrCurrent(asset.AcquisitionDate),
                Amount = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost
            });
            return; // Ukončíme funkci, protože žádné další odpisy se negenerují
        }
        // Najdi odpovídající odpisové sazby pro zvolenou skupinu
        var depreciationRate = DepreciationRates.FirstOrDefault(r => r.GroupNumber == asset.DepreciationGroup.GroupNumber);
        if (depreciationRate == null) return;

        int yearsOfDepreciation = asset.DepreciationGroup.DepreciationLength;
        int startingYear = GetYearOrCurrent(asset.AcquisitionDate);
        decimal baseValue = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost;
        decimal totalDepreciated = 0;

        // Výpočet odpisů pro první rok
        decimal firstYearDepreciation = Math.Ceiling(baseValue * depreciationRate.FirstYearRate / 100);
        if (firstYearDepreciation > baseValue) firstYearDepreciation = baseValue;
        
        asset.Depreciations.Add(new Depreciation
        {
            Year = startingYear,
            Amount = firstYearDepreciation
        });
        totalDepreciated += firstYearDepreciation;

        // Výpočet odpisů pro následující roky
        for (int i = 1; i < yearsOfDepreciation; i++)
        {
            decimal yearlyDepreciation = Math.Ceiling(baseValue * depreciationRate.FollowingYearsRate / 100);
            
            // Kontrola, abychom nepřešvihli celkovou hodnotu
            if (totalDepreciated + yearlyDepreciation > baseValue)
            {
                yearlyDepreciation = baseValue - totalDepreciated;
            }
            
            // V posledním roce dorovnáme zbytek (pokud by vznikl rozdíl zaokrouhlením dolů v sazbách, ale tady zaokrouhlujeme nahoru, takže spíš přešvihneme)
            // Ale pro jistotu, pokud je to poslední rok a zbývá něco málo (což by nemělo při zaokrouhlování nahoru), tak to tam dáme.
            // Spíše jde o to, že při zaokrouhlování nahoru se to odepíše dříve nebo poslední splátka bude menší.
            
            if (yearlyDepreciation > 0)
            {
                asset.Depreciations.Add(new Depreciation
                {
                    Year = startingYear + i,
                    Amount = yearlyDepreciation
                });
                totalDepreciated += yearlyDepreciation;
            }
        }
    }

    // Funkce pro zrychlené odpisování
    public static void GenerateAcceleratedDepreciations(Asset asset)
    {
        // Zkontrolujeme, zda je druh odpisů "Bez odpisů"
        if (asset.DepreciationMethod == "bez_odpisů")
        {
            // Vygenerujeme jeden záznam odpisu s celou daňovou hodnotou
            asset.Depreciations.Add(new Depreciation
            {
                Year = GetYearOrCurrent(asset.AcquisitionDate),
                Amount = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost
            });
            return; // Ukončíme funkci, protože žádné další odpisy se negenerují
        }

        var depreciationRate = AcceleratedDepreciationRates.FirstOrDefault(r => r.GroupNumber == asset.DepreciationGroup.GroupNumber);
        if (depreciationRate == null) return;

        int yearsOfDepreciation = asset.DepreciationGroup.DepreciationLength;
        int startingYear = GetYearOrCurrent(asset.AcquisitionDate);
        decimal baseValue = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost;
        decimal remainingValue = baseValue;
        decimal totalDepreciated = 0;

        decimal firstYearDepreciation = Math.Ceiling(baseValue / depreciationRate.FirstYearCoefficient);
        if (firstYearDepreciation > baseValue) firstYearDepreciation = baseValue;

        asset.Depreciations.Add(new Depreciation { Year = startingYear, Amount = firstYearDepreciation });
        remainingValue -= firstYearDepreciation;
        totalDepreciated += firstYearDepreciation;

        for (int i = 1; i < yearsOfDepreciation; i++)
        {
            // Zrychlené odpisy: (2 * zůstatková cena) / (koeficient - počet let odpisování)
            // Pozor: vzorec je (2 * ZC) / (k - n), kde n je počet již uplatněných let (tedy i).
            decimal yearlyDepreciation = Math.Ceiling((2 * remainingValue) / (depreciationRate.FollowingYearsCoefficient - i));
            
            if (totalDepreciated + yearlyDepreciation > baseValue)
            {
                yearlyDepreciation = baseValue - totalDepreciated;
            }

            // Pokud je to poslední rok, zkontrolujeme, zda je vše odepsáno.
            // U zrychlených odpisů by to mělo vyjít, ale pro jistotu.
            if (i == yearsOfDepreciation - 1 && totalDepreciated + yearlyDepreciation < baseValue)
            {
                 yearlyDepreciation = baseValue - totalDepreciated;
            }

            if (yearlyDepreciation > 0)
            {
                asset.Depreciations.Add(new Depreciation { Year = startingYear + i, Amount = yearlyDepreciation });
                remainingValue -= yearlyDepreciation;
                totalDepreciated += yearlyDepreciation;
            }
        }
    }

    // Statické tabulky pro rovnoměrné a zrychlené odpisy.
    // Jediným zdrojem pravdy je třída Legislation (viz níže) – zde
    // jsou pouze odkazy kvůli zpětné kompatibilitě stávajícího kódu.
    public static List<DepreciationRate> DepreciationRates = Legislation.RovnomerneOdpisy;

    public static List<AcceleratedDepreciationRate> AcceleratedDepreciationRates = Legislation.ZrychleneOdpisy;

    // =====================================================================
    //  LEGISLATIVA – jednotné místo pro daňové parametry odpisů majetku.
    //
    //  Vychází ze zákona č. 586/1992 Sb., o daních z příjmů (dále „ZDP"),
    //  ve znění účinném pro rok 2026. Při změně zákona stačí upravit hodnoty
    //  na jednom místě.
    // =====================================================================
    public static class Legislation
    {
        // Rok, pro který jsou parametry platné (informativní).
        public const int PlatnostRok = 2026;

        // § 26 odst. 2 ZDP – od 1. 1. 2021 je hranice vstupní ceny pro
        // hmotný majetek 80 000 Kč (do 31. 12. 2020 činila 40 000 Kč).
        // Majetek pod tuto hranici není hmotným majetkem a daňově se
        // neodpisuje (uplatní se jako jednorázový náklad, příp. účetně).
        public const decimal HmotnyMajetekHranice = 80000m;

        // § 33 odst. 1 ZDP – od 1. 1. 2021 je hranice pro technické
        // zhodnocení rovněž 80 000 Kč (souhrnně za zdaňovací období).
        public const decimal TechnickeZhodnoceniHranice = 80000m;

        // § 32a ZDP byl s účinností od 1. 1. 2021 zrušen. U nehmotného
        // majetku pořízeného od tohoto roku se daňové odpisy neuplatňují
        // a uznává se účetní odpis (§ 24 odst. 2 písm. v) ZDP).
        public const int NehmotnyMajetekDanoveOdpisyZrusenyOdRoku = 2021;

        // § 30a ZDP – mimořádné odpisy:
        //   * obecně: hmotný majetek zařazený v odpisové skupině 1 a 2
        //     pořízený v období 1. 1. 2020 – 31. 12. 2023,
        //   * od 1. 1. 2024 pouze bezemisní (zero-emission) vozidla.
        public const int MimoradneOdpisyObecneOd = 2020;
        public const int MimoradneOdpisyObecneDo = 2023;
        public const int MimoradneOdpisyBezemisniVozidloOd = 2024;
        public const int MimoradneOdpisyBezemisniVozidloDo = 2028;

        // Rovnoměrné odpisy – roční odpisové sazby dle § 31 odst. 1 písm. a) ZDP.
        public static readonly List<DepreciationRate> RovnomerneOdpisy = new List<DepreciationRate>
        {
            new DepreciationRate { GroupNumber = 1, FirstYearRate = 20,     FollowingYearsRate = 40,     IncreasedRate = 33.3M },
            new DepreciationRate { GroupNumber = 2, FirstYearRate = 11,     FollowingYearsRate = 22.25M, IncreasedRate = 20 },
            new DepreciationRate { GroupNumber = 3, FirstYearRate = 5.5M,   FollowingYearsRate = 10.5M,  IncreasedRate = 10 },
            new DepreciationRate { GroupNumber = 4, FirstYearRate = 2.15M,  FollowingYearsRate = 5.15M,  IncreasedRate = 5 },
            new DepreciationRate { GroupNumber = 5, FirstYearRate = 1.4M,   FollowingYearsRate = 3.4M,   IncreasedRate = 3.4M },
            new DepreciationRate { GroupNumber = 6, FirstYearRate = 1.02M,  FollowingYearsRate = 2.02M,  IncreasedRate = 2 }
        };

        // Zrychlené odpisy – koeficienty dle § 32 odst. 1 ZDP.
        public static readonly List<AcceleratedDepreciationRate> ZrychleneOdpisy = new List<AcceleratedDepreciationRate>
        {
            new AcceleratedDepreciationRate { GroupNumber = 1, FirstYearCoefficient = 3,  FollowingYearsCoefficient = 4,  IncreasedRate = 3 },
            new AcceleratedDepreciationRate { GroupNumber = 2, FirstYearCoefficient = 5,  FollowingYearsCoefficient = 6,  IncreasedRate = 5 },
            new AcceleratedDepreciationRate { GroupNumber = 3, FirstYearCoefficient = 10, FollowingYearsCoefficient = 11, IncreasedRate = 10 },
            new AcceleratedDepreciationRate { GroupNumber = 4, FirstYearCoefficient = 20, FollowingYearsCoefficient = 21, IncreasedRate = 20 },
            new AcceleratedDepreciationRate { GroupNumber = 5, FirstYearCoefficient = 30, FollowingYearsCoefficient = 31, IncreasedRate = 30 },
            new AcceleratedDepreciationRate { GroupNumber = 6, FirstYearCoefficient = 50, FollowingYearsCoefficient = 51, IncreasedRate = 50 }
        };

        // Minimální doba odpisování dle § 30 odst. 1 ZDP (v letech).
        public static readonly Dictionary<int, int> DobaOdpisovani = new Dictionary<int, int>
        {
            { 1, 3 }, { 2, 5 }, { 3, 10 }, { 4, 20 }, { 5, 30 }, { 6, 50 }
        };
    }

    // Pomocné třídy a další obslužné funkce, které již byly součástí původního kódu...

    public class DepreciationRate
    {
        public int GroupNumber { get; set; }
        public decimal FirstYearRate { get; set; }
        public decimal FollowingYearsRate { get; set; }
        public decimal IncreasedRate { get; set; }
    }

    public class AcceleratedDepreciationRate
    {
        public int GroupNumber { get; set; }
        public decimal FirstYearCoefficient { get; set; }
        public decimal FollowingYearsCoefficient { get; set; }
        public decimal IncreasedRate { get; set; }
    }

    public class Asset
    {
        [JsonPropertyName("assetNumber")]
        public int AssetNumber { get; set; } // Unikátní číslo majetku

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("assetType")]
        public string AssetType { get; set; }

        [JsonPropertyName("depreciationGroup")]
        public DepreciationGroup DepreciationGroup { get; set; }

        [JsonPropertyName("depreciationMethod")]
        public string DepreciationMethod { get; set; }

        // Bezemisní (zero-emission) vozidlo – rozhoduje o nároku na mimořádné
        // odpisy dle § 30a ZDP u majetku pořízeného od 1. 1. 2024.
        [JsonPropertyName("isZeroEmissionVehicle")]
        public bool IsZeroEmissionVehicle { get; set; }

        [JsonPropertyName("acquisitionCost")]
        public decimal AcquisitionCost { get; set; }

        [JsonPropertyName("taxValue")]
        public decimal? TaxValue { get; set; }

        [JsonPropertyName("accountingValue")]
        public decimal? AccountingValue { get; set; }

        [JsonPropertyName("acquisitionDate")]
        public string AcquisitionDate { get; set; }

        [JsonPropertyName("commissioningDate")]
        public string CommissioningDate { get; set; }

        [JsonPropertyName("warrantyPeriod")]
        public int? WarrantyPeriod { get; set; }

        [JsonPropertyName("serialNumber")]
        public string SerialNumber { get; set; }

        [JsonPropertyName("partNumber")]
        public string PartNumber { get; set; }

        [JsonPropertyName("manufacturer")]
        public string Manufacturer { get; set; }

        [JsonPropertyName("supplier")]
        public string Supplier { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        // Nové vlastnosti pro vyřazení majetku
        [JsonPropertyName("disposalMethod")]
        public string DisposalMethod { get; set; } // Způsob vyřazení (prodej, likvidace, převod)

        [JsonPropertyName("disposalDate")]
        public string DisposalDate { get; set; } // Datum vyřazení

        [JsonPropertyName("disposalPrice")]
        public decimal? DisposalPrice { get; set; } // Cena vyřazení

        [JsonPropertyName("documentNumber")]
        public string DocumentNumber { get; set; } // Číslo dokladu

        [JsonPropertyName("technicalAppreciations")]
        public List<TechnicalAppreciation> TechnicalAppreciations { get; set; } = new List<TechnicalAppreciation>();

        [JsonPropertyName("depreciations")]
        public List<Depreciation> Depreciations { get; set; } = new List<Depreciation>();  // Seznam pro více řádků odpisů
    }

    public class TechnicalAppreciation
    {
        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    public class Depreciation
    {
        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }
    }

    public class DepreciationGroup
    {
        [JsonPropertyName("groupNumber")]
        public int GroupNumber { get; set; }

        [JsonPropertyName("groupName")]
        public string GroupName { get; set; }

        [JsonPropertyName("depreciationLength")]
        public int DepreciationLength { get; set; }
    }
}