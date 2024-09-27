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

        // Zajištění existence složky s JSON soubory
        if (!Directory.Exists(jsonFolder))
        {
            Directory.CreateDirectory(jsonFolder);
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
    }

    public static decimal CalculateResidualValue(Asset asset)
    {
        // Počáteční zůstatková hodnota je pořizovací cena
        decimal residualValue = asset.AcquisitionCost;

        // Získáme aktuální rok
        int currentYear = DateTime.Now.Year;

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
        string assetNumber = request.QueryString["assetNumber"];
        string filePath = Path.Combine(jsonFolder, $"{assetNumber}.json");

        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath);
            Asset asset = JsonSerializer.Deserialize<Asset>(json);

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

            response.OutputStream.Close();
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
            List<Asset> assets = LoadAllAssets(jsonFolder);

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
        var assets = GetAllAssets(jsonFolder);
        var json = JsonSerializer.Serialize(assets);

        // Nastavení odpovědi
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        using (var writer = new StreamWriter(response.OutputStream))
        {
            writer.Write(json);
        }
        response.OutputStream.Close();
    }

    // Pomocná funkce pro načtení všech majetků
    public static List<Asset> GetAllAssets(string folder)
    {
        var assets = new List<Asset>();

        string[] files = Directory.GetFiles(folder, "*.json");

        foreach (var file in files)
        {
            string json = File.ReadAllText(file);
            Asset asset = JsonSerializer.Deserialize<Asset>(json);
            assets.Add(asset);
        }

        return assets;
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
        // Načtení všech stávajících majetků
        List<Asset> assets = LoadAllAssets(jsonFolder);

        // Získání dalšího čísla majetku
        int nextAssetNumber = GetNextAssetNumber(assets);

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
                    // Přiřazení nového čísla majetku
                    newAsset.AssetNumber = nextAssetNumber;

                    // Uložení nového majetku jako JSON soubor
                    string newAssetFilePath = Path.Combine(jsonFolder, $"{newAsset.AssetNumber}.json");
                    File.WriteAllText(newAssetFilePath, JsonSerializer.Serialize(newAsset));

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

    // Pomocná funkce pro načtení všech majetků z JSON souborů
    public static List<Asset> LoadAllAssets(string folder)
    {
        List<Asset> assets = new List<Asset>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            string json = File.ReadAllText(file);
            Asset asset = JsonSerializer.Deserialize<Asset>(json);
            assets.Add(asset);
        }
        return assets;
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
        response.OutputStream.Close();
    }

    // Pomocná funkce pro načtení všech výrobců a dodavatelů z JSON souborů
    public static ManufacturersAndSuppliers GetManufacturersAndSuppliers(string folder)
    {
        var manufacturers = new HashSet<string>();
        var suppliers = new HashSet<string>();

        string[] files = Directory.GetFiles(folder, "*.json");

        foreach (var file in files)
        {
            string json = File.ReadAllText(file);
            Asset asset = JsonSerializer.Deserialize<Asset>(json);

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
        string assetNumber = request.QueryString["assetNumber"];
        if (string.IsNullOrEmpty(assetNumber))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.OutputStream.Close();
            return;
        }

        // Cesta k JSON souboru majetku
        string filePath = Path.Combine(jsonFolder, $"{assetNumber}.json");
        if (!File.Exists(filePath))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.OutputStream.Close();
            return;
        }

        // Načíst obsah JSON souboru
        string json = File.ReadAllText(filePath);
        response.StatusCode = (int)HttpStatusCode.OK;
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    // Funkce pro aktualizaci assetu
    public static void HandleUpdateAsset(HttpListenerRequest request, HttpListenerResponse response, string jsonFolder)
    {
        string assetNumber = request.QueryString["assetNumber"];
        string filePath = Path.Combine(jsonFolder, $"{assetNumber}.json");

        if (File.Exists(filePath))
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string json = reader.ReadToEnd();
                    Asset updatedData = JsonSerializer.Deserialize<Asset>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    Asset existingAsset = JsonSerializer.Deserialize<Asset>(File.ReadAllText(filePath));

                    // Aktualizace polí vyřazení
                    existingAsset.DisposalMethod = updatedData.DisposalMethod;
                    existingAsset.DisposalDate = string.IsNullOrWhiteSpace(updatedData.DisposalDate) ? null : updatedData.DisposalDate;
                    existingAsset.DisposalPrice = updatedData.DisposalPrice.HasValue ? updatedData.DisposalPrice : null;
                    existingAsset.DocumentNumber = updatedData.DocumentNumber;

                    File.WriteAllText(filePath, JsonSerializer.Serialize(existingAsset, new JsonSerializerOptions { WriteIndented = true }));

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
        string assetNumber = request.QueryString["assetNumber"];
        if (string.IsNullOrEmpty(assetNumber))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.OutputStream.Close();
            return;
        }

        // Cesta k JSON souboru majetku
        string filePath = Path.Combine(jsonFolder, $"{assetNumber}.json");
        if (!File.Exists(filePath))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.OutputStream.Close();
            return;
        }

        // Načíst majetek
        string json = File.ReadAllText(filePath);
        Asset asset = JsonSerializer.Deserialize<Asset>(json);

        // Pokud již jsou odpisy vygenerovány, vrátit existující odpisy
        if (asset.Depreciations != null && asset.Depreciations.Count > 0)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = true, depreciations = asset.Depreciations }));
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
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
        else if (asset.DepreciationMethod == "bez_odpisů")
        {
            GenerateNoDepraciations(asset);
        }

        // Uložit aktualizovaný asset s odpisy
        File.WriteAllText(filePath, JsonSerializer.Serialize(asset, new JsonSerializerOptions { WriteIndented = true }));

        // Vrátit úspěch a nové odpisy
        response.StatusCode = (int)HttpStatusCode.OK;
        byte[] responseBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { success = true, depreciations = asset.Depreciations }));
        response.ContentLength64 = responseBuffer.Length;
        response.OutputStream.Write(responseBuffer, 0, responseBuffer.Length);
        response.OutputStream.Close();
    }


    public static void GenerateNoDepraciations(Asset asset)
    {
        if (asset.DepreciationMethod == "bez_odpisů")
        {
            // Vygenerujeme jeden záznam odpisu s celou daňovou hodnotou
            asset.Depreciations.Add(new Depreciation
            {
                Year = DateTime.Parse(asset.AcquisitionDate).Year,
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
                Year = DateTime.Parse(asset.AcquisitionDate).Year,
                Amount = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost
            });
            return; // Ukončíme funkci, protože žádné další odpisy se negenerují
        }
        // Najdi odpovídající odpisové sazby pro zvolenou skupinu
        var depreciationRate = DepreciationRates.FirstOrDefault(r => r.GroupNumber == asset.DepreciationGroup.GroupNumber);
        if (depreciationRate == null) return;

        int yearsOfDepreciation = asset.DepreciationGroup.DepreciationLength;
        int startingYear = DateTime.Parse(asset.AcquisitionDate).Year;

        // Výpočet odpisů pro první rok
        decimal firstYearDepreciation = asset.AcquisitionCost * depreciationRate.FirstYearRate / 100;
        asset.Depreciations.Add(new Depreciation
        {
            Year = startingYear,
            Amount = firstYearDepreciation
        });

        // Výpočet odpisů pro následující roky
        for (int i = 1; i < yearsOfDepreciation; i++)
        {
            decimal yearlyDepreciation = asset.AcquisitionCost * depreciationRate.FollowingYearsRate / 100;
            asset.Depreciations.Add(new Depreciation
            {
                Year = startingYear + i,
                Amount = yearlyDepreciation
            });
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
                Year = DateTime.Parse(asset.AcquisitionDate).Year,
                Amount = asset.TaxValue.HasValue ? asset.TaxValue.Value : asset.AcquisitionCost
            });
            return; // Ukončíme funkci, protože žádné další odpisy se negenerují
        }

        var depreciationRate = AcceleratedDepreciationRates.FirstOrDefault(r => r.GroupNumber == asset.DepreciationGroup.GroupNumber);
        if (depreciationRate == null) return;

        int yearsOfDepreciation = asset.DepreciationGroup.DepreciationLength;
        int startingYear = DateTime.Parse(asset.AcquisitionDate).Year;
        decimal remainingValue = asset.AcquisitionCost;

        decimal firstYearDepreciation = asset.AcquisitionCost / depreciationRate.FirstYearCoefficient;
        asset.Depreciations.Add(new Depreciation { Year = startingYear, Amount = firstYearDepreciation });
        remainingValue -= firstYearDepreciation;

        for (int i = 1; i < yearsOfDepreciation; i++)
        {
            decimal yearlyDepreciation = (2 * remainingValue) / (depreciationRate.FollowingYearsCoefficient - i);
            asset.Depreciations.Add(new Depreciation { Year = startingYear + i, Amount = yearlyDepreciation });
            remainingValue -= yearlyDepreciation;
        }
    }

    // Statické tabulky pro rovnoměrné a zrychlené odpisy
    public static List<DepreciationRate> DepreciationRates = new List<DepreciationRate>
    {
        new DepreciationRate { GroupNumber = 1, FirstYearRate = 20, FollowingYearsRate = 40, IncreasedRate = 33.3M },
        new DepreciationRate { GroupNumber = 2, FirstYearRate = 11, FollowingYearsRate = 22.25M, IncreasedRate = 20 },
        new DepreciationRate { GroupNumber = 3, FirstYearRate = 5.5M, FollowingYearsRate = 10.5M, IncreasedRate = 10 },
        new DepreciationRate { GroupNumber = 4, FirstYearRate = 2.15M, FollowingYearsRate = 5.15M, IncreasedRate = 5 },
        new DepreciationRate { GroupNumber = 5, FirstYearRate = 1.4M, FollowingYearsRate = 3.4M, IncreasedRate = 3.4M },
        new DepreciationRate { GroupNumber = 6, FirstYearRate = 1.02M, FollowingYearsRate = 2.02M, IncreasedRate = 2 }
    };

    public static List<AcceleratedDepreciationRate> AcceleratedDepreciationRates = new List<AcceleratedDepreciationRate>
    {
        new AcceleratedDepreciationRate { GroupNumber = 1, FirstYearCoefficient = 3, FollowingYearsCoefficient = 4, IncreasedRate = 3 },
        new AcceleratedDepreciationRate { GroupNumber = 2, FirstYearCoefficient = 5, FollowingYearsCoefficient = 6, IncreasedRate = 5 },
        new AcceleratedDepreciationRate { GroupNumber = 3, FirstYearCoefficient = 10, FollowingYearsCoefficient = 11, IncreasedRate = 10 },
        new AcceleratedDepreciationRate { GroupNumber = 4, FirstYearCoefficient = 20, FollowingYearsCoefficient = 21, IncreasedRate = 20 },
        new AcceleratedDepreciationRate { GroupNumber = 5, FirstYearCoefficient = 30, FollowingYearsCoefficient = 31, IncreasedRate = 30 },
        new AcceleratedDepreciationRate { GroupNumber = 6, FirstYearCoefficient = 50, FollowingYearsCoefficient = 51, IncreasedRate = 50 }
    };

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

        [JsonPropertyName("depreciations")]
        public List<Depreciation> Depreciations { get; set; } = new List<Depreciation>();  // Seznam pro více řádků odpisů
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