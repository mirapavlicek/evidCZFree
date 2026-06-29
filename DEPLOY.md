# Nasazení na testovací notebook přes Tailscale

Tento návod popisuje, jak rozběhnout **Evidence Majetku** na testovacím notebooku (NTB)
a zpřístupnit ho ostatním zařízením v rámci [Tailscale](https://tailscale.com/) tailnetu.

> Aplikace běží jako jednoduchý konzolový server (HttpListener) na portu **8080** a data
> ukládá do `data/evidence.db` (LiteDB). Frontend volá API **relativně** (same‑origin),
> takže funguje na jakékoli adrese/hostname, pod kterou je server dostupný.

---

## 1. Příprava buildu

### Varianta A – cílový NTB má nainstalovaný .NET 10
```bash
git clone https://github.com/mirapavlicek/evidCZFree.git
cd evidCZFree/evidCZFree
dotnet run
```

### Varianta B – samostatný (self‑contained) balíček bez nutnosti instalovat .NET
Sestav na svém stroji a zkopíruj výslednou složku na NTB. Zvol RID podle systému NTB:

```bash
# Windows
dotnet publish -c Release -r win-x64   --self-contained true -o publish-win

# Linux
dotnet publish -c Release -r linux-x64 --self-contained true -o publish-linux

# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained true -o publish-mac
```
Výsledná složka obsahuje spustitelný soubor `evidCZFree` (resp. `evidCZFree.exe`)
spolu s adresáři `html/` a `assets/`. Spouštět je nutné **z této složky** (server
hledá `html/` a `assets/` v aktuálním adresáři).

---

## 2. Tailscale na notebooku

1. Nainstaluj Tailscale: <https://tailscale.com/download>
2. Přihlas zařízení: `tailscale up`
3. Zjisti jeho adresu v tailnetu:
   ```bash
   tailscale ip -4          # např. 100.x.y.z
   tailscale status         # MagicDNS jméno, např. testovaci-ntb
   ```

---

## 3. Zpřístupnění aplikace přes Tailscale

Vyber jednu ze dvou variant.

### Varianta 1 (doporučená): `tailscale serve` + HTTPS

Aplikace zůstane na `localhost:8080` a Tailscale ji bezpečně vystaví do tailnetu
včetně automatického HTTPS certifikátu.

```bash
# v jednom terminálu spusť aplikaci (výchozí bind na localhost)
dotnet run                         # nebo ./evidCZFree z self-contained balíčku

# ve druhém terminálu vystav port 8080 do tailnetu
tailscale serve --bg 8080
tailscale serve status
```
Aplikace pak bude dostupná z libovolného zařízení v tailnetu na adrese:
```
https://<jmeno-ntb>.<tvuj-tailnet>.ts.net/
```

Vypnutí: `tailscale serve --https=443 off`

### Varianta 2: přímý bind na všechna rozhraní

Server se naváže na všechna síťová rozhraní a přistupuje se přímo přes Tailscale IP.
Adresu lze nastavit **argumentem** nebo **proměnnou prostředí** `EVIDENCE_URL_PREFIX`:

```bash
# argumentem
dotnet run -- "http://+:8080/"

# nebo proměnnou prostředí
EVIDENCE_URL_PREFIX="http://+:8080/" dotnet run
```
Přístup z jiného zařízení v tailnetu:
```
http://100.x.y.z:8080/        # 100.x.y.z = Tailscale IP notebooku
```

Poznámky k variantě 2:
- **Linux/macOS:** prefix `http://+:8080/` funguje bez zvláštních práv.
- **Windows:** prefix `+`/`*` vyžaduje buď spuštění jako správce, nebo jednorázové
  povolení URL ACL:
  ```powershell
  netsh http add urlacl url=http://+:8080/ user=%USERNAME%
  ```
  a povolení portu ve firewallu:
  ```powershell
  New-NetFirewallRule -DisplayName "Evidence 8080" -Direction Inbound -LocalPort 8080 -Protocol TCP -Action Allow
  ```
  Případně se lze vyhnout ACL navázáním přímo na Tailscale IP:
  `EVIDENCE_URL_PREFIX="http://100.x.y.z:8080/"`.

---

## 4. (Volitelné) Běh jako služba

### Linux – systemd
`/etc/systemd/system/evidence.service`:
```ini
[Unit]
Description=Evidence Majetku
After=network-online.target tailscaled.service

[Service]
WorkingDirectory=/opt/evidence
ExecStart=/opt/evidence/evidCZFree
Environment=EVIDENCE_URL_PREFIX=http://+:8080/
Restart=on-failure

[Install]
WantedBy=multi-user.target
```
```bash
sudo systemctl daemon-reload && sudo systemctl enable --now evidence
```

### Windows
Spouštění přes Plánovač úloh (při přihlášení / startu), nebo nástrojem
[NSSM](https://nssm.cc/) jako systémovou službu.

---

## 5. Poznámky a bezpečnost

- **Data:** ukládají se do `data/evidence.db`. Záloha = zkopírovat tento soubor.
  Existující `data/*.json` se při startu automaticky naimportují (viz README).
- **Přihlášení** v aplikaci je jen demonstrační a ve výchozím stavu se nevynucuje.
  Tailnet sám funguje jako přístupová vrstva – aplikaci uvnitř tailnetu vidí jen
  tvá zařízení. Pro veřejné vystavení (`tailscale funnel`) doporučuji doplnit
  skutečnou autentizaci.
- **HTTPS:** poskytuje pouze varianta 1 (`tailscale serve`). Přímý bind (varianta 2)
  jede po HTTP uvnitř šifrovaného tailnetu.
