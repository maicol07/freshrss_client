# Audit tecnico e UI/UX

Data: 27 agosto 2026. Audit statico iniziale e stato degli interventi applicati.

## Verdetto

La base è recuperabile. Il problema principale non è la dimensione del repository: è la concentrazione di sincronizzazione, persistenza, stato UI e side effect in `MainViewModel`.

Gli interventi P0 e parte dei P1 sono stati applicati e verificati con 37 test e build Release senza errori o warning. Restano da valutare la QA visuale con package identity, il reader HTML e la riduzione ulteriore del ViewModel.

Prima di qualsiasi redesign vanno risolti quattro rischi:

1. "Segna tutti come letti" su `Senza categoria` può segnare come letto l'intero account.
2. Un errore di rete durante il fetch viene trattato come una lista vuota e può sovrascrivere la cache valida.
3. La password API è salvata in chiaro e inviata nella query string del login.
4. Più operazioni scrivono gli stessi JSON senza serializzazione o scrittura atomica.

## Priorità

| Priorità | Intervento | Costo | Verifica |
|---|---|---:|---|
| P0 | Correggere `mark all`, risultati HTTP e salvataggi concorrenti | M | test di regressione e fault injection |
| P0 | Spostare la password in Credential Locker e fare login via POST | S | nessuna password in file, URL o log |
| P1 | Rendere cache e coda offline coerenti | M | riavvio, offline, cancellazione e sync concorrente |
| P1 | Correggere lifecycle pagina e single-instance activation | S | navigazione ripetuta e seconda istanza |
| P1 | Layout master-detail adattivo e accessibilità | M | 500/800/1200 epx, tastiera, Narrator, High Contrast |
| P2 | Ridurre boilerplate, codice morto e logging | S | analyzer puliti, diff negativo |
| P2 | Allineare CI, packaging e documentazione | S | build Release, test e artifact installabile |

## Bug e correttezza

### P0

- **Azione distruttiva fuori ambito.** `MarkAllAsReadInActiveStreamAsync` chiama sempre il bulk endpoint ([MainViewModel.cs:1455](FreshRssClient/ViewModels/MainViewModel.cs#L1455)). Il servizio traduce `uncategorized` in `reading-list` ([FreshRssService.cs:553](FreshRssClient/Services/FreshRssService.cs#L553)). Risultato: dalla vista "Senza categoria" il server può segnare come letto tutto. Per `uncategorized`, eseguire il bulk per ciascun feed della categoria oppure solo gli ID esplicitamente caricati. Mai convertirlo nel flusso globale.

- **Errore remoto scambiato per risultato vuoto.** I metodi di fetch catturano ogni eccezione e restituiscono collezioni vuote ([FreshRssService.cs:478](FreshRssClient/Services/FreshRssService.cs#L478), [FreshRssService.cs:707](FreshRssClient/Services/FreshRssService.cs#L707)). Il ViewModel salva quelle collezioni e mostra successo ([MainViewModel.cs:876](FreshRssClient/ViewModels/MainViewModel.cs#L876), [MainViewModel.cs:985](FreshRssClient/ViewModels/MainViewModel.cs#L985)). Un 500, timeout o JSON invalido può quindi cancellare la vista e la cache. Restituire dati oppure un errore tipizzato, mai "vuoto" per entrambi i casi.

- **Scritture concorrenti e non atomiche.** Cache, pending reads e notifiche fanno read-modify-write sugli stessi file. `MarkSelectedAsReadAsync` avvia rimozioni parallele ([MainViewModel.cs:1478](FreshRssClient/ViewModels/MainViewModel.cs#L1478)); ogni rimozione rilegge e riscrive l'intero file ([MainViewModel.cs:1620](FreshRssClient/ViewModels/MainViewModel.cs#L1620)). `SaveCache` parte inoltre in fire-and-forget ([MainViewModel.cs:876](FreshRssClient/ViewModels/MainViewModel.cs#L876)). Sono possibili aggiornamenti persi, JSON troncati e cache associata allo stream sbagliato. Usare un solo writer asincrono con `SemaphoreSlim`, snapshot in memoria e replace atomico del file.

### P1

- **Chiamata API fittizia per determinare lo stato.** `UpdateStatusTexts` invoca `MarkAsReadAsync("test")` ([MainViewModel.cs:683](FreshRssClient/ViewModels/MainViewModel.cs#L683)). È una scrittura remota reale e lo `Task.Status` non rappresenta la connessione. Eliminare il metodo; lo stato deve derivare dall'ultimo risultato di autenticazione/sync.

- **Cache parziale spacciata per cache completa.** Il fetch riceve filtro e ricerca ([MainViewModel.cs:849](FreshRssClient/ViewModels/MainViewModel.cs#L849)); la lista già filtrata viene poi salvata ([MainViewModel.cs:876](FreshRssClient/ViewModels/MainViewModel.cs#L876)). Cercare "foo" o scegliere "letti" può sovrascrivere lo stream offline con quel sottoinsieme. Salvare il risultato canonico, poi filtrare solo per la presentazione.

- **Race sullo stream della cache.** `SaveCache` legge `ActiveStreamId` dentro il task differito ([MainViewModel.cs:1233](FreshRssClient/ViewModels/MainViewModel.cs#L1233)). Se l'utente cambia feed nel frattempo, gli articoli possono finire sotto la chiave nuova. Catturare la chiave prima del fetch e passarla esplicitamente al salvataggio.

- **La pagina articoli smette di aggiornarsi dopo Settings.** `MainWindow` conserva la stessa istanza ma la rimuove dal visual tree ([MainWindow.xaml.cs:186](FreshRssClient/MainWindow.xaml.cs#L186)). `ArticlesPage.Unloaded` annulla tutte le sottoscrizioni e rimuove anche il proprio handler ([ArticlesPage.xaml.cs:148](FreshRssClient/Views/ArticlesPage.xaml.cs#L148)); quando la pagina viene riaggiunta non si iscrive più. Gestire `Loaded`/`Unloaded` in modo idempotente oppure non scollegare una pagina mantenuta viva.

- **Redirezione single-instance non attesa.** L'app chiama `RedirectActivationToAsync` e termina subito ([App.xaml.cs:96](FreshRssClient/App.xaml.cs#L96)). Microsoft specifica che il chiamante deve attendere il completamento, altrimenti la redirezione può fallire: [App lifecycle instancing](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing). Questo rende intermittenti seconda istanza e click sulle notifiche.

- **Deep link della notifica fragile.** `ActivateFromToast` avvia la selezione globale e cerca subito l'articolo nella collezione corrente ([MainWindow.xaml.cs:358](FreshRssClient/MainWindow.xaml.cs#L358)). Se l'articolo non è nei primi elementi o il fetch non è finito, non succede nulla. La navigazione deve attendere un lookup per ID o mostrare un errore esplicito.

- **"Segna come non letto" non ha coda offline.** Lo stato locale cambia anche se la POST fallisce ([MainViewModel.cs:1019](FreshRssClient/ViewModels/MainViewModel.cs#L1019)). La coda supporta soltanto letture. Modellare una sola coda di mutazioni `{ articleId, desiredReadState }`; l'ultima intenzione per ID vince.

- **Conteggi errati dopo bulk.** Per feed e categorie vengono sottratti solo gli articoli visibili, mentre il bulk modifica anche quelli non caricati ([MainViewModel.cs:1406](FreshRssClient/ViewModels/MainViewModel.cs#L1406)). Dopo successo, azzerare il conteggio dello stream interessato e ricalcolare i totali, oppure sincronizzare subito.

- **Notifiche limitate alla vista attiva.** Il timer scarica solo `ActiveStreamId` ([MainViewModel.cs:851](FreshRssClient/ViewModels/MainViewModel.cs#L851)), quindi notifica soltanto il feed/categoria aperto. La deduplica usa un `HashSet.Take(500)`, ordine non cronologico ([MainViewModel.cs:1591](FreshRssClient/ViewModels/MainViewModel.cs#L1591)). Fare il polling delle nuove entry globali con timestamp/continuation e persistere un cursore, non centinaia di ID arbitrari.

- **Il lettore mostra solo il sommario.** Entrambe le viste assegnano `Summary` al corpo ([ArticlesPage.xaml.cs:347](FreshRssClient/Views/ArticlesPage.xaml.cs#L347), [ArticleDetailPage.xaml.cs:29](FreshRssClient/Views/ArticleDetailPage.xaml.cs#L29)); `Content` non viene mostrato. La feature descritta come "full article reader" nel README non esiste. O rinominarla "anteprima" e aprire il browser, oppure implementare un reader HTML isolato e sanitizzato.

### P2

- `App.UnhandledException` imposta sempre `Handled = true` ([App.xaml.cs:25](FreshRssClient/App.xaml.cs#L25)). Continuare dopo un'eccezione XAML sconosciuta può lasciare lo stato incoerente. Gestire solo errori noti e lasciare terminare quelli non recuperabili.
- Il log badge viene scritto a ogni variazione del conteggio ([NotificationService.cs:134](FreshRssClient/Services/NotificationService.cs#L134)) e non ruota. Rimuoverlo dalla Release; `badge_log.txt` alla root è già un residuo locale non ignorato.
- Il fallback timestamp usa `DateTime.Now` per dati invalidi ([FreshRssService.cs:713](FreshRssClient/Services/FreshRssService.cs#L713)), facendo apparire in cima articoli corrotti. Restituire errore o una data minima esplicita.
- `FetchArticlesAsync` e le mutazioni nascondono status code e body. L'utente vede "nessun articolo" invece di 401, 429 o 500. Conservare status e messaggio sicuro nel risultato.

## Sicurezza e privacy

- **Password in chiaro.** `settings.json` contiene `ApiPassword` ([MainViewModel.cs:1053](FreshRssClient/ViewModels/MainViewModel.cs#L1053)). Spostarla in `Windows.Security.Credentials.PasswordVault`, salvarla solo dopo login riuscito e offrire "ricorda credenziali". Microsoft dice esplicitamente di non salvare password in chiaro nei dati app: [Credential Locker](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker).

- **Password nell'URL.** Il login usa GET con `Passwd` nella query ([FreshRssService.cs:287](FreshRssClient/Services/FreshRssService.cs#L287)). URL, proxy e log server possono conservarla. FreshRSS documenta il login come POST form: [Google Reader API di FreshRSS](https://freshrss.github.io/FreshRSS/en/developers/06_GoogleReader_API.html).

- **Schemi URI non limitati.** Link e immagini provenienti dai feed vengono passati a `Launcher`/`BitmapImage` accettando qualunque URI assoluto ([ArticlesPage.xaml.cs:557](FreshRssClient/Views/ArticlesPage.xaml.cs#L557), [StringToImageSourceConverter:764](FreshRssClient/Views/ArticlesPage.xaml.cs#L764)). Accettare `https` e, se scelto dall'utente, `http`; mantenere `ms-appx` solo per asset interni.

- **OpenGraph amplia inutilmente il rischio.** Il client segue redirect e scarica URL controllati dagli articoli senza limite di risposta, filtro per indirizzi privati o cancellation token ([OpenGraphService.cs:17](FreshRssClient/Services/OpenGraphService.cs#L17)). Può sondare servizi LAN e moltiplica fino a cinque richieste per sync. Raccomandazione: eliminare la feature; l'app estrae già immagini dal feed. Se resta, limitare schema, DNS/IP, redirect, byte, timeout regex e concorrenza.

- **Favicon esterne (trade-off esplicito).** Il servizio usa prima `iconUrl` fornito da FreshRSS; se manca, costruisce `google.com/s2/favicons` solo per host pubblici. Host locali/privati usano l'asset interno, evitando di inviare domini LAN a Google. FreshRSS include `iconUrl` nella subscription list: [implementazione FreshRSS](https://github.com/FreshRSS/FreshRSS/blob/edge/p/api/greader.php).

- **Capability inutile.** `systemAIModels` è dichiarata ma non usata ([Package.appxmanifest:48](FreshRssClient/Package.appxmanifest#L48)). Rimuoverla. `runFullTrust` resta necessaria per l'app desktop e la tray Win32.

- **HTTP self-hosted.** Non bloccarlo alla cieca: molti server LAN lo usano. Mostrare un warning persistente e richiedere conferma per URL non HTTPS; consentire senza warning solo loopback se serve sviluppo.

- **CI.** Impostare `permissions: contents: read`, eseguire i test e valutare il pin delle action a SHA. Non introdurre un secret di signing finché il formato di distribuzione non è deciso.

## UI/UX e layout

### Problemi verificati nel markup

- Non esistono `AutomationProperties`, `AccessKey`, `KeyboardAccelerator`, `VisualStateManager` o `AdaptiveTrigger` in alcun XAML.
- I pulsanti a sola icona non hanno nome accessibile. I tooltip esistono solo per parte della toolbar; i pulsanti "segna letto" nei template non ne hanno.
- Le azioni frequenti misurano 24, 28 o 32 epx ([ArticlesPage.xaml:76](FreshRssClient/Views/ArticlesPage.xaml#L76), [ArticlesPage.xaml:156](FreshRssClient/Views/ArticlesPage.xaml#L156), [ArticlesPage.xaml:258](FreshRssClient/Views/ArticlesPage.xaml#L258)). Microsoft raccomanda target touch da 40x40 epx: [targeting](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-targeting).
- Il master-detail ha colonna sinistra fissa a 350 epx ([ArticlesPage.xaml:220](FreshRssClient/Views/ArticlesPage.xaml#L220)); Settings forza 600 epx ([SettingsPage.xaml:10](FreshRssClient/Views/SettingsPage.xaml#L10)). Su finestre strette il contenuto viene compresso o tagliato. Usare breakpoint XAML; Microsoft indica `AdaptiveTrigger` per questo caso: [responsive layouts](https://learn.microsoft.com/en-us/windows/apps/develop/ui/layouts-with-xaml).
- `SelectionMode="Extended"`, checkbox sempre visibili, Ctrl/Shift, click centrale e menu contestuale implementano quattro modelli di selezione. È ridondante e imprevedibile. Usare il modello nativo del `ListView`: singola selezione normalmente, modalità multipla esplicita con checkbox.
- Lo stato vuoto non distingue: account non configurato, errore, offline senza cache, filtro senza risultati e ricerca senza risultati. Servono messaggi e azioni diverse.
- Le impostazioni salvano al `LostFocus` e avviano subito riconnessione. Aggiungere `Connetti e salva`, validazione inline e stato per campo. Le preferenze non di account possono restare autosave.
- Il filtro attivo cambia solo colore. Aggiungere testo/checked state accessibile nella toolbar, senza affidarsi al colore.

### Layout raccomandato

Una sola esperienza master-detail, non due implementazioni list/grid:

- `>= 900 epx`: NavigationView + lista 360 epx + reader.
- `600-899 epx`: NavigationView compact + lista/reader, uno alla volta.
- `< 600 epx`: pane overlay, lista full width, dettaglio navigato con Back.
- Toolbar con `CommandBar`: filtro, aggiorna, seleziona. Etichette visibili quando c'è spazio.
- Una sola selezione multipla esplicita. `Esc` esce, `Ctrl+A` seleziona, `Ctrl+R` sincronizza, `/` focalizza la ricerca.
- Reader con larghezza testo 680-760 epx, corpo selezionabile, dimensione testo regolabile e link esterni indicati.

Questa scelta elimina `ArticleDetailPage`, il template grid duplicato e gran parte di `ApplyLayoutMode`/`SyncSelectionToUI`. La griglia di card è più costosa, mostra meno titoli e non aiuta il flusso tipico di un feed reader.

## Codice e architettura

- **Non aggiungere un container DI.** Le interfacce dei servizi sono già usate dai fake dei test. Il costruttore injection attuale basta.
- **Ridurre `MainViewModel`, non frammentarlo in dieci manager.** Estrarre un solo `AppStateStore` responsabile di snapshot atomici e coda mutazioni. Il ViewModel resta coordinatore UI/sync.
- **Usare ciò che è già installato.** `[ObservableProperty]` e i partial hook di CommunityToolkit.Mvvm possono sostituire centinaia di righe di proprietà manuali senza nuova dipendenza.
- **Risultati tipizzati.** `bool` e lista vuota non bastano. Un piccolo `ApiResult<T>` con status HTTP, valore ed errore separa offline, auth e risposta vuota. È giustificato perché cambia il comportamento di tutti i metodi HTTP.
- **Una sola pipeline sync.** Oggi init, manual sync e timer duplicano autenticazione, cancellazione e status. Un metodo `SyncAsync(reason, token)` basta; il timer lo chiama senza `Task.Run`, perché il lavoro è I/O asincrono.
- **Scritture attese.** Niente `SafeFireAndForget` per persistenza o mutazioni remote. Usarlo solo per eventi UI che non possono restituire `Task`, con errore mostrato o loggato.
- **Non introdurre SQLite ora.** Un writer serializzato e replace atomico risolvono i bug con meno codice. Passare a SQLite solo se cache, query o migrazioni diventano misurabilmente costose.
- **Reader HTML, solo se davvero richiesto.** La soluzione completa richiede WebView2 isolato, CSP e sanitizzazione. Se non è una funzione centrale, mantenere una buona anteprima testuale e aprire il browser.

## Semplificazioni concrete

- `delete:` rimuovere `dotnet-install.ps1`, 1.699 righe vendorizzate e non referenziate. CI usa già `setup-dotnet`; per locale basta `global.json` più istruzioni.
- `delete:` rimuovere `MainPage.xaml` e `.cs`, mai istanziati.
- `delete:` rimuovere l'overload `SafeFireAndForget.Run(Task)`, mai chiamato.
- `delete:` rimuovere `DrawRoundedRectangle`, mai chiamato.
- `yagni:` rimuovere `ILocalization`, interfaccia con una sola implementazione e nessun consumer sostituibile. Tenere il tipo concreto o generare gli accessor `.resx`.
- `shrink:` sostituire le proprietà osservabili manuali con i generatori MVVM già presenti.
- `native:` usare prima `iconUrl` di FreshRSS e Google solo come fallback per host pubblici.
- `shrink:` usare un'icona tray statica e lasciare il conteggio a tooltip + badge taskbar. Così spariscono il disegno GDI+, le extension grafiche e `System.Drawing.Common`.
- `delete:` se si adotta il master-detail unico, eliminare grid mode e `ArticleDetailPage` duplicata.
- `delete:` eliminare OpenGraph scraping se il fallback immagini non giustifica traffico e rischio.

net: -1.800 linee e -1 dipendenza possibili senza ridurre sincronizzazione, offline, notifiche o accessibilità. Il taglio supera 2.000 linee scegliendo layout unico e niente OpenGraph.

## Dipendenze

- `CommunityToolkit.Mvvm 8.4.2` è corrente e va sfruttato meglio, non sostituito.
- `CommunityToolkit.WinUI.Controls.SettingsControls 8.0.240109` è vecchio; la stabile disponibile è `8.2.251219`: [NuGet](https://www.nuget.org/packages/CommunityToolkit.WinUI.Controls.SettingsControls/). Aggiornare con test visivo delle Settings.
- `Microsoft.WindowsAppSDK 2.1.3` non è più l'ultima stabile; `2.4.0` è disponibile: [NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSdk/). Non saltare versione insieme ai fix P0. Fare un PR separato con smoke test lifecycle, TitleBar, tray e packaging.
- `System.Drawing.Common 10.0.8` ha patch successive, ma la scelta migliore è rimuoverlo con il badge tray disegnato a runtime. Se resta, aggiornare alla patch `10.0.11`: [NuGet](https://www.nuget.org/packages/System.Drawing.Common/).
- Non aggiungere Moq, DI, logging framework, SQLite o parser HTML finché un requisito concreto non li rende più piccoli della soluzione nativa.

## Build, test e release

- Build e test sono riproducibili con `global.json` (`10.0.100`, `rollForward: latestFeature`); la suite conta 37 test superati e la build Release è pulita. È il modello documentato da Microsoft: [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).
- La workflow pubblica senza eseguire `dotnet test` ([build.yml:22](.github/workflows/build.yml#L22)). Inserire i test prima del publish e conservare l'artifact solo dopo successo.
- Packaging ambiguo: `WindowsPackageType=None` è commentato, MSIX è abilitato, ma README e CI promettono una cartella portable. Scegliere un solo contratto. MSIX è preferibile per startup task, identità, notifiche e Credential Locker; un build unpackaged richiede configurazione esplicita e ha limitazioni documentate: [distribuzione WinUI unpackaged](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).
- Il manifest dichiara minimo `17763`, il README `19041`. Allineare TFM, manifest, CI e requisiti pubblici.
- `Publisher="CN=AppPublisher"` è placeholder e la CI non firma. Prima di chiamare l'artifact "release", produrre e verificare un MSIX firmato oppure documentare chiaramente un build unpackaged non firmato.
- Abilitare analyzer SDK già inclusi: `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`. Non aggiungere StyleCop ora.

## Test mancanti da aggiungere prima del refactor

1. `MarkAll_Uncategorized_DoesNotMarkGlobalReadingList`.
2. `Sync_WhenArticlesFetchFails_PreservesCacheAndReportsError`.
3. `PendingMutations_ConcurrentCompletion_PreservesAllFailures`.
4. `SearchAndFilter_DoNotOverwriteCanonicalCache`.
5. `StreamChangesDuringSave_UsesOriginalCacheKey`.
6. `MarkUnread_Offline_ReplaysDesiredState`.
7. `ArticlesPage_SettingsRoundTrip_RemainsSubscribed`.
8. `SecondInstance_WaitsForRedirect` e toast su articolo non caricato.
9. `Authenticate_UsesPostAndNeverPlacesPasswordInUri`.
10. UI automation per nomi accessibili, tastiera e breakpoint.

## Ordine d'intervento consigliato

1. Test di regressione P0, poi correzione `mark all`.
2. POST login + Credential Locker + allowlist URI.
3. Risultati API tipizzati; non sovrascrivere cache su errore.
4. `AppStateStore` con un writer e file atomico; unificare read/unread pending.
5. Correggere lifecycle pagina, activation e deep link.
6. Layout master-detail adattivo e accessibilità.
7. Tagli Ponytail, analyzer, CI e aggiornamenti dipendenze in PR separati.

## Limiti della verifica

- Build e test: eseguiti con SDK .NET 10; 37/37 test superati e build Release pulita.
- Ispezione visuale runtime: la build installata era disponibile, ma l'autorizzazione del controllo UI è scaduta prima dell'apertura. I rilievi UI sopra derivano quindi dal markup e dal flusso degli eventi, non da screenshot runtime.
- Nessun file applicativo, impostazione, account o dato FreshRSS è stato modificato.
