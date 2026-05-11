# BotManager – TODO list

## 1. Automatická aktualizace board zprávy po změně týmu
- [x] Po úspěšném `/pridat-tym`, `/odebrat-tym`, `/upravit-tym` v pluginu zavolat aktualizaci board zprávy (seznam týmů)
- [x] Stejná aktualizace při změně přes web UI (admin editace týmů přes BotTeamsDto endpoint)
- [x] Zajistit, aby update board zprávy byl sdílená utility funkce (ne duplikovaný kód v každém handleru)

---

## 2. Testování přidání/odebrání reakcí
- [ ] Ověřit, že `ReactionAdded` event správně přiřadí roli uživateli
- [ ] Ověřit, že `ReactionRemoved` event správně odebere roli uživateli
- [ ] Otestovat případ kdy role na serveru neexistuje (bot ji musí vytvořit)
- [ ] Otestovat případ kdy bot nemá dostatečná oprávnění (Manage Roles)

---

## 3. Kontrola rolí na serveru při aktualizaci týmů
- [ ] Po každé změně týmu (přidání / odebrání / editace) zkontrolovat, zda existují Discord role pro všechny týmy
- [ ] Chybějící role automaticky vytvořit
- [ ] Role u odstraněných týmů smazat (nebo dát na výběr v konfiguraci)

---

## 4. Board zpráva – přidat reakční tlačítka (emojis)
- [ ] Při prvním vytvoření board zprávy (`[DBA_BOARD]`) přidat emoji reakce podle aktuálních týmů
- [ ] Zajistit, že reakce jsou vždy v synchronizaci se seznamem týmů

---

## 5. Automatická synchronizace emojis při změnách týmů
- [ ] Při přidání týmu – přidat příslušný emoji na board zprávu
- [ ] Při odebrání týmu – odebrat příslušný emoji z board zprávy
- [ ] Při změně emoji týmu – odebrat starý emoji, přidat nový

---

## 6. Graceful shutdown botů při vypnutí API / serveru
- [ ] Napojit `IHostApplicationLifetime.ApplicationStopping` event
- [ ] Při zastavení aplikace projít všechny online boty a zavolat `StopBotAsync` s důvodem „Vypnutí serveru"
- [ ] Zapsat správný důvod a čas zastavení do `BotHistory`

---

## 7. Refactoring registrace commandů – každý command jako samostatný handler
- [ ] Definovat abstrakci (interface / base class) pro command handler: název, popis, parametry, oprávnění, execute metoda
- [ ] Každý command přesunout do vlastního souboru (např. `Commands/PridatTymCommand.cs`)
- [ ] Plugin dynamicky discovery a registrace commandů místo hardcoded switch-case v `HandleCommandAsync`
- [ ] Slash command definice generovat automaticky z handler metadat (ne duplikovat v `DiscordBotAllianceService`)
- [ ] Registrace do DB taky řídit z handler metadat

---

## 8. Zobrazení zaregistrovaných commandů v detailu bota
- [ ] API endpoint nebo rozšíření stávajícího `GET /api/bot/admin/{id}` – vrátit seznam `BotCommand` z DB
- [ ] Frontend: v detailu bota přidat panel „Zaregistrované příkazy"
- [ ] Zobrazit: název, popis, permission level, počet použití, počet chyb
- [ ] (Volitelně) možnost příkaz deaktivovat přímo z UI (toggle `IsEnabled`)
